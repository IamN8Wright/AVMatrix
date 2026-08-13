namespace AVMatrixStudio;

internal sealed class GoogleDriveSyncForm : Form
{
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly TextBox _clientId = new();
    private readonly TextBox _shareLink = new();
    private readonly Label _accountState = new();
    private readonly Label _fileState = new();
    private readonly Label _details = new();
    private readonly Label _protectionState = new();
    private readonly Button _connect = UiTheme.PrimaryButton("Sign in with Google");
    private readonly Button _link = UiTheme.SecondaryButton("Connect share link");
    private readonly Button _protection = UiTheme.SecondaryButton("File protection…");
    private readonly Button _pull = UiTheme.SecondaryButton("Pull from Google Drive");
    private readonly Button _push = UiTheme.PrimaryButton("Merge & push to Google Drive");
    private readonly Button _masterSignIn = UiTheme.SecondaryButton("Master sign in");
    private readonly Button _accounts = UiTheme.SecondaryButton("Accounts");
    private readonly Button _checkout = UiTheme.PrimaryButton("Check out client…");
    private readonly Button _checkIn = UiTheme.PrimaryButton("Check in & push");
    private readonly Button _releaseCheckout = UiTheme.DangerButton("Release");
    private string? _filePassword;
    private string? _operationPassword;
    private bool _forgetOperationPassword;
    private bool _busy;
    private MasterSession? _masterSession;
    private readonly bool _openAccountsOnShown;
    private readonly bool _connectionOnly;

    public bool DataPulled { get; private set; }

