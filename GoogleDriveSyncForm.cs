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
    private readonly Button _protection = UiTheme.SecondaryButton("File protectionâ€¦");
    private readonly Button _pull = UiTheme.SecondaryButton("Pull from Google Drive");
    private readonly Button _push = UiTheme.PrimaryButton("Merge & push to Google Drive");
    private readonly Button _masterSignIn = UiTheme.SecondaryButton("Master sign in");
    private readonly Button _accounts = UiTheme.SecondaryButton("Accounts");
    private readonly Button _checkout = UiTheme.PrimaryButton("Check out clientâ€¦");
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
        var import = UiTheme.SecondaryButton("Import OAuth JSONâ€¦");
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
        var local = UiTheme.SecondaryButton("Local / file shareâ€¦");
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
        ßÎ¹¶‰žËkºwµçY¥…Ñ¥½¸è™…±Í”¤ì4(€€€€€€€ô°€‰Q¡”µ…ÍÑ•È…½Õ¹ÑÌ½Õ±¹½Ð‰”ÕÁ‘…Ñ•¸ˆ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬¡•­½ÕÑ±¥•¹ÑÍå¹Œ ¤4(€€€ì4(€€€€€€€¥˜€¡}‰ÕÍä¤É•ÑÕÉ¸ì4(€€€€€€€…Ý…¥ÐIÕ¹	ÕÍåÍå¹Œ¡…Íå¹Œ€ ¤€ôø4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ô…Ý…¥Ð%¹ÍÁ•Ñ]¥Ñ¡A…ÍÍÝ½É‘Íå¹Œ¡ÁÉ½µÁÐèÑÉÕ”¤ì4(€€€€€€€€€€€¥˜€¡Í¹…ÁÍ¡½Ð¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÍ•ÍÍ¥½¸€ô…Ý…¥Ð¹ÍÕÉ•5…ÍÑ•ÉM•ÍÍ¥½¹Íå¹Œ¡Í¹…ÁÍ¡½Ð°™½É•AÉ½µÁÐè™…±Í”¤ì4(€€€€€€€€€€€¥˜€¡Í•ÍÍ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€5…ÍÑ•É•ÍÍM•ÉÙ¥”¹I•ÅÕ¥É•]É¥Ñ”¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°Í•ÍÍ¥½¸¤ì4(€€€€€€€€€€€Ù…ÈÁ•Éµ¥ÑÑ•‘±¥•¹ÑÌ€ôÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹±¥•¹ÑÌ4(€€€€€€€€€€€€€€€€¹]¡•É”¡±¥•¹Ð€ôø5…ÍÑ•É•ÍÍM•ÉÙ¥”¹…¹•ÍÍ±¥•¹Ð 4(€€€€€€€€€€€€€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°4(€€€€€€€€€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€€€€€€€€€±¥•¹Ð¹%¤¤4(€€€€€€€€€€€€€€€€¹Q½1¥ÍÐ ¤ì4(€€€€€€€€€€€ÕÍ¥¹œÙ…ÈÍ•±•Ñ¥½¸€ô¹•Ü±¥•¹Ñ¡•­½ÕÑM•±•Ñ¥½¹½É´ 4(€€€€€€€€€€€€€€€Á•Éµ¥ÑÑ•‘±¥•¹ÑÌ°4(€€€€€€€€€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ¹¡•­½ÕÑÌ¤ì4(€€€€€€€€€€€¥˜€¡Í•±•Ñ¥½¸¹M¡½Ý¥…±½œ¡Ñ¡¥Ì¤€„ô¥…±½I•ÍÕ±Ð¹=,ñðÍ•±•Ñ¥½¸¹M•±•Ñ•‘±¥•¹Ñ%¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€±¥•¹Ñ¡•­½ÕÑI•ÍÕ±ÐÉ•ÍÕ±Ðì4(€€€€€€€€€€€ÑÉä4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É•ÍÕ±Ð€ô…Ý…¥Ð½½±•É¥Ù•Må¹M•ÉÙ¥”¹¡•­½ÕÑ±¥•¹ÑÍå¹Œ 4(€€€€€€€€€€€€€€€€€€€}‘…Ñ„°4(€€€€€€€€€€€€€€€€€€€}ÍÑ½É”°4(€€€€€€€€€€€€€€€€€€€Í•±•Ñ¥½¸¹M•±•Ñ•‘±¥•¹Ñ%¹Y…±Õ”°4(€€€€€€€€€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€€€€€€€€€™½É”è™…±Í”°4(€€€€€€€€€€€€€€€€€€€}½Á•É…Ñ¥½¹A…ÍÍÝ½É€üü}™¥±•A…ÍÍÝ½É¤ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€…Ñ €¡±¥•¹Ñ1½­•‘á•ÁÑ¥½¸±½­•¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…È¡½±‘•È€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡±½­•¹¡•­½ÕÐ¹¥ÍÁ±…å9…µ”¤4(€€€€€€€€€€€€€€€€€€€€ü±½­•¹¡•­½ÕÐ¹UÍ•É¹…µ”4(€€€€€€€€€€€€€€€€€€€€è±½­•¹¡•­½ÕÐ¹¥ÍÁ±…å9…µ”ì4(€€€€€€€€€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€€€€€€€€€‰í±½­•¹±¥•¹Ñ9…µ•ô¥Ì¡•­•½ÕÐ‰äí¡½±‘•Éô½¸í±½­•¹¡•­½ÕÐ¹5…¡¥¹•9…µ•ô¹qÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€‰	•™½É”‰½½Ñ¥¹œÑ¡•´°…Í¬Ñ¡”Ñ•¡¹¥¥…¸¥¸Á•ÉÍ½¸Ý¡•Ñ¡•ÈÑ¡•¥È¡…¹•Ì¡…Ù”‰••¸ÁÕÍ¡•¸€ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€‰	½½Ñ¥¹œÉ•±•…Í•ÌÑ¡•¥È±½¬¥µµ•‘¥…Ñ•±äìÕ¹ÁÕÍ¡•Ý½É¬É•µ…¥¹Ì½¹±ä½¸Ñ¡•¥ÈA¹qÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€‰	½½ÐÑ¡…Ð¡•­½ÕÐ…¹½¹Ñ¥¹Õ”üˆ°4(€€€€€€€€€€€€€€€€€€€€€€€€‰	½½Ð•á¥ÍÑ¥¹œ¡•­½ÕÐˆ°4(€€€€€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°4(€€€€€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹]…É¹¥¹œ°4(€€€€€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤4(€€€€€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€€€€€É•ÍÕ±Ð€ô…Ý…¥Ð½½±•É¥Ù•Må¹M•ÉÙ¥”¹¡•­½ÕÑ±¥•¹ÑÍå¹Œ 4(€€€€€€€€€€€€€€€€€€€}‘…Ñ„°4(€€€€€€€€€€€€€€€€€€€}ÍÑ½É”°4(€€€€€€€€€€€€€€€€€€€Í•±•Ñ¥½¸¹M•±•Ñ•‘±¥•¹Ñ%¹Y…±Õ”°4(€€€€€€€€€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€€€€€€€€€™½É”èÑÉÕ”°4(€€€€€€€€€€€€€€€€€€€}½Á•É…Ñ¥½¹A…ÍÍÝ½É€üü}™¥±•A…ÍÍÝ½É¤ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€…Ñ…AÕ±±•€ôÑÉÕ”ì4(€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ô€‰¡•­•½ÕÐíÉ•ÍÕ±Ð¹±¥•¹Ñ9…µ•ô¹qÉq¹íÉ•ÍÕ±Ð¹MÕ‰µ…ÑÉ¥á1½…Ñ¥½¹ôˆì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰¡•­•½ÕÐíÉ•ÍÕ±Ð¹±¥•¹Ñ9…µ•ô¸%ÑÌ½¹™¥ÕÉ…Ñ¥½¸™¥±•Ì…É”¹½Ü…Ù…¥±…‰±”¥¸•… ‘•Ù¥”Ì•‘¥Ñ½È¹qÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€‰MÕˆµµ…ÑÉ¥àèíÉ•ÍÕ±Ð¹MÕ‰µ…ÑÉ¥á1½…Ñ¥½¹õqÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€‰1½…°É•½Ù•Éä½ÁäéqÉq¹íÉ•ÍÕ±Ð¹I•½Ù•Éå	…­ÕÁA…Ñ¡ôˆ°4(€€€€€€€€€€€€€€€É•ÍÕ±Ð¹	½½Ñ•‘AÉ•Ù¥½ÕÍ¡•­½ÕÐ€ü€‰¡•­½ÕÐ‰½½Ñ•…¹É•Á±…•ˆ€è€‰±¥•¹Ð¡•­•½ÕÐˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€ô°€‰Q¡”½½±”É¥Ù”±¥•¹Ð½Õ±¹½Ð‰”¡•­•½ÕÐ¸ˆ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬¡•­%¹±¥•¹ÑÍå¹Œ ¤4(€€€ì4(€€€€€€€¥˜€¡}‰ÕÍä¤É•ÑÕÉ¸ì4(€€€€€€€…Ý…¥ÐIÕ¹	ÕÍåÍå¹Œ¡…Íå¹Œ€ ¤€ôø4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ô…Ý…¥Ð%¹ÍÁ•Ñ]¥Ñ¡A…ÍÍÝ½É‘Íå¹Œ¡ÁÉ½µÁÐèÑÉÕ”¤ì4(€€€€€€€€€€€¥˜€¡Í¹…ÁÍ¡½Ð¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÍ•ÍÍ¥½¸€ô…Ý…¥Ð¹ÍÕÉ•5…ÍÑ•ÉM•ÍÍ¥½¹Íå¹Œ¡Í¹…ÁÍ¡½Ð°™½É•AÉ½µÁÐè™…±Í”¤ì4(€€€€€€€€€€€¥˜€¡Í•ÍÍ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…È±¥•¹Ñ9…µ”€ô}‘…Ñ„¹±¥•¹ÑÌ¹M¥¹±•=É•™…Õ±Ð¡±¥•¹Ð€ôø4(€€€€€€€€€€€€€€€±¥•¹Ð¹%€ôô}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¤ü¹9…µ”€üü€‰Ñ¡¥Ì±¥•¹Ðˆì4(€€€€€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€€€€€‰AÕÍ …±°¡…¹•Ì…¹½¹™¥ÕÉ…Ñ¥½¸™¥±•Ì™½Èí±¥•¹Ñ9…µ•ô°Ñ¡•¸É•±•…Í”¥ÑÌ¡•­½ÕÐüˆ°4(€€€€€€€€€€€€€€€€€€€€‰¡•¬¥¸½½±”É¥Ù”±¥•¹Ðˆ°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹EÕ•ÍÑ¥½¸°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤4(€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÉ•ÍÕ±Ð€ô…Ý…¥Ð½½±•É¥Ù•Må¹M•ÉÙ¥”¹¡•­%¹±¥•¹ÑÍå¹Œ 4(€€€€€€€€€€€€€€€}‘…Ñ„°}ÍÑ½É”°Í•ÍÍ¥½¸°}½Á•É…Ñ¥½¹A…ÍÍÝ½É€üü}™¥±•A…ÍÍÝ½É¤ì4(€€€€€€€€€€€…Ñ…AÕ±±•€ôÑÉÕ”ì4(€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ô€‰í±¥•¹Ñ9…µ•ôÝ…Ì¡•­•¥¸…¹¥ÑÌ±½¬Ý…ÌÉ•±•…Í•¸ˆì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰í±¥•¹Ñ9…µ•ôÝ…Ì¡•­•¥¸¹qÉq¹qÉq¹I•½Ù•Éä½ÁäéqÉq¹íÉ•ÍÕ±Ð¹I•½Ù•Éå	…­ÕÁA…Ñ¡ôˆ°4(€€€€€€€€€€€€€€€€‰±¥•¹Ð¡•­•¥¸ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€ô°€‰Q¡”½½±”É¥Ù”±¥•¹Ð½Õ±¹½Ð‰”¡•­•¥¸¸ˆ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬I•±•…Í•¡•­½ÕÑÍå¹Œ ¤4(€€€ì4(€€€€€€€¥˜€¡}‰ÕÍäñð€…}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¹!…ÍY…±Õ”¤É•ÑÕÉ¸ì4(€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰I•±•…Í”Ñ¡¥Ì¡•­½ÕÐÝ¥Ñ¡½ÕÐÁÕÍ¡¥¹œü1½…°±¥•¹Ð¡…¹•Ì…¹‘½Ý¹±½…‘•½¹™¥ÕÉ…Ñ¥½¸™¥±•Ì€ˆ€¬4(€€€€€€€€€€€€€€€€‰Ý¥±°‰”É•Á±…•‰äÑ¡”ÕÉÉ•¹Ðµ…ÍÑ•È¥¹Ù•¹Ñ½Éä¸ˆ°4(€€€€€€€€€€€€€€€€‰I•±•…Í”¡•­½ÕÐÝ¥Ñ¡½ÕÐÁÕÍ¡¥¹œˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹]…É¹¥¹œ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€…Ý…¥ÐIÕ¹	ÕÍåÍå¹Œ¡…Íå¹Œ€ ¤€ôø4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ô…Ý…¥Ð%¹ÍÁ•Ñ]¥Ñ¡A…ÍÍÝ½É‘Íå¹Œ¡ÁÉ½µÁÐèÑÉÕ”¤ì4(€€€€€€€€€€€¥˜€¡Í¹…ÁÍ¡½Ð¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÍ•ÍÍ¥½¸€ô…Ý…¥Ð¹ÍÕÉ•5…ÍÑ•ÉM•ÍÍ¥½¹Íå¹Œ¡Í¹…ÁÍ¡½Ð°™½É•AÉ½µÁÐè™…±Í”¤ì4(€€€€€€€€€€€¥˜€¡Í•ÍÍ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€…Ý…¥Ð½½±•É¥Ù•Må¹M•ÉÙ¥”¹I•±•…Í•¡•­½ÕÑÍå¹Œ 4(€€€€€€€€€€€€€€€}‘…Ñ„°}ÍÑ½É”°Í•ÍÍ¥½¸°}½Á•É…Ñ¥½¹A…ÍÍÝ½É€üü}™¥±•A…ÍÍÝ½É¤ì4(€€€€€€€€€€€…Ñ…AÕ±±•€ôÑÉÕ”ì4(€€€€€€€ô°€‰Q¡”½½±”É¥Ù”¡•­½ÕÐ½Õ±¹½Ð‰”É•±•…Í•¸ˆ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥¡½½Í•¥±•AÉ½Ñ•Ñ¥½¸ ¤4(€€€ì4(€€€€€€€¥˜€¡}‘…Ñ„¹5…ÍÑ•É•ÍÌ¹±¥•¹ÑMÕ‰µ…ÑÉ¥•Ì¹½Õ¹Ð€ø€À¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰Q¡¥Ìµ…ÍÑ•È…±É•…‘ä¡…Ì±¥•¹ÐÍÕˆµµ…ÑÉ¥•Ì¸-••À¥ÑÌÕÉÉ•¹Ð™¥±”µÁÉ½Ñ•Ñ¥½¸Á…ÍÍÝ½ÉÍ¼€ˆ€¬4(€€€€€€€€€€€€€€€€‰Ñ¡½Í”½¹™¥ÕÉ…Ñ¥½¸µ™¥±”Á…­…•ÌÉ•µ…¥¸Õ¹±½­…‰±”¸™ÕÑÕÉ”Á…ÍÍÝ½ÉµÉ½Ñ…Ñ¥½¸Ý½É­™±½Ü€ˆ€¬4(€€€€€€€€€€€€€€€€‰…¸É”µ•¹ÉåÁÐ•Ù•ÉäÍÕˆµµ…ÑÉ¥àÑ½•Ñ¡•È¸ˆ°4(€€€€€€€€€€€€€€€€‰¥±”ÁÉ½Ñ•Ñ¥½¸¥Ì±½­•ˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(€€€€€€€Ù…ÈÁÉ½Ñ•Ñ¥½¸€ôA…ÍÍÝ½É‘¥…±½œ¹AÉ½µÁÑ½É9•Ý¥±”¡Ñ¡¥Ì¤ì4(€€€€€€€¥˜€¡ÁÉ½Ñ•Ñ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€}™¥±•A…ÍÍÝ½É€ôÁÉ½Ñ•Ñ¥½¸¹A…ÍÍÝ½Éì4(€€€€€€€}ÁÉ½Ñ•Ñ¥½¹MÑ…Ñ”¹Q•áÐ€ô}™¥±•A…ÍÍÝ½É¥Ì¹Õ±°4(€€€€€€€€€€€€ü€‰Q¡”¹•áÐÁÕÍ Ý¥±°ÍÑ½É”…¸Õ¹ÁÉ½Ñ•Ñ•€¹¹…ÍŒ™¥±”¸ˆ4(€€€€€€€€€€€€è€‰Q¡”¹•áÐÁÕÍ Ý¥±°Á…ÍÍÝ½ÉµÁÉ½Ñ•Ð…¹•¹ÉåÁÐÑ¡”µ…ÍÑ•È…Ì½µÁ…Ð)]¸ˆì4(€€€€€€€}ÁÉ½Ñ•Ñ¥½¹MÑ…Ñ”¹½É•½±½È€ô}™¥±•A…ÍÍÝ½É¥Ì¹Õ±°€üU¥Q¡•µ”¹µ‰•È€èU¥Q¡•µ”¹É••¸ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥U¹±¥¹­¥±” ¤4(€€€ì4(€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥±•%¤¤É•ÑÕÉ¸ì4(€€€€€€€¥˜€¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¹!…ÍY…±Õ”¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰¡•¬¥¸…¹ÁÕÍ °½ÈÉ•±•…Í”Ñ¡”…Ñ¥Ù”±¥•¹Ð¡•­½ÕÐ‰•™½É”Õ¹±¥¹­¥¹œ½½±”É¥Ù”¸ˆ°4(€€€€€€€€€€€€€€€€‰±¥•¹Ð¡•­½ÕÐ…Ñ¥Ù”ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰U¹±¥¹¬Ñ¡¥Ì½½±”É¥Ù”µ…ÍÑ•ÈüQ¡”É¥Ù”™¥±”…¹½½±”Í¥¸µ¥¸Ý¥±°É•µ…¥¸¸ˆ°4(€€€€€€€€€€€€€€€€‰U¹±¥¹¬É¥Ù”™¥±”ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°5•ÍÍ…•	½á%½¸¹EÕ•ÍÑ¥½¸°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤É•ÑÕÉ¸ì4(€€€€€€€Ù…Èµ…ÍÑ•É-•ä€ô}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥±•%ì4(€€€€€€€½½±•É¥Ù•Må¹M•ÉÙ¥”¹U¹±¥¹¬¡}‘…Ñ„°}ÍÑ½É”¤ì4(€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¹½¹Ñ•áÐ¹±•…È¡Må¹Q…É•Ð¹½½±•É¥Ù”°µ…ÍÑ•É-•ä¤ì4(€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸€ô¹Õ±°ì4(€€€€€€€}™¥±•A…ÍÍÝ½É€ô¹Õ±°ì4(€€€€€€€I•™É•Í¡1½…±MÑ…Ñ” ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥I•™É•Í¡1½…±MÑ…Ñ”¡‰½½°ÕÁ‘…Ñ•%¹ÁÕÑ¥•±‘Ì€ôÑÉÕ”¤4(€€€ì4(€€€€€€€¥˜€¡ÕÁ‘…Ñ•%¹ÁÕÑ¥•±‘Ì¤4(€€€€€€€ì4(€€€€€€€€€€€}±¥•¹Ñ%¹Q•áÐ€ô}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•=ÕÑ¡±¥•¹Ñ%ì4(€€€€€€€€€€€}Í¡…É•1¥¹¬¹Q•áÐ€ô}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•M¡…É•1¥¹¬ì4(€€€€€€€ô4(€€€€€€€Ù…È½¹™¥ÕÉ•€ô½½±•É¥Ù•M•ÉÙ¥”¹!…Í½¹™¥ÕÉ•‘±¥•¹Ð¡}‘…Ñ„¹M•ÑÑ¥¹Ì¤ì4(€€€€€€€Ù…ÈÍ¥¹•‘%¸€ô½¹™¥ÕÉ•€˜˜½½±•É¥Ù•M•ÉÙ¥”¹!…Í½½±•M¥¹%¸ì4(€€€€€€€}…½Õ¹ÑMÑ…Ñ”¹Q•áÐ€ôÍ¥¹•‘%¸€ü€‹Š^<€½¹¹•Ñ•ˆ€è½¹™¥ÕÉ•€ü€‹Š^<€I•…‘äÑ¼Í¥¸¥¸ˆ€è€‹Š^<€M•ÑÕÀÉ•ÅÕ¥É•ˆì4(€€€€€€€}…½Õ¹ÑMÑ…Ñ”¹½É•½±½È€ôÍ¥¹•‘%¸€üU¥Q¡•µ”¹É••¸€èU¥Q¡•µ”¹µ‰•Èì4(€€€€€€€Ù…È±¥¹­•€ô€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥±•%¤ì4(€€€€€€€Ù…È¡•­½ÕÑÑ¥Ù”€ô}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¹!…ÍY…±Õ”ì4(€€€€€€€Ù…È½½±•¡•­½ÕÑÑ¥Ù”€ô¡•­½ÕÑÑ¥Ù”€˜˜4(€€€€€€€€€€€}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ…É•Ð€ôô¹…µ•½˜¡Må¹Q…É•Ð¹½½±•É¥Ù”¤ì4(€€€€€€€}™¥±•MÑ…Ñ”¹Q•áÐ€ô±¥¹­•€ü€‰1¥¹­•ˆ€è€‰9½Ð±¥¹­•ˆì4(€€€€€€€}™¥±•MÑ…Ñ”¹½É•½±½È€ô±¥¹­•€üU¥Q¡•µ”¹É••¸€èU¥Q¡•µ”¹5ÕÑ•ì4(€€€€€€€}ÁÕ±°¹¹…‰±•€ôÍ¥¹•‘%¸€˜˜±¥¹­•€˜˜€…}‰ÕÍä€˜˜€…¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}ÁÕÍ ¹¹…‰±•€ôÍ¥¹•‘%¸€˜˜±¥¹­•€˜˜€…}‰ÕÍä€˜˜€…¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}½¹¹•Ð¹¹…‰±•€ô½¹™¥ÕÉ•€˜˜€…}‰ÕÍäì4(€€€€€€€}±¥¹¬¹¹…‰±•€ôÍ¥¹•‘%¸€˜˜€…}‰ÕÍäì4(€€€€€€€}ÁÉ½Ñ•Ñ¥½¸¹¹…‰±•€ô™…±Í”ì4(€€€€€€€}ÁÉ½Ñ•Ñ¥½¸¹Y¥Í¥‰±”€ô™…±Í”ì4(€€€€€€€}µ…ÍÑ•ÉM¥¹%¸¹¹…‰±•€ôÍ¥¹•‘%¸€˜˜±¥¹­•€˜˜€…}‰ÕÍäì4(€€€€€€€}…½Õ¹ÑÌ¹¹…‰±•€ôÍ¥¹•‘%¸€˜˜±¥¹­•€˜˜€…}‰ÕÍäì4(€€€€€€€}…½Õ¹ÑÌ¹Y¥Í¥‰±”€ô}µ…ÍÑ•ÉM•ÍÍ¥½¸¥Ì¹½Ð¹Õ±°€˜˜€…}½¹¹•Ñ¥½¹=¹±äì4(€€€€€€€}¡•­½ÕÐ¹¹…‰±•€ô™…±Í”ì4(€€€€€€€}¡•­%¸¹¹…‰±•€ôÍ¥¹•‘%¸€˜˜±¥¹­•€˜˜€…}‰ÕÍä€˜˜½½±•¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}É•±•…Í•¡•­½ÕÐ¹¹…‰±•€ôÍ¥¹•‘%¸€˜˜±¥¹­•€˜˜€…}‰ÕÍä€˜˜½½±•¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}µ…ÍÑ•ÉM¥¹%¸¹Q•áÐ€ô}µ…ÍÑ•ÉM•ÍÍ¥½¸¥Ì¹Õ±°€ü€‰5…ÍÑ•ÈÍ¥¸¥¸ˆ€è}µ…ÍÑ•ÉM•ÍÍ¥½¸¹¥ÍÁ±…å9…µ”ì4(€€€€€€€¥˜€¡}½¹¹•Ñ¥½¹=¹±ä¤4(€€€€€€€ì4(€€€€€€€€€€€}ÁÕ±°¹¹…‰±•€ô™…±Í”ì4(€€€€€€€€€€€}ÁÕÍ ¹¹…‰±•€ô™…±Í”ì4(€€€€€€€€€€€}µ…ÍÑ•ÉM¥¹%¸¹Y¥Í¥‰±”€ô™…±Í”ì4(€€€€€€€€€€€}…½Õ¹ÑÌ¹Y¥Í¥‰±”€ô™…±Í”ì4(€€€€€€€€€€€}¡•­%¸¹Y¥Í¥‰±”€ô™…±Í”ì4(€€€€€€€€€€€}É•±•…Í•¡•­½ÕÐ¹Y¥Í¥‰±”€ô™…±Í”ì4(€€€€€€€ô4(€€€€€€€¥˜€ …½¹™¥ÕÉ•¤4(€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ô€‰½½±”É•ÅÕ¥É•Ì…¸=ÕÑ •Í­Ñ½À±¥•¹Ð™½È‘¥É•Ð½¹±¥¹”…•ÍÌ¸€ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€€€€€‰%µÁ½ÉÐÑ¡”±¥•¹Ð)M=8ÍÕÁÁ±¥•™½ÈÑ¡”%¹8à1…‰Ì½½±”±½ÕÁÉ½©•Ð¸ˆì4(€€€€€€€•±Í”¥˜€ …±¥¹­•¤4(€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ôÍ¥¹•‘%¸4(€€€€€€€€€€€€€€€€ü€‰A…ÍÑ”Ñ¡”Í¡…É”±¥¹¬™½È…¸•á¥ÍÑ¥¹œ€¹¹…ÍŒ™¥±”…¹±¥¬½¹¹•ÐÍ¡…É”±¥¹¬¸ˆ4(€€€€€€€€€€€€€€€€è€‰±¥¬M¥¸¥¸Ý¥Ñ ½½±”°Ñ¡•¸½¹¹•ÐÑ¡”Í¡…É•€¹¹…ÍŒ±¥¹¬¸ˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥M¡½ÝM¹…ÁÍ¡½Ð¡½½±•É¥Ù•M¹…ÁÍ¡½ÐÍ¹…ÁÍ¡½Ð¤4(€€€ì4(€€€€€€€}‘…Ñ„¹5…ÍÑ•É•ÍÌ€ô5…ÍÑ•É•ÍÍM•ÉÙ¥”¹±½¹”¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ¤ì4(€€€€€€€½½±•É¥Ù•Må¹M•ÉÙ¥”¹¹ÍÕÉ•	…Í•±¥¹•%™M…™”¡}‘…Ñ„°}ÍÑ½É”°Í¹…ÁÍ¡½Ð¤ì4(€€€€€€€Ù…ÈÍ…Ù•‘Ð€ôÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹áÁ½ÉÑ•‘UÑŒ€ôô‘•™…Õ±Ð4(€€€€€€€€€€€€ü€‰Õ¹­¹½Ý¸Ñ¥µ”ˆ4(€€€€€€€€€€€€èÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹áÁ½ÉÑ•‘UÑŒ¹Q½1½…±Q¥µ” ¤¹Q½MÑÉ¥¹œ ‰554°åååä éµ´ÑÐˆ¤ì4(€€€€€€€Ù…ÈÍ…Ù•‘	ä€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹M…Ù•‘	ä¤4(€€€€€€€€€€€€ü€‰…¸•…É±¥•ÈÉ•Ù¥Í¥½¸ˆ4(€€€€€€€€€€€€èÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹M…Ù•‘	äì4(€€€€€€€Ù…È¡…ÍáÑ•É¹…±¡…¹•Ì€ô½½±•É¥Ù•Må¹M•ÉÙ¥”¹!…ÍáÑ•É¹…±¡…¹•Ì¡}‘…Ñ„°Í¹…ÁÍ¡½Ð¤ì4(€€€€€€€}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•I•µ½Ñ•¡…¹•Í•Ñ•Ñ•€ô¡…ÍáÑ•É¹…±¡…¹•Ìì4(€€€€€€€}ÍÑ½É”¹M…Ù”¡}‘…Ñ„¤ì4(€€€€€€€}™¥±•MÑ…Ñ”¹Q•áÐ€ô¡…ÍáÑ•É¹…±¡…¹•Ì4(€€€€€€€€€€€€ü€‰9•Ý•È™¥±”…Ù…¥±…‰±”ˆ4(€€€€€€€€€€€€èÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð¤4(€€€€€€€€€€€€€€€€ü€‰AÕ±°É•ÅÕ¥É•ˆ4(€€€€€€€€€€€€€€€€è€‰UÀÑ¼‘…Ñ”ˆì4(€€€€€€€}™¥±•MÑ…Ñ”¹½É•½±½È€ô}™¥±•MÑ…Ñ”¹Q•áÐ€ôô€‰UÀÑ¼‘…Ñ”ˆ€üU¥Q¡•µ”¹É••¸€èU¥Q¡•µ”¹µ‰•Èì4(€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ô4(€€€€€€€€€€€€‰íÍ¹…ÁÍ¡½Ð¹5•Ñ…‘…Ñ„¹9…µ•õqÉq¸ˆ€¬4(€€€€€€€€€€€€‰íÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹±¥•¹Ñ½Õ¹Ðé8Áô±¥•¹Ð¡Ì¤€ƒŠˆ€€ˆ€¬4(€€€€€€€€€€€€‰íÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹ÅÕ¥Áµ•¹Ñ½Õ¹Ðé8Áô•ÅÕ¥Áµ•¹ÐÉ•½É¡Ì¤€ƒŠˆ€€ˆ€¬4(€€€€€€€€€€€€‰íÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ¹¡•­½ÕÑÌ¹½Õ¹Ðé8Áô¡•­•½ÕÑqÉq¸ˆ€¬4(€€€€€€€€€€€€‰I•Ù¥Í¥½¸íM¡½ÉÑI•Ù¥Í¥½¸¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹I•Ù¥Í¥½¹%¥ô€ƒŠˆ€M…Ù•íÍ…Ù•‘Ñô‰äíÍ…Ù•‘	åôˆ€¬4(€€€€€€€€€€€€¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ…É•Ð€ôô¹…µ•½˜¡Må¹Q…É•Ð¹½½±•É¥Ù”¤4(€€€€€€€€€€€€€€€€ü€‰qÉq¹±¥•¹Ð¡•­½ÕÐ…Ñ¥Ù”…Ìí}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑUÍ•É¹…µ•ôˆ4(€€€€€€€€€€€€€€€€èÍÑÉ¥¹œ¹µÁÑä¤ì4(€€€€€€€}ÁÉ½Ñ•Ñ¥½¹MÑ…Ñ”¹Q•áÐ€ôÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹A…ÍÍÝ½É‘AÉ½Ñ•Ñ•4(€€€€€€€€€€€€ü€‰A…ÍÍÝ½ÉµÁÉ½Ñ•Ñ•…¹•¹ÉåÁÑ•…Ì½µÁ…Ð)]¸ˆ4(€€€€€€€€€€€€è€‰Q¡¥ÌÉ¥Ù”µ…ÍÑ•È¥Ì¹½ÐÁ…ÍÍÝ½ÉÁÉ½Ñ•Ñ•¸UÍ”¥±”ÁÉ½Ñ•Ñ¥½¸‰•™½É”„ÁÕÍ Ñ¼ÁÉ½Ñ•Ð¥Ð¸ˆì4(€€€€€€€}ÁÉ½Ñ•Ñ¥½¹MÑ…Ñ”¹½É•½±½È€ôÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹A…ÍÍÝ½É‘AÉ½Ñ•Ñ•€üU¥Q¡•µ”¹É••¸€èU¥Q¡•µ”¹µ‰•Èì4(€€€€€€€}ÁÉ½Ñ•Ñ¥½¸¹¹…‰±•€ôÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ¹±¥•¹ÑMÕ‰µ…ÑÉ¥•Ì¹½Õ¹Ð€ôô€Àì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬IÕ¹	ÕÍåÍå¹Œ¡Õ¹ŒñQ…Í¬ø½Á•É…Ñ¥½¸°ÍÑÉ¥¹œ•ÉÉ½É5•ÍÍ…”¤4(€€€ì4(€€€€€€€}‰ÕÍä€ôÑÉÕ”ì4(€€€€€€€UÍ•]…¥ÑÕÉÍ½È€ôÑÉÕ”ì4(€€€€€€€I•™É•Í¡1½…±MÑ…Ñ”¡ÕÁ‘…Ñ•%¹ÁÕÑ¥•±‘Ìè™…±Í”¤ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€…Ý…¥Ð½Á•É…Ñ¥½¸ ¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡M¡…É•‘5…ÍÑ•É½¹™±¥Ñá•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°•á•ÁÑ¥½¸¹5•ÍÍ…”€¬€‰qÉq¹qÉq¹9¼½½±”É¥Ù”‘…Ñ„Ý…Ì½Ù•ÉÝÉ¥ÑÑ•¸¸ˆ°4(€€€€€€€€€€€€€€€€‰9•Ý•Èµ…ÍÑ•È‘•Ñ•Ñ•ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹]…É¹¥¹œ¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€M¡½ÝÉÉ½È¡•ÉÉ½É5•ÍÍ…”°•á•ÁÑ¥½¸¤ì4(€€€€€€€ô4(€€€€€€€™¥¹…±±ä4(€€€€€€€ì4(€€€€€€€€€€€}‰ÕÍä€ô™…±Í”ì4(€€€€€€€€€€€UÍ•]…¥ÑÕÉÍ½È€ô™…±Í”ì4(€€€€€€€€€€€¥˜€¡}™½É•Ñ=Á•É…Ñ¥½¹A…ÍÍÝ½É¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€}½Á•É…Ñ¥½¹A…ÍÍÝ½É€ô¹Õ±°ì4(€€€€€€€€€€€€€€€}™½É•Ñ=Á•É…Ñ¥½¹A…ÍÍÝ½É€ô™…±Í”ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€I•™É•Í¡1½…±MÑ…Ñ”¡ÕÁ‘…Ñ•%¹ÁÕÑ¥•±‘Ìè™…±Í”¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥I•µ•µ‰•É5…ÍÑ•ÉM•ÍÍ¥½¸¡‰½½°Í¡½Ý9½Ñ¥™¥…Ñ¥½¸¤4(€€€ì4(€€€€€€€¥˜€¡}µ…ÍÑ•ÉM•ÍÍ¥½¸¥Ì¹Õ±°ñðÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥±•%¤¤É•ÑÕÉ¸ì4(€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¹½¹Ñ•áÐ¹M•Ð 4(€€€€€€€€€€€Må¹Q…É•Ð¹½½±•É¥Ù”°4(€€€€€€€€€€€}‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥±•%°4(€€€€€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸¤ì4(€€€€€€€¥˜€¡Í¡½Ý9½Ñ¥™¥…Ñ¥½¸¤5…ÍÑ•ÉM¥¹%¹9½Ñ¥™¥…Ñ¥½¸¹M¡½Ý½È¡Ñ¡¥Ì°}µ…ÍÑ•ÉM•ÍÍ¥½¸¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥M¡½ÝÉÉ½È¡ÍÑÉ¥¹œµ•ÍÍ…”°á•ÁÑ¥½¸•á•ÁÑ¥½¸¤€ôø4(€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰íµ•ÍÍ…•õqÉq¹qÉq¹í•á•ÁÑ¥½¸¹5•ÍÍ…•ôˆ°4(€€€€€€€€€€€€‰½½±”É¥Ù”½¹±¥¹”Íå¹Œˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œM¡½ÉÑI•Ù¥Í¥½¸¡ÍÑÉ¥¹œÉ•Ù¥Í¥½¸¤€ôø4(€€€€€€€ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡É•Ù¥Í¥½¸¤4(€€€€€€€€€€€€ü€‰±•…äˆ4(€€€€€€€€€€€€èÉ•Ù¥Í¥½¹l¸¹5…Ñ ¹5¥¸ à°É•Ù¥Í¥½¸¹1•¹Ñ ¥t¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤ì4)ô4