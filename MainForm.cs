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
        _adminButton = UiTheme.SidebarButton("Access — users & password");
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
            Text = "Choose a client to open locations, rooms, and equipment",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.7f),
            Location = new Point(26, 62)
        });
        return top;
    }

    private Control BuildAboutTopBar()
    {
        var top = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        top.Controls.Add(new Label
        {
            Text = "About InNasc",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(16, FontStyle.Bold),
            Location = new Point(24, 27)
        });
        top.Controls.Add(new Label
        {
            Text = "Credits, revision information, and application details",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.7f),
            Location = new Point(26, 58)
        });
        _aboutBackButton = UiTheme.SecondaryButton("Back to sign in");
        _aboutBackButton.AutoSize = false;
        _aboutBackButton.Size = new Size(132, 38);
        _aboutBackButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _aboutBackButton.Location = new Point(0, 35);
        _aboutBackButton.Click += (_, _) => ShowMasterWelcomePage();
        _aboutBackButton.Visible = false;
        top.Controls.Add(_aboutBackButton);
        top.Resize += (_, _) => _aboutBackButton.Location = new Point(
            Math.Max(24, top.ClientSize.Width - _aboutBackButton.Width - 24),
            35);
        return top;
    }

    private Control BuildTopBar()
    {
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            Padding = new Padding(24, 0, 22, 0)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = "Equipment Operations",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(16, FontStyle.Bold),
            Location = new Point(0, 27)
        });
        heading.Controls.Add(new Label
        {
            Text = "Manual verification, addressing, and equipment records",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.7f),
            Location = new Point(2, 58)
        });
        var actions = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0),
            Margin = Padding.Empty
        };

        _checkButton.AutoSize = false;
        _checkButton.Size = new Size(112, 34);
        var import = UiTheme.SecondaryButton("Import Excel");
        import.AutoSize = false;
        import.Size = new Size(110, 34);
        import.Click += (_, _) => ImportExcel();
        var export = UiTheme.SecondaryButton("Export Excel");
        export.AutoSize = false;
        export.Size = new Size(110, 34);
        export.Click += (_, _) => ExportExcel();
        buttonRow.Controls.Add(export);
        buttonRow.Controls.Add(import);
        buttonRow.Controls.Add(_checkButton);

        var nicRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 1, 0, 0),
            Margin = Padding.Empty
        };
        var verifyFrom = new Label
        {
            Text = "Verify from",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 10, 8, 0)
        };
        _nicPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        _nicPicker.Width = 235;
        _nicPicker.Margin = new Padding(0, 2, 0, 0);
        UiTheme.ConfigureUniformComboBox(_nicPicker);
        _nicPicker.SelectedIndexChanged += NicPicker_SelectedIndexChanged;
        _refreshNicsButton = UiTheme.SecondaryButton("Refresh NICs");
        _refreshNicsButton.AutoSize = false;
        _refreshNicsButton.Size = new Size(102, 32);
        _refreshNicsButton.Margin = new Padding(8, 1, 0, 0);
        _refreshNicsButton.Click += (_, _) => RefreshNetworkAdapters();
        nicRow.Controls.Add(_nicPicker);
        nicRow.Controls.Add(verifyFrom);
        nicRow.Controls.Add(_refreshNicsButton);
        actions.Controls.Add(buttonRow, 0, 0);
        actions.Controls.Add(nicRow, 0, 1);

        top.Controls.Add(heading, 0, 0);
        top.Controls.Add(actions, 1, 0);
        return top;
    }

    private Control BuildContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Canvas,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22, 18, 22, 18)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var scope = new Panel { Dock = DockStyle.Fill };
        _scopeTitle.AutoSize = true;
        _scopeTitle.Font = UiTheme.Font(20, FontStyle.Bold);
        _scopeTitle.ForeColor = UiTheme.Text;
        _scopeTitle.Location = new Point(0, 0);
        _scopeSubtitle.AutoSize = true;
        _scopeSubtitle.Font = UiTheme.Font(9);
        _scopeSubtitle.ForeColor = UiTheme.Muted;
        _scopeSubtitle.Location = new Point(2, 47);
        scope.Controls.AddRange([_scopeTitle, _scopeSubtitle]);

        var workspaceActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 12, 6)
        };
        workspaceActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspaceActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        workspaceActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        workspaceActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        workspaceActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _workspaceCheckoutStatus.AutoSize = false;
        _workspaceCheckoutStatus.Dock = DockStyle.Fill;
        _workspaceCheckoutStatus.ForeColor = UiTheme.Muted;
        _workspaceCheckoutStatus.Font = UiTheme.Font(8.3f, FontStyle.Bold);
        _workspaceCheckoutStatus.TextAlign = ContentAlignment.MiddleRight;
        _workspaceCheckoutStatus.AutoEllipsis = true;
        _workspaceCheckoutStatus.Margin = new Padding(0, 0, 12, 0);
        _workspaceCheckoutStatus.Visible = false;

        _workspaceCheckoutButton = UiTheme.SecondaryButton("Check out client");
        _workspaceCheckoutButton.AutoSize = false;
        _workspaceCheckoutButton.Dock = DockStyle.Fill;
        _workspaceCheckoutButton.Margin = Padding.Empty;
        _workspaceCheckoutButton.TextAlign = ContentAlignment.MiddleCenter;
        _workspaceCheckoutButton.Visible = false;
        _workspaceCheckoutButton.Click += (_, _) =>
        {
            if (_activeClient is not null)
                CheckoutClientFromCard(_activeClient);
        };

        var add = UiTheme.PrimaryButton("+ Add");
        add.AutoSize = false;
        add.Dock = DockStyle.Fill;
        add.Margin = Padding.Empty;
        add.TextAlign = ContentAlignment.MiddleCenter;
        add.Click += (_, _) => AddEquipment();

        workspaceActions.Controls.Add(_workspaceCheckoutStatus, 0, 0);
        workspaceActions.Controls.Add(_workspaceCheckoutButton, 1, 0);
        workspaceActions.Controls.Add(add, 3, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            Padding = new Padding(0, 0, 0, 12)
        };
        for (var i = 0; i < 5; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        metrics.Controls.Add(CreateMetricCard("TOTAL EQUIPMENT", _totalMetric, UiTheme.Blue), 0, 0);
        metrics.Controls.Add(CreateMetricCard("ONLINE", _onlineMetric, UiTheme.Green), 1, 0);
        metrics.Controls.Add(CreateMetricCard("OFFLINE", _offlineMetric, UiTheme.Red), 2, 0);
        metrics.Controls.Add(CreateMetricCard("WAITING", _waitingMetric, UiTheme.Blue), 3, 0);
        metrics.Controls.Add(CreateMetricCard("NO IP", _unaddressedMetric, UiTheme.GrayLed), 4, 0);

        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 4,
            Padding = new Padding(12, 10, 12, 8),
            Margin = new Padding(0)
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _search.Dock = DockStyle.Fill;
        _search.AutoSize = false;
        _search.PlaceholderText = "Search equipment, IP, MAC, serial, room…";
        _search.Font = UiTheme.Font(10);
        _search.TextChanged += (_, _) => RefreshGrid();
        filters.Controls.Add(_search, 0, 0);
        _statusFilter.Dock = DockStyle.Fill;
        _statusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusFilter.Font = UiTheme.Font(10);
        UiTheme.ConfigureUniformComboBox(_statusFilter);
        _statusFilter.Items.AddRange([
            "All statuses", "Online", "Offline", "Partially online", "MAC mismatch",
            "Waiting to verify", "No IP"
        ]);
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) => RefreshGrid();
        filters.Controls.Add(_statusFilter, 1, 0);
        _manufacturerFilter.Dock = DockStyle.Fill;
        _manufacturerFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _manufacturerFilter.Font = UiTheme.Font(10);
        UiTheme.ConfigureUniformComboBox(_manufacturerFilter);
        _manufacturerFilter.SelectedIndexChanged += (_, _) => RefreshGrid();
        filters.Controls.Add(_manufacturerFilter, 2, 0);
        var clear = UiTheme.SecondaryButton("Clear");
        clear.AutoSize = false;
        clear.Dock = DockStyle.Fill;
        clear.Click += (_, _) =>
        {
            _search.Clear();
            _statusFilter.SelectedIndex = 0;
            if (_manufacturerFilter.Items.Count > 0) _manufacturerFilter.SelectedIndex = 0;
        };
        filters.Controls.Add(clear, 3, 0);

        ConfigureGrid();
        content.Controls.Add(scope, 0, 0);
        content.Controls.Add(workspaceActions, 0, 1);
        content.Controls.Add(metrics, 0, 2);
        content.Controls.Add(filters, 0, 3);
        content.Controls.Add(_grid, 0, 4);
        return content;
    }

    private static Control CreateMetricCard(string title, Label valueLabel, Color accent)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(17, 11, 12, 8)
        };
        card.Controls.Add(new Panel
        {
            BackColor = accent,
            Location = new Point(0, 12),
            Size = new Size(4, 47)
        });
        card.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8, FontStyle.Bold),
            Location = new Point(17, 11)
        });
        valueLabel.Text = "0";
        valueLabel.AutoSize = true;
        valueLabel.ForeColor = UiTheme.Text;
        valueLabel.Font = UiTheme.Font(21, FontStyle.Bold);
        valueLabel.Location = new Point(15, 31);
        card.Controls.Add(valueLabel);
        return card;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = UiTheme.Surface;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ColumnHeadersHeight = 42;
        _grid.RowTemplate.Height = 41;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiTheme.HeaderSurface,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            SelectionBackColor = UiTheme.HeaderSurface,
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(9.2f),
            SelectionBackColor = UiTheme.Selection,
            SelectionForeColor = UiTheme.Text,
            Padding = new Padding(4, 0, 4, 0)
        };
        _grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.AlternateSurface;
        _grid.GridColor = UiTheme.GridLine;
        AddGridColumn("Status", "VERIFICATION", 112);
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "OpenPortal",
            HeaderText = "WEB PORTAL",
            Width = 104,
            Text = "Open",
            UseColumnTextForButtonValue = false,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        AddGridColumn("Description", "DESCRIPTION", 210);
        AddGridColumn("Manufacturer", "MANUFACTURER", 125);
        AddGridColumn("PartNumber", "MODEL / PART", 135);
        AddGridColumn("Hostname", "HOSTNAME", 125);
        AddGridColumn("PrimaryIp", "PRIMARY IP", 118);
        AddGridColumn("Mac1", "MAC ADDRESS", 145);
        AddGridColumn("SerialNumber", "SERIAL", 125);
        AddGridColumn("Firmware", "FIRMWARE", 92);
        AddGridColumn("ConfigurationFiles", "CONFIG FILES", 105);
        AddGridColumn("Location", "LOCATION", 120);
        AddGridColumn("Room", "ROOM", 105);
        AddGridColumn("LastChecked", "LAST CHECKED", 145);

        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
        _grid.CellClick += Grid_CellClick;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _gridSingleClickTimer.Tick += GridSingleClickTimer_Tick;
        _grid.CellMouseDown += Grid_CellMouseDown;
        _grid.CellContentClick += Grid_CellContentClick;
        _grid.MouseMove += Grid_MouseMove;
        _grid.MouseUp += (_, _) => _gridDragRowIndex = -1;
        _grid.SelectionChanged += (_, _) =>
        {
            var selectedCount = SelectedEquipmentContexts().Count;
            if (selectedCount > 1)
                _statusLabel.Text =
                    $"{selectedCount:N0} equipment records selected — drag them onto a room or use Move to room";
        };
        _grid.ContextMenuStrip = BuildEquipmentMenu();
        _toolTip.SetToolTip(_grid,
            "Use Ctrl or Shift to select multiple rows, then drag them onto a room to move them together.");
        UiTheme.EnableDoubleBuffer(_grid);
    }

    private void Grid_CellClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 ||
            _grid.Columns[eventArgs.ColumnIndex].Name == "OpenPortal" ||
            _grid.Rows[eventArgs.RowIndex].Tag is not EquipmentContext context ||
            Control.ModifierKeys != Keys.None ||
            !HasInterfaceDetails(context.Equipment))
            return;

        _gridSingleClickTimer.Stop();
        _pendingGridSingleClick = context;
        _gridSingleClickTimer.Start();
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        _gridSingleClickTimer.Stop();
        _pendingGridSingleClick = null;
        if (eventArgs.RowIndex >= 0 && eventArgs.ColumnIndex >= 0 &&
            _grid.Columns[eventArgs.ColumnIndex].Name != "OpenPortal")
            EditSelectedEquipment();
    }

    private void GridSingleClickTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _gridSingleClickTimer.Stop();
        var context = _pendingGridSingleClick;
        _pendingGridSingleClick = null;
        if (context is null || !HasInterfaceDetails(context.Equipment)) return;

        var expanded = !_expandedEquipmentIds.Remove(context.Equipment.Id);
        if (expanded) _expandedEquipmentIds.Add(context.Equipment.Id);
        RefreshGrid(context.Equipment.Id);
        _statusLabel.Text = expanded
            ? $"{context.Equipment.Description}: showing IP interfaces"
            : $"{context.Equipment.Description}: IP interfaces hidden";
    }

    private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 ||
            _grid.Columns[eventArgs.ColumnIndex].Name != "OpenPortal" ||
            _grid.Rows[eventArgs.RowIndex].Tag is not NetworkInterfaceContext context ||
            string.IsNullOrWhiteSpace(context.NetworkInterface.PortalUrl))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                context.NetworkInterface.PortalUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The web portal could not be opened.\r\n\r\n{exception.Message}",
                "Open web portal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddGridColumn(string name, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Programmatic
        });
    }

    private ContextMenuStrip BuildEquipmentMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Edit", null, (_, _) => EditSelectedEquipment());
        var verify = menu.Items.Add("Verify this device", null, async (_, _) =>
        {
            var selected = SelectedEquipmentContexts();
            if (selected.Count > 0)
                await RunNetworkCheckAsync(selected.Select(item => item.Equipment).ToList());
        });
        menu.Items.Add(new ToolStripSeparator());
        var move = menu.Items.Add("Move to room…", null, (_, _) => MoveSelectedEquipment());
        menu.Items.Add("Duplicate", null, (_, _) => DuplicateSelectedEquipment());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => DeleteSelectedEquipment());
        menu.Opening += (_, _) =>
        {
            var count = SelectedEquipmentContexts().Count;
            verify.Text = count > 1 ? $"Verify {count:N0} selected devices" : "Verify this device";
            move.Text = count > 1 ? $"Move {count:N0} items to room…" : "Move to room…";
            verify.Enabled = count > 0;
            move.Enabled = count > 0;
        };
        return menu;
    }

    private void Grid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0) return;
        if (eventArgs.Button == MouseButtons.Right)
        {
            if (!_grid.Rows[eventArgs.RowIndex].Selected)
                _grid.ClearSelection();
            _grid.Rows[eventArgs.RowIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[eventArgs.RowIndex].Cells[0];
            _gridDragRowIndex = -1;
            return;
        }
        if (eventArgs.Button != MouseButtons.Left) return;
        _gridDragStart = new Point(eventArgs.X, eventArgs.Y);
        _gridDragRowIndex = eventArgs.RowIndex;
    }

    private void Grid_MouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left || _gridDragRowIndex < 0 ||
            _gridDragRowIndex >= _grid.Rows.Count)
            return;

        var dragSize = SystemInformation.DragSize;
        var dragBounds = new Rectangle(
            _gridDragStart.X - dragSize.Width / 2,
            _gridDragStart.Y - dragSize.Height / 2,
            dragSize.Width,
            dragSize.Height);
        if (dragBounds.Contains(eventArgs.Location)) return;
        var context = EquipmentContextFromTag(_grid.Rows[_gridDragRowIndex].Tag);
        if (context is null) return;
        var selected = SelectedEquipmentContexts();
        if (selected.Count == 0 || selected.All(item => item.Equipment.Id != context.Equipment.Id))
            selected = [context];

        _gridDragRowIndex = -1;
        var description = selected.Count == 1
            ? selected[0].Equipment.Description
            : $"{selected.Count:N0} selected equipment records";
        var payload = new EquipmentDragPayload(
            selected.Select(item => item.Equipment.Id).ToArray(),
            description,
            selected.Select(item => item.Room.Id).Distinct().ToArray());
        _statusLabel.Text = $"Moving {description} — drop onto a room";
        _grid.DoDragDrop(payload, DragDropEffects.Move);
        ClearDropHighlight();
    }

    private void Tree_ItemDrag(object? sender, ItemDragEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left ||
            eventArgs.Item is not TreeNode node ||
            node.Tag is not RoomRecord room)
            return;
        var sourceLocation = FindLocation(room);
        if (sourceLocation is null) return;

        _tree.SelectedNode = node;
        _statusLabel.Text = $"Moving {room.Name} — drop onto a location";
        _tree.DoDragDrop(
            new RoomDragPayload(room.Id, room.Name, sourceLocation.Id),
            DragDropEffects.Move);
        ClearDropHighlight();
    }

    private void Tree_DragOver(object? sender, DragEventArgs eventArgs)
    {
        var point = _tree.PointToClient(new Point(eventArgs.X, eventArgs.Y));
        var targetNode = _tree.GetNodeAt(point);
        if (GetEquipmentDragPayload(eventArgs.Data) is { } equipmentPayload)
        {
            if (targetNode?.Tag is not RoomRecord targetRoom ||
                equipmentPayload.SourceRoomIds.All(id => id == targetRoom.Id))
            {
                eventArgs.Effect = DragDropEffects.None;
                ClearDropHighlight();
                _statusLabel.Text = "Drop equipment onto a different room";
                return;
            }

            eventArgs.Effect = DragDropEffects.Move;
            HighlightDropNode(targetNode);
            _statusLabel.Text = $"Move {equipmentPayload.Description} to {ContainerPath(targetRoom)}";
            ScrollTreeDuringDrag(point, targetNode);
            return;
        }

        if (GetRoomDragPayload(eventArgs.Data) is { } roomPayload)
        {
            if (targetNode?.Tag is not LocationRecord targetLocation ||
                targetLocation.Id == roomPayload.SourceLocationId)
            {
                eventArgs.Effect = DragDropEffects.None;
                ClearDropHighlight();
                _statusLabel.Text = "Drop the room onto a different location";
                return;
            }

            eventArgs.Effect = DragDropEffects.Move;
            HighlightDropNode(targetNode);
            _statusLabel.Text = $"Move {roomPayload.RoomName} to {LocationPath(targetLocation)}";
            ScrollTreeDuringDrag(point, targetNode);
            return;
        }

        eventArgs.Effect = DragDropEffects.None;
        ClearDropHighlight();
    }

    private void ScrollTreeDuringDrag(Point point, TreeNode targetNode)
    {
        if (point.Y < 24)
            targetNode.PrevVisibleNode?.EnsureVisible();
        else if (point.Y > _tree.ClientSize.Height - 24)
            targetNode.NextVisibleNode?.EnsureVisible();
    }

    private void Tree_DragDrop(object? sender, DragEventArgs eventArgs)
    {
        var point = _tree.PointToClient(new Point(eventArgs.X, eventArgs.Y));
        var targetNode = _tree.GetNodeAt(point);
        var equipmentPayload = GetEquipmentDragPayload(eventArgs.Data);
        var roomPayload = GetRoomDragPayload(eventArgs.Data);
        ClearDropHighlight();
        if (equipmentPayload is not null && targetNode?.Tag is RoomRecord targetRoom)
        {
            MoveEquipmentToRoom(equipmentPayload.EquipmentIds, targetRoom);
            return;
        }
        if (roomPayload is not null && targetNode?.Tag is LocationRecord targetLocation)
            MoveRoomToLocation(roomPayload.RoomId, targetLocation);
    }

    private static EquipmentDragPayload? GetEquipmentDragPayload(IDataObject? data) =>
        data?.GetDataPresent(typeof(EquipmentDragPayload)) == true
            ? data.GetData(typeof(EquipmentDragPayload)) as EquipmentDragPayload
            : null;

    private static RoomDragPayload? GetRoomDragPayload(IDataObject? data) =>
        data?.GetDataPresent(typeof(RoomDragPayload)) == true
            ? data.GetData(typeof(RoomDragPayload)) as RoomDragPayload
            : null;

    private void HighlightDropNode(TreeNode node)
    {
        if (ReferenceEquals(_dropTargetNode, node)) return;
        ClearDropHighlight();
        _dropTargetNode = node;
        node.BackColor = UiTheme.Blue;
        node.ForeColor = Color.White;
    }

    private void ClearDropHighlight()
    {
        if (_dropTargetNode is null) return;
        _dropTargetNode.BackColor = Color.Empty;
        _dropTargetNode.ForeColor = Color.Empty;
        _dropTargetNode = null;
    }

    private Control BuildStatusBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(22, 4, 22, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Ready";
        _statusLabel.ForeColor = UiTheme.Muted;
        _statusLabel.Font = UiTheme.Font(8.5f);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Margin = Padding.Empty;
        panel.Controls.Add(_statusLabel, 0, 0);

        _signedInLabel.AutoSize = true;
        _signedInLabel.ForeColor = UiTheme.Muted;
        _signedInLabel.Font = UiTheme.Font(8.5f, FontStyle.Bold);
        _signedInLabel.Anchor = AnchorStyles.Right;
        _signedInLabel.TextAlign = ContentAlignment.MiddleRight;
        _signedInLabel.Margin = new Padding(18, 0, 0, 0);
        _signedInLabel.Visible = false;
        panel.Controls.Add(_signedInLabel, 1, 0);
        return panel;
    }

    private void ShowWelcomePage()
    {
        if (MasterSessionContext.Current is null)
        {
            ShowMasterWelcomePage();
            return;
        }
        _activeClient = null;
        _masterWelcomePage.Visible = false;
        _tree.Visible = false;
        _hierarchyActions.Visible = false;
        _welcomePage.RefreshClients();
        _aboutPage.Visible = false;
        _aboutTopBar.Visible = false;
        _operationsContent.Visible = false;
        _operationsTopBar.Visible = false;
        _welcomePage.Visible = true;
        _welcomeTopBar.Visible = true;
        RefreshWorkspaceCheckoutState();
        _welcomePage.BringToFront();
        _welcomeTopBar.BringToFront();
        _statusLabel.Text = "Choose a client to open its equipment workspace";
    }

    private void ShowMasterWelcomePage()
    {
        _activeClient = null;
        _tree.Visible = false;
        _hierarchyActions.Visible = false;
        _adminPanel.Visible = false;
        _welcomePage.Visible = false;
        _aboutPage.Visible = false;
        _operationsContent.Visible = false;
        _operationsTopBar.Visible = false;
        _welcomeTopBar.Visible = false;
        _aboutTopBar.Visible = false;
        _masterWelcomePage.RefreshState();
        _masterWelcomePage.Visible = true;
        _masterWelcomePage.BringToFront();
        RefreshWorkspaceCheckoutState();
        _statusLabel.Text = "Choose a company workspace and sign in";
        _signedInLabel.Visible = false;
    }

    private void ChooseLocalMaster()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose InNasc Master File",
            Filter = InNascFileTypes.CompanyFilter
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _legacyMasterPassword = null;
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (PortableDataService.IsPasswordProtected(bytes))
            {
                _legacyMasterPassword = LegacyMasterMigrationForm.Prompt(this);
                if (_legacyMasterPassword is null) return;
            }
            _ = SharedSyncService.LinkPath(
                dialog.FileName,
                _data,
                _store,
                _legacyMasterPassword);
            _masterWelcomePage.SelectTarget(SyncTarget.SharedFile);
            _masterWelcomePage.SetBusy(false,
                _legacyMasterPassword is null
                    ? "Company selected. Enter your user account below."
                    : "Legacy company selected. The Owner must sign in to complete the one-time migration.");
        }
        catch (Exception exception)
        {
            _masterWelcomePage.ShowError(exception.Message);
        }
    }

    private void ConfigureGoogleDriveFromWelcome()
    {
        using var google = new GoogleDriveSyncForm(_data, _store, connectionOnly: true);
        google.ShowDialog(this);
        _masterWelcomePage.SelectTarget(SyncTarget.GoogleDrive);
        _masterWelcomePage.RefreshState();
    }

    private void CreateMasterFromWelcome()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Create InNasc Master File",
            Filter = "InNasc company (*.nasc)|*.nasc",
            DefaultExt = "nasc",
            AddExtension = true,
            FileName = "InNasc-Company.nasc",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var owner = MasterOwnerSetupForm.Prompt(this);
        if (owner is null) return;
        try
        {
            _data.MasterAccess = owner.Access;
            var result = SharedSyncService.CreateMaster(
                dialog.FileName,
                _data,
                _store,
                owner.Session);
            _data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
            _store.Save(_data);
            MasterSessionContext.Set(
                SyncTarget.SharedFile,
                result.Path,
                owner.Session);
            MasterSignInNotification.ShowFor(this, owner.Session);
            RefreshAfterSharedDataChange();
        }
        catch (Exception exception)
        {
            MasterSessionContext.Clear();
            _masterWelcomePage.ShowError(exception.Message);
        }
    }

    private async Task SignInFromWelcomeAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            _masterWelcomePage.ShowError("Enter both a username and password.");
            return;
        }
        _masterWelcomePage.SetBusy(true, "Signing in and synchronizing the company workspace…");
        try
        {
            if (_masterWelcomePage.SelectedTarget == SyncTarget.GoogleDrive)
                await SignInGoogleMasterAsync(username, password);
            else
                SignInSharedMaster(username, password);
            _masterWelcomePage.ClearCredentials();
            RefreshAfterSharedDataChange();
            MasterSignInNotification.ShowFor(this, MasterSessionContext.Current!.Session);
        }
        catch (Exception exception)
        {
            MasterSessionContext.Clear();
            _masterWelcomePage.ShowError(exception.Message);
        }
        finally
        {
            if (MasterSessionContext.Current is null)
                _masterWelcomePage.SetBusy(false, string.Empty);
        }
    }

    private void SignInSharedMaster(string username, string password)
    {
        var path = _data.Settings.SharedMasterPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose a local or file-share Master first.");
        var bytes = File.ReadAllBytes(path);
        if (PortableDataService.IsPasswordProtected(bytes) && _legacyMasterPassword is null)
        {
            _legacyMasterPassword = LegacyMasterMigrationForm.Prompt(this);
            if (_legacyMasterPassword is null)
                throw new OperationCanceledException("The legacy company migration was canceled.");
        }
        var access = PortableDataService.ReadMasterAccess(bytes, _legacyMasterPassword);
        var session = MasterAccessService.UsesAccountUnlock(access)
            ? MasterAccessService.SignIn(access, username, password)
            : MasterAccessService.UpgradeLegacyOwner(access, username, password);
        if (!MasterAccessService.UsesAccountUnlock(
                PortableDataService.ReadMasterAccess(bytes, _legacyMasterPassword)))
        {
            _ = SharedSyncService.MigrateClientSubmatrices(
                _data,
                _store,
                _legacyMasterPassword,
                session);
            SharedSyncService.SaveAccessControl(
                _data,
                _store,
                access,
                session,
                initialSetup: false,
                _legacyMasterPassword);
            _legacyMasterPassword = null;
        }
        var snapshot = SharedSyncService.Inspect(path, session.MasterKey);
        if (!ResumeOwnedCheckout(
                snapshot.Contents.Data.MasterAccess,
                session,
                SyncTarget.SharedFile,
                snapshot.Fingerprint))
            _ = SharedSyncService.Pull(_data, _store, session.MasterKey, session);
        _data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
        _store.Save(_data);
        MasterSessionContext.Set(SyncTarget.SharedFile, path, session);
    }

    private async Task SignInGoogleMasterAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId))
            throw new InvalidOperationException("Connect a Google Drive Master first.");
        if (!GoogleDriveService.HasGoogleSignIn)
            throw new InvalidOperationException("Sign in with Google before opening this company workspace.");
        var remote = await GoogleDriveService.DownloadAsync(_data.Settings);
        if (PortableDataService.IsPasswordProtected(remote.Contents) && _legacyMasterPassword is null)
        {
            _legacyMasterPassword = LegacyMasterMigrationForm.Prompt(this);
            if (_legacyMasterPassword is null)
                throw new OperationCanceledException("The legacy company migration was canceled.");
        }
        var access = PortableDataService.ReadMasterAccess(remote.Contents, _legacyMasterPassword);
        var accountUnlock = MasterAccessService.UsesAccountUnlock(access);
        var session = accountUnlock
            ? MasterAccessService.SignIn(access, username, password)
            : MasterAccessService.UpgradeLegacyOwner(access, username, password);
        if (!accountUnlock)
        {
            var legacyMaster = PortableDataService.ImportBytes(
                remote.Contents,
                _legacyMasterPassword).Data;
            _ = await GoogleDriveSyncService.MigrateClientSubmatricesAsync(
                _data,
                legacyMaster,
                _store,
                _legacyMasterPassword,
                session);
            await GoogleDriveSyncService.SaveAccessControlAsync(
                _data,
                _store,
                access,
                session,
                initialSetup: false,
                _legacyMasterPassword);
            _legacyMasterPassword = null;
        }
        var snapshot = await GoogleDriveSyncService.InspectAsync(_data, session.MasterKey);
        if (!ResumeOwnedCheckout(
                snapshot.Contents.Data.MasterAccess,
                session,
                SyncTarget.GoogleDrive,
                snapshot.Fingerprint))
            _ = GoogleDriveSyncService.Pull(_data, _store, snapshot, session.MasterKey, session);
        _data.Settings.LastMasterTarget = nameof(SyncTarget.GoogleDrive);
        _store.Save(_data);
        MasterSessionContext.Set(SyncTarget.GoogleDrive, _data.Settings.GoogleDriveFileId, session);
    }

    private bool ResumeOwnedCheckout(
        MasterAccessControl access,
        MasterSession session,
        SyncTarget target,
        string masterFingerprint)
    {
        if (!_data.Settings.ActiveCheckoutClientId.HasValue) return false;
        if (!string.Equals(
                _data.Settings.ActiveCheckoutTarget,
                target.ToString(),
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "This PC has an unfinished checkout from a different Master connection. " +
                "Reconnect to that Master and check it in before switching.");
        var checkout = access.Checkouts.FirstOrDefault(item =>
            item.ClientId == _data.Settings.ActiveCheckoutClientId &&
            item.CheckoutToken == _data.Settings.ActiveCheckoutToken);
        if (checkout is null || checkout.UserId != session.UserId)
        {
            _ = CheckoutRecoveryService.PreserveLostCheckout(
                _data,
                _store,
                access);
            return false;
        }
        _data.MasterAccess = MasterAccessService.Clone(access);
        _data.Settings.MasterWorkspaceReadOnly = false;
        if (target == SyncTarget.GoogleDrive)
        {
            _data.Settings.GoogleDriveFingerprint = masterFingerprint;
            _data.Settings.GoogleDriveRemoteChangesDetected = false;
        }
        else
        {
            _data.Settings.SharedMasterFingerprint = masterFingerprint;
        }
        _store.Save(_data);
        return true;
    }

    private void ShowAboutPage()
    {
        _activeClient = null;
        _masterWelcomePage.Visible = false;
        _tree.Visible = false;
        _hierarchyActions.Visible = false;
        _welcomePage.Visible = false;
        _welcomeTopBar.Visible = false;
        _operationsContent.Visible = false;
        _operationsTopBar.Visible = false;
        _aboutPage.Visible = true;
        _aboutTopBar.Visible = true;
        _aboutPage.BringToFront();
        _aboutTopBar.BringToFront();
        _aboutBackButton.Visible = MasterSessionContext.Current is null;
        _statusLabel.Text = $"InNasc revision {AppInfo.Revision}";
    }

    private async Task UpdateAppAsync()
    {
        using var progressForm = new AppUpdateProgressForm();
        progressForm.Show(this);
        progressForm.BringToFront();
        UseWaitCursor = true;
        AppUpdateCandidate? candidate = null;
        try
        {
            var progress = new Progress<string>(progressForm.SetStatus);
            candidate = await AppUpdateService.DownloadAndVerifyAsync(
                progress,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The update could not be downloaded or verified.\r\n\r\n{exception.Message}",
                "Update InNasc",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        finally
        {
            UseWaitCursor = false;
            if (!progressForm.IsDisposed) progressForm.Close();
        }

        if (!AppUpdateService.IsNewer(candidate))
        {
            AppUpdateService.Discard(candidate);
            MessageBox.Show(this,
                $"InNasc {AppInfo.Revision} is already current.\r\n\r\n" +
                $"Published release: {candidate.Version}",
                "No update needed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var install = MessageBox.Show(this,
            $"InNasc {candidate.Version} is ready to install.\r\n\r\n" +
            $"Verified SHA-256: {candidate.Sha256[..16]}…\r\n\r\n" +
            "The app will close, keep a rollback copy, install the update, and reopen. Install now?",
            "Install verified update",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (install != DialogResult.Yes)
        {
            AppUpdateService.Discard(candidate);
            return;
        }

        try
        {
            TrySave(showError: false);
            AppUpdateService.InstallAndRestart(candidate);
        }
        catch (Exception exception)
        {
            AppUpdateService.Discard(candidate);
            MessageBox.Show(this,
                $"The update could not be started.\r\n\r\n{exception.Message}",
                "Update InNasc",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OpenClient(ClientRecord client)
    {
        var active = MasterSessionContext.Current;
        if (active is null)
        {
            ShowMasterWelcomePage();
            return;
        }
        if (!MasterAccessService.CanAccessClient(
                _data.MasterAccess,
                active.Session,
                client.Id))
        {
            MessageBox.Show(this,
                "This client is not assigned to your InNasc account.",
                "Client access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            ShowWelcomePage();
            return;
        }
        _activeClient = client;
        _masterWelcomePage.Visible = false;
        RefreshTree(client.Id);
        _tree.Visible = true;
        _hierarchyActions.Visible = true;
        var node = FindNodeById(_tree.Nodes, client.Id);
        if (node is not null) _tree.SelectedNode = node;
        _aboutPage.Visible = false;
        _aboutTopBar.Visible = false;
        _welcomePage.Visible = false;
        _welcomeTopBar.Visible = false;
        _operationsContent.Visible = true;
        _operationsTopBar.Visible = true;
        _operationsContent.BringToFront();
        _operationsTopBar.BringToFront();
        RefreshWorkspaceCheckoutState();
        RefreshGrid();
    }

    private void OpenSettings()
    {
        var previousDarkMode = _data.Settings.DarkMode;
        using var settings = new SettingsForm(_data, _store.DataPath);
        if (settings.ShowDialog(this) != DialogResult.OK) return;
        Text = string.IsNullOrWhiteSpace(_data.ProjectName) ? "InNasc" : _data.ProjectName;
        TrySave();
        if (previousDarkMode != _data.Settings.DarkMode)
        {
            UiTheme.SetDarkMode(_data.Settings.DarkMode);
            UiTheme.ApplyTheme(this);
            RefreshSyncIndicator();
            _welcomePage.RefreshClients();
            RefreshGrid();
            if (_scannerWindow is { IsDisposed: false }) UiTheme.ApplyTheme(_scannerWindow);
        }
        if (settings.DataImported)
        {
            _activeClient = null;
            ResetVerificationStates();
            RefreshTree();
            RefreshManufacturerFilter();
            ShowWelcomePage();
            _statusLabel.Text =
                $"Imported {settings.ImportedClientCount:N0} client(s) and " +
                $"{settings.ImportedEquipmentCount:N0} equipment record(s)";
        }
        else
        {
            _statusLabel.Text = "Settings saved";
        }
    }

    private void OpenSharedSync(bool openAccountsOnShown = false)
    {
        using var sync = new SharedSyncForm(_data, _store, openAccountsOnShown);
        sync.ShowDialog(this);
        RefreshSyncIndicator();
        if (!sync.DataPulled) return;

        _activeClient = null;
        Text = string.IsNullOrWhiteSpace(_data.ProjectName) ? "InNasc" : _data.ProjectName;
        ResetVerificationStates();
        RefreshTree();
        RefreshManufacturerFilter();
        _welcomePage.RefreshClients();
        ShowWelcomePage();
        RefreshGrid();
        _statusLabel.Text = "Synchronized the latest shared company file data";
    }

    private void OpenGoogleDriveSync()
    {
        using var sync = new GoogleDriveSyncForm(_data, _store);
        sync.ShowDialog(this);
        RefreshSyncIndicator();
        if (!sync.DataPulled) return;

        _activeClient = null;
        Text = string.IsNullOrWhiteSpace(_data.ProjectName) ? "InNasc" : _data.ProjectName;
        ResetVerificationStates();
        RefreshTree();
        RefreshManufacturerFilter();
        _welcomePage.RefreshClients();
        ShowWelcomePage();
        RefreshGrid();
        _statusLabel.Text = "Synchronized the latest Google Drive company data";
    }

    private async void OpenMasterAdmin()
    {
        var active = MasterSessionContext.Current;
        if (active is null)
        {
            RefreshMasterSessionUi();
            return;
        }

        try
        {
            MasterAccessControl access;
            if (active.Target == SyncTarget.GoogleDrive)
            {
                var snapshot = await GoogleDriveSyncService.InspectAsync(
                    _data, active.Session.MasterKey);
                access = snapshot.Contents.Data.MasterAccess;
            }
            else
            {
                access = SharedSyncService.Inspect(
                    _data.Settings.SharedMasterPath,
                    active.Session.MasterKey).Contents.Data.MasterAccess;
            }

            using var accounts = new MasterUserManagementForm(
                access,
                active.Session,
                _data.Clients);
            if (accounts.ShowDialog(this) != DialogResult.OK) return;
            if (active.Target == SyncTarget.GoogleDrive)
            {
                await GoogleDriveSyncService.SaveAccessControlAsync(
                    _data,
                    _store,
                    accounts.ResultAccess,
                    active.Session,
                    initialSetup: false,
                    active.Session.MasterKey);
            }
            else
            {
                SharedSyncService.SaveAccessControl(
                    _data,
                    _store,
                    accounts.ResultAccess,
                    active.Session,
                    initialSetup: false,
                    active.Session.MasterKey);
            }
            var refreshed = MasterAccessService.RefreshSession(
                accounts.ResultAccess,
                active.Session);
            MasterSessionContext.Set(active.Target, active.MasterKey, refreshed);
            if (_activeClient is not null &&
                !MasterAccessService.CanAccessClient(
                    accounts.ResultAccess,
                    refreshed,
                    _activeClient.Id))
                _activeClient = null;
            _welcomePage.RefreshClients();
            RefreshTree();
            RefreshManufacturerFilter();
            RefreshGrid();
            RefreshMasterSessionUi();
            _statusLabel.Text = "Company access updated";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"Company access could not be updated.\r\n\r\n{exception.Message}",
                "Company access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task LogoutAsync()
    {
        var active = MasterSessionContext.Current;
        if (active is null)
        {
            ShowMasterWelcomePage();
            return;
        }

        var syncState = SyncIndicatorService.Evaluate(_data, active.Target);
        if (syncState.State == SyncIndicatorState.NeedsSync)
        {
            var action = _data.Settings.ActiveCheckoutClientId.HasValue
                ? "check in and push the checked-out client"
                : "merge this PC's changes into the company file";
            var choice = MessageBox.Show(this,
                $"This PC is not synchronized. Do you want to {action} before logging out?\r\n\r\n" +
                "Choose No to leave the local work on this PC without pushing it.",
                "Synchronize before logout",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);
            if (choice == DialogResult.Cancel) return;
            if (choice == DialogResult.Yes && active.Session.CanWrite &&
                !await SyncBeforeLogoutAsync(active)) return;
        }

        TrySave(showError: false);
        MasterSessionContext.Clear();
        InNascGlobalSessionContext.Clear();
        _legacyMasterPassword = null;
        SignOutRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private async Task<bool> SyncBeforeLogoutAsync(ActiveMasterSession active)
    {
        try
        {
            if (_data.Settings.ActiveCheckoutClientId.HasValue)
            {
                if (active.Target == SyncTarget.GoogleDrive)
                    await GoogleDriveSyncService.CheckInClientAsync(
                        _data, _store, active.Session, active.Session.MasterKey);
                else
                    SharedSyncService.CheckInClient(
                        _data, _store, active.Session, active.Session.MasterKey);
                return true;
            }

            if (!active.Session.CanWrite)
            {
                MessageBox.Show(this,
                    "This Read-only account cannot merge local changes into the company file.",
                    "Read-only access",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            if (active.Target == SyncTarget.GoogleDrive)
            {
                try
                {
                    await GoogleDriveSyncService.PushAsync(
                        _data, _store, active.Session.MasterKey, session: active.Session);
                }
                catch (MergeResolutionRequiredException conflict)
                {
                    using var resolver = new MergeConflictForm(conflict.Conflicts);
                    if (resolver.ShowDialog(this) != DialogResult.OK || resolver.Preference is null)
                        return false;
                    await GoogleDriveSyncService.PushAsync(
                        _data,
                        _store,
                        active.Session.MasterKey,
                        resolver.Preference.Value,
                        active.Session);
                }
            }
            else
            {
                try
                {
                    SharedSyncService.Push(
                        _data, _store, active.Session.MasterKey, session: active.Session);
                }
                catch (MergeResolutionRequiredException conflict)
                {
                    using var resolver = new MergeConflictForm(conflict.Conflicts);
                    if (resolver.ShowDialog(this) != DialogResult.OK || resolver.Preference is null)
                        return false;
                    SharedSyncService.Push(
                        _data,
                        _store,
                        active.Session.MasterKey,
                        resolver.Preference.Value,
                        active.Session);
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The company file could not be synchronized, so logout was canceled.\r\n\r\n{exception.Message}",
                "Synchronization required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private void RefreshAfterSharedDataChange()
    {
        _activeClient = null;
        Text = string.IsNullOrWhiteSpace(_data.ProjectName) ? "InNasc" : _data.ProjectName;
        ResetVerificationStates();
        RefreshTree();
        RefreshManufacturerFilter();
        _welcomePage.RefreshClients();
        ShowWelcomePage();
        RefreshGrid();
        RefreshMasterSessionUi();
        _statusLabel.Text = "Synchronized the latest shared company file data";
    }

    private async Task RunLiveCoauthoringAsync()
    {
        if (IsDisposed || _checkoutInProgress ||
            Application.OpenForms.Cast<Form>().Any(form => form != this && form.Visible))
            return;
        var active = MasterSessionContext.Current;
        if (active?.Target != SyncTarget.GoogleDrive) return;
        if (!await _liveCoauthoringGate.WaitAsync(0)) return;

        try
        {
            _liveCoauthoringStatus = _data.Settings.ActiveCheckoutClientId.HasValue
                ? "Checking checkout ownership…"
                : "Live coauthoring syncing…";
            UpdateSignedInLabel();
            var result = await LiveCoauthoringService.SynchronizeOnceAsync(
                _data,
                _store,
                active,
                _lifetimeCancellation.Token);
            _liveCoauthoringStatus = result.Status;
            if (result.DataChanged) RefreshAfterLiveDataChange();
            UpdateSignedInLabel();
            RefreshSyncIndicator();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The application is closing.
        }
        catch
        {
            _liveCoauthoringStatus = "Live coauthoring waiting for Google Drive";
            UpdateSignedInLabel();
        }
        finally
        {
            _liveCoauthoringGate.Release();
        }
    }

    private void RefreshAfterLiveDataChange()
    {
        var activeClientId = _activeClient?.Id;
        Text = string.IsNullOrWhiteSpace(_data.ProjectName)
            ? "InNasc"
            : _data.ProjectName;
        ResetVerificationStates();
        _welcomePage.RefreshClients();
        RefreshManufacturerFilter();

        if (activeClientId.HasValue)
        {
            _activeClient = _data.Clients.FirstOrDefault(client => client.Id == activeClientId);
            if (_activeClient is not null)
            {
                RefreshTree(_activeClient.Id);
                RefreshWorkspaceCheckoutState();
                RefreshGrid();
                return;
            }
        }

        RefreshTree();
        RefreshGrid();
    }

    private async void CheckoutClientFromCard(ClientRecord client)
    {
        if (_checkoutInProgress) return;
        var active = MasterSessionContext.Current;
        if (active is null)
        {
            ShowMasterWelcomePage();
            return;
        }
        if (!active.Session.CanWrite)
        {
            MessageBox.Show(this,
                "This Read-only account cannot check out a client.",
                "Read-only access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        if (!MasterAccessService.CanAccessClient(
                _data.MasterAccess,
                active.Session,
                client.Id))
        {
            MessageBox.Show(this,
                "This client is not assigned to your InNasc account.",
                "Client access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        if (_data.Settings.ActiveCheckoutClientId.HasValue &&
            _data.Settings.ActiveCheckoutClientId != client.Id)
        {
            MessageBox.Show(this,
                "Check in the client currently held by this PC before checking out another client.",
                "Client checkout active",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        try
        {
            if (_data.Settings.ActiveCheckoutClientId == client.Id)
            {
                if (MessageBox.Show(this,
                        $"Push all changes and configuration files for {client.Name}, then release its checkout?",
                        "Check in client",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                await RunCheckoutProgressAsync(
                    client,
                    checkingIn: true,
                    async () =>
                    {
                        if (active.Target == SyncTarget.GoogleDrive)
                            await GoogleDriveSyncService.CheckInClientAsync(
                                _data, _store, active.Session, active.Session.MasterKey);
                        else
                            await Task.Run(() => SharedSyncService.CheckInClient(
                                _data, _store, active.Session, active.Session.MasterKey));
                    });
                RefreshAfterSharedDataChange();
                _statusLabel.Text = $"Checked in {client.Name} and released its lock";
                return;
            }

            ClientCheckoutResult result;
            try
            {
                ClientCheckoutResult? pendingResult = null;
                await RunCheckoutProgressAsync(
                    client,
                    checkingIn: false,
                    async () =>
                    {
                        pendingResult = active.Target == SyncTarget.GoogleDrive
                            ? await GoogleDriveSyncService.CheckoutClientAsync(
                                _data, _store, client.Id, active.Session, false, active.Session.MasterKey)
                            : await Task.Run(() => SharedSyncService.CheckoutClient(
                                _data, _store, client.Id, active.Session, false, active.Session.MasterKey));
                    });
                result = pendingResult ??
                    throw new InvalidOperationException("The client checkout did not return a result.");
            }
            catch (ClientLockedException locked)
            {
                var holder = string.IsNullOrWhiteSpace(locked.Checkout.DisplayName)
                    ? locked.Checkout.Username
                    : locked.Checkout.DisplayName;
                if (MessageBox.Show(this,
                        $"{locked.ClientName} is checked out by {holder} on {locked.Checkout.MachineName}.\r\n\r\n" +
                        "Ask the technician whether their changes have been pushed before taking over. " +
                        "Taking over releases their lock immediately; unpushed work remains only on their PC.\r\n\r\n" +
                        "Take over this checkout now?",
                        "Take over client checkout",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                ClientCheckoutResult? takeoverResult = null;
                await RunCheckoutProgressAsync(
                    client,
                    checkingIn: false,
                    async () =>
                    {
                        takeoverResult = active.Target == SyncTarget.GoogleDrive
                            ? await GoogleDriveSyncService.CheckoutClientAsync(
                                _data, _store, client.Id, active.Session, true, active.Session.MasterKey)
                            : await Task.Run(() => SharedSyncService.CheckoutClient(
                                _data, _store, client.Id, active.Session, true, active.Session.MasterKey));
                    },
                    takingOver: true);
                result = takeoverResult ??
                    throw new InvalidOperationException("The client takeover did not return a result.");
            }
            RefreshAfterSharedDataChange();
            OpenClient(_data.Clients.Single(item => item.Id == client.Id));
            MessageBox.Show(this,
                $"{result.ClientName} is checked out to {active.Session.DisplayName}. " +
                "Its configuration files are now available in the device editor.",
                result.BootedPreviousCheckout ? "Checkout taken over" : "Client checked out",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The client checkout could not be completed.\r\n\r\n{exception.Message}",
                "Client checkout",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task RunCheckoutProgressAsync(
        ClientRecord client,
        bool checkingIn,
        Func<Task> operation,
        bool takingOver = false)
    {
        _checkoutInProgress = true;
        RefreshWorkspaceCheckoutState();
        _welcomePage.RefreshClients();
        using var progress = new ClientCheckoutProgressForm(
            client.Name,
            checkingIn,
            takingOver);
        progress.Show(this);
        progress.BringToFront();
        UseWaitCursor = true;
        try
        {
            await Task.Yield();
            await operation();
        }
        finally
        {
            UseWaitCursor = false;
            if (!progress.IsDisposed) progress.Close();
            _checkoutInProgress = false;
            RefreshWorkspaceCheckoutState();
            _welcomePage.RefreshClients();
        }
    }

    private void MasterSessionContext_Changed(object? sender, EventArgs eventArgs) =>
        RefreshMasterSessionUi();

    private void RefreshMasterSessionUi()
    {
        if (IsDisposed) return;
        var active = MasterSessionContext.Current;
        var signedIn = active is not null;
        if (active?.Target == SyncTarget.GoogleDrive)
        {
            if (string.IsNullOrWhiteSpace(_liveCoauthoringStatus))
                _liveCoauthoringStatus = "Live coauthoring connected";
            if (!_liveCoauthoringTimer.Enabled) _liveCoauthoringTimer.Start();
        }
        else
        {
            _liveCoauthoringTimer.Stop();
            _liveCoauthoringStatus = string.Empty;
        }
        _homeButton.Visible = signedIn;
        _scannerButton.Visible = signedIn;
        _syncButton.Visible = signedIn;
        _settingsButton.Visible = signedIn;
        _aboutButton.Visible = true;
        _aboutBackButton.Visible = !signedIn && _aboutPage.Visible;
        _adminPanel.Visible = active is not null && !_masterWelcomePage.Visible;
        _adminButton.Text = active?.Session.IsOwner == true
            ? "Admin — manage users"
            : "Access — users & password";
        _signedInLabel.Visible = active is not null;
        UpdateSignedInLabel();
        RefreshWorkspaceCheckoutState();
        RefreshSyncIndicator();
    }

    private void UpdateSignedInLabel()
    {
        if (_signedInLabel.IsDisposed) return;
        var active = MasterSessionContext.Current;
        if (active is null)
        {
            _signedInLabel.Text = string.Empty;
            return;
        }
        var identity =
            $"Signed in as {active.Session.DisplayName} ({MasterSignInNotification.RoleText(active.Session.Role)})";
        _signedInLabel.Text = active.Target == SyncTarget.GoogleDrive &&
                              !string.IsNullOrWhiteSpace(_liveCoauthoringStatus)
            ? $"● {_liveCoauthoringStatus}  •  {identity}"
            : identity;
        _signedInLabel.ForeColor = active.Target == SyncTarget.GoogleDrive &&
                                   _liveCoauthoringStatus.Contains(
                                       "connected",
                                       StringComparison.OrdinalIgnoreCase)
            ? UiTheme.Green
            : UiTheme.Muted;
    }

    private void RefreshWorkspaceCheckoutState()
    {
        if (_workspaceCheckoutButton is null || _workspaceCheckoutButton.IsDisposed) return;
        var active = MasterSessionContext.Current;
        var client = _activeClient;
        var visible = active is not null && client is not null && _operationsTopBar.Visible;
        _workspaceCheckoutButton.Visible = visible;
        _workspaceCheckoutStatus.Visible = visible;
        if (!visible || active is null || client is null) return;

        if (_checkoutInProgress)
        {
            _workspaceCheckoutStatus.Text = "●  CLIENT CHECKOUT IN PROGRESS";
            _workspaceCheckoutStatus.ForeColor = UiTheme.Blue;
            _workspaceCheckoutButton.Text = "Working…";
            _workspaceCheckoutButton.Enabled = false;
            return;
        }

        var canAccess = MasterAccessService.CanAccessClient(
            _data.MasterAccess,
            active.Session,
            client.Id);
        var checkout = _data.MasterAccess.Checkouts
            .FirstOrDefault(item => item.ClientId == client.Id);
        var ownsCheckout = checkout is not null &&
                           _data.Settings.ActiveCheckoutClientId == client.Id &&
                           _data.Settings.ActiveCheckoutToken == checkout.CheckoutToken &&
                           checkout.UserId == active.Session.UserId;
        _workspaceCheckoutButton.Enabled = canAccess && active.Session.CanWrite;

        if (ownsCheckout)
        {
            _workspaceCheckoutStatus.Text = "●  CHECKED OUT TO YOU — CONFIGURATION FILES AVAILABLE";
            _workspaceCheckoutStatus.ForeColor = UiTheme.Blue;
            _workspaceCheckoutButton.Text = "Check in & push";
            _workspaceCheckoutButton.BackColor = UiTheme.Blue;
            _workspaceCheckoutButton.ForeColor = Color.White;
            _workspaceCheckoutButton.FlatAppearance.BorderSize = 0;
            return;
        }

        _workspaceCheckoutButton.BackColor = UiTheme.Surface;
        _workspaceCheckoutButton.ForeColor = UiTheme.Text;
        _workspaceCheckoutButton.FlatAppearance.BorderColor = UiTheme.Border;
        _workspaceCheckoutButton.FlatAppearance.BorderSize = 1;
        if (checkout is null)
        {
            _workspaceCheckoutStatus.Text = "●  CHECKED IN — AVAILABLE TO CHECK OUT";
            _workspaceCheckoutStatus.ForeColor = UiTheme.Green;
            _workspaceCheckoutButton.Text = active.Session.CanWrite
                ? "Check out client"
                : "Read-only access";
            return;
        }

        var holder = string.IsNullOrWhiteSpace(checkout.DisplayName)
            ? checkout.Username
            : checkout.DisplayName;
        _workspaceCheckoutStatus.Text = $"●  CHECKED OUT BY {holder.ToUpperInvariant()}";
        _workspaceCheckoutStatus.ForeColor = UiTheme.Amber;
        _workspaceCheckoutButton.Text = active.Session.CanWrite
            ? "Take over checkout"
            : "Checked out";
    }

    private void OpenIpScanner()
    {
        if (_scannerWindow is null || _scannerWindow.IsDisposed)
        {
            _scannerWindow = new N8IpScannerForm();
            _scannerWindow.FormClosed += (_, _) => _scannerWindow = null;
            _scannerWindow.Show(this);
            return;
        }
        if (_scannerWindow.WindowState == FormWindowState.Minimized)
            _scannerWindow.WindowState = FormWindowState.Normal;
        _scannerWindow.BringToFront();
        _scannerWindow.Activate();
    }

    private void ResetVerificationStates()
    {
        foreach (var equipment in GetAllContexts().Select(item => item.Equipment))
            equipment.ResetNetworkVerification();
    }

    private void RefreshTree(Guid? selectId = null)
    {
        if (selectId is null && _tree.SelectedNode?.Tag is { } current)
            selectId = EntityId(current);

        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var accessibleClients = AccessibleClients();
        var clients = _activeClient is null
            ? accessibleClients
            : accessibleClients.Where(client => ReferenceEquals(client, _activeClient));
        foreach (var client in clients.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var clientNode = new TreeNode($"▣  {client.Name}") { Tag = client };
            foreach (var location in client.Locations.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var locationNode = new TreeNode($"⌖  {location.Name}") { Tag = location };
                foreach (var room in location.Rooms.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                    locationNode.Nodes.Add(new TreeNode($"□  {room.Name}") { Tag = room });
                clientNode.Nodes.Add(locationNode);
            }
            _tree.Nodes.Add(clientNode);
        }
        _tree.ExpandAll();
        _tree.EndUpdate();

        var toSelect = selectId is null ? null : FindNodeById(_tree.Nodes, selectId.Value);
        _tree.SelectedNode = toSelect ?? FirstRoomNode() ?? _tree.Nodes.Cast<TreeNode>().FirstOrDefault();
    }

    private static TreeNode? FindNodeById(TreeNodeCollection nodes, Guid id)
    {
        foreach (TreeNode node in nodes)
        {
            if (EntityId(node.Tag) == id) return node;
            var child = FindNodeById(node.Nodes, id);
            if (child is not null) return child;
        }
        return null;
    }

    private TreeNode? FirstRoomNode()
    {
        foreach (TreeNode client in _tree.Nodes)
        foreach (TreeNode location in client.Nodes)
        foreach (TreeNode room in location.Nodes)
            return room;
        return null;
    }

    private static Guid? EntityId(object? entity) => entity switch
    {
        ClientRecord client => client.Id,
        LocationRecord location => location.Id,
        RoomRecord room => room.Id,
        _ => null
    };

    private void Tree_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        _tree.SelectedNode = e.Node;
        if (e.Button != MouseButtons.Right) return;

        var menu = new ContextMenuStrip();
        if (e.Node.Tag is ClientRecord client)
        {
            menu.Items.Add("Edit client details", null, (_, _) => EditClient(client));
            menu.Items.Add(new ToolStripSeparator());
        }
        if (e.Node.Tag is RoomRecord)
        {
            menu.Items.Add("Move room…", null, (_, _) => MoveSelectedRoom());
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add("Rename", null, (_, _) => RenameSelectedContainer());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => DeleteSelectedContainer());
        menu.Show(_tree, e.Location);
    }

    private void AddClient()
    {
        if (!EnsureWorkspaceWritable()) return;
        var active = MasterSessionContext.Current;
        if (active is not null &&
            !MasterAccessService.HasAllClientAccess(_data.MasterAccess, active.Session))
        {
            MessageBox.Show(this,
                "This account is assigned to selected clients only. Ask an Owner to add the client or grant access to all current and future clients.",
                "Client access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        var room = new RoomRecord { Name = "Room 1" };
        var location = new LocationRecord { Name = "Main Location", Rooms = [room] };
        var client = new ClientRecord { Name = "New Client", Locations = [location] };
        using var editor = new ClientEditorForm(client, true);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        _data.Clients.Add(client);
        TrySave();
        RefreshTree(client.Id);
        _welcomePage.RefreshClients();
        ShowWelcomePage();
        RefreshGrid();
    }

    private void EditClient(ClientRecord client)
    {
        if (!EnsureWorkspaceWritable(client)) return;
        using var editor = new ClientEditorForm(client, false);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        TrySave();
        RefreshTree(client.Id);
        _welcomePage.RefreshClients();
        RefreshGrid();
    }

    private void DeleteClientFromWelcome()
    {
        if (!EnsureWorkspaceWritable()) return;
        if (AccessibleClients().Count == 0)
        {
            MessageBox.Show(this, "There are no clients to delete.", "Delete client",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new DeleteClientForm(AccessibleClients());
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedClient is not { } client) return;
        if (!EnsureWorkspaceWritable(client)) return;

        var equipmentCount = client.Locations.Sum(location =>
            location.Rooms.Sum(room => room.Equipment.Count));
        var detail = equipmentCount == 0
            ? "This client has no equipment records."
            : $"This will also delete {equipmentCount:N0} equipment record(s).";
        if (MessageBox.Show(this,
                $"Delete {client.Name}?\r\n\r\n{detail}\r\n\r\nThis cannot be undone.",
                "Confirm client deletion",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _data.Clients.Remove(client);
        TrySave();
        RefreshTree();
        _welcomePage.RefreshClients();
        RefreshManufacturerFilter();
        ShowWelcomePage();
        _statusLabel.Text = $"Deleted client: {client.Name}";
    }

    private void AddLocation()
    {
        if (!EnsureWorkspaceWritable()) return;
        var client = SelectedClient();
        if (client is null)
        {
            MessageBox.Show(this, "Select a client first.", "Add location",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var name = InputDialog.Show(this, "Add location", $"Location name for {client.Name}");
        if (name is null) return;
        var location = new LocationRecord { Name = name, Rooms = [new RoomRecord { Name = "Room 1" }] };
        client.Locations.Add(location);
        TrySave();
        RefreshTree(location.Id);
        _welcomePage.RefreshClients();
        RefreshGrid();
    }

    private void AddRoom()
    {
        if (!EnsureWorkspaceWritable()) return;
        var location = SelectedLocation();
        if (location is null)
        {
            MessageBox.Show(this, "Select a location first.", "Add room",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var name = InputDialog.Show(this, "Add room", $"Room name for {location.Name}");
        if (name is null) return;
        var room = new RoomRecord { Name = name };
        location.Rooms.Add(room);
        TrySave();
        RefreshTree(room.Id);
        _welcomePage.RefreshClients();
        RefreshGrid();
    }

    private void RenameSelectedContainer()
    {
        var selected = _tree.SelectedNode?.Tag;
        var currentName = selected switch
        {
            ClientRecord client => client.Name,
            LocationRecord location => location.Name,
            RoomRecord room => room.Name,
            _ => string.Empty
        };
        if (selected is null || currentName.Length == 0) return;
        var name = InputDialog.Show(this, "Rename", "New name", currentName);
        if (name is null) return;
        switch (selected)
        {
            case ClientRecord client: client.Name = name; break;
            case LocationRecord location: location.Name = name; break;
            case RoomRecord room: room.Name = name; break;
        }
        TrySave();
        RefreshTree(EntityId(selected));
        _welcomePage.RefreshClients();
        RefreshGrid();
    }

    private void DeleteSelectedContainer()
    {
        if (!EnsureWorkspaceWritable()) return;
        var selected = _tree.SelectedNode?.Tag;
        if (selected is null) return;
        var equipmentCount = selected switch
        {
            ClientRecord client => client.Locations.Sum(location => location.Rooms.Sum(room => room.Equipment.Count)),
            LocationRecord location => location.Rooms.Sum(room => room.Equipment.Count),
            RoomRecord room => room.Equipment.Count,
            _ => 0
        };
        var kind = selected switch
        {
            ClientRecord => "client",
            LocationRecord => "location",
            RoomRecord => "room",
            _ => "container"
        };
        var message = equipmentCount == 0
            ? $"Delete this {kind}?"
            : $"Delete this {kind} and its {equipmentCount} equipment record(s)?";
        if (MessageBox.Show(this, message, "Confirm delete", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        switch (selected)
        {
            case ClientRecord client:
                _data.Clients.Remove(client);
                break;
            case LocationRecord location:
                FindClient(location)?.Locations.Remove(location);
                break;
            case RoomRecord room:
                FindLocation(room)?.Rooms.Remove(room);
                break;
        }
        TrySave();
        RefreshTree();
        _welcomePage.RefreshClients();
        RefreshManufacturerFilter();
        RefreshGrid();
        if (selected is ClientRecord) ShowWelcomePage();
    }

    private void AddEquipment()
    {
        if (!EnsureWorkspaceWritable()) return;
        var room = ResolveRoomForEntry();
        if (room is null)
        {
            MessageBox.Show(this, "Create a client, location, and room before adding equipment.",
                "Add equipment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var equipment = new EquipmentRecord { NetworkState = NetworkState.NoAddress };
        var client = FindClient(room);
        using var editor = new EquipmentEditorForm(
            equipment,
            true,
            ContainerPath(room),
            client is not null && CanEditConfigurationFiles(client));
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        room.Equipment.Add(equipment);
        TrySave();
        _welcomePage.RefreshClients();
        RefreshManufacturerFilter();
        RefreshGrid(equipment.Id);
    }

    private void EditSelectedEquipment()
    {
        if (!EnsureWorkspaceWritable()) return;
        if (SelectedEquipmentContext() is not { } selected) return;
        using var editor = new EquipmentEditorForm(
            selected.Equipment,
            false,
            ContainerPath(selected.Client, selected.Location, selected.Room),
            CanEditConfigurationFiles(selected.Client));
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        TrySave();
        RefreshManufacturerFilter();
        RefreshGrid(selected.Equipment.Id);
    }

    private void DuplicateSelectedEquipment()
    {
        if (!EnsureWorkspaceWritable()) return;
        if (SelectedEquipmentContext() is not { } selected) return;
        var duplicate = selected.Equipment.CloneForDuplicate();
        selected.Room.Equipment.Add(duplicate);
        TrySave();
        _welcomePage.RefreshClients();
        RefreshGrid(duplicate.Id);
    }

    private void MoveSelectedRoom()
    {
        if (!EnsureWorkspaceWritable()) return;
        if (_tree.SelectedNode?.Tag is not RoomRecord room) return;
        var currentLocation = FindLocation(room);
        if (currentLocation is null) return;

        var destinations = AccessibleClients()
            .SelectMany(client => client.Locations.Select(location =>
                new LocationDestination(client, location)))
            .ToList();
        if (destinations.All(destination => destination.Location.Id == currentLocation.Id))
        {
            MessageBox.Show(this,
                "Create another location before moving this room.",
                "Move room",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var picker = new MoveRoomForm(room, destinations, currentLocation.Id);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedDestination is not { } destination)
            return;
        MoveRoomToLocation(room.Id, destination.Location);
    }

    private void MoveRoomToLocation(Guid roomId, LocationRecord targetLocation)
    {
        if (!EnsureWorkspaceWritable()) return;
        var room = _data.Clients
            .SelectMany(client => client.Locations)
            .SelectMany(location => location.Rooms)
            .FirstOrDefault(item => item.Id == roomId);
        var sourceLocation = room is null ? null : FindLocation(room);
        var sourceClient = sourceLocation is null ? null : FindClient(sourceLocation);
        var targetClient = FindClient(targetLocation);
        if (room is null || sourceLocation is null || sourceClient is null || targetClient is null ||
            ReferenceEquals(sourceLocation, targetLocation))
            return;

        var equipmentCount = room.Equipment.Count;
        var equipmentSummary = equipmentCount == 1
            ? "1 equipment record"
            : $"{equipmentCount:N0} equipment records";
        var fromPath = $"{sourceClient.Name}  ›  {sourceLocation.Name}";
        var toPath = $"{targetClient.Name}  ›  {targetLocation.Name}";
        var duplicateName = targetLocation.Rooms.Any(item =>
            !ReferenceEquals(item, room) &&
            string.Equals(item.Name, room.Name, StringComparison.CurrentCultureIgnoreCase));
        var duplicateWarning = duplicateName
            ? "\r\n\r\nA room with this name already exists at the destination."
            : string.Empty;
        if (MessageBox.Show(this,
                $"Move {room.Name}?\r\n\r\nThe room and its {equipmentSummary} will move together." +
                $"\r\n\r\nFrom:  {fromPath}\r\nTo:       {toPath}{duplicateWarning}",
                "Confirm room move",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        sourceLocation.Rooms.Remove(room);
        targetLocation.Rooms.Add(room);
        _activeClient = targetClient;
        TrySave();
        _welcomePage.RefreshClients();
        RefreshTree(room.Id);
        RefreshManufacturerFilter();
        RefreshGrid();
        _statusLabel.Text = $"Moved {room.Name} to {toPath}";
    }

    private void MoveSelectedEquipment()
    {
        if (!EnsureWorkspaceWritable()) return;
        var selected = SelectedEquipmentContexts();
        if (selected.Count == 0) return;
        var destinations = AccessibleClients()
            .SelectMany(client => client.Locations.SelectMany(location =>
                location.Rooms.Select(room => new RoomDestination(client, location, room))))
            .ToList();
        if (destinations.All(destination =>
                selected.All(item => item.Room.Id == destination.Room.Id)))
        {
            MessageBox.Show(this,
                "Create another room before moving the selected equipment.",
                "Move equipment",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var picker = new MoveEquipmentForm(
            selected.Select(item => item.Equipment.Description).ToList(),
            destinations,
            selected.Select(item => item.Room.Id));
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedDestination is not { } destination)
            return;
        MoveEquipmentToRoom(selected.Select(item => item.Equipment.Id).ToArray(), destination.Room);
    }

    private void MoveEquipmentToRoom(IReadOnlyCollection<Guid> equipmentIds, RoomRecord targetRoom)
    {
        if (!EnsureWorkspaceWritable()) return;
        var selectedIds = equipmentIds.ToHashSet();
        var sources = GetAllContexts()
            .Where(item => selectedIds.Contains(item.Equipment.Id) && !ReferenceEquals(item.Room, targetRoom))
            .ToList();
        var targetLocation = FindLocation(targetRoom);
        var targetClient = targetLocation is null ? null : FindClient(targetLocation);
        if (sources.Count == 0 || targetLocation is null || targetClient is null) return;

        var toPath = ContainerPath(targetClient, targetLocation, targetRoom);
        var sourceRoomCount = sources.Select(item => item.Room.Id).Distinct().Count();
        var itemSummary = sources.Count == 1
            ? sources[0].Equipment.Description
            : $"{sources.Count:N0} equipment records";
        var fromSummary = sources.Count == 1
            ? ContainerPath(sources[0].Client, sources[0].Location, sources[0].Room)
            : $"{sourceRoomCount:N0} current room{(sourceRoomCount == 1 ? string.Empty : "s")}";
        var previewNames = string.Join("\r\n", sources.Take(5)
            .Select(item => $"• {item.Equipment.Description}"));
        if (sources.Count > 5)
            previewNames += $"\r\n• and {sources.Count - 5:N0} more";
        var previewBlock = sources.Count > 1 ? $"{previewNames}\r\n\r\n" : string.Empty;
        if (MessageBox.Show(this,
                $"Move {itemSummary}?\r\n\r\n{previewBlock}From:  {fromSummary}\r\nTo:       {toPath}",
                "Confirm equipment move",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        foreach (var source in sources)
        {
            source.Room.Equipment.Remove(source.Equipment);
            targetRoom.Equipment.Add(source.Equipment);
            source.Equipment.UpdatedUtc = DateTime.UtcNow;
        }
        _activeClient = targetClient;
        TrySave();
        _welcomePage.RefreshClients();
        RefreshTree(targetRoom.Id);
        RefreshManufacturerFilter();
        RefreshGrid();
        SelectEquipmentRows(sources.Select(item => item.Equipment.Id));
        _statusLabel.Text = sources.Count == 1
            ? $"Moved {sources[0].Equipment.Description} to {toPath}"
            : $"Moved {sources.Count:N0} equipment records to {toPath}";
    }

    private void DeleteSelectedEquipment()
    {
        if (!EnsureWorkspaceWritable()) return;
        if (SelectedEquipmentContext() is not { } selected) return;
        if (MessageBox.Show(this, $"Delete {selected.Equipment.Description}?", "Confirm delete",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        selected.Room.Equipment.Remove(selected.Equipment);
        TrySave();
        _welcomePage.RefreshClients();
        RefreshManufacturerFilter();
        RefreshGrid();
    }

    private EquipmentContext? SelectedEquipmentContext() =>
        EquipmentContextFromTag(_grid.CurrentRow?.Tag);

    private List<EquipmentContext> SelectedEquipmentContexts()
    {
        var selected = _grid.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Selected)
            .Select(row => EquipmentContextFromTag(row.Tag))
            .Where(item => item is not null)
            .Select(item => item!)
            .DistinctBy(item => item.Equipment.Id)
            .ToList();
        if (selected.Count == 0 && SelectedEquipmentContext() is { } current)
            selected.Add(current);
        return selected;
    }

    private void SelectEquipmentRows(IEnumerable<Guid> equipmentIds)
    {
        var selectedIds = equipmentIds.ToHashSet();
        _grid.ClearSelection();
        DataGridViewRow? first = null;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var context = EquipmentContextFromTag(row.Tag);
            if (context is null || row.Tag is NetworkInterfaceContext ||
                !selectedIds.Contains(context.Equipment.Id))
                continue;
            row.Selected = true;
            first ??= row;
        }
        if (first is not null)
            _grid.CurrentCell = first.Cells[0];
    }

    private static EquipmentContext? EquipmentContextFromTag(object? tag) => tag switch
    {
        EquipmentContext context => context,
        NetworkInterfaceContext networkInterface => networkInterface.EquipmentContext,
        _ => null
    };

    private string ContainerPath(RoomRecord room)
    {
        var location = FindLocation(room);
        var client = location is null ? null : FindClient(location);
        return client is null || location is null
            ? room.Name
            : ContainerPath(client, location, room);
    }

    private string LocationPath(LocationRecord location)
    {
        var client = FindClient(location);
        return client is null ? location.Name : $"{client.Name}  ›  {location.Name}";
    }

    private static string ContainerPath(
        ClientRecord client,
        LocationRecord location,
        RoomRecord room) =>
        $"{client.Name}  ›  {location.Name}  ›  {room.Name}";

    private RoomRecord? ResolveRoomForEntry()
    {
        if (_tree.SelectedNode?.Tag is RoomRecord room) return room;
        if (_tree.SelectedNode?.Tag is LocationRecord location)
        {
            if (location.Rooms.Count == 0) location.Rooms.Add(new RoomRecord { Name = "Room 1" });
            return location.Rooms[0];
        }
        if (_tree.SelectedNode?.Tag is ClientRecord client)
        {
            if (client.Locations.Count == 0)
                client.Locations.Add(new LocationRecord { Name = "Main Location" });
            if (client.Locations[0].Rooms.Count == 0)
                client.Locations[0].Rooms.Add(new RoomRecord { Name = "Room 1" });
            return client.Locations[0].Rooms[0];
        }
        return AccessibleClients().SelectMany(item => item.Locations)
            .SelectMany(item => item.Rooms)
            .FirstOrDefault();
    }

    private ClientRecord? SelectedClient() => _tree.SelectedNode?.Tag switch
    {
        ClientRecord client => client,
        LocationRecord location => FindClient(location),
        RoomRecord room => FindClient(room),
        _ => null
    };

    private LocationRecord? SelectedLocation() => _tree.SelectedNode?.Tag switch
    {
        LocationRecord location => location,
        RoomRecord room => FindLocation(room),
        _ => null
    };

    private ClientRecord? FindClient(LocationRecord target) =>
        _data.Clients.FirstOrDefault(client => client.Locations.Contains(target));

    private ClientRecord? FindClient(RoomRecord target) =>
        _data.Clients.FirstOrDefault(client => client.Locations.Any(location => location.Rooms.Contains(target)));

    private bool CanEditConfigurationFiles(ClientRecord client)
    {
        var masterLinked = !string.IsNullOrWhiteSpace(_data.Settings.SharedMasterPath) ||
            !string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId);
        return !masterLinked ||
            MasterSessionContext.Current?.Session.CanWrite == true &&
            _data.Settings.ActiveCheckoutClientId == client.Id &&
            _data.Settings.ActiveCheckoutToken.HasValue;
    }

    private LocationRecord? FindLocation(RoomRecord target) =>
        _data.Clients.SelectMany(client => client.Locations).FirstOrDefault(location => location.Rooms.Contains(target));

    private List<ClientRecord> AccessibleClients()
    {
        var session = MasterSessionContext.Current?.Session;
        if (session is null) return [];
        return _data.Clients
            .Where(client => MasterAccessService.CanAccessClient(
                _data.MasterAccess,
                session,
                client.Id))
            .ToList();
    }

    private List<EquipmentContext> GetAllContexts()
    {
        var results = new List<EquipmentContext>();
        foreach (var client in AccessibleClients())
        foreach (var location in client.Locations)
        foreach (var room in location.Rooms)
        foreach (var equipment in room.Equipment)
            results.Add(new EquipmentContext(client, location, room, equipment));
        return results;
    }

    private IEnumerable<EquipmentContext> GetScopedContexts()
    {
        var all = GetAllContexts();
        return _tree.SelectedNode?.Tag switch
        {
            ClientRecord client => all.Where(item => ReferenceEquals(item.Client, client)),
            LocationRecord location => all.Where(item => ReferenceEquals(item.Location, location)),
            RoomRecord room => all.Where(item => ReferenceEquals(item.Room, room)),
            _ => all
        };
    }

    private void RefreshGrid(Guid? selectEquipmentId = null)
    {
        RefreshWorkspaceCheckoutState();
        var scoped = GetScopedContexts().ToList();
        UpdateScopeText(scoped.Count);
        UpdateMetrics(scoped);

        IEnumerable<EquipmentContext> filtered = scoped;
        var search = _search.Text.Trim();
        if (search.Length > 0)
            filtered = filtered.Where(item => SearchText(item).Contains(search, StringComparison.CurrentCultureIgnoreCase));

        filtered = (_statusFilter.SelectedItem as string) switch
        {
            "Online" => filtered.Where(item => item.Equipment.NetworkState == NetworkState.Reachable),
            "Offline" => filtered.Where(item => item.Equipment.NetworkState == NetworkState.Unreachable),
            "Partially online" => filtered.Where(item => item.Equipment.NetworkState == NetworkState.Partial),
            "MAC mismatch" => filtered.Where(item => item.Equipment.NetworkState == NetworkState.MacMismatch),
            "Waiting to verify" => filtered.Where(item => item.Equipment.NetworkState == NetworkState.Unknown),
            "No IP" => filtered.Where(item => item.Equipment.NetworkState == NetworkState.NoAddress),
            _ => filtered
        };
        if (_manufacturerFilter.SelectedIndex > 0 && _manufacturerFilter.SelectedItem is string manufacturer)
            filtered = filtered.Where(item => string.Equals(item.Equipment.Manufacturer, manufacturer,
                StringComparison.CurrentCultureIgnoreCase));

        var visible = SortContexts(filtered).ToList();
        _grid.Rows.Clear();
        foreach (var context in visible)
        {
            var equipment = context.Equipment;
            equipment.EnsureNetworkInterfaces();
            var interfaceDetails = equipment.NetworkInterfaces
                .Where(HasInterfaceData)
                .ToList();
            var hasInterfaceDetails = interfaceDetails.Count > 0;
            var expanded = hasInterfaceDetails && _expandedEquipmentIds.Contains(equipment.Id);
            var description = hasInterfaceDetails
                ? $"{(expanded ? "▾" : "▸")} {equipment.Description}"
                : equipment.Description;
            var rowIndex = _grid.Rows.Add(
                StatusText(equipment.NetworkState, equipment.LastLatencyMs),
                string.Empty,
                description,
                equipment.Manufacturer,
                equipment.PartNumber,
                equipment.Hostname,
                equipment.PrimaryIp,
                equipment.Mac1,
                equipment.SerialNumber,
                equipment.Firmware,
                equipment.ConfigurationFiles.Count == 0
                    ? string.Empty
                    : $"{equipment.ConfigurationFiles.Count:N0} file(s)",
                context.Location.Name,
                context.Room.Name,
                FormatLastChecked(equipment.LastCheckedUtc));
            var row = _grid.Rows[rowIndex];
            row.Tag = context;
            row.Cells[_grid.Columns["OpenPortal"].Index] = new DataGridViewTextBoxCell();
            row.Cells[0].Style.ForeColor = StatusColor(equipment.NetworkState);
            row.Cells[0].Style.Font = UiTheme.Font(8.7f, FontStyle.Bold);
            row.Cells[0].ToolTipText = string.IsNullOrWhiteSpace(equipment.LastNetworkError)
                ? "Waiting for manual verification."
                : equipment.LastNetworkError;
            if (hasInterfaceDetails)
                row.Cells["Description"].ToolTipText =
                    "Click once to show or hide this device's IP interfaces. Double-click to edit the device.";
            if (selectEquipmentId == equipment.Id)
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
            }

            if (!expanded) continue;
            foreach (var networkInterface in interfaceDetails)
            {
                var interfaceRowIndex = _grid.Rows.Add(
                    StatusText(networkInterface.NetworkState, networkInterface.LastLatencyMs),
                    string.IsNullOrWhiteSpace(networkInterface.PortalUrl) ? string.Empty : "Open",
                    $"    ↳ {InterfaceTypeText(networkInterface.Type)} interface",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    networkInterface.IpAddress,
                    networkInterface.MacAddress,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    FormatLastChecked(networkInterface.LastCheckedUtc));
                var interfaceRow = _grid.Rows[interfaceRowIndex];
                interfaceRow.Tag = new NetworkInterfaceContext(context, networkInterface);
                interfaceRow.Height = 36;
                interfaceRow.Cells[0].Style.ForeColor = StatusColor(networkInterface.NetworkState);
                interfaceRow.Cells[0].Style.Font = UiTheme.Font(8.5f, FontStyle.Bold);
                interfaceRow.Cells["Description"].Style.ForeColor = UiTheme.Muted;
                interfaceRow.Cells["Description"].Style.Font = UiTheme.Font(8.8f, FontStyle.Italic);
                interfaceRow.Cells[0].ToolTipText = InterfaceTooltip(networkInterface);
                if (string.IsNullOrWhiteSpace(networkInterface.PortalUrl))
                    interfaceRow.Cells[_grid.Columns["OpenPortal"].Index] = new DataGridViewTextBoxCell();
                else
                    interfaceRow.Cells["OpenPortal"].ToolTipText =
                        $"Open {networkInterface.PortalUrl} in the default browser";
            }
        }
        _statusLabel.Text = $"Showing {visible.Count} of {scoped.Count} equipment records";
    }

    private static bool HasInterfaceDetails(EquipmentRecord equipment)
    {
        equipment.EnsureNetworkInterfaces();
        return equipment.NetworkInterfaces.Any(HasInterfaceData);
    }

    private static bool HasInterfaceData(NetworkInterfaceRecord networkInterface) =>
        !string.IsNullOrWhiteSpace(networkInterface.IpAddress) ||
        !string.IsNullOrWhiteSpace(networkInterface.MacAddress);

    private void UpdateScopeText(int count)
    {
        switch (_tree.SelectedNode?.Tag)
        {
            case ClientRecord client:
                _scopeTitle.Font = UiTheme.Font(24, FontStyle.Bold);
                _scopeTitle.Text = client.Name;
                _scopeSubtitle.Text = $"Client overview  •  {client.Locations.Count} location(s)  •  {count} equipment record(s)";
                break;
            case LocationRecord location:
                _scopeTitle.Font = UiTheme.Font(20, FontStyle.Bold);
                _scopeTitle.Text = location.Name;
                _scopeSubtitle.Text = $"{FindClient(location)?.Name}  /  Location  •  {location.Rooms.Count} room(s)";
                break;
            case RoomRecord room:
                _scopeTitle.Font = UiTheme.Font(20, FontStyle.Bold);
                var roomLocation = FindLocation(room);
                _scopeTitle.Text = room.Name;
                _scopeSubtitle.Text = $"{(roomLocation is null ? string.Empty : FindClient(roomLocation)?.Name)}  /  {roomLocation?.Name}  /  Room";
                break;
            default:
                _scopeTitle.Font = UiTheme.Font(20, FontStyle.Bold);
                _scopeTitle.Text = "All equipment";
                _scopeSubtitle.Text = "Every client, location, and room";
                break;
        }
    }

    private void UpdateMetrics(IReadOnlyCollection<EquipmentContext> contexts)
    {
        _totalMetric.Text = contexts.Count.ToString("N0");
        _onlineMetric.Text = contexts.Count(item => item.Equipment.NetworkState == NetworkState.Reachable).ToString("N0");
        _offlineMetric.Text = contexts.Count(item => item.Equipment.NetworkState == NetworkState.Unreachable).ToString("N0");
        _waitingMetric.Text = contexts.Count(item => item.Equipment.NetworkState == NetworkState.Unknown).ToString("N0");
        _unaddressedMetric.Text = contexts.Count(item => item.Equipment.NetworkState == NetworkState.NoAddress).ToString("N0");
    }

    private IEnumerable<EquipmentContext> SortContexts(IEnumerable<EquipmentContext> contexts)
    {
        string Key(EquipmentContext item) => _sortColumn switch
        {
            "Status" => item.Equipment.NetworkState.ToString(),
            "Manufacturer" => item.Equipment.Manufacturer,
            "PartNumber" => item.Equipment.PartNumber,
            "Hostname" => item.Equipment.Hostname,
            "PrimaryIp" => IpSortKey(item.Equipment.PrimaryIp),
            "Mac1" => item.Equipment.Mac1,
            "SerialNumber" => item.Equipment.SerialNumber,
            "Firmware" => item.Equipment.Firmware,
            "ConfigurationFiles" => item.Equipment.ConfigurationFiles.Count.ToString("D8"),
            "Location" => item.Location.Name,
            "Room" => item.Room.Name,
            "LastChecked" => item.Equipment.LastCheckedUtc?.ToString("O") ?? string.Empty,
            _ => item.Equipment.Description
        };
        return _sortAscending
            ? contexts.OrderBy(Key, StringComparer.CurrentCultureIgnoreCase)
            : contexts.OrderByDescending(Key, StringComparer.CurrentCultureIgnoreCase);
    }

    private static string IpSortKey(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 && parts.All(part => byte.TryParse(part, out _))
            ? string.Join('.', parts.Select(part => byte.Parse(part).ToString("D3")))
            : ip;
    }

    private static string SearchText(EquipmentContext item)
    {
        var equipment = item.Equipment;
        var interfaces = string.Join('|', equipment.NetworkInterfaces.Select(networkInterface =>
            $"{networkInterface.Type}|{networkInterface.IpAddress}|{networkInterface.MacAddress}|" +
            $"{networkInterface.ObservedMacAddress}|{networkInterface.MacVerificationMessage}"));
        var configurationFiles = string.Join('|',
            equipment.ConfigurationFiles.Select(file => $"{file.FileName}|{file.Notes}|{file.AddedBy}"));
        return string.Join('|',
            item.Client.Name, item.Location.Name, item.Room.Name,
            equipment.Description, equipment.Manufacturer, equipment.PartNumber,
            equipment.EquipmentId, equipment.Hostname, equipment.SerialNumber,
            equipment.Firmware, equipment.PrimaryIp, equipment.SecondaryIp,
            equipment.TargetIp, equipment.DanteIp, equipment.Mac1, equipment.Mac2,
            equipment.Mac3, equipment.SerialConnection, equipment.Notes, interfaces,
            configurationFiles);
    }

    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var column = _grid.Columns[e.ColumnIndex];
        if (column.SortMode != DataGridViewColumnSortMode.Programmatic) return;
        if (_sortColumn == column.Name)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column.Name;
            _sortAscending = true;
        }
        foreach (DataGridViewColumn item in _grid.Columns)
            item.HeaderCell.SortGlyphDirection = SortOrder.None;
        column.HeaderCell.SortGlyphDirection = _sortAscending ? SortOrder.Ascending : SortOrder.Descending;
        RefreshGrid();
    }

    private void RefreshManufacturerFilter()
    {
        var selected = _manufacturerFilter.SelectedItem as string;
        var manufacturers = GetAllContexts()
            .Select(item => item.Equipment.Manufacturer.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _manufacturerFilter.Items.Clear();
        _manufacturerFilter.Items.Add("All manufacturers");
        foreach (var manufacturer in manufacturers) _manufacturerFilter.Items.Add(manufacturer);
        var selectedIndex = selected is null ? -1 : _manufacturerFilter.Items.IndexOf(selected);
        _manufacturerFilter.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
    }

    private void LoadNetworkAdapters()
    {
        _loadingNics = true;
        _nicPicker.Items.Clear();
        var adapters = NetworkAdapterService.GetAvailableAdapters();
        foreach (var adapter in adapters) _nicPicker.Items.Add(adapter);
        var selectedIndex = adapters.FindIndex(adapter =>
            string.Equals(adapter.NicId, _data.Settings.SelectedNicId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(adapter.Ipv4Address, _data.Settings.SelectedSourceIpv4, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 && adapters.Count > 0) selectedIndex = 0;
        if (selectedIndex >= 0) _nicPicker.SelectedIndex = selectedIndex;
        else _nicPicker.Text = "No active IPv4 adapters";
        _loadingNics = false;
        if (_nicPicker.SelectedItem is NetworkAdapterChoice choice)
        {
            _data.Settings.SelectedNicId = choice.NicId;
            _data.Settings.SelectedSourceIpv4 = choice.Ipv4Address;
            TrySave(showError: false);
        }
    }

    private void RefreshNetworkAdapters()
    {
        if (_loadingNics) return;
        var previousNic = _data.Settings.SelectedNicId;
        var previousIp = _data.Settings.SelectedSourceIpv4;
        _refreshNicsButton.Enabled = false;
        _refreshNicsButton.Text = "Refreshing…";
        try
        {
            LoadNetworkAdapters();
            var selectionChanged =
                !string.Equals(previousNic, _data.Settings.SelectedNicId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previousIp, _data.Settings.SelectedSourceIpv4, StringComparison.OrdinalIgnoreCase);
            if (selectionChanged)
            {
                ResetVerificationStates();
                RefreshGrid();
            }
            _statusLabel.Text = _nicPicker.Items.Count == 0
                ? "No active IPv4 network adapters were found"
                : $"Refreshed {_nicPicker.Items.Count:N0} NIC address option(s)";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"NIC refresh failed: {exception.Message}";
        }
        finally
        {
            _refreshNicsButton.Text = "Refresh NICs";
            _refreshNicsButton.Enabled = true;
        }
    }

    private void NicPicker_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loadingNics || _nicPicker.SelectedItem is not NetworkAdapterChoice choice) return;
        _data.Settings.SelectedNicId = choice.NicId;
        _data.Settings.SelectedSourceIpv4 = choice.Ipv4Address;
        ResetVerificationStates();
        TrySave();
        RefreshGrid();
    }

    private async Task VerifyScopedDevicesAsync()
    {
        if (string.IsNullOrWhiteSpace(_data.Settings.SelectedSourceIpv4))
        {
            MessageBox.Show(this, "Choose a network adapter in the Ping from menu before verifying devices.",
                "Choose network adapter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await RunNetworkCheckAsync(GetScopedContexts().Select(item => item.Equipment).ToList());
    }

    private async Task RunNetworkCheckAsync(IReadOnlyCollection<EquipmentRecord>? equipment = null)
    {
        if (_monitoring) return;
        if (string.IsNullOrWhiteSpace(_data.Settings.SelectedSourceIpv4))
        {
            MessageBox.Show(this, "Choose a network adapter in the Ping from menu before verifying devices.",
                "Choose network adapter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _monitoring = true;
        _checkButton.Enabled = false;
        _checkButton.Text = "Checking…";
        try
        {
            var targets = equipment ?? GetScopedContexts().Select(item => item.Equipment).ToList();
            if (targets.Count == 0)
            {
                _statusLabel.Text = "No equipment to check";
                return;
            }
            _statusLabel.Text = $"Verifying {targets.Count} device(s) from {_data.Settings.SelectedSourceIpv4}…";
            await _networkMonitor.CheckAllAsync(
                targets,
                _data.Settings.SelectedSourceIpv4,
                _data.Settings.PingTimeoutMilliseconds);
            TrySave(showError: false);
            RefreshGrid();
            _statusLabel.Text = $"Manual verification completed at {DateTime.Now:h:mm:ss tt}";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"Network check error: {exception.Message}";
        }
        finally
        {
            _monitoring = false;
            _checkButton.Enabled = true;
            _checkButton.Text = "Verify devices";
        }
    }

    private void ImportExcel()
    {
        if (!EnsureWorkspaceWritable()) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Import equipment spreadsheet",
            Filter = "Excel workbooks (*.xlsx)|*.xlsx",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var scans = dialog.FileNames.Select(XlsxService.ScanImport).ToList();
            var imported = scans.SelectMany(scan => scan.ImportedRows).ToList();
            var reviewableProblems = scans.Sum(scan =>
                scan.SkippedRows.Count + scan.SheetIssues.Count);
            if (imported.Count == 0 && reviewableProblems == 0)
            {
                MessageBox.Show(this, "No equipment rows were found in the selected workbook(s).",
                    "Import Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var client = SelectedClient() ?? AccessibleClients().FirstOrDefault();
            var addClientAfterReview = client is null;
            if (client is null)
                client = new ClientRecord { Name = "Imported Client" };

            var location = SelectedLocation() ?? client.Locations.FirstOrDefault();
            if (location is null)
                location = new LocationRecord { Name = "Imported Location" };

            var targetRoom = _tree.SelectedNode?.Tag as RoomRecord;
            var plan = ExcelImportMergeService.Analyze(
                client,
                location,
                targetRoom,
                imported);
            using var preview = new ExcelImportPreviewForm(plan, scans);
            if (preview.ShowDialog(this) != DialogResult.OK)
            {
                _statusLabel.Text = "Excel import canceled — no data was changed";
                return;
            }

            var result = ExcelImportMergeService.Apply(
                client,
                location,
                targetRoom,
                plan);
            if (addClientAfterReview)
                _data.Clients.Add(client);

            TrySave();
            RefreshTree(location.Id);
            _welcomePage.RefreshClients();
            RefreshManufacturerFilter();
            RefreshGrid();
            var enrichmentSummary = result.FilledBlankFields == 0 &&
                                    result.AddedNetworkInterfaces == 0
                ? string.Empty
                : $"\r\nFilled {result.FilledBlankFields:N0} blank field(s) and added " +
                  $"{result.AddedNetworkInterfaces:N0} network interface(s).";
            MessageBox.Show(this,
                $"Processed {result.ImportedRows:N0} Excel row(s): " +
                $"{result.AddedDevices:N0} new device(s), " +
                $"{result.MergedDevices:N0} merged into existing device(s), " +
                $"{result.UnchangedDuplicates:N0} unchanged duplicate(s), and " +
                $"{result.AmbiguousRows:N0} ambiguous row(s) left untouched." +
                enrichmentSummary,
                "Import complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The workbook could not be imported.\r\n\r\n{exception.Message}",
                "Import Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportExcel()
    {
        var client = _activeClient ?? SelectedClient();
        if (client is null)
        {
            MessageBox.Show(this,
                "Choose a client before exporting to Excel.",
                "Export Excel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        ExportClientExcel(client);
    }

    private void ExportClientExcel(ClientRecord client)
    {
        var session = MasterSessionContext.Current?.Session;
        if (session is null ||
            !MasterAccessService.CanAccessClient(_data.MasterAccess, session, client.Id))
        {
            MessageBox.Show(this,
                "This client is not assigned to your InNasc account.",
                "Client access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = $"Export {client.Name} to Excel",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FilterIndex = 1,
            DefaultExt = "xlsx",
            AddExtension = true,
            RestoreDirectory = true,
            FileName = $"{SafeFileName(client.Name)}.xlsx"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var filePath = Path.ChangeExtension(dialog.FileName, ".xlsx");
            if (!string.Equals(filePath, dialog.FileName, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(filePath) &&
                MessageBox.Show(this,
                    $"{Path.GetFileName(filePath)} already exists. Replace it?",
                    "Confirm Excel export",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var exportedCount = XlsxService.ExportClient(filePath, _data, client);
            _statusLabel.Text =
                $"Exported {exportedCount:N0} {client.Name} records to {Path.GetFileName(filePath)}";
            MessageBox.Show(this,
                $"Created a real .xlsx workbook for {client.Name} with " +
                $"{exportedCount:N0} equipment record(s).",
                "Export complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The workbook could not be exported.\r\n\r\n{exception.Message}",
                "Export Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Trim()
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray()).Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "Client" : cleaned;
    }

    private bool EnsureWorkspaceWritable(ClientRecord? targetClient = null)
    {
        if (_data.Settings.MasterWorkspaceReadOnly)
        {
            MessageBox.Show(this,
                "This workspace was opened with a Read-only InNasc account. " +
                "Sign in with an Owner or Tech account to make changes.",
                "Read-only company workspace",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        var client = targetClient ?? _activeClient ?? SelectedClient();
        if (client is null) return true;
        var session = MasterSessionContext.Current?.Session;
        if (session is null ||
            !MasterAccessService.CanAccessClient(_data.MasterAccess, session, client.Id))
        {
            MessageBox.Show(this,
                "This client is not assigned to your InNasc account.",
                "Client access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }
        var checkout = _data.MasterAccess.Checkouts
            .FirstOrDefault(item => item.ClientId == client.Id);
        if (checkout is null) return true;
        var ownsCheckout = _data.Settings.ActiveCheckoutClientId == client.Id &&
                           _data.Settings.ActiveCheckoutToken == checkout.CheckoutToken &&
                           MasterSessionContext.Current?.Session.UserId == checkout.UserId;
        if (ownsCheckout) return true;
        var holder = string.IsNullOrWhiteSpace(checkout.DisplayName)
            ? checkout.Username
            : checkout.DisplayName;
        MessageBox.Show(this,
            $"{client.Name} is checked out by {holder}. Its records are locked on this PC until that checkout is checked in, released, or taken over from the client card.",
            "Client checked out",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private void TrySave(bool showError = true)
    {
        try
        {
            _store.Save(_data);
            RefreshSyncIndicator();
        }
        catch (Exception exception)
        {
            if (showError)
                MessageBox.Show(this, $"Changes could not be saved.\r\n\r\n{exception.Message}",
                    "Save error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshSyncIndicator()
    {
        if (_syncButton is null || _syncButton.IsDisposed) return;
        // Only the company file used by the current app session should determine the
        // indicator. A saved, inactive local link must not keep a synchronized
        // Google Drive session amber (or vice versa).
        var status = SyncIndicatorService.Evaluate(
            _data,
            MasterSessionContext.Current?.Target);
        var color = status.State == SyncIndicatorState.Synced
            ? UiTheme.Green
            : UiTheme.Amber;
        var previous = _syncButton.Image;
        _syncButton.Image = AppIcons.Sync(color: color);
        previous?.Dispose();
        _toolTip.SetToolTip(_syncButton, status.Tooltip);
        _syncButton.AccessibleDescription = status.Tooltip;
    }

    private static Color StatusColor(NetworkState state) => state switch
    {
        NetworkState.Reachable => UiTheme.Green,
        NetworkState.Unreachable => UiTheme.Red,
        NetworkState.NoAddress => UiTheme.GrayLed,
        NetworkState.Partial => UiTheme.Amber,
        NetworkState.MacMismatch => UiTheme.Yellow,
        _ => UiTheme.Blue
    };

    private static string StatusText(NetworkState state, long? latency) => state switch
    {
        NetworkState.Reachable => $"●  {latency ?? 0} ms",
        NetworkState.Unreachable => "●  Offline",
        NetworkState.NoAddress => "●  No IP",
        NetworkState.Partial => "●  Partial",
        NetworkState.MacMismatch => "●  MAC mismatch",
        _ => "●  Waiting"
    };

    private static string InterfaceTypeText(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.CobraNet => "CobraNet",
        NetworkInterfaceType.AES67 => "AES67",
        _ => type.ToString()
    };

    private static string InterfaceTooltip(NetworkInterfaceRecord networkInterface)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(networkInterface.LastNetworkError))
            lines.Add(networkInterface.LastNetworkError);
        if (!string.IsNullOrWhiteSpace(networkInterface.MacVerificationMessage))
            lines.Add(networkInterface.MacVerificationMessage);
        if (networkInterface.HttpPortOpen || networkInterface.HttpsPortOpen)
        {
            var ports = new List<string>();
            if (networkInterface.HttpPortOpen) ports.Add("80/HTTP");
            if (networkInterface.HttpsPortOpen) ports.Add("443/HTTPS");
            lines.Add($"Web portal detected on {string.Join(" and ", ports)}.");
        }
        return lines.Count == 0 ? "Waiting for manual verification." : string.Join("\r\n", lines);
    }

    private static string FormatLastChecked(DateTime? utc) => utc is null
        ? "Not checked"
        : utc.Value.ToLocalTime().ToString("MMM d, h:mm:ss tt");
}
