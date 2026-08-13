namespace InNasc;

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
    private readonly Button _protection = UiTheme.SecondaryButton("File protection‚Ä¶");
    private readonly Button _pull = UiTheme.SecondaryButton("Pull from Google Drive");
    private readonly Button _push = UiTheme.PrimaryButton("Merge & push to Google Drive");
    private readonly Button _masterSignIn = UiTheme.SecondaryButton("Master sign in");
    private readonly Button _accounts = UiTheme.SecondaryButton("Accounts");
    private readonly Button _checkout = UiTheme.PrimaryButton("Check out client‚Ä¶");
    private readonly Button _checkIn = UiTheme.PrimaryButton("Check in & push");
    private readonly Button _releaseCheckout = UiTheme.DangerButton("Release");
    private string? _filePassword;
    private string? _operationPassword;
    private bool _forgetOperationPassword;
    private bool _busy;
    private MasterSession? _masterSession;
    private MasterSession? _legacyDriveSession;
    private readonly MasterSession? _companySession;
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
        _companySession = MasterSessionContext.Get(
            SyncTarget.GoogleDrive,
            _data.Settings.GoogleDriveFileId);
        _masterSession = _companySession;
        _filePassword = _companySession?.MasterKey;
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
        var import = UiTheme.SecondaryButton("Import OAuth JSON‚Ä¶");
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
        var local = UiTheme.SecondaryButton("Local / file share‚Ä¶");
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
            _masterSession = _companySession;
            _legacyDriveSession = null;
            _operationPassword = null;
            _filePassword = _companySession?.MasterKey;
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
        if (!_connectionOnly &&
            MasterSessionContext.Current is not null &&
            InNascGlobalSessionContext.Current is null)
        {
            MessageBox.Show(this,
                "Log out first, then connect the other Google Drive Master from the welcome screen.",
                "Switch company workspace",
                MessageBox.Buttons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var shareLink = _shareLink.Text.Trim();
        try
        {
            var fileId = GoogleDriveService.ParseFileId(shareLink);
            if (!string.Equals(fileId, _data.Settings.GoogleDriveFileId, StringComparison.Ordinal))
            {
                MasterSessionContext.Clear(SyncTarget.GoogleDrive, _data.Settings.GoogleDriveFileId);
                _masterSession = _companySession;
                _legacyDriveSession = null;
                _operationPassword = null;
                _filePassword = _companySession?.MasterKey;
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
                _details.Text = $"Linked to {metadata.Name}. Close this window and sign in with your InNasc account.";
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

            MasterSession? session;
            if (_legacyDriveSession is not null)
            {
                session = RequireCompanySession();
                if (MessageBox.Show(this,
                        "This Google Drive company still uses the old AV Matrix account protection.\r\n\r\n" +
                        "InNasc must migrate it to the current company key before it can be pulled. " +
                        "The Drive data itself is preserved, old client payloads are re-keyed, and recovery copies are saved locally.\r\n\r\n" +
                        "Migrate the Drive company now and then continue the pull?",
                        "Migrate legacy Google Drive company",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                snapshot = await MigrateLegacyDriveAsync(snapshot, session);
            }
            else
            {
                session = await EnsureMasterSessionAsync(snapshot, forcePrompt: false);
                if (session is null && snapshot.Contents.Data.MasterAccess.IsConfigured) return;
            }

            var savedBy = string.IsNullOrWhiteSpace(snapshot.Contents.SavedBy)
                ? "an earlier revision"
                : snapshot.Contents.SavedBy;
            if (MessageBox.Show(this,
                    $"[‹€ò\⁄›ê€€ù[ùÀê€Y[ù€›[ùìåH€Y[ù
 H[ôà
¬à	