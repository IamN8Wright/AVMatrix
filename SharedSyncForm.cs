namespace InNasc;

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
    private readonly Button _checkout = UiTheme.PrimaryButton("Check out clientâ€¦");
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
        Text = "Company file sync";
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
            Text = "Company file sync",
            AutoSize = true,
            Font = UiTheme.Font(20, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = "Use one .nasc company file on a file share, synced folder, or Google Drive online.",
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

        var link = UiTheme.SecondaryButton("Link existingâ€¦");
        link.AutoSize = false;
        link.Size = new Size(126, 34);
        link.Location = new Point(18, 82);
        link.Click += (_, _) => LinkExisting();
        panel.Controls.Add(link);

        var create = UiTheme.SecondaryButton("Create new company fileâ€¦");
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

        var google = UiTheme.SecondaryButton("Google Drive onlineâ€¦");
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
                "Log out first, then choose the other company workspace from the welcome screen.",
                "Switch company workspace",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "Link an InNasc shared company file",
            Filter = "InNasc master (*.nasc)|*.nasc",
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
                "Create company workspace",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Create an InNasc shared company file",
            Filter = "InNasc master (*.nasc)|*.nasc",
            DefaultExt = "nasc",
            AddExtension = true,
            FileName = "InNasc-Company.nasc"
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
                $"Created the shared company file with {result.ClientCount:N0} client(s) and " +
                $"{result.EquipmentCount:N0} equipment record(s).\r\n\r\n" +
                "This master is encrypted and unlocked by its user accounts.\r\n\r\n" +
                "Have each collaborator link to this same file and pull once to establish a merge baseline. " +
                "After that, Merge & push combines independent work automatically.",
                "Company file created",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("The shared company file could not be created.", exception);
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
            var savedBy = string.IsNullOrWhiteSpace(snapshot.ConÛx¶‰žËkºwµç@€€€¥¹¥Ñ¥…±M•ÑÕÀèÑÉÕ”°4(€€€€€€€€€€€€€€€½Ý¹•È¹M•ÍÍ¥½¸¹5…ÍÑ•É-•ä¤ì4(€€€€€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸€ô½Ý¹•È¹M•ÍÍ¥½¸ì4(€€€€€€€€€€€}µ…ÍÑ•ÉA…ÍÍÝ½É€ô½Ý¹•È¹M•ÍÍ¥½¸¹5…ÍÑ•É-•äì4(€€€€€€€€€€€I•µ•µ‰•É5…ÍÑ•ÉM•ÍÍ¥½¸¡Í¡½Ý9½Ñ¥™¥…Ñ¥½¸èÑÉÕ”¤ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰Q¡”™¥ÉÍÐ=Ý¹•È…½Õ¹ÐÝ…Ì…‘‘•Ñ¼Ñ¡¥Ìµ…ÍÑ•È¸ˆ°4(€€€€€€€€€€€€€€€€‰½µÁ…¹ä=Ý¹•ÈÉ•…Ñ•ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸}µ…ÍÑ•ÉM•ÍÍ¥½¸ì4(€€€€€€€ô4(€€€€€€€¥˜€ …™½É•AÉ½µÁÐ€˜˜}µ…ÍÑ•ÉM•ÍÍ¥½¸¥Ì¹½Ð¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€ÑÉä4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸€ô5…ÍÑ•É•ÍÍM•ÉÙ¥”¹I•™É•Í¡M•ÍÍ¥½¸¡…•ÍÌ°}µ…ÍÑ•ÉM•ÍÍ¥½¸¤ì4(€€€€€€€€€€€€€€€I•µ•µ‰•É5…ÍÑ•ÉM•ÍÍ¥½¸¡Í¡½Ý9½Ñ¥™¥…Ñ¥½¸è™…±Í”¤ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸}µ…ÍÑ•ÉM•ÍÍ¥½¸ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€…Ñ €¡5…ÍÑ•ÉÕÑ¡½É¥é…Ñ¥½¹á•ÁÑ¥½¸¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¹½¹Ñ•áÐ¹±•…È¡Må¹Q…É•Ð¹M¡…É•‘¥±”°}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤ì4(€€€€€€€€€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸€ô¹Õ±°ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(€€€€€€€Ù…ÈÍ¥¹•‘%¸€ô5…ÍÑ•ÉM¥¹%¹½É´¹AÉ½µÁÐ¡Ñ¡¥Ì°…•ÍÌ¤ì4(€€€€€€€¥˜€¡Í¥¹•‘%¸¥Ì¹Õ±°¤É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸€ôÍ¥¹•‘%¸ì4(€€€€€€€I•µ•µ‰•É5…ÍÑ•ÉM•ÍÍ¥½¸¡Í¡½Ý9½Ñ¥™¥…Ñ¥½¸èÑÉÕ”¤ì4(€€€€€€€É•ÑÕÉ¸}µ…ÍÑ•ÉM•ÍÍ¥½¸ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥5…¹…•½Õ¹ÑÌ ¤4(€€€ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÁ…ÍÍÝ½É€ôI•ÅÕ•ÍÑA…ÍÍÝ½É‘%™9••‘•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤ì4(€€€€€€€€€€€¥˜€¡A½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%ÍA…ÍÍÝ½É‘AÉ½Ñ•Ñ•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤€˜˜Á…ÍÍÝ½É¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ôM¡…É•‘Må¹M•ÉÙ¥”¹%¹ÍÁ•Ð¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ °Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€Ù…ÈÍ•ÍÍ¥½¸€ô¹ÍÕÉ•M•ÍÍ¥½¸¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€¥˜€¡Í•ÍÍ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€ÕÍ¥¹œÙ…È…½Õ¹ÑÌ€ô¹•Ü5…ÍÑ•ÉUÍ•É5…¹…•µ•¹Ñ½É´ 4(€€€€€€€€€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°4(€€€€€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹±¥•¹ÑÌ¤ì4(€€€€€€€€€€€¥˜€¡…½Õ¹ÑÌ¹M¡½Ý¥…±½œ¡Ñ¡¥Ì¤€„ô¥…±½I•ÍÕ±Ð¹=,¤É•ÑÕÉ¸ì4(€€€€€€€€€€€M¡…É•‘Må¹M•ÉÙ¥”¹M…Ù••ÍÍ½¹ÑÉ½° 4(€€€€€€€€€€€€€€€}‘…Ñ„°4(€€€€€€€€€€€€€€€}ÍÑ½É”°4(€€€€€€€€€€€€€€€…½Õ¹ÑÌ¹I•ÍÕ±Ñ•ÍÌ°4(€€€€€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€€€€€¥¹¥Ñ¥…±M•ÑÕÀè™…±Í”°4(€€€€€€€€€€€€€€€Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸€ô5…ÍÑ•É•ÍÍM•ÉÙ¥”¹I•™É•Í¡M•ÍÍ¥½¸ 4(€€€€€€€€€€€€€€€…½Õ¹ÑÌ¹I•ÍÕ±Ñ•ÍÌ°Í•ÍÍ¥½¸¤ì4(€€€€€€€€€€€I•µ•µ‰•É5…ÍÑ•ÉM•ÍÍ¥½¸¡Í¡½Ý9½Ñ¥™¥…Ñ¥½¸è™…±Í”¤ì4(€€€€€€€€€€€I•™É•Í¡5…ÍÑ•ÉMÑ…Ñ” ¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€M¡½ÝÉÉ½È ‰Q¡”µ…ÍÑ•È…½Õ¹ÑÌ½Õ±¹½Ð‰”ÕÁ‘…Ñ•¸ˆ°•á•ÁÑ¥½¸¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥¡•­½ÕÑ±¥•¹Ð ¤4(€€€ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÁ…ÍÍÝ½É€ôI•ÅÕ•ÍÑA…ÍÍÝ½É‘%™9••‘•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤ì4(€€€€€€€€€€€¥˜€¡A½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%ÍA…ÍÍÝ½É‘AÉ½Ñ•Ñ•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤€˜˜Á…ÍÍÝ½É¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ôM¡…É•‘Må¹M•ÉÙ¥”¹%¹ÍÁ•Ð¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ °Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€Ù…ÈÍ•ÍÍ¥½¸€ô¹ÍÕÉ•M•ÍÍ¥½¸¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€¥˜€¡Í•ÍÍ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€5…ÍÑ•É•ÍÍM•ÉÙ¥”¹I•ÅÕ¥É•]É¥Ñ”¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°Í•ÍÍ¥½¸¤ì4(€€€€€€€€€€€Ù…ÈÁ•Éµ¥ÑÑ•‘±¥•¹ÑÌ€ôÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹±¥•¹ÑÌ4(€€€€€€€€€€€€€€€€¹]¡•É”¡±¥•¹Ð€ôø5…ÍÑ•É•ÍÍM•ÉÙ¥”¹…¹•ÍÍ±¥•¹Ð 4(€€€€€€€€€€€€€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°4(€€€€€€€€€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€€€€€€€€€±¥•¹Ð¹%¤¤4(€€€€€€€€€€€€€€€€¹Q½1¥ÍÐ ¤ì4(€€€€€€€€€€€ÕÍ¥¹œÙ…ÈÍ•±•Ñ¥½¸€ô¹•Ü±¥•¹Ñ¡•­½ÕÑM•±•Ñ¥½¹½É´ 4(€€€€€€€€€€€€€€€Á•Éµ¥ÑÑ•‘±¥•¹ÑÌ°4(€€€€€€€€€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ¹¡•­½ÕÑÌ¤ì4(€€€€€€€€€€€¥˜€¡Í•±•Ñ¥½¸¹M¡½Ý¥…±½œ¡Ñ¡¥Ì¤€„ô¥…±½I•ÍÕ±Ð¹=,ñðÍ•±•Ñ¥½¸¹M•±•Ñ•‘±¥•¹Ñ%¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€±¥•¹Ñ¡•­½ÕÑI•ÍÕ±ÐÉ•ÍÕ±Ðì4(€€€€€€€€€€€ÑÉä4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É•ÍÕ±Ð€ôM¡…É•‘Må¹M•ÉÙ¥”¹¡•­½ÕÑ±¥•¹Ð 4(€€€€€€€€€€€€€€€€€€€}‘…Ñ„°}ÍÑ½É”°Í•±•Ñ¥½¸¹M•±•Ñ•‘±¥•¹Ñ%¹Y…±Õ”°Í•ÍÍ¥½¸°™½É”è™…±Í”°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€…Ñ €¡±¥•¹Ñ1½­•‘á•ÁÑ¥½¸±½­•¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…È¡½±‘•È€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡±½­•¹¡•­½ÕÐ¹¥ÍÁ±…å9…µ”¤4(€€€€€€€€€€€€€€€€€€€€ü±½­•¹¡•­½ÕÐ¹UÍ•É¹…µ”4(€€€€€€€€€€€€€€€€€€€€è±½­•¹¡•­½ÕÐ¹¥ÍÁ±…å9…µ”ì4(€€€€€€€€€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€€€€€€€€€‰í±½­•¹±¥•¹Ñ9…µ•ô¥Ì¡•­•½ÕÐ‰äí¡½±‘•Éô½¸í±½­•¹¡•­½ÕÐ¹5…¡¥¹•9…µ•ô¹qÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€‰	•™½É”‰½½Ñ¥¹œÑ¡•´°…Í¬Ñ¡”Ñ•¡¹¥¥…¸¥¸Á•ÉÍ½¸Ý¡•Ñ¡•ÈÑ¡•¥È¡…¹•Ì¡…Ù”‰••¸ÁÕÍ¡•¸€ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€‰	½½Ñ¥¹œÉ•±•…Í•ÌÑ¡•¥È±½¬¥µµ•‘¥…Ñ•±äì…¹äÕ¹ÁÕÍ¡•Ý½É¬É•µ…¥¹Ì½¹±ä½¸Ñ¡•¥ÈA¹qÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€‰	½½ÐÑ¡…Ð¡•­½ÕÐ…¹½¹Ñ¥¹Õ”üˆ°4(€€€€€€€€€€€€€€€€€€€€€€€€‰	½½Ð•á¥ÍÑ¥¹œ¡•­½ÕÐˆ°4(€€€€€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°4(€€€€€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹]…É¹¥¹œ°4(€€€€€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤4(€€€€€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€€€€€É•ÍÕ±Ð€ôM¡…É•‘Må¹M•ÉÙ¥”¹¡•­½ÕÑ±¥•¹Ð 4(€€€€€€€€€€€€€€€€€€€}‘…Ñ„°}ÍÑ½É”°Í•±•Ñ¥½¸¹M•±•Ñ•‘±¥•¹Ñ%¹Y…±Õ”°Í•ÍÍ¥½¸°™½É”èÑÉÕ”°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€…Ñ…AÕ±±•€ôÑÉÕ”ì4(€€€€€€€€€€€I•™É•Í¡5…ÍÑ•ÉMÑ…Ñ” ¤ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰¡•­•½ÕÐíÉ•ÍÕ±Ð¹±¥•¹Ñ9…µ•ô¸%ÑÌ½¹™¥ÕÉ…Ñ¥½¸™¥±•Ì…É”¹½Ü…Ù…¥±…‰±”¥¸•… ‘•Ù¥”Ì•‘¥Ñ½È¹qÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€‰±¥•¹ÐÍÕˆµµ…ÑÉ¥àéqÉq¹íÉ•ÍÕ±Ð¹MÕ‰µ…ÑÉ¥á1½…Ñ¥½¹õqÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€‰I•½Ù•Éä½Áä½˜Ñ¡”ÁÉ•Ù¥½ÕÌ±½…°Ý½É­ÍÁ…”éqÉq¹íÉ•ÍÕ±Ð¹I•½Ù•Éå	…­ÕÁA…Ñ¡ôˆ°4(€€€€€€€€€€€€€€€É•ÍÕ±Ð¹	½½Ñ•‘AÉ•Ù¥½ÕÍ¡•­½ÕÐ€ü€‰¡•­½ÕÐ‰½½Ñ•…¹É•Á±…•ˆ€è€‰±¥•¹Ð¡•­•½ÕÐˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€M¡½ÝÉÉ½È ‰Q¡”±¥•¹Ð½Õ±¹½Ð‰”¡•­•½ÕÐ¸ˆ°•á•ÁÑ¥½¸¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥¡•­%¹±¥•¹Ð ¤4(€€€ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÁ…ÍÍÝ½É€ôI•ÅÕ•ÍÑA…ÍÍÝ½É‘%™9••‘•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤ì4(€€€€€€€€€€€¥˜€¡A½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%ÍA…ÍÍÝ½É‘AÉ½Ñ•Ñ•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤€˜˜Á…ÍÍÝ½É¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ôM¡…É•‘Må¹M•ÉÙ¥”¹%¹ÍÁ•Ð¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ °Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€Ù…ÈÍ•ÍÍ¥½¸€ô¹ÍÕÉ•M•ÍÍ¥½¸¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€¥˜€¡Í•ÍÍ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…È±¥•¹Ñ9…µ”€ô}‘…Ñ„¹±¥•¹ÑÌ¹M¥¹±•=É•™…Õ±Ð¡±¥•¹Ð€ôø4(€€€€€€€€€€€€€€€±¥•¹Ð¹%€ôô}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¤ü¹9…µ”€üü€‰Ñ¡¥Ì±¥•¹Ðˆì4(€€€€€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€€€€€‰AÕÍ …±°¡…¹•Ì…¹½¹™¥ÕÉ…Ñ¥½¸™¥±•Ì™½Èí±¥•¹Ñ9…µ•ô°Ñ¡•¸É•±•…Í”¥ÑÌ¡•­½ÕÐüˆ°4(€€€€€€€€€€€€€€€€€€€€‰¡•¬¥¸±¥•¹Ðˆ°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹EÕ•ÍÑ¥½¸°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤4(€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÉ•ÍÕ±Ð€ôM¡…É•‘Må¹M•ÉÙ¥”¹¡•­%¹±¥•¹Ð¡}‘…Ñ„°}ÍÑ½É”°Í•ÍÍ¥½¸°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€…Ñ…AÕ±±•€ôÑÉÕ”ì4(€€€€€€€€€€€I•™É•Í¡5…ÍÑ•ÉMÑ…Ñ” ¤ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰í±¥•¹Ñ9…µ•ôÝ…Ì¡•­•¥¸…¹¥ÑÌ±½¬Ý…ÌÉ•±•…Í•¹qÉq¹qÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€‰I•½Ù•Éä½ÁäéqÉq¹íÉ•ÍÕ±Ð¹I•½Ù•Éå	…­ÕÁA…Ñ¡ôˆ°4(€€€€€€€€€€€€€€€€‰±¥•¹Ð¡•­•¥¸ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€M¡½ÝÉÉ½È ‰Q¡”±¥•¹Ð½Õ±¹½Ð‰”¡•­•¥¸¸ˆ°•á•ÁÑ¥½¸¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥I•±•…Í•¡•­½ÕÐ ¤4(€€€ì4(€€€€€€€¥˜€ …}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¹!…ÍY…±Õ”¤É•ÑÕÉ¸ì4(€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰I•±•…Í”Ñ¡¥Ì¡•­½ÕÐÝ¥Ñ¡½ÕÐÁÕÍ¡¥¹œü1½…°±¥•¹Ð¡…¹•Ì…¹‘½Ý¹±½…‘•½¹™¥ÕÉ…Ñ¥½¸™¥±•Ì€ˆ€¬4(€€€€€€€€€€€€€€€€‰Ý¥±°‰”É•Á±…•‰äÑ¡”ÕÉÉ•¹Ðµ…ÍÑ•È¥¹Ù•¹Ñ½Éä¸±½…°É•½Ù•Éä½Áä¥Ì¹½ÐÉ•…Ñ•‰äÑ¡¥Ì…Ñ¥½¸¸ˆ°4(€€€€€€€€€€€€€€€€‰I•±•…Í”¡•­½ÕÐÝ¥Ñ¡½ÕÐÁÕÍ¡¥¹œˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹]…É¹¥¹œ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÁ…ÍÍÝ½É€ôI•ÅÕ•ÍÑA…ÍÍÝ½É‘%™9••‘•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤ì4(€€€€€€€€€€€¥˜€¡A½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%ÍA…ÍÍÝ½É‘AÉ½Ñ•Ñ•¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤€˜˜Á…ÍÍÝ½É¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ôM¡…É•‘Må¹M•ÉÙ¥”¹%¹ÍÁ•Ð¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ °Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€Ù…ÈÍ•ÍÍ¥½¸€ô¹ÍÕÉ•M•ÍÍ¥½¸¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€¥˜€¡Í•ÍÍ¥½¸¥Ì¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€M¡…É•‘Må¹M•ÉÙ¥”¹I•±•…Í•¡•­½ÕÐ¡}‘…Ñ„°}ÍÑ½É”°Í•ÍÍ¥½¸°Á…ÍÍÝ½É¤ì4(€€€€€€€€€€€…Ñ…AÕ±±•€ôÑÉÕ”ì4(€€€€€€€€€€€I•™É•Í¡5…ÍÑ•ÉMÑ…Ñ” ¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€M¡½ÝÉÉ½È ‰Q¡”¡•­½ÕÐ½Õ±¹½Ð‰”É•±•…Í•¸ˆ°•á•ÁÑ¥½¸¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥I•™É•Í¡5…ÍÑ•ÉMÑ…Ñ”¡M¡…É•‘5…ÍÑ•ÉM¹…ÁÍ¡½Ðü­¹½Ý¹M¹…ÁÍ¡½Ð€ô¹Õ±°¤4(€€€ì4(€€€€€€€Ù…ÈÁ…Ñ €ô}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ì4(€€€€€€€}Á…Ñ ¹Q•áÐ€ôÁ…Ñ ì4(€€€€€€€Ù…È±¥¹­•€ô€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Á…Ñ ¤ì4(€€€€€€€Ù…È¡•­½ÕÑÑ¥Ù”€ô}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¹!…ÍY…±Õ”ì4(€€€€€€€}ÁÕ±°¹¹…‰±•€ô±¥¹­•€˜˜€…¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}ÁÕÍ ¹¹…‰±•€ô±¥¹­•€˜˜€…¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}Õ¹±¥¹¬¹¹…‰±•€ô±¥¹­•ì4(€€€€€€€}Í¥¹%¸¹¹…‰±•€ô±¥¹­•ì4(€€€€€€€}…½Õ¹ÑÌ¹¹…‰±•€ô±¥¹­•ì4(€€€€€€€}…½Õ¹ÑÌ¹Y¥Í¥‰±”€ô}µ…ÍÑ•ÉM•ÍÍ¥½¸¥Ì¹½Ð¹Õ±°ì4(€€€€€€€}¡•­½ÕÐ¹¹…‰±•€ô±¥¹­•€˜˜€…¡•­½ÕÑÑ¥Ù”€˜˜€¡}µ…ÍÑ•ÉM•ÍÍ¥½¸ü¹…¹]É¥Ñ”€üüÑÉÕ”¤ì4(€€€€€€€}¡•­%¸¹¹…‰±•€ô±¥¹­•€˜˜¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}É•±•…Í•¡•­½ÕÐ¹¹…‰±•€ô±¥¹­•€˜˜¡•­½ÕÑÑ¥Ù”ì4(€€€€€€€}Í¥¹%¸¹Q•áÐ€ô}µ…ÍÑ•ÉM•ÍÍ¥½¸¥Ì¹Õ±°€ü€‰M¥¸¥¸ˆ€è}µ…ÍÑ•ÉM•ÍÍ¥½¸¹¥ÍÁ±…å9…µ”ì4(€€€€€€€¥˜€ …±¥¹­•¤4(€€€€€€€ì4(€€€€€€€€€€€}ÍÑ…Ñ”¹Q•áÐ€ô€‰9½Ð±¥¹­•ˆì4(€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ô€‰1¥¹¬…¸•á¥ÍÑ¥¹œµ…ÍÑ•È½ÈÉ•…Ñ”½¹”™É½´Ñ¡¥ÌAÌÕÉÉ•¹Ð‘…Ñ„¸ˆì4(€€€€€€€€€€€}Õ¥‘…¹”¹Q•áÐ€ô€‰½È„™¥±”Í•ÉÙ•È°¡½½Í”Ñ¡”Í¡…É•½ÈU9Á…Ñ ¸€ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€€€€€€‰½È‘¥É•Ð±½Õ…•ÍÌ°¡½½Í”½½±”É¥Ù”½¹±¥¹”…‰½Ù”¸ˆì4(€€€€€€€€€€€}Õ¥‘…¹”¹½É•½±½È€ôU¥Q¡•µ”¹µ‰•Èì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€¥˜€¡­¹½Ý¹M¹…ÁÍ¡½Ð¥Ì¹Õ±°€˜˜¥±”¹á¥ÍÑÌ¡Á…Ñ ¤€˜˜4(€€€€€€€€€€€€€€€A½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%ÍA…ÍÍÝ½É‘AÉ½Ñ•Ñ•¡Á…Ñ ¤€˜˜ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡}µ…ÍÑ•ÉA…ÍÍÝ½É¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€}ÍÑ…Ñ”¹Q•áÐ€ô€‰½µÁ…¹ä…Ù…¥±…‰±”ƒŠP±½­•ˆì4(€€€€€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ô€‰Q¡¥ÌÍ¡…É•½µÁ…¹ä™¥±”¥ÌÁ…ÍÍÝ½ÉÁÉ½Ñ•Ñ•…¹•¹ÉåÁÑ•…Ì)]¸ˆì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹Q•áÐ€ô€‰AÕ±°½ÈÁÕÍ Ñ¼•¹Ñ•ÈÑ¡”Á…ÍÍÝ½É¸%Ð¥Ì¹•Ù•ÈÍÑ½É•¥¸Ñ¡”µ…ÍÑ•È™¥±”¸ˆì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹½É•½±½È€ôU¥Q¡•µ”¹µ‰•Èì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€Ù…ÈÍ¹…ÁÍ¡½Ð€ô­¹½Ý¹M¹…ÁÍ¡½Ð€üüM¡…É•‘Må¹M•ÉÙ¥”¹%¹ÍÁ•Ð¡Á…Ñ °}µ…ÍÑ•ÉA…ÍÍÝ½É¤ì4(€€€€€€€€€€€M¡…É•‘Må¹M•ÉÙ¥”¹¹ÍÕÉ•	…Í•±¥¹•%™M…™”¡}‘…Ñ„°}ÍÑ½É”°Í¹…ÁÍ¡½Ð¤ì4(€€€€€€€€€€€Ù…ÈÍ…Ù•‘Ð€ôÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹áÁ½ÉÑ•‘UÑŒ€ôô‘•™…Õ±Ð4(€€€€€€€€€€€€€€€€ü€‰Õ¹­¹½Ý¸Ñ¥µ”ˆ4(€€€€€€€€€€€€€€€€èÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹áÁ½ÉÑ•‘UÑŒ¹Q½1½…±Q¥µ” ¤¹Q½MÑÉ¥¹œ ‰554°åååä éµ´ÑÐˆ¤ì4(€€€€€€€€€€€Ù…ÈÍ…Ù•‘	ä€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹M…Ù•‘	ä¤4(€€€€€€€€€€€€€€€€ü€‰…¸•…É±¥•ÈÉ•Ù¥Í¥½¸ˆ4(€€€€€€€€€€€€€€€€èÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹M…Ù•‘	äì4(€€€€€€€€€€€}ÍÑ…Ñ”¹Q•áÐ€ô€‰½µÁ…¹ä…Ù…¥±…‰±”ˆì4(€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ô4(€€€€€€€€€€€€€€€€‰íÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹±¥•¹Ñ½Õ¹Ðé8Áô±¥•¹Ð¡Ì¤€ƒŠˆ€€ˆ€¬4(€€€€€€€€€€€€€€€€‰íÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹ÅÕ¥Áµ•¹Ñ½Õ¹Ðé8Áô•ÅÕ¥Áµ•¹ÐÉ•½É¡Ì¤€ƒŠˆ€€ˆ€¬4(€€€€€€€€€€€€€€€€‰íÍ¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¹5…ÍÑ•É•ÍÌ¹¡•­½ÕÑÌ¹½Õ¹Ðé8Áô¡•­•½ÕÑqÉq¸ˆ€¬4(€€€€€€€€€€€€€€€€‰I•Ù¥Í¥½¸íM¡½ÉÑI•Ù¥Í¥½¸¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹I•Ù¥Í¥½¹%¥ô€ƒŠˆ€M…Ù•íÍ…Ù•‘Ñô‰äíÍ…Ù•‘	åôˆ€¬4(€€€€€€€€€€€€€€€€¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%¹!…ÍY…±Õ”4(€€€€€€€€€€€€€€€€€€€€ü€‰qÉq¹±¥•¹Ð¡•­½ÕÐ…Ñ¥Ù”…Ìí}‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑUÍ•É¹…µ•ôˆ4(€€€€€€€€€€€€€€€€€€€€èÍÑÉ¥¹œ¹µÁÑä¤ì4(€€€€€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•É¥¹•ÉÁÉ¥¹Ð¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹Q•áÐ€ô€‰AÕ±°É•ÅÕ¥É•‰•™½É”Ñ¡”™¥ÉÍÐÁÕÍ ™É½´Ñ¡¥ÌA¸ˆì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹½É•½±½È€ôU¥Q¡•µ”¹µ‰•Èì4(€€€€€€€€€€€ô4(€€€€€€€€€€€•±Í”¥˜€¡M¡…É•‘Må¹M•ÉÙ¥”¹!…ÍáÑ•É¹…±¡…¹•Ì¡}‘…Ñ„°Í¹…ÁÍ¡½Ð¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹Q•áÐ€ô€‰Q¡”µ…ÍÑ•È¡…Ì¹•Ý•È¡…¹•Ì¸UÍ”5•É”€˜ÁÕÍ Ñ¼½µ‰¥¹”Ñ¡•´°€ˆ€¬4(€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€‰½ÈAÕ±°Ñ¼É•Á±…”Ñ¡¥ÌAÌ±½…°‘…Ñ„¸ˆì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹½É•½±½È€ôU¥Q¡•µ”¹µ‰•Èì4(€€€€€€€€€€€ô4(€€€€€€€€€€€•±Í”4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…È±…ÍÑMå¹Œ€ô}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•É1…ÍÑMå¹UÑŒü¹Q½1½…±Q¥µ” ¤4(€€€€€€€€€€€€€€€€€€€€¹Q½MÑÉ¥¹œ ‰554°åååä éµ´ÑÐˆ¤€üü€‰Ñ¡¥ÌÍ•ÍÍ¥½¸ˆì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹Q•áÐ€ô€‰Q¡¥ÌAµ…Ñ¡•ÌÑ¡”±…ÍÐ­¹½Ý¸µ…ÍÑ•ÈÉ•Ù¥Í¥½¸¸1…ÍÐÍå¹Œèí±…ÍÑMå¹ô¸ˆì4(€€€€€€€€€€€€€€€}Õ¥‘…¹”¹½É•½±½È€ôU¥Q¡•µ”¹É••¸ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€}ÍÑ…Ñ”¹Q•áÐ€ô€‰5…ÍÑ•ÈÕ¹…Ù…¥±…‰±”ˆì4(€€€€€€€€€€€}‘•Ñ…¥±Ì¹Q•áÐ€ôÁ…Ñ ì4(€€€€€€€€€€€}Õ¥‘…¹”¹Q•áÐ€ô•á•ÁÑ¥½¸¹5•ÍÍ…”ì4(€€€€€€€€€€€}Õ¥‘…¹”¹½É•½±½È€ôU¥Q¡•µ”¹I•ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥M¡½ÝÉÉ½È¡ÍÑÉ¥¹œµ•ÍÍ…”°á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€ì4(€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€‰íµ•ÍÍ…•õqÉq¹qÉq¹í•á•ÁÑ¥½¸¹5•ÍÍ…•ôˆ°4(€€€€€€€€€€€€‰½µÁ…¹ä™¥±”Íå¹Œˆ°4(€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°4(€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(€€€€€€€I•™É•Í¡5…ÍÑ•ÉMÑ…Ñ” ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œM¡½ÉÑI•Ù¥Í¥½¸¡ÍÑÉ¥¹œÉ•Ù¥Í¥½¸¤€ôø4(€€€€€€€ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡É•Ù¥Í¥½¸¤4(€€€€€€€€€€€€ü€‰±•…äˆ4(€€€€€€€€€€€€èÉ•Ù¥Í¥½¹l¸¹5…Ñ ¹5¥¸ à°É•Ù¥Í¥½¸¹1•¹Ñ ¥t¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤ì4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥=Á•¹½½±•É¥Ù•=¹±¥¹” ¤4(€€€ì4(€€€€€€€ÕÍ¥¹œÙ…È½½±”€ô¹•Ü½½±•É¥Ù•Må¹½É´¡}‘…Ñ„°}ÍÑ½É”¤ì4(€€€€€€€½½±”¹M¡½Ý¥…±½œ¡Ñ¡¥Ì¤ì4(€€€€€€€¥˜€¡½½±”¹…Ñ…AÕ±±•¤…Ñ…AÕ±±•€ôÑÉÕ”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥I•µ•µ‰•É5…ÍÑ•ÉM•ÍÍ¥½¸¡‰½½°Í¡½Ý9½Ñ¥™¥…Ñ¥½¸¤4(€€€ì4(€€€€€€€¥˜€¡}µ…ÍÑ•ÉM•ÍÍ¥½¸¥Ì¹Õ±°ñðÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ ¤¤É•ÑÕÉ¸ì4(€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¹½¹Ñ•áÐ¹M•Ð 4(€€€€€€€€€€€Må¹Q…É•Ð¹M¡…É•‘¥±”°4(€€€€€€€€€€€}‘…Ñ„¹M•ÑÑ¥¹Ì¹M¡…É•‘5…ÍÑ•ÉA…Ñ °4(€€€€€€€€€€€}µ…ÍÑ•ÉM•ÍÍ¥½¸¤ì4(€€€€€€€¥˜€¡Í¡½Ý9½Ñ¥™¥…Ñ¥½¸¤5…ÍÑ•ÉM¥¹%¹9½Ñ¥™¥…Ñ¥½¸¹M¡½Ý½È¡Ñ¡¥Ì°}µ…ÍÑ•ÉM•ÍÍ¥½¸¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑÉ¥¹œüI•ÅÕ•ÍÑA…ÍÍÝ½É‘%™9••‘•¡ÍÑÉ¥¹œÁ…Ñ ¤4(€€€ì4(€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}µ…ÍÑ•ÉM•ÍÍ¥½¸ü¹5…ÍÑ•É-•ä¤¤4(€€€€€€€€€€€É•ÑÕÉ¸}µ…ÍÑ•ÉM•ÍÍ¥½¸¹5…ÍÑ•É-•äì4(€€€€€€€¥˜€ …¥±”¹á¥ÍÑÌ¡Á…Ñ ¤ñð€…A½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%ÍA…ÍÍÝ½É‘AÉ½Ñ•Ñ•¡Á…Ñ ¤¤É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡}µ…ÍÑ•ÉA…ÍÍÝ½É¤¤É•ÑÕÉ¸}µ…ÍÑ•ÉA…ÍÍÝ½Éì4(€€€€€€€Ù…ÈÁÉ½µÁÐ€ôA…ÍÍÝ½É‘¥…±½œ¹AÉ½µÁÑ½ÉAÉ½Ñ•Ñ•‘¥±”¡Ñ¡¥Ì°…±±½ÝI•µ•µ‰•É½ÉM•ÍÍ¥½¸èÑÉÕ”¤ì4(€€€€€€€¥˜€¡ÁÉ½µÁÐ¥Ì¹Õ±°¤É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€¥˜€¡ÁÉ½µÁÐ¹I•µ•µ‰•É½ÉM•ÍÍ¥½¸¤}µ…ÍÑ•ÉA…ÍÍÝ½É€ôÁÉ½µÁÐ¹A…ÍÍÝ½Éì4(€€€€€€€É•ÑÕÉ¸ÁÉ½µÁÐ¹A…ÍÍÝ½Éì4(€€€ô4)ô4(