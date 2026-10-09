using System.Reflection;

namespace InNasc.SmokeTests;

internal static class WorkspaceUiRegression
{
    public static void Run()
    {
        var access = new MasterAccessControl();
        MasterAccessService.CreateInitialOwner(access, "qc-owner", "QC Owner", "QC-Owner-password-533");
        var session = MasterAccessService.SignIn(access, "qc-owner", "QC-Owner-password-533");
        MasterSessionContext.Set(SyncTarget.SharedFile, Path.Combine(Path.GetTempPath(), "QC-Master.nasc"), session);
        try
        {
            var room = new RoomRecord { Name = "Conference" };
            for (var i = 0; i < 169; i++) room.Equipment.Add(new EquipmentRecord { Description = "Device " + i });
            var location = new LocationRecord { Name = "CSC", Rooms = [room] };
            var client = new ClientRecord { Name = "Providence", Locations = [location] };
            var data = new AppData { Clients = [client], MasterAccess = access };
            DataStore.Normalize(data);
            using var form = new MainForm(data, new DataStore());
            Call(form, "OpenClient", client);
            var tree = Field<TreeView>(form, "_tree");
            tree.SelectedNode = tree.Nodes[0].Nodes[0];
            Call(form, "RefreshGrid", (object?)null);
            Assert(Field<Label>(form, "_totalMetric").Text == "169", "Initial location count is wrong.");

            var next = ClientSubmatrixService.CloneClient(client);
            next.Locations[0].Rooms.Add(new RoomRecord { Name = "Meeting Room" });
            next.Locations[0].Rooms.Add(new RoomRecord { Name = "Huddle Room" });
            for (var i = 0; i < 3; i++) next.Locations[0].Rooms[0].Equipment.Add(new EquipmentRecord());
            data.Clients = [next];
            DataStore.Normalize(data);
            // Reproduce the historical plain-push path: the inventory is replaced
            // while the tree still contains the previous location instance.
            Call(form, "RefreshGrid", (object?)null);
            Assert(Field<Label>(form, "_totalMetric").Text == "172", "Graph replacement blanked the location.");
            Assert(Field<Label>(form, "_scopeSubtitle").Text.Contains("Providence"), "Parent client path went blank.");
            Assert(Field<Label>(form, "_signedInLabel").Text.Contains("172"), "Footer count remained at 169.");
            for (var cycle = 0; cycle < 240; cycle++)
            {
                data.Clients = data.Clients.Select(ClientSubmatrixService.CloneClient).ToList();
                Call(form, "RefreshAfterLiveDataChange");
                Assert(tree.SelectedNode?.Tag is LocationRecord selected && selected.Id == location.Id,
                    "A sync cycle moved the selected location.");
                Assert(Field<Label>(form, "_totalMetric").Text == "172", "Repeated sync lost devices.");
            }
            Assert(tree.Nodes[0].Nodes[0].Nodes.Count == 3, "The current tree does not show all rooms.");
            VerifyCheckoutActions(data);
            VerifySyncActionStates();
            VerifyWorkspaceSyncRouting(data, session);
            VerifyBackupButton();
            Console.WriteLine("Windows UI QC passed: parent path, current count, footer count, selection and 240 refresh cycles.");
        }
        finally { MasterSessionContext.Clear(); }
    }
    private static void VerifyCheckoutActions(AppData data)
    {
        var synthetic = new AppData
        {
            Clients = data.Clients.Select(ClientSubmatrixService.CloneClient).ToList(),
            Settings = new AppSettings
            {
                SharedMasterPath = Path.Combine(Path.GetTempPath(), "Missing-QC-Company.nasc"),
                ActiveCheckoutClientId = data.Clients[0].Id,
                ActiveCheckoutToken = Guid.NewGuid(),
                ActiveCheckoutTarget = nameof(SyncTarget.SharedFile)
            }
        };
        using var shared = new SharedSyncForm(synthetic, new DataStore());
        var button = (Button)typeof(SharedSyncForm).GetField("_push", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(shared)!;
        Assert(button.Enabled && button.Text == "Check in & push",
            "The main sync action must remain available for a shared-file checkout.");
        var recovery = (Button)typeof(SharedSyncForm).GetField("_recoverInventory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(shared)!;
        Assert(recovery.Enabled, "Missing-record recovery is unavailable for an active checkout.");
        synthetic.Settings.ActiveCheckoutTarget = nameof(SyncTarget.GoogleDrive);
        typeof(SharedSyncForm).GetMethod("RefreshMasterState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(shared, [null]);
        Assert(!button.Enabled && !recovery.Enabled,
            "A checkout for a different backend must not be checked in or recovered here.");
        using var google = new GoogleDriveSyncForm(synthetic, new DataStore(), connectionOnly: true);
        var googleButton = (Button)typeof(GoogleDriveSyncForm).GetField("_push", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(google)!;
        Assert(googleButton.Text == "Check in & push to Google Drive" && !googleButton.Enabled,
            "Google sync must name the checkout action, and connection-only mode must prevent writes.");
        var prepared = CheckoutInventoryService.PrepareRecovery(synthetic.Clients[0], synthetic.Clients[0]);
        using var preview = new CheckoutRecoveryPreviewForm(prepared);
        Assert(preview.CancelButton is Button && preview.AcceptButton is Button,
            "The recovery preview must offer both cancel and explicit apply actions.");
        Console.WriteLine("Windows checkout UI QC passed: main action, recovery action, backend mismatch and connection-only mode.");
    }

    private static void VerifyBackupButton()
    {
        using var form = (Form)Activator.CreateInstance(typeof(BackupPrivacyOptionsForm), nonPublic: true)!;
        var button = form.Controls.OfType<Button>().Single(item => item.Text == "Continue");
        foreach (var scale in new[] { 1f, 1.25f, 1.5f })
        {
            if (scale != 1f) form.Scale(new SizeF(scale, scale));
            var text = TextRenderer.MeasureText(button.Text, button.Font);
            Assert(button.ClientSize.Width >= text.Width + button.Padding.Horizontal &&
                button.ClientSize.Height >= text.Height + button.Padding.Vertical,
                "The backup Continue label must fit on one line at common Windows scaling settings.");
            Assert(form.ClientRectangle.Contains(button.Bounds), "The backup button extends beyond the dialog.");
        }
        Console.WriteLine("Windows backup UI QC passed: Continue text and bounds at 100%, 125%, and enlarged scaling.");
    }

    private static void VerifySyncActionStates()
    {
        // No real credentials or company file: state transitions below use explicit
        // synthetic prerequisites, and never invoke a cloud or publishing handler.
        var data = new AppData();
        using var google = new GoogleDriveSyncForm(data, new DataStore());
        google.Show();
        var push = SyncField<Button>(google, "_push");
        var pull = SyncField<Button>(google, "_pull");
        var checkIn = SyncField<Button>(google, "_checkIn");
        var recovery = SyncField<Button>(google, "_recoverInventory");
        var actionState = SyncField<Label>(google, "_actionState");
        void SetState(bool configured, bool signedIn, bool linked) =>
            typeof(GoogleDriveSyncForm).GetMethod("RefreshActionAvailability",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(google, [configured, signedIn, linked]);
        SetState(true, false, true);
        Assert(!push.Enabled && push.BackColor == UiTheme.HeaderSurface && push.Cursor == Cursors.Default,
            "A signed-out action must look disabled and use the default cursor.");
        Assert(actionState.Text.Contains("Sign in with Google"), "Signed-out sync must explain how to enable it.");
        Assert(!checkIn.Visible && !recovery.Visible, "Checkout controls must be hidden without a checkout.");
        SetState(true, true, false);
        Assert(!push.Enabled && actionState.Text.Contains("share link"), "An unlinked sync must explain the missing link.");
        SetState(true, true, true);
        Assert(push.Enabled && pull.Enabled && push.BackColor == UiTheme.Blue && push.Cursor == Cursors.Hand,
            "Connected sync actions must become enabled and recover their accent styling.");
        Assert(!push.UseMnemonic && push.Text.Contains("&"), "Sync action labels must render the literal ampersand.");
        AssertClickableBounds(google, push);
        AssertClickableBounds(google, pull);
        push.Select();
        Assert(push.ContainsFocus, "The enabled sync action must be reachable by keyboard focus.");
        data.Settings.ActiveCheckoutClientId = Guid.NewGuid();
        data.Settings.ActiveCheckoutTarget = nameof(SyncTarget.GoogleDrive);
        SetState(true, true, true);
        Assert(push.Enabled && checkIn.Enabled && recovery.Enabled && checkIn.Visible && recovery.Visible && !pull.Enabled,
            "Google checkout actions must be available while full-master pull is disabled.");
        Assert(push.Text.Contains("Check in") && actionState.Text.Contains("Pull is unavailable"),
            "Checkout sync must name the available action and explain the disabled pull.");
        AssertClickableBounds(google, checkIn);
        AssertClickableBounds(google, recovery);
        var checkoutText = TextRenderer.MeasureText(checkIn.Text, checkIn.Font);
        Assert(checkIn.ClientSize.Width >= checkoutText.Width + checkIn.Padding.Horizontal,
            "The literal Check in & push label must fit in its button.");
        google.Size = google.MinimumSize;
        AssertClickableBounds(google, push);
        AssertClickableBounds(google, recovery);
        typeof(GoogleDriveSyncForm).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(google, true);
        SetState(true, true, true);
        Assert(!push.Enabled && !checkIn.Enabled && !recovery.Enabled && actionState.Text.Contains("running"),
            "Busy sync must disable actions and explain the temporary wait.");
        typeof(GoogleDriveSyncForm).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(google, false);
        SetState(true, true, true);
        Assert(push.Enabled && checkIn.Enabled && recovery.Enabled, "Finishing sync must restore checkout actions.");
        data.Settings.ActiveCheckoutTarget = nameof(SyncTarget.SharedFile);
        SetState(true, true, true);
        Assert(!push.Enabled && !checkIn.Visible && !recovery.Visible && actionState.Text.Contains("Local / file share"),
            "A file checkout must direct users to its own backend without offering Google writes.");
        data.Settings.ActiveCheckoutTarget = nameof(SyncTarget.GoogleDrive);
        using var setup = new GoogleDriveSyncForm(data, new DataStore(), connectionOnly: true);
        typeof(GoogleDriveSyncForm).GetMethod("RefreshActionAvailability", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(setup, [true, true, true]);
        Assert(!SyncField<Button>(setup, "_push").Enabled && !SyncField<Button>(setup, "_checkIn").Enabled &&
            !SyncField<Button>(setup, "_recoverInventory").Enabled,
            "Connection setup must never enable publishing controls.");
        var wasDark = UiTheme.IsDarkMode;
        try
        {
            UiTheme.SetDarkMode(true);
            SetState(true, false, true);
            Assert(push.BackColor == UiTheme.HeaderSurface && push.ForeColor == UiTheme.Muted,
                "Dark-mode unavailable actions must use muted styling.");
            SetState(true, true, true);
            Assert(push.BackColor == UiTheme.Blue && push.ForeColor == Color.White,
                "Dark-mode enabled actions must restore the primary style.");
        }
        finally { UiTheme.SetDarkMode(wasDark); }
        google.Close();
        data.Settings.ActiveCheckoutTarget = nameof(SyncTarget.SharedFile);
        data.Settings.SharedMasterPath = Path.Combine(Path.GetTempPath(), "Missing-Action-QC-Company.nasc");
        using var shared = new SharedSyncForm(data, new DataStore());
        shared.Show();
        shared.Size = shared.MinimumSize;
        AssertClickableBounds(shared, SyncField<Button>(shared, "_push"));
        AssertClickableBounds(shared, SyncField<Button>(shared, "_checkIn"));
        AssertClickableBounds(shared, SyncField<Button>(shared, "_recoverInventory"));
        Assert(!SyncField<Button>(shared, "_pull").Enabled &&
            SyncField<Button>(shared, "_pull").BackColor == UiTheme.HeaderSurface,
            "The full-master pull must look unavailable during a file checkout.");
        shared.Close();
        Console.WriteLine("Windows sync action QC passed: signed out, missing link, connected, checkout, busy/re-enable, backend mismatch, setup-only, keyboard focus and unobstructed button bounds.");
    }

    private static T SyncField<T>(object form, string name) => (T)form.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static void VerifyWorkspaceSyncRouting(AppData source, MasterSession session)
    {
        var root = Path.Combine(Path.GetTempPath(), "InNasc-Sync-Routing-" + Guid.NewGuid().ToString("N"));
        var previous = MasterSessionContext.Current;
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "Company.nasc");
            var store = new DataStore(Path.Combine(root, "Workstation"));
            var data = new AppData
            {
                Clients = source.Clients.Select(ClientSubmatrixService.CloneClient).ToList(),
                MasterAccess = MasterAccessService.Clone(source.MasterAccess),
                Settings = new AppSettings
                {
                    SharedMasterPath = path,
                    GoogleDriveFileId = "qc-cloud-copy",
                    LastMasterTarget = nameof(SyncTarget.GoogleDrive),
                    ActiveCheckoutClientId = source.Clients[0].Id,
                    ActiveCheckoutToken = Guid.NewGuid(),
                    ActiveCheckoutTarget = nameof(SyncTarget.SharedFile),
                    GoogleDriveFingerprint = "original-cloud-baseline"
                }
            };
            data.MasterAccess.Checkouts.Add(new ClientCheckoutRecord
            {
                ClientId = data.Settings.ActiveCheckoutClientId.Value,
                CheckoutToken = data.Settings.ActiveCheckoutToken.Value,
                UserId = session.UserId
            });
            PortableDataService.ExportMaster(path, data, session);
            MasterSessionContext.Set(SyncTarget.SharedFile, path, session);
            store.Save(data);
            var saved = File.ReadAllBytes(store.DataPath);
            var remote = new AppData { MasterAccess = MasterAccessService.Clone(data.MasterAccess) };
            remote.MasterAccess.Checkouts.Clear();
            var snapshot = new GoogleDriveSnapshot(
                new GoogleDriveFileMetadata("qc-cloud-copy", "Cloud copy.nasc", "application/octet-stream",
                    DateTime.UtcNow, "1", 0, true, []),
                "remote-cloud-revision",
                new PortableImport(remote, DateTime.UtcNow, AppInfo.Revision, "cloud-revision", "QC", true), []);
            using (var cloudView = new GoogleDriveSyncForm(data, store))
            {
                typeof(GoogleDriveSyncForm).GetMethod("ShowSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(cloudView, [snapshot]);
                Assert(data.MasterAccess.Checkouts.Count == 1 &&
                    data.MasterAccess.Checkouts[0].CheckoutToken == data.Settings.ActiveCheckoutToken,
                    "A Google file with zero checkouts must not erase an active company-file checkout.");
                Assert(data.Settings.GoogleDriveFingerprint == "original-cloud-baseline" &&
                    !data.Settings.GoogleDriveRemoteChangesDetected && File.ReadAllBytes(store.DataPath).SequenceEqual(saved),
                    "Viewing another backend must not change or save the active workspace or its baseline.");
            }

            using var main = new MainForm(data, store);
            main.Show();
            Type? openedType = null;
            bool checkInEnabled = false;
            using var closeDialog = new System.Windows.Forms.Timer { Interval = 50 };
            closeDialog.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form =>
                    form.Visible && form is SharedSyncForm or GoogleDriveSyncForm);
                if (dialog is null) return;
                openedType = dialog.GetType();
                var action = SyncField<Button>(dialog, "_push");
                checkInEnabled = action.Enabled && action.Text.Contains("Check in");
                closeDialog.Stop();
                dialog.Close();
            };
            closeDialog.Start();
            Field<Button>(main, "_syncButton").PerformClick();
            closeDialog.Stop();
            Assert(openedType == typeof(SharedSyncForm) && checkInEnabled,
                "Clicking the sidebar sync icon must open the checkout's company-file dialog with Check in & push enabled.");
            Assert(data.MasterAccess.Checkouts.Count == 1 && data.Settings.ActiveCheckoutClientId.HasValue,
                "Opening and closing sync must preserve checkout ownership.");
            data.Settings.ActiveCheckoutClientId = null;
            data.Settings.LastMasterTarget = nameof(SyncTarget.GoogleDrive);
            using (var welcome = new MasterWelcomeControl(data))
                Assert(welcome.SelectedTarget == SyncTarget.GoogleDrive,
                    "Welcome must remember Google Drive when both connection types are configured.");
            MasterSessionContext.Clear();
            MasterSessionContext.Set(SyncTarget.GoogleDrive, data.Settings.GoogleDriveFileId, session);
            using var googleSync = (Form)typeof(MainForm).GetMethod("CreateWorkspaceSyncForm",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            Assert(googleSync is GoogleDriveSyncForm, "A Google sign-in must route to Google Drive sync.");
            main.Close();
            Console.WriteLine("Windows sync routing QC passed: actual sidebar button click, enabled file check-in, zero-checkout cloud inspection isolation, remembered Google login and Google sync routing.");
        }
        finally
        {
            MasterSessionContext.Clear();
            if (previous is not null) MasterSessionContext.Set(previous.Target, previous.MasterKey, previous.Session);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void AssertClickableBounds(Form form, Button button)
    {
        form.PerformLayout();
        Assert(button.Visible && button.Enabled, "The tested action must be visible and enabled.");
        foreach (var viewport in form.Controls.OfType<Panel>().Where(panel => panel.AutoScroll))
            viewport.ScrollControlIntoView(button);
        var screenPoint = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
        Control current = form;
        while (current.GetChildAtPoint(current.PointToClient(screenPoint), GetChildAtPointSkip.Invisible) is Control child)
            current = child;
        Assert(current == button, "Another control covers the sync action's click target.");
        Assert(form.ClientRectangle.Contains(form.PointToClient(screenPoint)), "The sync action falls outside the dialog.");
        var screenBounds = button.RectangleToScreen(button.ClientRectangle);
        for (Control? parent = button.Parent; parent is not null; parent = parent.Parent)
            Assert(parent.ClientRectangle.Contains(parent.RectangleToClient(screenBounds)),
                "A sync action is clipped by its parent layout or cannot be scrolled into view.");
    }
    private static T Field<T>(object form, string name) => (T)typeof(MainForm)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static void Call(object form, string name, params object?[] args) => typeof(MainForm)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Windows UI QC: " + message);
    }
}
