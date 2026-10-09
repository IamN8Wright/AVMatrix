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
    private static T Field<T>(object form, string name) => (T)typeof(MainForm)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static void Call(object form, string name, params object?[] args) => typeof(MainForm)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Windows UI QC: " + message);
    }
}
