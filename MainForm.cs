namespace InNasc;

public sealed class MainForm : Form
{
    public event EventHandler? SignOutRequested;

    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly NetworkMonitor _networkMonitor = new();

    private readonly TreeView _tree = new();
    private readonly ComboBox _nicPicker = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _statusFilter = new();
    private readonly ComboBox _manufacturerFilter = new();
    private readonly DataGridView _grid = new();
    private readonly Label _scopeTitle = new();
    private readonly Label _scopeSubtitle = new();
    private readonly Label _totalMetric = new();
    private readonly Label _onlineMetric = new();
    private readonly Label _offlineMetric = new();
    private readonly Label _waitingMetric = new();
    private readonly Label _unaddressedMetric = new();
    private readonly Label _statusLabel = new();
    private readonly Label _signedInLabel = new();
    private readonly Label _workspaceCheckoutStatus = new();
    private readonly ToolTip _toolTip = new();
    private readonly Button _checkButton;
    private Button _workspaceCheckoutButton = null!;
    private Button _refreshNicsButton = null!;
    private Button _syncButton = null!;
    private readonly System.Windows.Forms.Timer _syncStatusTimer = new() { Interval = 30000 };
    private readonly System.Windows.Forms.Timer _liveCoauthoringTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _gridSingleClickTimer = new()
    {
        Interval = SystemInformation.DoubleClickTime
    };
    private readonly SemaphoreSlim _liveCoauthoringGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly HashSet<Guid> _expandedEquipmentIds = [];
    private readonly Panel _topHost = new();
    private readonly Panel _contentHost = new();
    private readonly Panel _hierarchyActions = new();
    private readonly Panel _adminPanel = new();
    private Button _homeButton = null!;
    private Button _scannerButton = null!;
    private Button _adminButton = null!;
    private Button _logoutButton = null!;
    private Button _settingsButton = null!;
    private Button _aboutButton = null!;
    private Button _aboutBackButton = null!;
    private readonly MasterWelcomeControl _masterWelcomePage;
    private readonly ClientWelcomeControl _welcomePage;
    private readonly AboutControl _aboutPage;
    private Control _operationsTopBar = null!;
    private Control _welcomeTopBar = null!;
    private Control _aboutTopBar = null!;
    private Control _operationsContent = null!;
    private N8IpScannerForm? _scannerWindow;
    private ClientRecord? _activeClient;
    private string? _legacyMasterPassword;

    private bool _loadingNics;
    private bool _monitoring;
    private bool _checkoutInProgress;
    private string _liveCoauthoringStatus = string.Empty;
    private string _sortColumn = "Description";
    private bool _sortAscending = true;
    private Point _gridDragStart;
    private int _gridDragRowIndex = -1;
    private TreeNode? _dropTargetNode;
    private EquipmentContext? _pendingGridSingleClick;