    public GoogleDriveSyncForm(
        AppData data,
        DataStore store,
        bool openAccountsOnShown = false,
        bool connectionOnly = false)
    {
        _data = data;
        _store = store;
        _openAccountsOnShown = openAccountsOnShown;
        _connectionOnly = connectionOnly;
        _masterSession = MasterSessionContext.Get(
            SyncTarget.GoogleDrive,
            _data.Settings.GoogleDriveFileId);
        _filePassword = _masterSession?.MasterKey;
        Text = "Google Drive online sync";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 760);
        Size = new Size(890, 790);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(28, 18, 28, 14)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        shell.Controls.Add(BuildHeading(), 0, 0);
        shell.Controls.Add(BuildOAuthPanel(), 0, 1);
        shell.Controls.Add(BuildFilePanel(), 0, 2);
        shell.Controls.Add(BuildStatusPanel(), 0, 3);
        shell.Controls.Add(BuildMasterActions(), 0, 4);
        shell.Controls.Add(BuildActions(), 0, 5);
        shell.Controls.Add(BuildFooter(), 0, 6);
        Controls.Add(shell);
        UiTheme.ApplyTheme(this);
        RefreshLocalState();
        Shown += async (_, _) =>
        {
            if (!_connectionOnly && GoogleDriveService.HasGoogleSignIn &&
                !string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId))
                await RefreshRemoteAsync(promptForPassword: false);
            if (_openAccountsOnShown) await ManageAccountsAsync();
        };
    }

    private Control BuildHeading()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Google Drive online sync",
            AutoSize = true,
            Font = UiTheme.Font(20, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = "Pull and push one shared .nasc master directly through the Google Drive API.",
            AutoSize = true,
            Font = UiTheme.Font(9.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 42)
        });
        return panel;
    }

    private Control BuildOAuthPanel()
    {
        var panel = Card();
        AddSectionLabel(panel, "GOOGLE SIGN-IN", 16);
        panel.Controls.Add(new Label
        {
            Text = "Import an OAuth Desktop client JSON from the InN8 Labs Google Cloud project, then sign in once.",
            AutoSize = true,
            Font = UiTheme.Font(8.8f),
            ForeColor = UiTheme.Text,
            Location = new Point(18, 39)
        });
        _clientId.ReadOnly = true;
        _clientId.PlaceholderText = "OAuth client is not configured";
        _clientId.Location = new Point(18, 68);
        _clientId.Size = new Size(475, 29);
        _clientId.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_clientId);
        var import = UiTheme.SecondaryButton("Import OAuth JSON…");
        import.AutoSize = false;
        import.Size = new Size(154, 34);
        import.Location = new Point(18, 105);
        import.Click += (_, _) => ImportOAuthClient();
        panel.Controls.Add(import);
        _connect.AutoSize = false;
        _connect.Size = new Size(154, 34);
        _connect.Location = new Point(182, 105);
        _connect.Click += async (_, _) => await ConnectGoogleAsync();
        panel.Controls.Add(_connect);
        var disconnect = UiTheme.SecondaryButton("Disconnect");
        disconnect.AutoSize = false;
        disconnect.Size = new Size(108, 34);
        disconnect.Location = new Point(346, 105);
        disconnect.Click += (_, _) => DisconnectGoogle();
        panel.Controls.Add(disconnect);
        _accountState.AutoSize = true;
        _accountState.Font = UiTheme.Font(8.8f, FontStyle.Bold);
        _accountState.Location = new Point(510, 74);
        panel.Controls.Add(_accountState);
        return panel;
    }

    private Control BuildFilePanel()
    {
        var panel = Card();
        AddSectionLabel(panel, "GOOGLE DRIVE MASTER", 16);
        _shareLink.PlaceholderText = "Paste the Google Drive share link to an existing .nasc file";
        _shareLink.Location = new Point(18, 42);
        _shareLink.Size = new Size(725, 29);
        _shareLink.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_shareLink);
        _link.AutoSize = false;
        _link.Size = new Size(144, 34);
        _link.Location = new Point(18, 82);
        _link.Click += async (_, _) => await LinkFileAsync();
        panel.Controls.Add(_link);
        var refresh = UiTheme.SecondaryButton("Refresh status");
        refresh.AutoSize = false;
        refresh.Size = new Size(124, 34);
        refresh.Location = new Point(172, 82);
        refresh.Click += async (_, _) => await RefreshRemoteAsync(promptForPassword: true);
        panel.Controls.Add(refresh);
        _protection.AutoSize = false;
        _protection.Size = new Size(132, 34);
        _protection.Location = new Point(306, 82);
        _protection.Click += (_, _) => ChooseFileProtection();
        panel.Controls.Add(_protection);
        _fileState.AutoSize = true;
        _fileState.Font = UiTheme.Font(8.8f, FontStyle.Bold);
        _fileState.Location = new Point(458, 89);
        panel.Controls.Add(_fileState);
        return panel;
    }

    private Control BuildStatusPanel()
    {
        var panel = Card();
        _details.AutoSize = false;
        _details.Location = new Point(22, 16);
        _details.Size = new Size(760, 72);
        _details.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _details.Font = UiTheme.Font(10);
        _details.ForeColor = UiTheme.Text;
        panel.Controls.Add(_details);
        _protectionState.AutoSize = false;
        _protectionState.Location = new Point(22, 94);
        _protectionState.Size = new Size(760, 36);
        _protectionState.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _protectionState.Font = UiTheme.Font(9, FontStyle.Bold);
        _protectionState.ForeColor = UiTheme.Amber;
        panel.Controls.Add(_protectionState);
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
            Location = new Point(0, 19),
            Size = new Size(760, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        foreach (var button in new[]
                 { _masterSignIn, _accounts, _checkout, _checkIn, _releaseCheckout })
        {
            button.AutoSize = false;
            button.Height = 34;
        }
        _masterSignIn.Width = 116;
        _accounts.Width = 92;
        _checkout.Width = 142;
        _checkIn.Width = 132;
        _releaseCheckout.Width = 86;
        _masterSignIn.Click += async (_, _) => await SignInToMasterAsync();
        _accounts.Click += async (_, _) => await ManageAccountsAsync();
        _checkout.Click += async (_, _) => await CheckoutClientAsync();
        _checkIn.Click += async (_, _) => await CheckInClientAsync();
        _releaseCheckout.Click += async (_, _) => await ReleaseCheckoutAsync();
        masterActions.Controls.AddRange(
            [_masterSignIn, _accounts, _checkout, _checkIn, _releaseCheckout]);
        _masterSignIn.Visible = false;
        _checkout.Visible = false;
        panel.Controls.Add(masterActions);
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 10, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _pull.Dock = DockStyle.Fill;
        _pull.Margin = new Padding(0, 0, 8, 0);
        _pull.Font = UiTheme.Font(11, FontStyle.Bold);
        _pull.Click += async (_, _) => await PullAsync();
        _push.Dock = DockStyle.Fill;
        _push.Margin = new Padding(8, 0, 0, 0);
        _push.Font = UiTheme.Font(11, FontStyle.Bold);
        _push.Click += async (_, _) => await PushAsync();
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
            Padding = new Padding(0, 8, 0, 0)
        };
        var close = UiTheme.SecondaryButton("Close");
        close.AutoSize = false;
        close.Size = new Size(84, 34);
        close.Click += (_, _) => Close();
        var unlink = UiTheme.DangerButton("Unlink file");
        unlink.AutoSize = false;
        unlink.Size = new Size(100, 34);
        unlink.Click += (_, _) => UnlinkFile();
        var local = UiTheme.SecondaryButton("Local / file share…");
        local.AutoSize = false;
        local.Size = new Size(146, 34);
        local.Click += (_, _) => OpenLocalFileShare();
        panel.Controls.Add(close);
        panel.Controls.Add(unlink);
        panel.Controls.Add(local);
        return panel;
    }

    private void OpenLocalFileShare()
    {
        using var local = new SharedSyncForm(_data, _store);
        local.ShowDialog(this);
        if (local.DataPulled) DataPulled = true;
        RefreshLocalState();
    }

    private static RoundedPanel Card() => new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 0, 0, 10),
        Padding = new Padding(18)
    };

    private static void AddSectionLabel(Control panel, string text, int top) =>
        panel.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(18, top)
        });

    private void ImportOAuthClient()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import Google OAuth Desktop client JSON",
            Filter = "Google OAuth client (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var configuration = GoogleDriveService.ReadOAuthClientFile(dialog.FileName);
            GoogleDriveService.ConfigureClient(configuration);
            _data.Settings.GoogleDriveOAuthClientId = configuration.ClientId;
            _store.Save(_data);
            RefreshLocalState();
            _details.Text = "OAuth Desktop client imported. Click Sign in with Google to authorize this app.";
        }
        catch (Exception exception)
        {
            ShowError("The OAuth client file could not be imported.", exception);
        }
    }

    private async Task ConnectGoogleAsync()
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            await GoogleDriveService.ConnectAsync(_data.Settings);
            RefreshLocalState();
            _details.Text = "Google sign-in completed. Paste and connect the shared .nasc link.";
        }, "Google sign-in could not be completed.");
    }

    private void DisconnectGoogle()
    {
        if (MessageBox.Show(this,
                "Disconnect Google Drive on this PC? The linked file will not be deleted.",
                "Disconnect Google Drive", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            GoogleDriveService.Disconnect();
            MasterSessionContext.Clear(SyncTarget.GoogleDrive, _data.Settings.GoogleDriveFileId);
            _masterSession = null;
            RefreshLocalState();
        }
        catch (Exception exception)
        {
            ShowError("Google Drive could not be disconnected.", exception);
        }
    }

    private async Task LinkFileAsync()
    {
        if (_busy) return;
        if (!_connectionOnly && MasterSessionContext.Current is not null)
        {
            MessageBox.Show(this,
                "Log out first, then connect the other Google Drive Master from the welcome screen.",
                "Switch Master Matrix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Capture the user's text before RunBusyAsync refreshes the form state.
        var shareLink = _shareLink.Text.Trim();
        try
        {
            var fileId = GoogleDriveService.ParseFileId(shareLink);
            if (!string.Equals(fileId, _data.Settings.GoogleDriveFileId, StringComparison.Ordinal))
            {
                MasterSessionContext.Clear(SyncTarget.GoogleDrive, _data.Settings.GoogleDriveFileId);
                _masterSession = null;
            }
        }
        catch (Exception exception)
        {
            ShowError("The Google Drive share link could not be connected.", exception);
            _shareLink.Focus();
            return;
        }

        await RunBusyAsync(async () =>
        {
            var metadata = await GoogleDriveSyncService.LinkAsync(shareLink, _data, _store);
            _fileState.Text = "Linked";
            _fileState.ForeColor = UiTheme.Green;
            _details.Text = $"Linked to {metadata.Name}. Pull before the first push from this PC.";
            if (_connectionOnly)
                _details.Text = $"Linked to {metadata.Name}. Close this window and sign in with your Master Matrix account.";
            else
                await RefreshRemoteAsync(promptForPassword: true);
        }, "The Google Drive share link could not be connected.");
    }

    private async Task RefreshRemoteAsync(bool promptForPassword)
    {
        if (_busy || string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId)) return;
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(promptForPassword);
            if (snapshot is null) return;
            ShowSnapshot(snapshot);
        }, "The Google Drive master could not be read.");
    }

    private async Task PullAsync()
    {
        if (_busy) return;
        if (_data.Settings.ActiveCheckoutClientId.HasValue)
        {
            MessageBox.Show(this,
                "Check in and push, or release the current client checkout before pulling the full master.",
                "Client is checked out", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(prompt: true);
            if (snapshot is null) return;
            var session = await EnsureMasterSessionAsync(snapshot, forcePrompt: false);
            if (session is null && snapshot.Contents.Data.MasterAccess.IsConfigured) return;
            var savedBy = string.IsNullOrWhiteSpace(snapshot.Contents.SavedBy)
                ? "an earlier revision"
                : snapshot.Contents.SavedBy;
            if (MessageBox.Show(this,
                    $"Pull {snapshot.Contents.ClientCount:N0} client(s) and " +
                    $"{snapshot.Contents.EquipmentCount:N0} equipment record(s) saved by {savedBy}?\r\n\r\n" +
                    "This replaces local client data. A recovery backup will be created first.",
                    "Confirm Google Drive pull", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var result = GoogleDriveSyncService.Pull(
                _data, _store, snapshot, _operationPassword ?? _filePassword, session);
            DataPulled = true;
            ShowSnapshot(snapshot);
            MessageBox.Show(this,
                $"Google Drive pull completed.\r\n\r\nRecovery copy:\r\n{result.RecoveryBackupPath}",
                "Pull complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }, "The Google Drive master could not be pulled.");
    }

    private async Task PushAsync()
    {
        if (_busy) return;
        if (_data.Settings.ActiveCheckoutClientId.HasValue)
        {
            MessageBox.Show(this,
                "Use Check in & push while working in a checked-out client sub-matrix.",
                "Client checkout active", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(prompt: true);
            if (snapshot is null) return;
            var session = await EnsureMasterSessionAsync(snapshot, forcePrompt: false);
            if (session is null && snapshot.Contents.Data.MasterAccess.IsConfigured) return;
            var localEquipment = _data.Clients.Sum(client => client.Locations.Sum(location =>
                location.Rooms.Sum(room => room.Equipment.Count)));
            if (MessageBox.Show(this,
                    $"Merge this PC's {_data.Clients.Count:N0} client(s) and " +
                    $"{localEquipment:N0} equipment record(s) into {snapshot.Metadata.Name}?\r\n\r\n" +
                    "Independent changes are combined automatically. If the same field was changed " +
                    "differently, you will be asked which value to keep.",
                    "Confirm Google Drive merge", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            GoogleDriveSyncResult result;
            try
            {
                result = await GoogleDriveSyncService.PushAsync(
                    _data, _store, _operationPassword ?? _filePassword,
                    session: session);
            }
            catch (MergeResolutionRequiredException conflict)
            {
                using var resolver = new MergeConflictForm(conflict.Conflicts);
                if (resolver.ShowDialog(this) != DialogResult.OK || resolver.Preference is null) return;
                result = await GoogleDriveSyncService.PushAsync(
                    _data,
                    _store,
                    _operationPassword ?? _filePassword,
                    resolver.Preference.Value,
                    session);
            }
            if (result.Action == "Merged") DataPulled = true;
            _details.Text =
                $"{(result.Action == "Merged" ? "Merged and pushed" : "Pushed")} revision " +
                $"{ShortRevision(result.RevisionId)} to {result.Metadata.Name}.";
            _fileState.Text = "Up to date";
            _fileState.ForeColor = UiTheme.Green;
        }, "The local data could not be pushed to Google Drive.");
    }

    private async Task<GoogleDriveSnapshot?> InspectWithPasswordAsync(bool prompt)
    {
        if (!string.IsNullOrWhiteSpace(_masterSession?.MasterKey))
            _filePassword = _masterSession.MasterKey;
        try
        {
            return await GoogleDriveSyncService.InspectAsync(_data, _filePassword);
        }
        catch (Exception exception) when (prompt &&
            exception is PasswordRequiredException or PasswordProtectionException)
        {
            var password = PasswordDialog.PromptForProtectedFile(this, allowRememberForSession: true);
            if (password is null) return null;
            var snapshot = await GoogleDriveSyncService.InspectAsync(_data, password.Password);
            _operationPassword = password.Password;
            _forgetOperationPassword = !password.RememberForSession;
            if (password.RememberForSession) _filePassword = password.Password;
            _protectionState.Text = "Password-protected JWE master. Password is held only for this app session.";
            return snapshot;
        }
    }

    private async Task<MasterSession?> EnsureMasterSessionAsync(
        GoogleDriveSnapshot snapshot,
        bool forcePrompt)
    {
        var access = snapshot.Contents.Data.MasterAccess;
        if (!access.IsConfigured)
        {
            var owner = MasterOwnerSetupForm.Prompt(this);
            if (owner is null) return null;
            await GoogleDriveSyncService.SaveAccessControlAsync(
                _data,
                _store,
                owner.Access,
                session: owner.Session,
                initialSetup: true,
                owner.Session.MasterKey);
            _masterSession = owner.Session;
            _filePassword = owner.Session.MasterKey;
            RememberMasterSession(showNotification: true);
            MessageBox.Show(this,
                "The first Owner account was added to this Google Drive master.",
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
                MasterSessionContext.Clear(SyncTarget.GoogleDrive, _data.Settings.GoogleDriveFileId);
                _masterSession = null;
            }
        }
        var signedIn = MasterSignInForm.Prompt(this, access);
        if (signedIn is null) return null;
        _masterSession = signedIn;
        RememberMasterSession(showNotification: true);
        return _masterSession;
    }

    private async Task SignInToMasterAsync()
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(prompt: true);
            if (snapshot is null) return;
            await EnsureMasterSessionAsync(snapshot, forcePrompt: true);
        }, "The Master Matrix sign-in could not be completed.");
    }

    private async Task ManageAccountsAsync()
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(prompt: true);
            if (snapshot is null) return;
            var session = await EnsureMasterSessionAsync(snapshot, forcePrompt: false);
            if (session is null) return;
            using var accounts = new MasterUserManagementForm(
                snapshot.Contents.Data.MasterAccess,
                session,
                snapshot.Contents.Data.Clients);
            if (accounts.ShowDialog(this) != DialogResult.OK) return;
            await GoogleDriveSyncService.SaveAccessControlAsync(
                _data,
                _store,
                accounts.ResultAccess,
                session,
                initialSetup: false,
                _operationPassword ?? _filePassword);
            _masterSession = MasterAccessService.RefreshSession(
                accounts.ResultAccess, session);
            RememberMasterSession(showNotification: false);
        }, "The master accounts could not be updated.");
    }

    private async Task CheckoutClientAsync()
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(prompt: true);
            if (snapshot is null) return;
            var session = await EnsureMasterSessionAsync(snapshot, forcePrompt: false);
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
                result = await GoogleDriveSyncService.CheckoutClientAsync(
                    _data,
                    _store,
                    selection.SelectedClientId.Value,
                    session,
                    force: false,
                    _operationPassword ?? _filePassword);
            }
            catch (ClientLockedException locked)
            {
                var holder = string.IsNullOrWhiteSpace(locked.Checkout.DisplayName)
                    ? locked.Checkout.Username
                    : locked.Checkout.DisplayName;
                if (MessageBox.Show(this,
                        $"{locked.ClientName} is checked out by {holder} on {locked.Checkout.MachineName}.\r\n\r\n" +
                        "Before booting them, ask the technician in person whether their changes have been pushed. " +
                        "Booting releases their lock immediately; unpushed work remains only on their PC.\r\n\r\n" +
                        "Boot that checkout and continue?",
                        "Boot existing checkout",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                result = await GoogleDriveSyncService.CheckoutClientAsync(
                    _data,
                    _store,
                    selection.SelectedClientId.Value,
                    session,
                    force: true,
                    _operationPassword ?? _filePassword);
            }
            DataPulled = true;
            _details.Text = $"Checked out {result.ClientName}.\r\n{result.SubmatrixLocation}";
            MessageBox.Show(this,
                $"Checked out {result.ClientName}. Its configuration files are now available in each device's editor.\r\n\r\n" +
                $"Sub-matrix: {result.SubmatrixLocation}\r\n\r\n" +
                $"Local recovery copy:\r\n{result.RecoveryBackupPath}",
                result.BootedPreviousCheckout ? "Checkout booted and replaced" : "Client checked out",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }, "The Google Drive client could not be checked out.");
    }

    private async Task CheckInClientAsync()
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(prompt: true);
            if (snapshot is null) return;
            var session = await EnsureMasterSessionAsync(snapshot, forcePrompt: false);
            if (session is null) return;
            var clientName = _data.Clients.SingleOrDefault(client =>
                client.Id == _data.Settings.ActiveCheckoutClientId)?.Name ?? "this client";
            if (MessageBox.Show(this,
                    $"Push all changes and configuration files for {clientName}, then release its checkout?",
                    "Check in Google Drive client",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var result = await GoogleDriveSyncService.CheckInClientAsync(
                _data, _store, session, _operationPassword ?? _filePassword);
            DataPulled = true;
            _details.Text = $"{clientName} was checked in and its lock was released.";
            MessageBox.Show(this,
                $"{clientName} was checked in.\r\n\r\nRecovery copy:\r\n{result.RecoveryBackupPath}",
                "Client checked in", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }, "The Google Drive client could not be checked in.");
    }

    private async Task ReleaseCheckoutAsync()
    {
        if (_busy || !_data.Settings.ActiveCheckoutClientId.HasValue) return;
        if (MessageBox.Show(this,
                "Release this checkout without pushing? Local client changes and downloaded configuration files " +
                "will be replaced by the current master inventory.",
                "Release checkout without pushing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        await RunBusyAsync(async () =>
        {
            var snapshot = await InspectWithPasswordAsync(prompt: true);
            if (snapshot is null) return;
            var session = await EnsureMasterSessionAsync(snapshot, forcePrompt: false);
            if (session is null) return;
            await GoogleDriveSyncService.ReleaseCheckoutAsync(
                _data, _store, session, _operationPassword ?? _filePassword);
            DataPulled = true;
        }, "The Google Drive checkout could not be released.");
    }

    private void ChooseFileProtection()
    {
        if (_data.MasterAccess.ClientSubmatrices.Count > 0)
        {
            MessageBox.Show(this,
                "This master already has client sub-matrices. Keep its current file-protection password so " +
                "those configuration-file packages remain unlockable. A future password-rotation workflow " +
                "can re-encrypt every sub-matrix together.",
                "File protection is locked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        var protection = PasswordDialog.PromptForNewFile(this);
        if (protection is null) return;
        _filePassword = protection.Password;
        _protectionState.Text = _filePassword is null
            ? "The next push will store an unprotected .nasc file."
            : "The next push will password-protect and encrypt the master as compact JWE.";
        _protectionState.ForeColor = _filePassword is null ? UiTheme.Amber : UiTheme.Green;
    }

    private void UnlinkFile()
    {
        if (string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId)) return;
        if (_data.Settings.ActiveCheckoutClientId.HasValue)
        {
            MessageBox.Show(this,
                "Check in and push, or release the active client checkout before unlinking Google Drive.",
                "Client checkout active", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                "Unlink this Google Drive master? The Drive file and Google sign-in will remain.",
                "Unlink Drive file", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var masterKey = _data.Settings.GoogleDriveFileId;
        GoogleDriveSyncService.Unlink(_data, _store);
        MasterSessionContext.Clear(SyncTarget.GoogleDrive, masterKey);
        _masterSession = null;
        _filePassword = null;
        RefreshLocalState();
    }

    private void RefreshLocalState(bool updateInputFields = true)
    {
        if (updateInputFields)
        {
            _clientId.Text = _data.Settings.GoogleDriveOAuthClientId;
            _shareLink.Text = _data.Settings.GoogleDriveShareLink;
        }
        var configured = GoogleDriveService.HasConfiguredClient(_data.Settings);
        var signedIn = configured && GoogleDriveService.HasGoogleSignIn;
        _accountState.Text = signedIn ? "●  Connected" : configured ? "●  Ready to sign in" : "●  Setup required";
        _accountState.ForeColor = signedIn ? UiTheme.Green : UiTheme.Amber;
        var linked = !string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId);
        var checkoutActive = _data.Settings.ActiveCheckoutClientId.HasValue;
        var googleCheckoutActive = checkoutActive &&
            _data.Settings.ActiveCheckoutTarget == nameof(SyncTarget.GoogleDrive);
        _fileState.Text = linked ? "Linked" : "Not linked";
        _fileState.ForeColor = linked ? UiTheme.Green : UiTheme.Muted;
        _pull.Enabled = signedIn && linked && !_busy && !checkoutActive;
        _push.Enabled = signedIn && linked && !_busy && !checkoutActive;
        _connect.Enabled = configured && !_busy;
        _link.Enabled = signedIn && !_busy;
        _protection.Enabled = false;
        _protection.Visible = false;
        _masterSignIn.Enabled = signedIn && linked && !_busy;
        _accounts.Enabled = signedIn && linked && !_busy;
        _accounts.Visible = _masterSession is not null && !_connectionOnly;
        _checkout.Enabled = false;
        _checkIn.Enabled = signedIn && linked && !_busy && googleCheckoutActive;
        _releaseCheckout.Enabled = signedIn && linked && !_busy && googleCheckoutActive;
        _masterSignIn.Text = _masterSession is null ? "Master sign in" : _masterSession.DisplayName;
        if (_connectionOnly)
        {
            _pull.Enabled = false;
            _push.Enabled = false;
            _masterSignIn.Visible = false;
            _accounts.Visible = false;
            _checkIn.Visible = false;
            _releaseCheckout.Visible = false;
        }
        if (!configured)
            _details.Text = "Google requires an OAuth Desktop client for direct online access. " +
                            "Import the client JSON supplied for the InN8 Labs Google Cloud project.";
        else if (!linked)
            _details.Text = signedIn
                ? "Paste the share link for an existing .nasc file and click Connect share link."
                : "Click Sign in with Google, then connect the shared .nasc link.";
    }

    private void ShowSnapshot(GoogleDriveSnapshot snapshot)
    {
        _data.MasterAccess = MasterAccessService.Clone(snapshot.Contents.Data.MasterAccess);
        GoogleDriveSyncService.EnsureBaselineIfSafe(_data, _store, snapshot);
        var savedAt = snapshot.Contents.ExportedUtc == default
            ? "unknown time"
            : snapshot.Contents.ExportedUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        var savedBy = string.IsNullOrWhiteSpace(snapshot.Contents.SavedBy)
            ? "an earlier revision"
            : snapshot.Contents.SavedBy;
        var hasExternalChanges = GoogleDriveSyncService.HasExternalChanges(_data, snapshot);
        _data.Settings.GoogleDriveRemoteChangesDetected = hasExternalChanges;
        _store.Save(_data);
        _fileState.Text = hasExternalChanges
            ? "Newer file available"
            : string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFingerprint)
                ? "Pull required"
                : "Up to date";
        _fileState.ForeColor = _fileState.Text == "Up to date" ? UiTheme.Green : UiTheme.Amber;
        _details.Text =
            $"{snapshot.Metadata.Name}\r\n" +
            $"{snapshot.Contents.ClientCount:N0} client(s)  •  " +
            $"{snapshot.Contents.EquipmentCount:N0} equipment record(s)  •  " +
            $"{snapshot.Contents.Data.MasterAccess.Checkouts.Count:N0} checked out\r\n" +
            $"Revision {ShortRevision(snapshot.Contents.RevisionId)}  •  Saved {savedAt} by {savedBy}" +
            (_data.Settings.ActiveCheckoutTarget == nameof(SyncTarget.GoogleDrive)
                ? $"\r\nClient checkout active as {_data.Settings.ActiveCheckoutUsername}"
                : string.Empty);
        _protectionState.Text = snapshot.Contents.PasswordProtected
            ? "Password-protected and encrypted as compact JWE."
            : "This Drive master is not password protected. Use File protection before a push to protect it.";
        _protectionState.ForeColor = snapshot.Contents.PasswordProtected ? UiTheme.Green : UiTheme.Amber;
        _protection.Enabled = snapshot.Contents.Data.MasterAccess.ClientSubmatrices.Count == 0;
    }

    private async Task RunBusyAsync(Func<Task> operation, string errorMessage)
    {
        _busy = true;
        UseWaitCursor = true;
        RefreshLocalState(updateInputFields: false);
        try
        {
            await operation();
        }
        catch (SharedMasterConflictException exception)
        {
            MessageBox.Show(this, exception.Message + "\r\n\r\nNo Google Drive data was overwritten.",
                "Newer master detected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            ShowError(errorMessage, exception);
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            if (_forgetOperationPassword)
            {
                _operationPassword = null;
                _forgetOperationPassword = false;
            }
            RefreshLocalState(updateInputFields: false);
        }
    }

    private void RememberMasterSession(bool showNotification)
    {
        if (_masterSession is null || string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId)) return;
        MasterSessionContext.Set(
            SyncTarget.GoogleDrive,
            _data.Settings.GoogleDriveFileId,
            _masterSession);
        if (showNotification) MasterSignInNotification.ShowFor(this, _masterSession);
    }

    private void ShowError(string message, Exception exception) =>
        MessageBox.Show(this, $"{message}\r\n\r\n{exception.Message}",
            "Google Drive online sync", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static string ShortRevision(string revision) =>
        string.IsNullOrWhiteSpace(revision)
            ? "legacy"
            : revision[..Math.Min(8, revision.Length)].ToUpperInvariant();
}
