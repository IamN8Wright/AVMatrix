namespace AVMatrixStudio;

internal sealed class SharedSyncForm : Form
{
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly TextBox _path = new();
    private readonly Label _state = new();
    private readonly Label _details = new();
    private readonly Label _guidance = new();
    private readonly Button _pull = UiTheme.SecondaryButton("Pull from master");
    private readonly Button _push = UiTheme.PrimaryButton("Merge & push");
    private readonly Button _unlink = UiTheme.DangerButton("Unlink");
    private readonly Button _signIn = UiTheme.SecondaryButton("Sign in");
    private readonly Button _accounts = UiTheme.SecondaryButton("Accounts");
    private readonly Button _checkout = UiTheme.PrimaryButton("Check out client…");
    private readonly Button _checkIn = UiTheme.PrimaryButton("Check in & push");
    private readonly Button _releaseCheckout = UiTheme.DangerButton("Release checkout");
    private string? _masterPassword;
    private MasterSession? _masterSession;
    private readonly bool _openAccountsOnShown;

    public bool DataPulled { get; private set; }

    public SharedSyncForm(AppData data, DataStore store, bool openAccountsOnShown = false)
    {
        _data = data;
        _store = store;
        _openAccountsOnShown = openAccountsOnShown;
        _masterSession = MasterSessionContext.Get(
            SyncTarget.SharedFile,
            _data.Settings.SharedMasterPath);
        _masterPassword = _masterSession?.MasterKey;
        Text = "Shared master sync";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 700);
        Size = new Size(820, 720);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(28, 22, 28, 20)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        shell.Controls.Add(BuildHeading(), 0, 0);
        shell.Controls.Add(BuildLinkPanel(), 0, 1);
        shell.Controls.Add(BuildStatusPanel(), 0, 2);
        shell.Controls.Add(BuildMasterActions(), 0, 3);
        shell.Controls.Add(BuildSyncActions(), 0, 4);
        shell.Controls.Add(BuildFooter(), 0, 5);
        Controls.Add(shell);
        UiTheme.ApplyTheme(this);
        RefreshMasterState();
        if (_openAccountsOnShown)
            Shown += (_, _) => BeginInvoke((Action)ManageAccounts);
    }

    private Control BuildHeading()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Shared master sync",
            AutoSize = true,
            Font = UiTheme.Font(20, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = "Use one .avmatrix master on a file share, synced folder, or Google Drive online.",
            AutoSize = true,
            Font = UiTheme.Font(9.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 42)
        });
        return panel;
    }

    private Control BuildLinkPanel()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(18, 14, 18, 14)
        };
        panel.Controls.Add(new Label
        {
            Text = "MASTER FILE",
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(18, 14)
        });
        _path.ReadOnly = true;
        _path.Font = UiTheme.Font(9.5f);
        _path.Location = new Point(18, 39);
        _path.Size = new Size(675, 29);
        _path.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_path);

        var link = UiTheme.SecondaryButton("Link existing…");
        link.AutoSize = false;
        link.Size = new Size(126, 34);
        link.Location = new Point(18, 82);
        link.Click += (_, _) => LinkExisting();
        panel.Controls.Add(link);

        var create = UiTheme.SecondaryButton("Create new master…");
        create.AutoSize = false;
        create.Size = new Size(154, 34);
        create.Location = new Point(154, 82);
        create.Click += (_, _) => CreateMaster();
        panel.Controls.Add(create);

        var refresh = UiTheme.SecondaryButton("Refresh status");
        refresh.AutoSize = false;
        refresh.Size = new Size(116, 34);
        refresh.Location = new Point(318, 82);
        refresh.Click += (_, _) => RefreshMasterState();
        panel.Controls.Add(refresh);

        var google = UiTheme.SecondaryButton("Google Drive online…");
        google.AutoSize = false;
        google.Size = new Size(164, 34);
        google.Location = new Point(444, 82);
        google.Click += (_, _) => OpenGoogleDriveOnline();
        panel.Controls.Add(google);
        return panel;
    }

    private Control BuildStatusPanel()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(22)
        };
        _state.AutoSize = false;
        _state.Font = UiTheme.Font(17, FontStyle.Bold);
        _state.ForeColor = UiTheme.Text;
        _state.Location = new Point(22, 20);
        _state.Size = new Size(650, 34);
        _state.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_state);

        _details.AutoSize = false;
        _details.Font = UiTheme.Font(10);
        _details.ForeColor = UiTheme.Text;
        _details.Location = new Point(24, 62);
        _details.Size = new Size(650, 62);
        _details.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_details);

        _guidance.AutoSize = false;
        _guidance.Font = UiTheme.Font(9.5f, FontStyle.Bold);
        _guidance.ForeColor = UiTheme.Amber;
        _guidance.Location = new Point(24, 132);
        _guidance.Size = new Size(650, 58);
        _guidance.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_guidance);

        return panel;
    }

    private Control BuildMasterActions()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "MASTER ACCESS & CLIENT CHECKOUT",
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 0)
        });
        var masterActions = new FlowLayoutPanel
        {
            Location = new Point(0, 20),
            Size = new Size(730, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        foreach (var button in new[] { _signIn, _accounts, _checkout, _checkIn, _releaseCheckout })
        {
            button.AutoSize = false;
            button.Height = 36;
        }
        _signIn.Width = 94;
        _accounts.Width = 94;
        _checkout.Width = 142;
        _checkIn.Width = 132;
        _releaseCheckout.Width = 142;
        _signIn.Click += (_, _) => SignInToMaster();
        _accounts.Click += (_, _) => ManageAccounts();
        _checkout.Click += (_, _) => CheckoutClient();
        _checkIn.Click += (_, _) => CheckInClient();
        _releaseCheckout.Click += (_, _) => ReleaseCheckout();
        masterActions.Controls.AddRange([_signIn, _accounts, _checkout, _checkIn, _releaseCheckout]);
        _signIn.Visible = false;
        _checkout.Visible = false;
        panel.Controls.Add(masterActions);
        return panel;
    }

    private Control BuildSyncActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _pull.Dock = DockStyle.Fill;
        _pull.Margin = new Padding(0, 0, 8, 0);
        _pull.Font = UiTheme.Font(11, FontStyle.Bold);
        _pull.Click += (_, _) => PullMaster();
        _push.Dock = DockStyle.Fill;
        _push.Margin = new Padding(8, 0, 0, 0);
        _push.Font = UiTheme.Font(11, FontStyle.Bold);
        _push.Click += (_, _) => PushMaster();
        panel.Controls.Add(_pull, 0, 0);
        panel.Controls.Add(_push, 1, 0);
        return panel;
    }

    private Control BuildFooter()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var close = UiTheme.SecondaryButton("Close");
        close.AutoSize = false;
        close.Size = new Size(84, 34);
        close.Click += (_, _) => Close();
        _unlink.AutoSize = false;
        _unlink.Size = new Size(84, 34);
        _unlink.Click += (_, _) => Unlink();
        panel.Controls.Add(close);
        panel.Controls.Add(_unlink);
        return panel;
    }

    private void LinkExisting()
    {
        if (MasterSessionContext.Current is not null)
        {
            MessageBox.Show(this,
                "Log out first, then choose the other Master Matrix from the welcome screen.",
                "Switch Master Matrix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "Link an AV Matrix Studio shared master",
            Filter = "AV Matrix Studio master (*.avmatrix)|*.avmatrix",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            MasterSessionContext.Clear(SyncTarget.SharedFile, _data.Settings.SharedMasterPath);
            _masterSession = null;
            var password = RequestPasswordIfNeeded(dialog.FileName);
            if (PortableDataService.IsPasswordProtected(dialog.FileName) && password is null) return;
            var snapshot = SharedSyncService.LinkExisting(dialog.FileName, _data, _store, password);
            RefreshMasterState(snapshot);
            MessageBox.Show(this,
                $"Linked to a master containing {snapshot.Contents.ClientCount:N0} client(s) and " +
                $"{snapshot.Contents.EquipmentCount:N0} equipment record(s).\r\n\r\n" +
                "Pull from the master before the first push from this PC.",
                "Master linked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("The master file could not be linked.", exception);
        }
    }

    private void CreateMaster()
    {
        if (MasterSessionContext.Current is not null)
        {
            MessageBox.Show(this,
                "Log out first, then use Create new Master File on the welcome screen.",
                "Create Master Matrix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Create an AV Matrix Studio shared master",
            Filter = "AV Matrix Studio master (*.avmatrix)|*.avmatrix",
            DefaultExt = "avmatrix",
            AddExtension = true,
            FileName = "AV-Matrix-Shared-Master.avmatrix"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var owner = MasterOwnerSetupForm.Prompt(this);
        if (owner is null) return;
        try
        {
            MasterSessionContext.Clear(SyncTarget.SharedFile, _data.Settings.SharedMasterPath);
            _masterPassword = owner.Session.MasterKey;
            _data.MasterAccess = owner.Access;
            _masterSession = owner.Session;
            var result = SharedSyncService.CreateMaster(
                dialog.FileName, _data, _store, owner.Session);
            RememberMasterSession(showNotification: true);
            RefreshMasterState();
            MessageBox.Show(this,
                $"Created the shared master with {result.ClientCount:N0} client(s) and " +
                $"{result.EquipmentCount:N0} equipment record(s).\r\n\r\n" +
                "This master is encrypted and unlocked by its user accounts.\r\n\r\n" +
                "Have each collaborator link to this same file and pull once to establish a merge baseline. " +
                "After that, Merge & push combines independent work automatically.",
                "Shared master created",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("The shared master could not be created.", exception);
        }
    }

    private void PullMaster()
    {
        if (_data.Settings.ActiveCheckoutClientId.HasValue)
        {
            MessageBox.Show(this,
                "Check in and push, or release the current client checkout before pulling the full master.",
                "Client is checked out", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var password = RequestPasswordIfNeeded(_data.Settings.SharedMasterPath);
            if (PortableDataService.IsPasswordProtected(_data.Settings.SharedMasterPath) && password is null) return;
            var snapshot = SharedSyncService.Inspect(_data.Settings.SharedMasterPath, password);
            var session = EnsureSession(snapshot.Contents.Data.MasterAccess, password);
            if (session is null && snapshot.Contents.Data.MasterAccess.IsConfigured) return;
            var savedBy = string.IsNullOrWhiteSpace(snapshot.Contents.SavedBy)
                ? "an earlier AV Matrix Studio revision"
                : snapshot.Contents.SavedBy;
            if (MessageBox.Show(this,
                    $"Pull revision saved by {savedBy}?\r\n\r\n" +
                    $"Master: {snapshot.Contents.ClientCount:N0} client(s), " +
                    $"{snapshot.Contents.EquipmentCount:N0} equipment record(s)\r\n\r\n" +
                    "This replaces the local client data on this PC. Any local changes that were not pushed will be lost.",
                    "Confirm pull",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var result = SharedSyncService.Pull(_data, _store, password, session);
            DataPulled = true;
            RefreshMasterState();
            MessageBox.Show(this,
                $"Pulled {result.ClientCount:N0} client(s) and {result.EquipmentCount:N0} equipment record(s)." +
                $"\r\n\r\nA recovery copy of the previous local data was saved as:\r\n" +
                result.RecoveryBackupPath,
                "Pull complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("The shared master could not be pulled.", exception);
        }
    }

    private void PushMaster()
    {
        if (_data.Settings.ActiveCheckoutClientId.HasValue)
        {
            MessageBox.Show(this,
                "Use Check in & push while working in a checked-out client sub-matrix.",
                "Client checkout active", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var localClientCount = _data.Clients.Count;
        var localEquipmentCount = _data.Clients.Sum(client =>
            client.Locations.Sum(location => location.Rooms.Sum(room => room.Equipment.Count)));
        if (MessageBox.Show(this,
                $"Merge this PC's {localClientCount:N0} client(s) and " +
                $"{localEquipmentCount:N0} equipment record(s) into the shared master?\r\n\r\n" +
                "Independent changes made by other technicians will be combined automatically. " +
                "If the same field was changed differently, you will be asked which value to keep.",
                "Confirm merge and push",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            var password = RequestPasswordIfNeeded(_data.Settings.SharedMasterPath);
            if (PortableDataService.IsPasswordProtected(_data.Settings.SharedMasterPath) && password is null) return;
            var snapshot = SharedSyncService.Inspect(_data.Settings.SharedMasterPath, password);
            var session = EnsureSession(snapshot.Contents.Data.MasterAccess, password);
            if (session is null && snapshot.Contents.Data.MasterAccess.IsConfigured) return;
            SharedSyncResult result;
            try
            {
                result = SharedSyncService.Push(_data, _store, password, session: session);
            }
            catch (MergeResolutionRequiredException conflict)
            {
                using var resolver = new MergeConflictForm(conflict.Conflicts);
                if (resolver.ShowDialog(this) != DialogResult.OK || resolver.Preference is null) return;
                result = SharedSyncService.Push(
                    _data, _store, password, resolver.Preference.Value, session);
            }
            if (result.Action == "Merged") DataPulled = true;
            RefreshMasterState();
            MessageBox.Show(this,
                $"{(result.Action == "Merged" ? "Merged and pushed" : "Pushed")} revision " +
                $"{ShortRevision(result.RevisionId)} with {result.EquipmentCount:N0} equipment record(s)." +
                (string.IsNullOrWhiteSpace(result.RecoveryBackupPath)
                    ? string.Empty
                    : $"\r\n\r\nA recovery copy of the previous master was saved as:\r\n" +
                      result.RecoveryBackupPath),
                result.Action == "Merged" ? "Merge complete" : "Push complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (SharedMasterConflictException exception)
        {
            MessageBox.Show(this,
                exception.Message + "\r\n\r\nNo master data was overwritten.",
                "Newer master detected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            RefreshMasterState();
        }
        catch (Exception exception)
        {
            ShowError("The local data could not be pushed.", exception);
        }
    }

    private void Unlink()
    {
        if (string.IsNullOrWhiteSpace(_data.Settings.SharedMasterPath)) return;
        if (_data.Settings.ActiveCheckoutClientId.HasValue)
        {
            MessageBox.Show(this,
                "Check in and push, or release the active client checkout before unlinking the master.",
                "Client checkout active", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                "Unlink this PC from the shared master? The master file will not be deleted.",
                "Unlink shared master",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        var masterKey = _data.Settings.SharedMasterPath;
        SharedSyncService.Unlink(_data, _store);
        MasterSessionContext.Clear(SyncTarget.SharedFile, masterKey);
        _masterSession = null;
        RefreshMasterState();
    }

    private void SignInToMaster()
    {
        try
        {
            var password = RequestPasswordIfNeeded(_data.Settings.SharedMasterPath);
            if (PortableDataService.IsPasswordProtected(_data.Settings.SharedMasterPath) && password is null) return;
            var snapshot = SharedSyncService.Inspect(_data.Settings.SharedMasterPath, password);
            EnsureSession(snapshot.Contents.Data.MasterAccess, password, forcePrompt: true);
            RefreshMasterState();
        }
        catch (Exception exception)
        {
            ShowError("The master sign-in could not be completed.", exception);
        }
    }

    private MasterSession? EnsureSession(
        MasterAccessControl access,
        string? password,
        bool forcePrompt = false)
    {
        if (!access.IsConfigured)
        {
            var owner = MasterOwnerSetupForm.Prompt(this);
            if (owner is null) return null;
            SharedSyncService.SaveAccessControl(
                _data,
                _store,
                owner.Access,
                session: owner.Session,
                initialSetup: true,
                owner.Session.MasterKey);
            _masterSession = owner.Session;
            _masterPassword = owner.Session.MasterKey;
            RememberMasterSession(showNotification: true);
            MessageBox.Show(this,
                "The first Owner account was added to this master.",
                "Master Owner created", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return _masterSession;
        }
        if (!forcePrompt && _masterSession is not null)
        {
            try
            {
                _masterSession = MasterAccessService.RefreshSession(access, _masterSession);
                RememberMasterSession(showNotification: false);
                return _masterSession;
            }
            catch (MasterAuthorizationException)
            {
                MasterSessionContext.Clear(SyncTarget.SharedFile, _data.Settings.SharedMasterPath);
                _masterSession = null;
            }
        }
        var signedIn = MasterSignInForm.Prompt(this, access);
        if (signedIn is null) return null;
        _masterSession = signedIn;
        RememberMasterSession(showNotification: true);
        return _masterSession;
    }

    private void ManageAccounts()
    {
        try
        {
            var password = RequestPasswordIfNeeded(_data.Settings.SharedMasterPath);
            if (PortableDataService.IsPasswordProtected(_data.Settings.SharedMasterPath) && password is null) return;
            var snapshot = SharedSyncService.Inspect(_data.Settings.SharedMasterPath, password);
            var session = EnsureSession(snapshot.Contents.Data.MasterAccess, password);
            if (session is null) return;
            using var accounts = new MasterUserManagementForm(
                snapshot.Contents.Data.MasterAccess,
                session,
                snapshot.Contents.Data.Clients);
            if (accounts.ShowDialog(this) != DialogResult.OK) return;
            SharedSyncService.SaveAccessControl(
                _data,
                _store,
                accounts.ResultAccess,
                session,
                initialSetup: false,
                password);
            _masterSession = MasterAccessService.RefreshSession(
                accounts.ResultAccess, session);
            RememberMasterSession(showNotification: false);
            RefreshMasterState();
        }
        catch (Exception exception)
        {
            ShowError("The master accounts could not be updated.", exception);
        }
    }

    private void CheckoutClient()
    {
        try
        {
            var password = RequestPasswordIfNeeded(_data.Settings.SharedMasterPath);
            if (PortableDataService.IsPasswordProtected(_data.Settings.SharedMasterPath) && password is null) return;
            var snapshot = SharedSyncService.Inspect(_data.Settings.SharedMasterPath, password);
            var session = EnsureSession(snapshot.Contents.Data.MasterAccess, password);
            if (session is null) return;
            MasterAccessService.RequireWrite(snapshot.Contents.Data.MasterAccess, session);
            var permittedClients = snapshot.Contents.Data.Clients
                .Where(client => MasterAccessService.CanAccessClient(
                    snapshot.Contents.Data.MasterAccess,
                    session,
                    client.Id))
                .ToList();
            using var selection = new ClientCheckoutSelectionForm(
                permittedClients,
                snapshot.Contents.Data.MasterAccess.Checkouts);
            if (selection.ShowDialog(this) != DialogResult.OK || selection.SelectedClientId is null) return;
            ClientCheckoutResult result;
            try
            {
                result = SharedSyncService.CheckoutClient(
                    _data, _store, selection.SelectedClientId.Value, session, force: false, password);
            }
            catch (ClientLockedException locked)
            {
                var holder = string.IsNullOrWhiteSpace(locked.Checkout.DisplayName)
                    ? locked.Checkout.Username
                    : locked.Checkout.DisplayName;
                if (MessageBox.Show(this,
                        $"{locked.ClientName} is checked out by {holder} on {locked.Checkout.MachineName}.\r\n\r\n" +
                        "Before booting them, ask the technician in person whether their changes have been pushed. " +
                        "Booting releases their lock immediately; any unpushed work remains only on their PC.\r\n\r\n" +
                        "Boot that checkout and continue?",
                        "Boot existing checkout",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                result = SharedSyncService.CheckoutClient(
                    _data, _store, selection.SelectedClientId.Value, session, force: true, password);
            }
            DataPulled = true;
            RefreshMasterState();
            MessageBox.Show(this,
                $"Checked out {result.ClientName}. Its configuration files are now available in each device's editor.\r\n\r\n" +
                $"Client sub-matrix:\r\n{result.SubmatrixLocation}\r\n\r\n" +
                $"Recovery copy of the previous local workspace:\r\n{result.RecoveryBackupPath}",
                result.BootedPreviousCheckout ? "Checkout booted and replaced" : "Client checked out",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("The client could not be checked out.", exception);
        }
    }

    private void CheckInClient()
    {
        try
        {
            var password = RequestPasswordIfNeeded(_data.Settings.SharedMasterPath);
            if (PortableDataService.IsPasswordProtected(_data.Settings.SharedMasterPath) && password is null) return;
            var snapshot = SharedSyncService.Inspect(_data.Settings.SharedMasterPath, password);
            var session = EnsureSession(snapshot.Contents.Data.MasterAccess, password);
            if (session is null) return;
            var clientName = _data.Clients.SingleOrDefault(client =>
                client.Id == _data.Settings.ActiveCheckoutClientId)?.Name ?? "this client";
            if (MessageBox.Show(this,
                    $"Push all changes and configuration files for {clientName}, then release its checkout?",
                    "Check in client",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var result = SharedSyncService.CheckInClient(_data, _store, session, password);
            DataPulled = true;
            RefreshMasterState();
            MessageBox.Show(this,
                $"{clientName} was checked in and its lock was released.\r\n\r\n" +
                $"Recovery copy:\r\n{result.RecoveryBackupPath}",
                "Client checked in", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("The client could not be checked in.", exception);
        }
    }

    private void ReleaseCheckout()
    {
        if (!_data.Settings.ActiveCheckoutClientId.HasValue) return;
        if (MessageBox.Show(this,
                "Release this checkout without pushing? Local client changes and downloaded configuration files " +
                "will be replaced by the current master inventory. A local recovery copy is not created by this action.",
                "Release checkout without pushing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            var password = RequestPasswordIfNeeded(_data.Settings.SharedMasterPath);
            if (PortableDataService.IsPasswordProtected(_data.Settings.SharedMasterPath) && password is null) return;
            var snapshot = SharedSyncService.Inspect(_data.Settings.SharedMasterPath, password);
            var session = EnsureSession(snapshot.Contents.Data.MasterAccess, password);
            if (session is null) return;
            SharedSyncService.ReleaseCheckout(_data, _store, session, password);
            DataPulled = true;
            RefreshMasterState();
        }
        catch (Exception exception)
        {
            ShowError("The checkout could not be released.", exception);
        }
    }

    private void RefreshMasterState(SharedMasterSnapshot? knownSnapshot = null)
    {
        var path = _data.Settings.SharedMasterPath;
        _path.Text = path;
        var linked = !string.IsNullOrWhiteSpace(path);
        var checkoutActive = _data.Settings.ActiveCheckoutClientId.HasValue;
        _pull.Enabled = linked && !checkoutActive;
        _push.Enabled = linked && !checkoutActive;
        _unlink.Enabled = linked;
        _signIn.Enabled = linked;
        _accounts.Enabled = linked;
        _accounts.Visible = _masterSession is not null;
        _checkout.Enabled = linked && !checkoutActive && (_masterSession?.CanWrite ?? true);
        _checkIn.Enabled = linked && checkoutActive;
        _releaseCheckout.Enabled = linked && checkoutActive;
        _signIn.Text = _masterSession is null ? "Sign in" : _masterSession.DisplayName;
        if (!linked)
        {
            _state.Text = "Not linked";
            _details.Text = "Link an existing master or create one from this PC's current data.";
            _guidance.Text = "For a file server, choose the shared or UNC path. " +
                             "For direct cloud access, choose Google Drive online above.";
            _guidance.ForeColor = UiTheme.Amber;
            return;
        }

        try
        {
            if (knownSnapshot is null && File.Exists(path) &&
                PortableDataService.IsPasswordProtected(path) && string.IsNullOrEmpty(_masterPassword))
            {
                _state.Text = "Master available — locked";
                _details.Text = "This shared master is password protected and encrypted as JWE.";
                _guidance.Text = "Pull or push to enter the password. It is never stored in the master file.";
                _guidance.ForeColor = UiTheme.Amber;
                return;
            }
            var snapshot = knownSnapshot ?? SharedSyncService.Inspect(path, _masterPassword);
            SharedSyncService.EnsureBaselineIfSafe(_data, _store, snapshot);
            var savedAt = snapshot.Contents.ExportedUtc == default
                ? "unknown time"
                : snapshot.Contents.ExportedUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
            var savedBy = string.IsNullOrWhiteSpace(snapshot.Contents.SavedBy)
                ? "an earlier revision"
                : snapshot.Contents.SavedBy;
            _state.Text = "Master available";
            _details.Text =
                $"{snapshot.Contents.ClientCount:N0} client(s)  •  " +
                $"{snapshot.Contents.EquipmentCount:N0} equipment record(s)  •  " +
                $"{snapshot.Contents.Data.MasterAccess.Checkouts.Count:N0} checked out\r\n" +
                $"Revision {ShortRevision(snapshot.Contents.RevisionId)}  •  Saved {savedAt} by {savedBy}" +
                (_data.Settings.ActiveCheckoutClientId.HasValue
                    ? $"\r\nClient checkout active as {_data.Settings.ActiveCheckoutUsername}"
                    : string.Empty);
            if (string.IsNullOrWhiteSpace(_data.Settings.SharedMasterFingerprint))
            {
                _guidance.Text = "Pull required before the first push from this PC.";
                _guidance.ForeColor = UiTheme.Amber;
            }
            else if (SharedSyncService.HasExternalChanges(_data, snapshot))
            {
                _guidance.Text = "The master has newer changes. Use Merge & push to combine them, " +
                                 "or Pull to replace this PC's local data.";
                _guidance.ForeColor = UiTheme.Amber;
            }
            else
            {
                var lastSync = _data.Settings.SharedMasterLastSyncUtc?.ToLocalTime()
                    .ToString("MMM d, yyyy h:mm tt") ?? "this session";
                _guidance.Text = $"This PC matches the last known master revision. Last sync: {lastSync}.";
                _guidance.ForeColor = UiTheme.Green;
            }
        }
        catch (Exception exception)
        {
            _state.Text = "Master unavailable";
            _details.Text = path;
            _guidance.Text = exception.Message;
            _guidance.ForeColor = UiTheme.Red;
        }
    }

    private void ShowError(string message, Exception exception)
    {
        MessageBox.Show(this,
            $"{message}\r\n\r\n{exception.Message}",
            "Shared master sync",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        RefreshMasterState();
    }

    private static string ShortRevision(string revision) =>
        string.IsNullOrWhiteSpace(revision)
            ? "legacy"
            : revision[..Math.Min(8, revision.Length)].ToUpperInvariant();

    private void OpenGoogleDriveOnline()
    {
        using var google = new GoogleDriveSyncForm(_data, _store);
        google.ShowDialog(this);
        if (google.DataPulled) DataPulled = true;
    }

    private void RememberMasterSession(bool showNotification)
    {
        if (_masterSession is null || string.IsNullOrWhiteSpace(_data.Settings.SharedMasterPath)) return;
        MasterSessionContext.Set(
            SyncTarget.SharedFile,
            _data.Settings.SharedMasterPath,
            _masterSession);
        if (showNotification) MasterSignInNotification.ShowFor(this, _masterSession);
    }

    private string? RequestPasswordIfNeeded(string path)
    {
        if (!string.IsNullOrWhiteSpace(_masterSession?.MasterKey))
            return _masterSession.MasterKey;
        if (!File.Exists(path) || !PortableDataService.IsPasswordProtected(path)) return null;
        if (!string.IsNullOrEmpty(_masterPassword)) return _masterPassword;
        var prompt = PasswordDialog.PromptForProtectedFile(this, allowRememberForSession: true);
        if (prompt is null) return null;
        if (prompt.RememberForSession) _masterPassword = prompt.Password;
        return prompt.Password;
    }
}