    public MainForm(AppData data, DataStore store)
    {
        _data = data;
        _store = store;
        UiTheme.SetDarkMode(_data.Settings.DarkMode);
        Text = string.IsNullOrWhiteSpace(_data.ProjectName) ? "InNasc" : _data.ProjectName;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 720);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        ResetVerificationStates();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 272));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Leave enough vertical room for the status text at Windows display scales
        // above 100%. The previous 28-pixel row clipped the font descenders.
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var sidebar = BuildSidebar();
        shell.Controls.Add(sidebar, 0, 0);
        shell.SetRowSpan(sidebar, 3);

        _checkButton = UiTheme.PrimaryButton("Verify devices");
        _checkButton.Click += async (_, _) => await VerifyScopedDevicesAsync();

        _topHost.Dock = DockStyle.Fill;
        _operationsTopBar = BuildTopBar();
        _welcomeTopBar = BuildWelcomeTopBar();
        _aboutTopBar = BuildAboutTopBar();
        _topHost.Controls.Add(_operationsTopBar);
        _topHost.Controls.Add(_welcomeTopBar);
        _topHost.Controls.Add(_aboutTopBar);
        shell.Controls.Add(_topHost, 1, 0);

        _contentHost.Dock = DockStyle.Fill;
        _operationsContent = BuildContent();
        _masterWelcomePage = new MasterWelcomeControl(_data);
        _masterWelcomePage.LocalMasterRequested += ChooseLocalMaster;
        _masterWelcomePage.GoogleDriveRequested += ConfigureGoogleDriveFromWelcome;
        _masterWelcomePage.CreateMasterRequested += CreateMasterFromWelcome;
        _masterWelcomePage.SignInRequested += async (username, password) =>
            await SignInFromWelcomeAsync(username, password);
        _welcomePage = new ClientWelcomeControl(_data);
        _welcomePage.ClientSelected += OpenClient;
        _welcomePage.ClientEditRequested += EditClient;
        _welcomePage.ClientExcelExportRequested += ExportClientExcel;
        _welcomePage.AddClientRequested += AddClient;
        _welcomePage.DeleteClientRequested += DeleteClientFromWelcome;
        _welcomePage.ClientCheckoutRequested += CheckoutClientFromCard;
        _aboutPage = new AboutControl();
        _aboutPage.UpdateRequested += async (_, _) => await UpdateAppAsync();
        _contentHost.Controls.Add(_operationsContent);
        _contentHost.Controls.Add(_masterWelcomePage);
        _contentHost.Controls.Add(_welcomePage);
        _contentHost.Controls.Add(_aboutPage);
        shell.Controls.Add(_contentHost, 1, 1);
        shell.Controls.Add(BuildStatusBar(), 1, 2);
        Controls.Add(shell);

        UiTheme.ApplyTheme(this);
        RefreshTree();
        LoadNetworkAdapters();
        RefreshManufacturerFilter();
        RefreshGrid();
        if (MasterSessionContext.Current is null)
            ShowMasterWelcomePage();
        else
            ShowWelcomePage();
        RefreshSyncIndicator();
        MasterSessionContext.Changed += MasterSessionContext_Changed;
        RefreshMasterSessionUi();
        _syncStatusTimer.Tick += (_, _) => RefreshSyncIndicator();
        _syncStatusTimer.Start();
        _liveCoauthoringTimer.Tick += async (_, _) => await RunLiveCoauthoringAsync();
        FormClosing += (_, _) => TrySave(showError: false);
        FormClosed += (_, _) =>
        {
            MasterSessionContext.Changed -= MasterSessionContext_Changed;
            _syncStatusTimer.Dispose();
            _liveCoauthoringTimer.Stop();
            _liveCoauthoringTimer.Dispose();
            _gridSingleClickTimer.Stop();
            _gridSingleClickTimer.Dispose();
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
        };
    }

    private Control BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Navy,
            Padding = new Padding(14, 0, 14, 14)
        };

        var brand = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = UiTheme.Navy };
        brand.Controls.Add(new InNascBrandLogo(54, 54)
        {
            Location = new Point(0, 14)
        });
        brand.Controls.Add(new Label
        {
            Text = "InNasc",
            AutoSize = false,
            ForeColor = Color.White,
            Font = UiTheme.Font(13, FontStyle.Bold),
            Location = new Point(62, 14),
            Size = new Size(182, 30),
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true
        });
        brand.Controls.Add(new Label
        {
            Text = "Systems in context.",
            AutoSize = false,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = UiTheme.Font(8.5f),
            Location = new Point(63, 44),
            Size = new Size(181, 24),
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        });

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = UiTheme.Navy,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(2, 5, 0, 7),
            Margin = Padding.Empty,
            AutoSize = false
        };

        _homeButton = UiTheme.SidebarIconButton(AppIcons.Home(), "Home");
        _homeButton.Size = new Size(42, 40);
        _homeButton.Margin = new Padding(0, 0, 6, 0);
        _homeButton.Click += (_, _) => ShowWelcomePage();
        _toolTip.SetToolTip(_homeButton, "Home");

        _scannerButton = UiTheme.SidebarIconButton(AppIcons.Scanner(), "N8's IP Scanner");
        _scannerButton.Size = new Size(42, 40);
        _scannerButton.Margin = new Padding(0, 0, 6, 0);
        _scannerButton.Click += (_, _) => OpenIpScanner();
        _toolTip.SetToolTip(_scannerButton, "N8's IP Scanner");

        _syncButton = UiTheme.SidebarIconButton(AppIcons.Sync(), "Company file sync");
        _syncButton.Size = new Size(42, 40);
        _syncButton.Margin = new Padding(0, 0, 6, 0);
        _syncButton.Click += (_, _) => OpenGoogleDriveSync();
        _toolTip.SetToolTip(_syncButton, "Google Drive sync (Local / file share is also available)");

        _settingsButton = UiTheme.SidebarIconButton(AppIcons.Settings(), "Settings");
        _settingsButton.Size = new Size(42, 40);
        _settingsButton.Margin = new Padding(0, 0, 6, 0);
        _settingsButton.Click += (_, _) => OpenSettings();
        _toolTip.SetToolTip(_settingsButton, "Settings");

        _aboutButton = UiTheme.SidebarIconButton(AppIcons.About(), "About");
        _aboutButton.Size = new Size(42, 40);
        _aboutButton.Margin = Padding.Empty;
        _aboutButton.Click += (_, _) => ShowAboutPage();
        _toolTip.SetToolTip(_aboutButton, "About InNasc");

        navigation.Controls.AddRange(
            [_homeButton, _scannerButton, _syncButton, _settingsButton, _aboutButton]);

        _hierarchyActions.Dock = DockStyle.Top;
        _hierarchyActions.Height = 82;
        _hierarchyActions.BackColor = UiTheme.Navy;
        _hierarchyActions.Controls.Add(new Label
        {
            Text = "CLIENT WORKSPACE",
            AutoSize = true,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = UiTheme.Font(8, FontStyle.Bold),
            Location = new Point(4, 3)
        });
        var buttons = new FlowLayoutPanel
        {
            Location = new Point(0, 28),
            Width = 244,
            Height = 40,
            WrapContents = false
        };
        var addLocation = UiTheme.SidebarButton("+ Location");
        addLocation.AutoSize = false;
        addLocation.Size = new Size(110, 36);
        addLocation.Click += (_, _) => AddLocation();
        var addRoom = UiTheme.SidebarButton("+ Room");
        addRoom.AutoSize = false;
        addRoom.Size = new Size(110, 36);
        addRoom.Click += (_, _) => AddRoom();
        buttons.Controls.AddRange([addLocation, addRoom]);
        _hierarchyActions.Controls.Add(buttons);

        _tree.Dock = DockStyle.Fill;
        _tree.BackColor = UiTheme.Navy;
        _tree.ForeColor = Color.FromArgb(226, 232, 240);
        _tree.BorderStyle = BorderStyle.None;
        _tree.Font = UiTheme.Font(9.5f);
        _tree.Indent = 18;
        _tree.ItemHeight = 31;
        _tree.HideSelection = false;
        _tree.ShowLines = false;
        _tree.ShowPlusMinus = true;
        _tree.AllowDrop = true;
        _tree.AfterSelect += (_, _) => RefreshGrid();
        _tree.NodeMouseClick += Tree_NodeMouseClick;
        _tree.ItemDrag += Tree_ItemDrag;
        _tree.DragEnter += Tree_DragOver;
        _tree.DragOver += Tree_DragOver;
        _tree.DragLeave += (_, _) => ClearDropHighlight();
        _tree.DragDrop += Tree_DragDrop;

        _adminPanel.Dock = DockStyle.Bottom;
        _adminPanel.Height = 112;
        _adminPanel.BackColor = UiTheme.Navy;
        _adminPanel.Visible = false;
        _adminPanel.Controls.Add(new Label
        {
            Text = "MASTER ACCESS",
            AutoSize = true,
            ForeColor = UiTheme.SidebarMuted,
            Font = UiTheme.Font(8, FontStyle.Bold),
            Location = new Point(4, 1)
        });
        _adminButton = UiTheme.SidebarButton("Access â€” users & password");
        _adminButton.AutoSize = false;
        _adminButton.Location = new Point(0, 25);
        _adminButton.Size = new Size(244, 36);
        _adminButton.Margin = Padding.Empty;
        _adminButton.Click += (_, _) => OpenMasterAdmin();
        _toolTip.SetToolTip(_adminButton, "Add and manage company workspace users");
        _adminPanel.Controls.Add(_adminButton);
        _logoutButton = UiTheme.SidebarButton("Log out");
        _logoutButton.AutoSize = false;
        _logoutButton.Location = new Point(0, 67);
        _logoutButton.Size = new Size(244, 36);
        _logoutButton.Margin = Padding.Empty;
        _logoutButton.Click += async (_, _) => await LogoutAsync();
        _toolTip.SetToolTip(_logoutButton, "Log out of this company workspace");
        _adminPanel.Controls.Add(_logoutButton);

        sidebar.Controls.Add(_tree);
        sidebar.Controls.Add(_adminPanel);
        sidebar.Controls.Add(_hierarchyActions);
        sidebar.Controls.Add(navigation);
        sidebar.Controls.Add(brand);
        return sidebar;
    }

    private Control BuildWelcomeTopBar()
    {
        var top = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        top.Controls.Add(new Label
        {
            Text = "Welcome to InNasc",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(20, FontStyle.Bold),
            Location = new Point(24, 20)
        });
        top.Controls.Add(new Label
        {
            Text = "Choß½ûÚÚ$z{-®éÜj×æWGv÷&²FFW'2vW&Rf÷VæB Ð¢¢B%&Vg&W6†VBµöæ–5–6¶W"ä—FV×2ä6÷VçC¤ãÒä”2FG&W72÷F–öâ‡2’#°Ð¢ÐÐ¢6F6‚„W†6WF–öâW†6WF–öâÐ¢°Ð¢÷7FGW4Æ&VÂåFW‡BÒB$ä”2&Vg&W6‚f–ÆVC¢¶W†6WF–öâäÖW76vWÒ#°Ð¢ÐÐ¢f–æÆÇÐ¢°Ð¢÷&Vg&W6„æ–74'WGFöâåFW‡BÒ%&Vg&W6‚ä”72#°Ð¢÷&Vg&W6„æ–74'WGFöâäVæ&ÆVBÒG'VS°Ð¢ÐÐ¢ÐÐ Ð¢&—fFRfö–Bæ–5–6¶W%õ6VÆV7FVD–æFW„6†ævVB†ö&¦V7Cò6VæFW"ÂWfVçD&w2RÐ¢°Ð¢–b…öÆöF–ætæ–72ÇÂöæ–5–6¶W"å6VÆV7FVD—FVÒ—2æ÷BæWGv÷&´FFW$6†ö–6R6†ö–6R’&WGW&ã°Ð¢öFFå6WGF–æw2å6VÆV7FVDæ–4–BÒ6†ö–6Räæ–4–C°Ð¢öFFå6WGF–æw2å6VÆV7FVE6÷W&6T—cBÒ6†ö–6Rä—cDFG&W73°Ð¢&W6WEfW&–f–6F–öå7FFW2‚“°Ð¢G'•6fR‚“°Ð¢&Vg&W6„w&–B‚“°Ð¢ÐÐ Ð¢&—fFR7–æ2F6²fW&–g•66÷VDFWf–6W47–æ2‚Ð¢°Ð¢–b‡7G&–ærä—4çVÆÄ÷%v†—FU76R…öFFå6WGF–æw2å6VÆV7FVE6÷W&6T—cB’Ð¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2Â$6†ö÷6RæWGv÷&²FFW"–âF†R–ærg&öÒÖVçR&Vf÷&RfW&–g––ærFWf–6W2â"ÀÐ¢$6†ö÷6RæWGv÷&²FFW""ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&ã°Ð¢ÐÐ¢v—B'VäæWGv÷&´6†V6´7–æ2„vWE66÷VD6öçFW‡G2‚’å6VÆV7B†—FVÒÓâ—FVÒäWV—ÖVçB’åFôÆ—7B‚’“°Ð¢ÐÐ Ð¢&—fFR7–æ2F6²'VäæWGv÷&´6†V6´7–æ2„•&VDöæÇ”6öÆÆV7F–öãÄWV—ÖVçE&V6÷&CãòWV—ÖVçBÒçVÆÂÐ¢°Ð¢–b…öÖöæ—F÷&–ær’&WGW&ã°Ð¢–b‡7G&–ærä—4çVÆÄ÷%v†—FU76R…öFFå6WGF–æw2å6VÆV7FVE6÷W&6T—cB’Ð¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2Â$6†ö÷6RæWGv÷&²FFW"–âF†R–ærg&öÒÖVçR&Vf÷&RfW&–g––ærFWf–6W2â"ÀÐ¢$6†ö÷6RæWGv÷&²FFW""ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&ã°Ð¢ÐÐ¢öÖöæ—F÷&–ærÒG'VS°Ð¢ö6†V6´'WGFöâäVæ&ÆVBÒfÇ6S°Ð¢ö6†V6´'WGFöâåFW‡BÒ$6†V6¶–æ~(
b#°Ð¢G'Ð¢°Ð¢f"F&vWG2ÒWV—ÖVçBóòvWE66÷VD6öçFW‡G2‚’å6VÆV7B†—FVÒÓâ—FVÒäWV—ÖVçB’åFôÆ—7B‚“°Ð¢–b‡F&vWG2ä6÷VçBÓÒÐ¢°Ð¢÷7FGW4Æ&VÂåFW‡BÒ$æòWV—ÖVçBFò6†V6²#°Ð¢&WGW&ã°Ð¢ÐÐ¢÷7FGW4Æ&VÂåFW‡BÒB%fW&–g––ær·F&vWG2ä6÷VçGÒFWf–6R‡2’g&öÒµöFFå6WGF–æw2å6VÆV7FVE6÷W&6T—cGÞ(
b#°Ð¢v—BöæWGv÷&´Ööæ—F÷"ä6†V6´ÆÄ7–æ2€Ð¢F&vWG2ÀÐ¢öFFå6WGF–æw2å6VÆV7FVE6÷W&6T—cBÀÐ¢öFFå6WGF–æw2å–æuF–ÖV÷WDÖ–ÆÆ—6V6öæG2“°Ð¢G'•6fR‡6†÷tW'&÷#¢fÇ6R“°Ð¢&Vg&W6„w&–B‚“°Ð¢÷7FGW4Æ&VÂåFW‡BÒB$ÖçVÂfW&–f–6F–öâ6ö×ÆWFVBB´FFUF–ÖRäæ÷s¦ƒ¦ÖÓ§72GGÒ#°Ð¢ÐÐ¢6F6‚„W†6WF–öâW†6WF–öâÐ¢°Ð¢÷7FGW4Æ&VÂåFW‡BÒB$æWGv÷&²6†V6²W'&÷#¢¶W†6WF–öâäÖW76vWÒ#°Ð¢ÐÐ¢f–æÆÇÐ¢°Ð¢öÖöæ—F÷&–ærÒfÇ6S°Ð¢ö6†V6´'WGFöâäVæ&ÆVBÒG'VS°Ð¢ö6†V6´'WGFöâåFW‡BÒ%fW&–g’FWf–6W2#°Ð¢ÐÐ¢ÐÐ Ð¢&—fFRfö–B–×÷'DW†6VÂ‚Ð¢°Ð¢–b‚Vç7W&Uv÷&·76Uw&—F&ÆR‚’’&WGW&ã°Ð¢W6–ærf"F–ÆörÒæWr÷Väf–ÆTF–ÆöpÐ¢°Ð¢F—FÆRÒ$–×÷'BWV—ÖVçB7&VG6†VWB"ÀÐ¢f–ÇFW"Ò$W†6VÂv÷&¶&öö·2‚¢ç†Ç7‚—Â¢ç†Ç7‚"ÀÐ¢×VÇF—6VÆV7BÒG'VPÐ¢Ó°Ð¢–b†F–Æörå6†÷tF–Æör‡F†—2’ÒF–Æöu&W7VÇBäô²’&WGW&ã°Ð Ð¢G'Ð¢°Ð¢f"66ç2ÒF–Æöräf–ÆTæÖW2å6VÆV7B…†Ç7…6W'f–6Rå66ä–×÷'B’åFôÆ—7B‚“°Ð¢f"–×÷'FVBÒ66ç2å6VÆV7DÖç’‡66âÓâ66âä–×÷'FVE&÷w2’åFôÆ—7B‚“°Ð¢f"&Wf–Wv&ÆU&ö&ÆV×2Ò66ç2å7VÒ‡66âÓàÐ¢66âå6¶—VE&÷w2ä6÷VçB²66âå6†VWD—77VW2ä6÷VçB“°Ð¢–b†–×÷'FVBä6÷VçBÓÒbb&Wf–Wv&ÆU&ö&ÆV×2ÓÒÐ¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2Â$æòWV—ÖVçB&÷w2vW&Rf÷VæB–âF†R6VÆV7FVBv÷&¶&öö²‡2’â"ÀÐ¢$–×÷'BW†6VÂ"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&ã°Ð¢ÐÐ Ð¢f"6Æ–VçBÒ6VÆV7FVD6Æ–VçB‚’óò66W76–&ÆT6Æ–VçG2‚’äf—'7D÷$FVfVÇB‚“°Ð¢f"FD6Æ–VçDgFW%&Wf–WrÒ6Æ–VçB—2çVÆÃ°Ð¢–b†6Æ–VçB—2çVÆÂÐ¢6Æ–VçBÒæWr6Æ–VçE&V6÷&B²æÖRÒ$–×÷'FVB6Æ–VçB"Ó°Ð Ð¢f"Æö6F–öâÒ6VÆV7FVDÆö6F–öâ‚’óò6Æ–VçBäÆö6F–öç2äf—'7D÷$FVfVÇB‚“°Ð¢–b†Æö6F–öâ—2çVÆÂÐ¢Æö6F–öâÒæWrÆö6F–öå&V6÷&B²æÖRÒ$–×÷'FVBÆö6F–öâ"Ó°Ð Ð¢f"F&vWE&ööÒÒ÷G&VRå6VÆV7FVDæöFSòåFr2&ööÕ&V6÷&C°Ð¢f"ÆâÒW†6VÄ–×÷'DÖW&vU6W'f–6RäæÇ—¦R€Ð¢6Æ–VçBÀÐ¢Æö6F–öâÀÐ¢F&vWE&ööÒÀÐ¢–×÷'FVB“°Ð¢W6–ærf"&Wf–WrÒæWrW†6VÄ–×÷'E&Wf–Wtf÷&Ò‡ÆâÂ66ç2“°Ð¢–b‡&Wf–Wrå6†÷tF–Æör‡F†—2’ÒF–Æöu&W7VÇBäô²Ð¢°Ð¢÷7FGW4Æ&VÂåFW‡BÒ$W†6VÂ–×÷'B6æ6VÆVB(	BæòFFv26†ævVB#°Ð¢&WGW&ã°Ð¢ÐÐ Ð¢f"&W7VÇBÒW†6VÄ–×÷'DÖW&vU6W'f–6RäÇ’€Ð¢6Æ–VçBÀÐ¢Æö6F–öâÀÐ¢F&vWE&ööÒÀÐ¢Æâ“°Ð¢–b†FD6Æ–VçDgFW%&Wf–WrÐ¢öFFä6Æ–VçG2äFB†6Æ–VçB“°Ð Ð¢G'•6fR‚“°Ð¢&Vg&W6…G&VR†Æö6F–öâä–B“°Ð¢÷vVÆ6öÖUvRå&Vg&W6„6Æ–VçG2‚“°Ð¢&Vg&W6„ÖçVf7GW&W$f–ÇFW"‚“°Ð¢&Vg&W6„w&–B‚“°Ð¢f"Vç&–6†ÖVçE7VÖÖ'’Ò&W7VÇBäf–ÆÆVD&Ææ´f–VÆG2ÓÒb`Ð¢&W7VÇBäFFVDæWGv÷&´–çFW&f6W2ÓÒ Ð¢ò7G&–æräV×GÐ¢¢B%Ç%Æäf–ÆÆVB·&W7VÇBäf–ÆÆVD&Ææ´f–VÆG3¤ãÒ&Ææ²f–VÆB‡2’æBFFVB"°Ð¢B'·&W7VÇBäFFVDæWGv÷&´–çFW&f6W3¤ãÒæWGv÷&²–çFW&f6R‡2’â#°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢B%&ö6W76VB·&W7VÇBä–×÷'FVE&÷w3¤ãÒW†6VÂ&÷r‡2“¢"°Ð¢B'·&W7VÇBäFFVDFWf–6W3¤ãÒæWrFWf–6R‡2’Â"°Ð¢B'·&W7VÇBäÖW&vVDFWf–6W3¤ãÒÖW&vVB–çFòW†—7F–ærFWf–6R‡2’Â"°Ð¢B'·&W7VÇBåVæ6†ævVDGWÆ–6FW3¤ãÒVæ6†ævVBGWÆ–6FR‡2’ÂæB"°Ð¢B'·&W7VÇBäÖ&–wV÷W5&÷w3¤ãÒÖ&–wV÷W2&÷r‡2’ÆVgBVçF÷V6†VBâ"°Ð¢Vç&–6†ÖVçE7VÖÖ'’ÀÐ¢$–×÷'B6ö×ÆWFR"ÀÐ¢ÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢ÐÐ¢6F6‚„W†6WF–öâW†6WF–öâÐ¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÂB%F†Rv÷&¶&öö²6÷VÆBæ÷B&R–×÷'FVBåÇ%ÆåÇ%Æç¶W†6WF–öâäÖW76vWÒ"ÀÐ¢$–×÷'BW†6VÂ"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâäW'&÷"“°Ð¢ÐÐ¢ÐÐ Ð¢&—fFRfö–BW‡÷'DW†6VÂ‚Ð¢°Ð¢f"6Æ–VçBÒö7F—fT6Æ–VçBóò6VÆV7FVD6Æ–VçB‚“°Ð¢–b†6Æ–VçB—2çVÆÂÐ¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢$6†ö÷6R6Æ–VçB&Vf÷&RW‡÷'F–ærFòW†6VÂâ"ÀÐ¢$W‡÷'BW†6VÂ"ÀÐ¢ÖW76vT&÷„'WGFöç2äô²ÀÐ¢ÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&ã°Ð¢ÐÐ¢W‡÷'D6Æ–VçDW†6VÂ†6Æ–VçB“°Ð¢ÐÐ Ð¢&—fFRfö–BW‡÷'D6Æ–VçDW†6VÂ„6Æ–VçE&V6÷&B6Æ–VçBÐ¢°Ð¢f"6W76–öâÒÖ7FW%6W76–öä6öçFW‡Bä7W'&VçCòå6W76–öã°Ð¢–b‡6W76–öâ—2çVÆÂÇÀÐ¢Ö7FW$66W756W'f–6Rä6ä66W746Æ–VçB…öFFäÖ7FW$66W72Â6W76–öâÂ6Æ–VçBä–B’Ð¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢%F†—26Æ–VçB—2æ÷B76–væVBFò–÷W"–äæ6266÷VçBâ"À¢$6Æ–VçB66W72"ÀÐ¢ÖW76vT&÷„'WGFöç2äô²ÀÐ¢ÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&ã°Ð¢ÐÐ¢W6–ærf"F–ÆörÒæWr6fTf–ÆTF–ÆöpÐ¢°Ð¢F—FÆRÒB$W‡÷'B¶6Æ–VçBäæÖWÒFòW†6VÂ"ÀÐ¢f–ÇFW"Ò$W†6VÂv÷&¶&öö²‚¢ç†Ç7‚—Â¢ç†Ç7‚"ÀÐ¢f–ÇFW$–æFW‚ÒÀÐ¢FVfVÇDW‡BÒ'†Ç7‚"ÀÐ¢FDW‡FVç6–öâÒG'VRÀÐ¢&W7F÷&TF—&V7F÷'’ÒG'VRÀÐ¢f–ÆTæÖRÒB'µ6fTf–ÆTæÖR†6Æ–VçBäæÖR—Òç†Ç7‚ Ð¢Ó°Ð¢–b†F–Æörå6†÷tF–Æör‡F†—2’ÒF–Æöu&W7VÇBäô²’&WGW&ã°Ð¢G'Ð¢°Ð¢f"f–ÆUF‚ÒF‚ä6†ævTW‡FVç6–öâ†F–Æöräf–ÆTæÖRÂ"ç†Ç7‚"“°Ð¢–b‚7G&–æräWVÇ2†f–ÆUF‚ÂF–Æöräf–ÆTæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’b`Ð¢f–ÆRäW†—7G2†f–ÆUF‚’b`Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢B'µF‚ävWDf–ÆTæÖR†f–ÆUF‚—ÒÇ&VG’W†—7G2â&WÆ6R—Cò"ÀÐ¢$6öæf—&ÒW†6VÂW‡÷'B"ÀÐ¢ÖW76vT&÷„'WGFöç2å–W4æòÀÐ¢ÖW76vT&÷„–6öâåv&æ–ærÀÐ¢ÖW76vT&÷„FVfVÇD'WGFöâä'WGFöã"’ÒF–Æöu&W7VÇBå–W2Ð¢&WGW&ã°Ð¢f"W‡÷'FVD6÷VçBÒ†Ç7…6W'f–6RäW‡÷'D6Æ–VçB†f–ÆUF‚ÂöFFÂ6Æ–VçB“°Ð¢÷7FGW4Æ&VÂåFW‡BÐÐ¢B$W‡÷'FVB¶W‡÷'FVD6÷VçC¤ãÒ¶6Æ–VçBäæÖWÒ&V6÷&G2FòµF‚ävWDf–ÆTæÖR†f–ÆUF‚—Ò#°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢B$7&VFVB&VÂç†Ç7‚v÷&¶&öö²f÷"¶6Æ–VçBäæÖWÒv—F‚"°Ð¢B'¶W‡÷'FVD6÷VçC¤ãÒWV—ÖVçB&V6÷&B‡2’â"ÀÐ¢$W‡÷'B6ö×ÆWFR"ÀÐ¢ÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢ÐÐ¢6F6‚„W†6WF–öâW†6WF–öâÐ¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÂB%F†Rv÷&¶&öö²6÷VÆBæ÷B&RW‡÷'FVBåÇ%ÆåÇ%Æç¶W†6WF–öâäÖW76vWÒ"ÀÐ¢$W‡÷'BW†6VÂ"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâäW'&÷"“°Ð¢ÐÐ¢ÐÐ Ð¢&—fFR7FF–27G&–ær6fTf–ÆTæÖR‡7G&–ærfÇVRÐ¢°Ð¢f"–çfÆ–BÒF‚ävWD–çfÆ–Df–ÆTæÖT6†'2‚’åFô†6…6WB‚“°Ð¢f"6ÆVæVBÒæWr7G&–ær‡fÇVRåG&–Ò‚Ð¢å6VÆV7B†6†&7FW"Óâ–çfÆ–Bä6öçF–ç2†6†&7FW"’òrÒr¢6†&7FW"Ð¢åFô'&’‚’’åG&–Ò‚rrÂrârÂrÒr“°Ð¢&WGW&â7G&–ærä—4çVÆÄ÷%v†—FU76R†6ÆVæVB’ò$6Æ–VçB"¢6ÆVæVC°Ð¢ÐÐ Ð¢&—fFR&ööÂVç7W&Uv÷&·76Uw&—F&ÆR„6Æ–VçE&V6÷&CòF&vWD6Æ–VçBÒçVÆÂÐ¢°Ð¢–b…öFFå6WGF–æw2äÖ7FW%v÷&·76U&VDöæÇ’Ð¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢%F†—2v÷&·76Rv2÷VæVBv—F‚&VBÖöæÇ’–äæ6266÷VçBâ"°¢%6–vâ–âv—F‚â÷væW"÷"FV6‚66÷VçBFòÖ¶R6†ævW2â"ÀÐ¢%&VBÖöæÇ’6ö×ç’v÷&·76R"ÀÐ¢ÖW76vT&÷„'WGFöç2äô²ÀÐ¢ÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&âfÇ6S°Ð¢ÐÐ Ð¢f"6Æ–VçBÒF&vWD6Æ–VçBóòö7F—fT6Æ–VçBóò6VÆV7FVD6Æ–VçB‚“°Ð¢–b†6Æ–VçB—2çVÆÂ’&WGW&âG'VS°Ð¢f"6W76–öâÒÖ7FW%6W76–öä6öçFW‡Bä7W'&VçCòå6W76–öã°Ð¢–b‡6W76–öâ—2çVÆÂÇÀÐ¢Ö7FW$66W756W'f–6Rä6ä66W746Æ–VçB…öFFäÖ7FW$66W72Â6W76–öâÂ6Æ–VçBä–B’Ð¢°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢%F†—26Æ–VçB—2æ÷B76–væVBFò–÷W"–äæ6266÷VçBâ"À¢$6Æ–VçB66W72"ÀÐ¢ÖW76vT&÷„'WGFöç2äô²ÀÐ¢ÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&âfÇ6S°Ð¢ÐÐ¢f"6†V6¶÷WBÒöFFäÖ7FW$66W72ä6†V6¶÷WG0Ð¢äf—'7D÷$FVfVÇB†—FVÒÓâ—FVÒä6Æ–VçD–BÓÒ6Æ–VçBä–B“°Ð¢–b†6†V6¶÷WB—2çVÆÂ’&WGW&âG'VS°Ð¢f"÷vç46†V6¶÷WBÒöFFå6WGF–æw2ä7F—fT6†V6¶÷WD6Æ–VçD–BÓÒ6Æ–VçBä–Bb`Ð¢öFFå6WGF–æw2ä7F—fT6†V6¶÷WEFö¶VâÓÒ6†V6¶÷WBä6†V6¶÷WEFö¶Vâb`Ð¢Ö7FW%6W76–öä6öçFW‡Bä7W'&VçCòå6W76–öâåW6W$–BÓÒ6†V6¶÷WBåW6W$–C°Ð¢–b†÷vç46†V6¶÷WB’&WGW&âG'VS°Ð¢f"†öÆFW"Ò7G&–ærä—4çVÆÄ÷%v†—FU76R†6†V6¶÷WBäF—7Æ”æÖRÐ¢ò6†V6¶÷WBåW6W&æÖPÐ¢¢6†V6¶÷WBäF—7Æ”æÖS°Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÀÐ¢B'¶6Æ–VçBäæÖWÒ—26†V6¶VB÷WB'’¶†öÆFW'Òâ—G2&V6÷&G2&RÆö6¶VBöâF†—22VçF–ÂF†B6†V6¶÷WB—26†V6¶VB–âÂ&VÆV6VBÂ÷"F¶Vâ÷fW"g&öÒF†R6Æ–VçB6&Bâ"ÀÐ¢$6Æ–VçB6†V6¶VB÷WB"ÀÐ¢ÖW76vT&÷„'WGFöç2äô²ÀÐ¢ÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°Ð¢&WGW&âfÇ6S°Ð¢ÐÐ Ð¢&—fFRfö–BG'•6fR†&ööÂ6†÷tW'&÷"ÒG'VRÐ¢°Ð¢G'Ð¢°Ð¢÷7F÷&Rå6fR…öFF“°Ð¢&Vg&W6…7–æ4–æF–6F÷"‚“°Ð¢ÐÐ¢6F6‚„W†6WF–öâW†6WF–öâÐ¢°Ð¢–b‡6†÷tW'&÷"Ð¢ÖW76vT&÷‚å6†÷r‡F†—2ÂB$6†ævW26÷VÆBæ÷B&R6fVBåÇ%ÆåÇ%Æç¶W†6WF–öâäÖW76vWÒ"ÀÐ¢%6fRW'&÷""ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâäW'&÷"“°Ð¢ÐÐ¢ÐÐ Ð¢&—fFRfö–B&Vg&W6…7–æ4–æF–6F÷"‚Ð¢°Ð¢–b…÷7–æ4'WGFöâ—2çVÆÂÇÂ÷7–æ4'WGFöâä—4F—7÷6VB’&WGW&ã°Ð¢òòöæÇ’F†R6ö×ç’f–ÆRW6VB'’F†R7W'&VçB6W76–öâ6†÷VÆBFWFW&Ö–æRF†PÐ¢òò–æF–6F÷"â6fVBÂ–æ7F—fRÆö6ÂÆ–æ²×W7Bæ÷B¶VW7–æ6‡&öæ—¦V@Ð¢òòvöövÆRG&—fR6W76–öâÖ&W"†÷"f–6RfW'6’àÐ¢f"7FGW2Ò7–æ4–æF–6F÷%6W'f–6RäWfÇVFR€Ð¢öFFÀÐ¢Ö7FW%6W76–öä6öçFW‡Bä7W'&VçCòåF&vWB“°Ð¢f"6öÆ÷"Ò7FGW2å7FFRÓÒ7–æ4–æF–6F÷%7FFRå7–æ6V@Ð¢òV•F†VÖRäw&VVàÐ¢¢V•F†VÖRäÖ&W#°Ð¢f"&Wf–÷W2Ò÷7–æ4'WGFöâä–ÖvS°Ð¢÷7–æ4'WGFöâä–ÖvRÒ–6öç2å7–æ2†6öÆ÷#¢6öÆ÷"“°Ð¢&Wf–÷W3òäF—7÷6R‚“°Ð¢÷FööÅF—å6WEFööÅF—…÷7–æ4'WGFöâÂ7FGW2åFööÇF—“°Ð¢÷7–æ4'WGFöâä66W76–&ÆTFW67&—F–öâÒ7FGW2åFööÇF—°Ð¢ÐÐ Ð¢&—fFR7FF–26öÆ÷"7FGW46öÆ÷"„æWGv÷&µ7FFR7FFR’Óâ7FFR7v—F6€Ð¢°Ð¢æWGv÷&µ7FFRå&V6†&ÆRÓâV•F†VÖRäw&VVâÀÐ¢æWGv÷&µ7FFRåVç&V6†&ÆRÓâV•F†VÖRå&VBÀÐ¢æWGv÷&µ7FFRäæôFG&W72ÓâV•F†VÖRäw&”ÆVBÀÐ¢æWGv÷&µ7FFRå'F–ÂÓâV•F†VÖRäÖ&W"ÀÐ¢æWGv÷&µ7FFRäÖ4Ö—6ÖF6‚ÓâV•F†VÖRå–VÆÆ÷rÀÐ¢òÓâV•F†VÖRä&ÇVPÐ¢Ó°Ð Ð¢&—fFR7FF–27G&–ær7FGW5FW‡B„æWGv÷&µ7FFR7FFRÂÆöæsòÆFVæ7’’Óâ7FFR7v—F6€Ð¢°Ð¢æWGv÷&µ7FFRå&V6†&ÆRÓâB.)xò¶ÆFVæ7’óòÒ×2"ÀÐ¢æWGv÷&µ7FFRåVç&V6†&ÆRÓâ.)xòöffÆ–æR"ÀÐ¢æWGv÷&µ7FFRäæôFG&W72Óâ.)xòæò•"ÀÐ¢æWGv÷&µ7FFRå'F–ÂÓâ.)xò'F–Â"ÀÐ¢æWGv÷&µ7FFRäÖ4Ö—6ÖF6‚Óâ.)xòÔ2Ö—6ÖF6‚"ÀÐ¢òÓâ.)xòv—F–ær Ð¢Ó°Ð Ð¢&—fFR7FF–27G&–ær–çFW&f6UG—UFW‡B„æWGv÷&´–çFW&f6UG—RG—R’ÓâG—R7v—F6€Ð¢°Ð¢æWGv÷&´–çFW&f6UG—Rä6ö'&æWBÓâ$6ö'&æWB"ÀÐ¢æWGv÷&´–çFW&f6UG—RäU3crÓâ$U3cr"ÀÐ¢òÓâG—RåFõ7G&–ær‚Ð¢Ó°Ð Ð¢&—fFR7FF–27G&–ær–çFW&f6UFööÇF—„æWGv÷&´–çFW&f6U&V6÷&BæWGv÷&´–çFW&f6RÐ¢°Ð¢f"Æ–æW2ÒæWrÆ—7CÇ7G&–æsâ‚“°Ð¢–b‚7G&–ærä—4çVÆÄ÷%v†—FU76R†æWGv÷&´–çFW&f6RäÆ7DæWGv÷&´W'&÷"’Ð¢Æ–æW2äFB†æWGv÷&´–çFW&f6RäÆ7DæWGv÷&´W'&÷"“°Ð¢–b‚7G&–ærä—4çVÆÄ÷%v†—FU76R†æWGv÷&´–çFW&f6RäÖ5fW&–f–6F–öäÖW76vR’Ð¢Æ–æW2äFB†æWGv÷&´–çFW&f6RäÖ5fW&–f–6F–öäÖW76vR“°Ð¢–b†æWGv÷&´–çFW&f6Rä‡GG÷'D÷VâÇÂæWGv÷&´–çFW&f6Rä‡GG5÷'D÷VâÐ¢°Ð¢f"÷'G2ÒæWrÆ—7CÇ7G&–æsâ‚“°Ð¢–b†æWGv÷&´–çFW&f6Rä‡GG÷'D÷Vâ’÷'G2äFB‚#ƒô…EE"“°Ð¢–b†æWGv÷&´–çFW&f6Rä‡GG5÷'D÷Vâ’÷'G2äFB‚#CC2ô…EE2"“°Ð¢Æ–æW2äFB‚B%vV"÷'FÂFWFV7FVBöâ·7G&–ærä¦ö–â‚"æB"Â÷'G2—Òâ"“°Ð¢ÐÐ¢&WGW&âÆ–æW2ä6÷VçBÓÒò%v—F–ærf÷"ÖçVÂfW&–f–6F–öââ"¢7G&–ærä¦ö–â‚%Ç%Æâ"ÂÆ–æW2“°Ð¢ÐÐ Ð¢&—fFR7FF–27G&–ærf÷&ÖDÆ7D6†V6¶VB„FFUF–ÖSòWF2’ÓâWF2—2çVÆÀÐ¢ò$æ÷B6†V6¶VB Ð¢¢WF2åfÇVRåFôÆö6ÅF–ÖR‚’åFõ7G&–ær‚$ÔÔÒBÂƒ¦ÖÓ§72GB"“°Ð§ÐÐ 