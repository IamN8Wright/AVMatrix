using System.Security.Cryptography;

namespace InNasc;

internal sealed class EquipmentEditorForm : Form
{
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NetworkInterfaceEditorRow> _networkRows = [];
    private readonly FlowLayoutPanel _networkRowsPanel = new();
    private readonly List<DeviceConfigurationFile> _configurationFiles = [];
    private readonly DataGridView _configurationGrid = new();
    private readonly EquipmentRecord _source;
    private readonly string _containerPath;
    private readonly bool _configurationFilesEditable;

    public EquipmentEditorForm(
        EquipmentRecord source,
        bool isNew,
        string containerPath,
        bool configurationFilesEditable = false)
    {
        _source = source;
        _source.EnsureNetworkInterfaces();
        _source.ConfigurationFiles ??= [];
        _configurationFiles.AddRange(_source.ConfigurationFiles.Select(CloneConfigurationFile));
        _containerPath = containerPath;
        _configurationFilesEditable = configurationFilesEditable;
        Text = isNew ? "Add equipment" : "Edit equipment";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 650);
        Size = new Size(830, 720);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = Text,
            AutoSize = true,
            Font = UiTheme.Font(18, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        heading.Controls.Add(new Label
        {
            Text = "Equipment identity, network addressing, and service notes",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 33)
        });

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(9.5f),
            Padding = new Point(16, 6)
        };
        tabs.TabPages.Add(BuildIdentityPage());
        tabs.TabPages.Add(BuildNetworkPage());
        tabs.TabPages.Add(BuildAccessPage());
        tabs.TabPages.Add(BuildConfigurationFilesPage());

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var save = UiTheme.PrimaryButton(isNew ? "Add equipment" : "Save changes");
        save.DialogResult = DialogResult.OK;
        save.Click += Save_Click;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);

        shell.Controls.Add(heading, 0, 0);
        shell.Controls.Add(tabs, 0, 1);
        shell.Controls.Add(footer, 0, 2);
        Controls.Add(shell);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private TabPage BuildIdentityPage()
    {
        var page = NewPage("Equipment");
        var form = NewFormTable();
        AddField(form, 0, "Description", nameof(EquipmentRecord.Description), _source.Description, true);
        AddField(form, 1, "Manufacturer", nameof(EquipmentRecord.Manufacturer), _source.Manufacturer);
        AddField(form, 1, "Model / part number", nameof(EquipmentRecord.PartNumber), _source.PartNumber, false, 1);
        AddField(form, 2, "Equipment ID", nameof(EquipmentRecord.EquipmentId), _source.EquipmentId);
        AddField(form, 2, "Hostname", nameof(EquipmentRecord.Hostname), _source.Hostname, false, 1);
        AddField(form, 3, "Serial number", nameof(EquipmentRecord.SerialNumber), _source.SerialNumber);
        AddField(form, 3, "Firmware", nameof(EquipmentRecord.Firmware), _source.Firmware, false, 1);
        AddField(form, 4, "Serial connection", nameof(EquipmentRecord.SerialConnection), _source.SerialConnection, true);
        page.Controls.Add(form);
        return page;
    }

    private TabPage BuildNetworkPage()
    {
        var page = NewPage("Network");
        page.AutoScroll = true;
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4),
            Width = 745
        };
        content.Controls.Add(new Label
        {
            Text = "IP & MAC INTERFACES",
            AutoSize = true,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Margin = new Padding(4, 4, 4, 2)
        });
        content.Controls.Add(new Label
        {
            Text = "Add one row for each device interface, such as its control, Dante, or AVB address.",
            AutoSize = false,
            Size = new Size(710, 24),
            Font = UiTheme.Font(9),
            ForeColor = UiTheme.Text,
            Margin = new Padding(4, 0, 4, 8)
        });

        _networkRowsPanel.AutoSize = true;
        _networkRowsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _networkRowsPanel.FlowDirection = FlowDirection.TopDown;
        _networkRowsPanel.WrapContents = false;
        _networkRowsPanel.Margin = Padding.Empty;
        _networkRowsPanel.Padding = Padding.Empty;
        _networkRowsPanel.Width = 720;
        content.Controls.Add(_networkRowsPanel);

        foreach (var networkInterface in _source.NetworkInterfaces)
            AddNetworkRow(networkInterface);

        var add = UiTheme.SecondaryButton("ï¼‹  Add IP / MAC");
        add.AutoSize = false;
        add.Size = new Size(150, 36);
        add.ForeColor = UiTheme.Blue;
        add.Font = UiTheme.Font(10, FontStyle.Bold);
        add.Margin = new Padding(4, 4, 4, 12);
        add.Click += (_, _) => AddNetworkRow(new NetworkInterfaceRecord());
        content.Controls.Add(add);

        var addressing = NewFormTable();
        addressing.Width = 720;
        addressing.Dock = DockStyle.None;
        addressing.Margin = Padding.Empty;
        AddField(addressing, 0, "Subnet mask (optional)", nameof(EquipmentRecord.Subnet), _source.Subnet);
        AddField(addressing, 0, "Gateway (optional)", nameof(EquipmentRecord.Gateway), _source.Gateway, false, 1);
        content.Controls.Add(addressing);
        page.Controls.Add(content);
        return page;
    }

    private void AddNetworkRow(NetworkInterfaceRecord source)
    {
        var row = new NetworkInterfaceEditorRow(source);
        row.RemoveButton.Click += (_, _) =>
        {
            if (_networkRows.Count <= 1) return;
            _networkRows.Remove(row);
            _networkRowsPanel.Controls.Remove(row.Container);
            row.Container.Dispose();
            RefreshNetworkRemoveButtons();
        };
        _networkRows.Add(row);
        _networkRowsPanel.Controls.Add(row.Container);
        RefreshNetworkRemoveButtons();
        row.IpAddress.Focus();
    }

    private void RefreshNetworkRemoveButtons()
    {
        foreach (var row in _networkRows) row.RemoveButton.Visible = _networkRows.Count > 1;
    }

    private TabPage BuildAccessPage()
    {
        var page = NewPage("Access & notes");
        var form = NewFormTable();
        AddField(form, 0, "Username", nameof(EquipmentRecord.Username), _source.Username);
        // This is an equipment access credential for technicians to reference,
        // so keep it readable and copyable in the device editor.
        AddField(form, 0, "Password", nameof(EquipmentRecord.Password), _source.Password, false, 1);
        AddField(form, 1, "Source file", nameof(EquipmentRecord.SourceFile), _source.SourceFile, true);
        AddField(form, 2, "Notes", nameof(EquipmentRecord.Notes), _source.Notes, true, 0, false, true);
        var containerCard = BuildContainerCard();
        form.Controls.Add(containerCard, 0, 3);
        form.SetColumnSpan(containerCard, 2);
        page.Controls.Add(form);
        return page;
    }

    private Control BuildContainerCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 94,
            Margin = new Padding(4, 10, 4, 4),
            Padding = new Padding(18, 12, 18, 10)
        };
        card.Controls.Add(new Panel
        {
            BackColor = UiTheme.Blue,
            Location = new Point(0, 12),
            Size = new Size(4, 66)
        });
        card.Controls.Add(new Label
        {
            Text = "CURRENT CONTAINER",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8, FontStyle.Bold),
            Location = new Point(18, 12)
        });
        card.Controls.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(_containerPath) ? "Not assigned" : _containerPath,
            AutoEllipsis = true,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(11, FontStyle.Bold),
            Location = new Point(18, 34),
            Size = new Size(690, 24)
        });
        card.Controls.Add(new Label
        {
            Text = "Drag its equipment row onto a room, or choose Move to room from the row menu.",
            AutoEllipsis = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.5f),
            Location = new Point(18, 61),
            Size = new Size(690, 20)
        });
        return card;
    }

    private TabPage BuildConfigurationFilesPage()
    {
        var page = NewPage("Configuration files");
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        shell.Controls.Add(new Label
        {
            Text = _configurationFilesEditable
                ? "Attach backups, presets, DSP files, switch configurations, or other device-specific files. " +
                  "File contents are stored in this client's checked-out sub-matrix."
                : "Configuration-file metadata is shown here. Check out this client from the master to " +
                  "download, add, remove, or extract the actual files.",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(9.5f),
            ForeColor = UiTheme.Muted,
            AutoEllipsis = true
        }, 0, 0);

        ConfigureConfigurationFilesGrid();
        shell.Controls.Add(_configurationGrid, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var upload = UiTheme.PrimaryButton("ï¼‹ Add filesâ€¦");
        upload.Enabled = _configurationFilesEditable;
        upload.Click += (_, _) => AddConfigurationFiles();
        var remove = UiTheme.DangerButton("Remove");
        remove.Enabled = _configurationFilesEditable;
        remove.Click += (_, _) => RemoveSelectedConfigurationFiles();
        actions.Controls.Add(upload);
        actions.Controls.Add(remove);
        shell.Controls.Add(actions, 0, 2);
        page.Controls.Add(shell);
        RefreshConfigurationFiles();
        return page;
    }

    private void ConfigureConfigurationFilesGrid()
    {
        _configurationGrid.Dock = DockStyle.Fill;
        _configurationGrid.ReadOnly = true;
        _configurationGrid.AllowUserToAddRows = false;
        _configurationGrid.AllowUserToDeleteRows = false;
        _configurationGrid.AllowUserToResizeRows = false;
        _configurationGrid.RowHeadersVisible = false;
        _configurationGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _configurationGrid.MultiSelect = true;
        _configurationGrid.BorderStyle = BorderStyle.FixedSingle;
        _configurationGrid.BackgroundColor = UiTheme.Surface;
        _configurationGrid.ColumnHeadersHeight = 38;
        _configurationGrid.RowTemplate.Height = 36;
        _configurationGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _configurationGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FileName",
            HeaderText = "FILE",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 44
        });
        _configurationGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Size",
            HeaderText = "SIZE",
            Width = 88
        });
        _configurationGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Added",
            HeaderText = "ADDED",
            Width = 145
        });
        _configurationGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Sha256",
            HeaderText = "SHA-256",
            Width = 145
        });
        _configurationGrid.Columns.Add(new DataGridViewLinkColumn
        {
            Name = "Download",
            HeaderText = "FILE ACTION",
            Width = 118,
            LinkColor = UiTheme.Blue,
            ActiveLinkColor = UiTheme.BlueHover,
            VisitedLinkColor = UiTheme.Blue,
            TrackVisitedState = false,
            UseColumnTextForLinkValue = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _configurationGrid.CellContentClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 ||
                _configurationGrid.Columns[eventArgs.ColumnIndex].Name != "Download")
                return;
            _configurationGrid.ClearSelection();
            _configurationGrid.Rows[eventArgs.RowIndex].Selected = true;
            SaveSelectedConfigurationFile();
        };
×ž|¶‰žËkºwµç@¥˜€¡‘¥…±½œ¹M¡½Ý¥…±½œ¡Ñ¡¥Ì¤€„ô¥…±½I•ÍÕ±Ð¹=,¤É•ÑÕÉ¸ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€¥±”¹]É¥Ñ•±±	åÑ•Ì¡‘¥…±½œ¹¥±•9…µ”°™¥±”¹•Ñ½¹Ñ•¹ÑÌ ¤¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰Q¡”™¥±”½Õ±¹½Ð‰”Í…Ù•¹qÉq¹qÉq¹í•á•ÁÑ¥½¸¹5•ÍÍ…•ôˆ°4(€€€€€€€€€€€€€€€€‰½¹™¥ÕÉ…Ñ¥½¸™¥±”ˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥I•µ½Ù•M•±•Ñ•‘½¹™¥ÕÉ…Ñ¥½¹¥±•Ì ¤4(€€€ì4(€€€€€€€¥˜€ …}½¹™¥ÕÉ…Ñ¥½¹¥±•Í‘¥Ñ…‰±”¤É•ÑÕÉ¸ì4(€€€€€€€¥˜€¡}½¹™¥ÕÉ…Ñ¥½¹É¥¹M•±•Ñ•‘I½ÝÌ¹½Õ¹Ð€ôô€À¤É•ÑÕÉ¸ì4(€€€€€€€Ù…ÈÍ•±•Ñ•€ô}½¹™¥ÕÉ…Ñ¥½¹É¥¹M•±•Ñ•‘I½ÝÌ¹…ÍÐñ…Ñ…É¥‘Y¥•ÝI½Üø ¤4(€€€€€€€€€€€€¹M•±•Ð¡É½Ü€ôø€¡•Ù¥•½¹™¥ÕÉ…Ñ¥½¹¥±”¥É½Ü¹Q…œ„¤4(€€€€€€€€€€€€¹Q½1¥ÍÐ ¤ì4(€€€€€€€¥˜€¡5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€‰I•µ½Ù”íÍ•±•Ñ•¹½Õ¹Ðé8Áô…ÑÑ…¡•½¹™¥ÕÉ…Ñ¥½¸™¥±”¡Ì¤™É½´Ñ¡¥Ì‘•Ù¥”üˆ°4(€€€€€€€€€€€€€€€€‰I•µ½Ù”½¹™¥ÕÉ…Ñ¥½¸™¥±•Ìˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9¼°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹]…É¹¥¹œ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á•™…Õ±Ñ	ÕÑÑ½¸¹	ÕÑÑ½¸È¤€„ô¥…±½I•ÍÕ±Ð¹e•Ì¤4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€™½É•… €¡Ù…È™¥±”¥¸Í•±•Ñ•¤}½¹™¥ÕÉ…Ñ¥½¹¥±•Ì¹I•µ½Ù”¡™¥±”¤ì4(€€€€€€€I•™É•Í¡½¹™¥ÕÉ…Ñ¥½¹¥±•Ì ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥I•™É•Í¡½¹™¥ÕÉ…Ñ¥½¹¥±•Ì ¤4(€€€ì4(€€€€€€€}½¹™¥ÕÉ…Ñ¥½¹É¥¹I½ÝÌ¹±•…È ¤ì4(€€€€€€€™½É•… €¡Ù…È™¥±”¥¸}½¹™¥ÕÉ…Ñ¥½¹¥±•Ì¹=É‘•É	ä¡™¥±”€ôø™¥±”¹¥±•9…µ”°MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È…¹½Ý¹±½…€ô}½¹™¥ÕÉ…Ñ¥½¹¥±•Í‘¥Ñ…‰±”€˜˜™¥±”¹½¹Ñ•¹Ñ%¹±Õ‘•€˜˜4(€€€€€€€€€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¹½¹Ñ•áÐ¹ÕÉÉ•¹Ðü¹M•ÍÍ¥½¸¹…¹]É¥Ñ”€„ô™…±Í”ì4(€€€€€€€€€€€Ù…È…Ñ¥½¹Q•áÐ€ô…¹½Ý¹±½…4(€€€€€€€€€€€€€€€€ü€‰½Ý¹±½…ˆ4(€€€€€€€€€€€€€€€€è5…ÍÑ•ÉM•ÍÍ¥½¹½¹Ñ•áÐ¹ÕÉÉ•¹Ðü¹M•ÍÍ¥½¸¹…¹]É¥Ñ”€ôô™…±Í”4(€€€€€€€€€€€€€€€€€€€€ü€‰=Ý¹•È½Q• ½¹±äˆ4(€€€€€€€€€€€€€€€€€€€€è€‰¡•¬½ÕÐ±¥•¹Ðˆì4(€€€€€€€€€€€Ù…ÈÉ½Ý%¹‘•à€ô}½¹™¥ÕÉ…Ñ¥½¹É¥¹I½ÝÌ¹‘ 4(€€€€€€€€€€€€€€€™¥±”¹¥±•9…µ”°4(€€€€€€€€€€€€€€€½Éµ…ÑM¥é”¡™¥±”¹M¥é•	åÑ•Ì¤°4(€€€€€€€€€€€€€€€™¥±”¹‘‘•‘UÑŒ¹Q½1½…±Q¥µ” ¤¹Q½MÑÉ¥¹œ ‰œˆ¤°4(€€€€€€€€€€€€€€€™¥±”¹M¡„ÈÔØ¹1•¹Ñ €ø€ÄØ€ü™¥±”¹M¡„ÈÔÙl¸¸ÄÙt€¬€‹Š˜ˆ€è™¥±”¹M¡„ÈÔØ°4(€€€€€€€€€€€€€€€…Ñ¥½¹Q•áÐ¤ì4(€€€€€€€€€€€Ù…ÈÉ½Ü€ô}½¹™¥ÕÉ…Ñ¥½¹É¥¹I½ÝÍmÉ½Ý%¹‘•átì4(€€€€€€€€€€€É½Ü¹Q…œ€ô™¥±”ì4(€€€€€€€€€€€¥˜€ ……¹½Ý¹±½…¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É½Ü¹•±±Íl‰½Ý¹±½…‰t€ô¹•Ü…Ñ…É¥‘Y¥•ÝQ•áÑ	½á•±°ìY…±Õ”€ô…Ñ¥½¹Q•áÐôì4(€€€€€€€€€€€€€€€É½Ü¹•±±Íl‰½Ý¹±½…‰t¹MÑå±”¹½É•½±½È€ôU¥Q¡•µ”¹5ÕÑ•ì4(€€€€€€€€€€€€€€€É½Ü¹•±±Íl‰½Ý¹±½…‰t¹Q½½±Q¥ÁQ•áÐ€ô5…ÍÑ•ÉM•ÍÍ¥½¹½¹Ñ•áÐ¹ÕÉÉ•¹Ðü¹M•ÍÍ¥½¸¹…¹]É¥Ñ”€ôô™…±Í”4(€€€€€€€€€€€€€€€€€€€€ü€‰=¹±ä…¸=Ý¹•È½ÈQ• …¸‘½Ý¹±½…½¹™¥ÕÉ…Ñ¥½¸™¥±•Ì¸ˆ4(€€€€€€€€€€€€€€€€€€€€è€‰5•Ñ…‘…Ñ„½¹±äƒŠP¡•¬½ÕÐÑ¡¥Ì±¥•¹ÐÑ¼‘½Ý¹±½…Ñ¡”™¥±”¸ˆì4(€€€€€€€€€€€ô4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ•Ù¥•½¹™¥ÕÉ…Ñ¥½¹¥±”±½¹•½¹™¥ÕÉ…Ñ¥½¹¥±”¡•Ù¥•½¹™¥ÕÉ…Ñ¥½¹¥±”Í½ÕÉ”¤€ôø¹•Ü ¤4(€€€ì4(€€€€€€€%€ôÍ½ÕÉ”¹%°4(€€€€€€€¥±•9…µ”€ôÍ½ÕÉ”¹¥±•9…µ”°4(€€€€€€€½¹Ñ•¹ÑQåÁ”€ôÍ½ÕÉ”¹½¹Ñ•¹ÑQåÁ”°4(€€€€€€€M¥é•	åÑ•Ì€ôÍ½ÕÉ”¹M¥é•	åÑ•Ì°4(€€€€€€€M¡„ÈÔØ€ôÍ½ÕÉ”¹M¡„ÈÔØ°4(€€€€€€€½¹Ñ•¹Ñ	…Í”ØÐ€ôÍ½ÕÉ”¹½¹Ñ•¹Ñ	…Í”ØÐ°4(€€€€€€€½¹Ñ•¹Ñ%¹±Õ‘•€ôÍ½ÕÉ”¹½¹Ñ•¹Ñ%¹±Õ‘•°4(€€€€€€€9½Ñ•Ì€ôÍ½ÕÉ”¹9½Ñ•Ì°4(€€€€€€€‘‘•‘	ä€ôÍ½ÕÉ”¹‘‘•‘	ä°4(€€€€€€€‘‘•‘UÑŒ€ôÍ½ÕÉ”¹‘‘•‘UÑŒ4(€€€ôì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ½¹Ñ•¹ÑQåÁ•½È¡ÍÑÉ¥¹œ•áÑ•¹Í¥½¸¤€ôø•áÑ•¹Í¥½¸¹Q½1½Ý•É%¹Ù…É¥…¹Ð ¤ÍÝ¥Ñ 4(€€€ì4(€€€€€€€€ˆ¹©Í½¸ˆ€ôø€‰…ÁÁ±¥…Ñ¥½¸½©Í½¸ˆ°4(€€€€€€€€ˆ¹áµ°ˆ€ôø€‰…ÁÁ±¥…Ñ¥½¸½áµ°ˆ°4(€€€€€€€€ˆ¹ÑáÐˆ½È€ˆ¹™œˆ½È€ˆ¹½¹˜ˆ½È€ˆ¹¥¹¤ˆ€ôø€‰Ñ•áÐ½Á±…¥¸ˆ°4(€€€€€€€€ˆ¹é¥Àˆ€ôø€‰…ÁÁ±¥…Ñ¥½¸½é¥Àˆ°4(€€€€€€€|€ôø€‰…ÁÁ±¥…Ñ¥½¸½½Ñ•ÐµÍÑÉ•…´ˆ4(€€€ôì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ½Éµ…ÑM¥é”¡±½¹œ‰åÑ•Ì¤€ôø‰åÑ•ÌÍÝ¥Ñ 4(€€€ì4(€€€€€€€€øô€ÄÀÈÑ0€¨€ÄÀÈÐ€¨€ÄÀÈÐ€ôø€‰í‰åÑ•Ì€¼€ ÄÀÈÑ€¨€ÄÀÈÐ€¨€ÄÀÈÐ¤èÀ¸Œôˆ°4(€€€€€€€€øô€ÄÀÈÑ0€¨€ÄÀÈÐ€ôø€‰í‰åÑ•Ì€¼€ ÄÀÈÑ€¨€ÄÀÈÐ¤èÀ¸Œô5ˆ°4(€€€€€€€€øô€ÄÀÈÑ0€ôø€‰í‰åÑ•Ì€¼€ÄÀÈÑèÀ¸Œô-ˆ°4(€€€€€€€|€ôø€‰í‰åÑ•Ìé8Áôˆ4(€€€ôì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒQ…‰A…”9•ÝA…”¡ÍÑÉ¥¹œÑ•áÐ¤€ôø¹•Ü¡Ñ•áÐ¤4(€€€ì4(€€€€€€€	…­½±½È€ôU¥Q¡•µ”¹MÕÉ™…”°4(€€€€€€€A…‘‘¥¹œ€ô¹•ÜA…‘‘¥¹œ ÄØ¤4(€€€ôì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒQ…‰±•1…å½ÕÑA…¹•°9•Ý½ÉµQ…‰±” ¤4(€€€ì4(€€€€€€€Ù…ÈÑ…‰±”€ô¹•ÜQ…‰±•1…å½ÕÑA…¹•°4(€€€€€€€ì4(€€€€€€€€€€€½¬€ô½­MÑå±”¹Q½À°4(€€€€€€€€€€€ÕÑ½M¥é”€ôÑÉÕ”°4(€€€€€€€€€€€½±Õµ¹½Õ¹Ð€ô€È°4(€€€€€€€€€€€I½Ý½Õ¹Ð€ô€Ø°4(€€€€€€€€€€€É½ÝMÑå±”€ôQ…‰±•1…å½ÕÑA…¹•±É½ÝMÑå±”¹‘‘I½ÝÌ°4(€€€€€€€€€€€A…‘‘¥¹œ€ô¹•ÜA…‘‘¥¹œ Ð¤4(€€€€€€€ôì4(€€€€€€€Ñ…‰±”¹½±Õµ¹MÑå±•Ì¹‘¡¹•Ü½±Õµ¹MÑå±”¡M¥é•QåÁ”¹A•É•¹Ð°€ÔÀ¤¤ì4(€€€€€€€Ñ…‰±”¹½±Õµ¹MÑå±•Ì¹‘¡¹•Ü½±Õµ¹MÑå±”¡M¥é•QåÁ”¹A•É•¹Ð°€ÔÀ¤¤ì4(€€€€€€€É•ÑÕÉ¸Ñ…‰±”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥‘‘¥•± 4(€€€€€€€Q…‰±•1…å½ÕÑA…¹•°Ñ…‰±”°4(€€€€€€€¥¹ÐÉ½Ü°4(€€€€€€€ÍÑÉ¥¹œ±…‰•°°4(€€€€€€€ÍÑÉ¥¹œ­•ä°4(€€€€€€€ÍÑÉ¥¹œÙ…±Õ”°4(€€€€€€€‰½½°ÍÁ…¸€ô™…±Í”°4(€€€€€€€¥¹Ð½±Õµ¸€ô€À°4(€€€€€€€‰½½°Á…ÍÍÝ½É€ô™…±Í”°4(€€€€€€€‰½½°µÕ±Ñ¥±¥¹”€ô™…±Í”¤4(€€€ì4(€€€€€€€Ý¡¥±”€¡Ñ…‰±”¹I½ÝMÑå±•Ì¹½Õ¹Ð€ðôÉ½Ü¤4(€€€€€€€€€€€Ñ…‰±”¹I½ÝMÑå±•Ì¹‘¡¹•ÜI½ÝMÑå±”¡M¥é•QåÁ”¹ÕÑ½M¥é”¤¤ì4(4(€€€€€€€Ù…È¡½±‘•È€ô¹•ÜQ…‰±•1…å½ÕÑA…¹•°4(€€€€€€€ì4(€€€€€€€€€€€½¬€ô½­MÑå±”¹Q½À°4(€€€€€€€€€€€ÕÑ½M¥é”€ô™…±Í”°4(€€€€€€€€€€€!•¥¡Ð€ôµÕ±Ñ¥±¥¹”€ü€ÄÜÀ€è€ÜØ°4(€€€€€€€€€€€½±Õµ¹½Õ¹Ð€ô€Ä°4(€€€€€€€€€€€I½Ý½Õ¹Ð€ô€È°4(€€€€€€€€€€€A…‘‘¥¹œ€ô¹•ÜA…‘‘¥¹œ À°€Ð°€À°€Ü¤°4(€€€€€€€€€€€5…É¥¸€ô¹•ÜA…‘‘¥¹œ¡½±Õµ¸€ôô€À€ü€Ð€è€à°€Ô°½±Õµ¸€ôô€À€ü€à€è€Ð°€È¤4(€€€€€€€ôì4(€€€€€€€¡½±‘•È¹½±Õµ¹MÑå±•Ì¹‘¡¹•Ü½±Õµ¹MÑå±”¡M¥é•QåÁ”¹A•É•¹Ð°€ÄÀÀ¤¤ì4(€€€€€€€¡½±‘•È¹I½ÝMÑå±•Ì¹‘¡¹•ÜI½ÝMÑå±”¡M¥é•QåÁ”¹‰Í½±ÕÑ”°€ÈÈ¤¤ì4(€€€€€€€¡½±‘•È¹I½ÝMÑå±•Ì¹‘¡¹•ÜI½ÝMÑå±”¡M¥é•QåÁ”¹A•É•¹Ð°€ÄÀÀ¤¤ì4(€€€€€€€Ù…È…ÁÑ¥½¸€ô¹•Ü1…‰•°4(€€€€€€€ì4(€€€€€€€€€€€Q•áÐ€ô±…‰•°¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤°4(€€€€€€€€€€€•ÍÍ¥‰±•9…µ”€ô€‰í±…‰•±ô™¥•±Ñ¥Ñ±”ˆ°4(€€€€€€€€€€€½¬€ô½­MÑå±”¹¥±°°4(€€€€€€€€€€€ÕÑ½M¥é”€ô™…±Í”°4(€€€€€€€€€€€Q•áÑ±¥¸€ô½¹Ñ•¹Ñ±¥¹µ•¹Ð¹5¥‘‘±•1•™Ð°4(€€€€€€€€€€€½¹Ð€ôU¥Q¡•µ”¹½¹Ð à°½¹ÑMÑå±”¹	½±¤°4(€€€€€€€€€€€½É•½±½È€ôU¥Q¡•µ”¹5ÕÑ•°4(€€€€€€€€€€€5…É¥¸€ôA…‘‘¥¹œ¹µÁÑä4(€€€€€€€ôì4(€€€€€€€Ù…ÈÑ•áÑ	½à€ô¹•ÜQ•áÑ	½à4(€€€€€€€ì4(€€€€€€€€€€€Q•áÐ€ôÙ…±Õ”°4(€€€€€€€€€€€A±…•¡½±‘•ÉQ•áÐ€ôÍÑÉ¥¹œ¹µÁÑä°4(€€€€€€€€€€€•ÍÍ¥‰±•9…µ”€ô±…‰•°°4(€€€€€€€€€€€½¬€ô½­MÑå±”¹¥±°°4(€€€€€€€€€€€5Õ±Ñ¥±¥¹”€ôµÕ±Ñ¥±¥¹”°4(€€€€€€€€€€€MÉ½±±	…ÉÌ€ôµÕ±Ñ¥±¥¹”€üMÉ½±±	…ÉÌ¹Y•ÉÑ¥…°€èMÉ½±±	…ÉÌ¹9½¹”°4(€€€€€€€€€€€UÍ•MåÍÑ•µA…ÍÍÝ½É‘¡…È€ôÁ…ÍÍÝ½É°4(€€€€€€€€€€€½¹Ð€ôU¥Q¡•µ”¹½¹Ð ÄÀ¤°4(€€€€€€€€€€€	…­½±½È€ôU¥Q¡•µ”¹%¹ÁÕÑMÕÉ™…”°4(€€€€€€€€€€€½É•½±½È€ôU¥Q¡•µ”¹Q•áÐ°4(€€€€€€€€€€€	½É‘•ÉMÑå±”€ô	½É‘•ÉMÑå±”¹¥á•‘M¥¹±”4(€€€€€€€ôì4(€€€€€€€¡½±‘•È¹½¹ÑÉ½±Ì¹‘¡…ÁÑ¥½¸°€À°€À¤ì4(€€€€€€€¡½±‘•È¹½¹ÑÉ½±Ì¹‘¡Ñ•áÑ	½à°€À°€Ä¤ì4(€€€€€€€Ñ…‰±”¹½¹ÑÉ½±Ì¹‘¡¡½±‘•È°½±Õµ¸°É½Ü¤ì4(€€€€€€€¥˜€¡ÍÁ…¸¤4(€€€€€€€€€€€Ñ…‰±”¹M•Ñ½±Õµ¹MÁ…¸¡¡½±‘•È°€È¤ì4(€€€€€€€}™¥•±‘Ím­•åt€ôÑ•áÑ	½àì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥M…Ù•}±¥¬¡½‰©•ÐüÍ•¹‘•È°Ù•¹ÑÉÌ”¤4(€€€ì4(€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Y…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹•ÍÉ¥ÁÑ¥½¸¤¤¤¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰¹Ñ•È…¸•ÅÕ¥Áµ•¹Ð‘•ÍÉ¥ÁÑ¥½¸¸ˆ°€‰•ÍÉ¥ÁÑ¥½¸É•ÅÕ¥É•ˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€¥…±½I•ÍÕ±Ð€ô¥…±½I•ÍÕ±Ð¹9½¹”ì4(€€€€€€€€€€€}™¥•±‘Ím¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹•ÍÉ¥ÁÑ¥½¸¥t¹½ÕÌ ¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(4(€€€€€€€Ù…ÈÁ•¹‘¥¹œ€ô¹•Ü1¥ÍÐð¡9•ÑÝ½É­%¹Ñ•É™…•‘¥Ñ½ÉI½ÜI½Ü°ÍÑÉ¥¹œ%À°ÍÑÉ¥¹œ5…Œ°4(€€€€€€€€€€€9•ÑÝ½É­%¹Ñ•É™…•QåÁ”QåÁ”¤ø ¤ì4(€€€€€€€™½É•… €¡Ù…ÈÉ½Ü¥¸}¹•ÑÝ½É­I½ÝÌ¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÉ…Ý%À€ôÉ½Ü¹%Á‘‘É•ÍÌ¹Q•áÐ¹QÉ¥´ ¤ì4(€€€€€€€€€€€Ù…ÈÉ…Ý5…Œ€ôÉ½Ü¹5…‘‘É•ÍÌ¹Q•áÐ¹QÉ¥´ ¤ì4(€€€€€€€€€€€Ù…È¹½Éµ…±¥é•‘%À€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€€€€€Ù…È¹½Éµ…±¥é•‘5…Œ€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€€€€€¥˜€¡É…Ý%À¹1•¹Ñ €ø€À€˜˜€…%ÁØÑ‘‘É•ÍÍQ•áÐ¹QÉåA…ÉÍ”¡É…Ý%À°½ÕÐ|°½ÕÐ¹½Éµ…±¥é•‘%À¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€ˆíÉ…Ý%Áôœ¥Ì¹½Ð„Ù…±¥%AØÐ…‘‘É•ÍÌ¸ˆ°€‰¡•¬%@…‘‘É•ÍÌˆ°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€€€€€¥…±½I•ÍÕ±Ð€ô¥…±½I•ÍÕ±Ð¹9½¹”ì4(€€€€€€€€€€€€€€€É½Ü¹%Á‘‘É•ÍÌ¹½ÕÌ ¤ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€¥˜€¡É…Ý5…Œ¹1•¹Ñ €ø€À€˜˜€…5…‘‘É•ÍÍQ•áÐ¹QÉåA…ÉÍ”¡É…Ý5…Œ°½ÕÐ¹½Éµ…±¥é•‘5…Œ¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°4(€€€€€€€€€€€€€€€€€€€€ˆíÉ…Ý5…ôœ¥Ì¹½Ð„Ù…±¥5…‘‘É•ÍÌ¸UÍ”€ÄÈ¡•á…‘•¥µ…°‘¥¥ÑÌì½±½¹Ì°¡åÁ¡•¹Ì°‘½ÑÌ°…¹ÍÁ…•Ì…É”…•ÁÑ•¸ˆ°4(€€€€€€€€€€€€€€€€€€€€‰¡•¬5…‘‘É•ÍÌˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€€€€€¥…±½I•ÍÕ±Ð€ô¥…±½I•ÍÕ±Ð¹9½¹”ì4(€€€€€€€€€€€€€€€É½Ü¹5…‘‘É•ÍÌ¹½ÕÌ ¤ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…È¥À€ôÉ…Ý%À¹1•¹Ñ €ôô€À€üÍÑÉ¥¹œ¹µÁÑä€è¹½Éµ…±¥é•‘%Àì4(€€€€€€€€€€€Ù…Èµ…Œ€ôÉ…Ý5…Œ¹1•¹Ñ €ôô€À€üÍÑÉ¥¹œ¹µÁÑä€è¹½Éµ…±¥é•‘5…Œì4(€€€€€€€€€€€Ù…ÈÑåÁ”€ôÉ½Ü¹QåÁ”¹M•±•Ñ•‘%Ñ•´¥Ì9•ÑÝ½É­%¹Ñ•É™…•QåÁ”Í•±•Ñ•‘QåÁ”4(€€€€€€€€€€€€€€€€üÍ•±•Ñ•‘QåÁ”4(€€€€€€€€€€€€€€€€è9•ÑÝ½É­%¹Ñ•É™…•QåÁ”¹5…¥¸ì4(€€€€€€€€€€€Á•¹‘¥¹œ¹‘ ¡É½Ü°¥À°µ…Œ°ÑåÁ”¤¤ì4(€€€€€€€ô4(€€€€€€€Ù…È‘ÕÁ±¥…Ñ•%À€ôÁ•¹‘¥¹œ¹]¡•É”¡¥Ñ•´€ôø¥Ñ•´¹%À¹1•¹Ñ €ø€À¤4(€€€€€€€€€€€€¹É½ÕÁ	ä¡¥Ñ•´€ôø¥Ñ•´¹%À°MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤4(€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±Ð¡É½ÕÀ€ôøÉ½ÕÀ¹½Õ¹Ð ¤€ø€Ä¤ì4(€€€€€€€¥˜€¡‘ÕÁ±¥…Ñ•%À¥Ì¹½Ð¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰%@…‘‘É•ÍÌí‘ÕÁ±¥…Ñ•%À¹-•åô¥Ì±¥ÍÑ•µ½É”Ñ¡…¸½¹”½¸Ñ¡¥Ì‘•Ù¥”¸ˆ°4(€€€€€€€€€€€€€€€€‰ÕÁ±¥…Ñ”%@…‘‘É•ÍÌˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€¥…±½I•ÍÕ±Ð€ô¥…±½I•ÍÕ±Ð¹9½¹”ì4(€€€€€€€€€€€‘ÕÁ±¥…Ñ•%À¹¥ÉÍÐ ¤¹I½Ü¹%Á‘‘É•ÍÌ¹½ÕÌ ¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(4(€€€€€€€}Í½ÕÉ”¹•ÍÉ¥ÁÑ¥½¸€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹•ÍÉ¥ÁÑ¥½¸¤¤ì4(€€€€€€€}Í½ÕÉ”¹5…¹Õ™…ÑÕÉ•È€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹5…¹Õ™…ÑÕÉ•È¤¤ì4(€€€€€€€}Í½ÕÉ”¹A…ÉÑ9Õµ‰•È€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹A…ÉÑ9Õµ‰•È¤¤ì4(€€€€€€€}Í½ÕÉ”¹ÅÕ¥Áµ•¹Ñ%€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹ÅÕ¥Áµ•¹Ñ%¤¤ì4(€€€€€€€}Í½ÕÉ”¹!½ÍÑ¹…µ”€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹!½ÍÑ¹…µ”¤¤ì4(€€€€€€€}Í½ÕÉ”¹M•É¥…±9Õµ‰•È€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹M•É¥…±9Õµ‰•È¤¤ì4(€€€€€€€}Í½ÕÉ”¹¥ÉµÝ…É”€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹¥ÉµÝ…É”¤¤ì4(€€€€€€€Ù…È¥¹Ñ•É™…•Ì€ô¹•Ü1¥ÍÐñ9•ÑÝ½É­%¹Ñ•É™…•I•½Éø ¤ì4(€€€€€€€™½É•… €¡Ù…ÈÁ•¹‘¥¹%Ñ•´¥¸Á•¹‘¥¹œ¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È¥Ñ•´€ôÁ•¹‘¥¹%Ñ•´¹I½Ü¹M½ÕÉ”ì4(€€€€€€€€€€€Ù…È¥À€ôÁ•¹‘¥¹%Ñ•´¹%Àì4(€€€€€€€€€€€Ù…Èµ…Œ€ôÁ•¹‘¥¹%Ñ•´¹5…Œì4(€€€€€€€€€€€Ù…ÈÑåÁ”€ôÁ•¹‘¥¹%Ñ•´¹QåÁ”ì4(€€€€€€€€€€€Ù…È¡…¹•€ô¥Ñ•´¹QåÁ”€„ôÑåÁ”ñð4(€€€€€€€€€€€€€€€€…ÍÑÉ¥¹œ¹ÅÕ…±Ì¡¥Ñ•´¹%Á‘‘É•ÍÌ°¥À°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ñð4(€€€€€€€€€€€€€€€€…5…‘‘É•ÍÍQ•áÐ¹ÅÕ…±Í9½Éµ…±¥é•¡¥Ñ•´¹5…‘‘É•ÍÌ°µ…Œ¤€˜˜4(€€€€€€€€€€€€€€€€„¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡¥Ñ•´¹5…‘‘É•ÍÌ¤€˜˜ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡µ…Œ¤¤ì4(€€€€€€€€€€€¥Ñ•´¹QåÁ”€ôÑåÁ”ì4(€€€€€€€€€€€¥Ñ•´¹%Á‘‘É•ÍÌ€ô¥Àì4(€€€€€€€€€€€¥Ñ•´¹5…‘‘É•ÍÌ€ôµ…Œì4(€€€€€€€€€€€¥˜€¡¡…¹•¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€¥Ñ•´¹9•ÑÝ½É­MÑ…Ñ”€ô¥À¹1•¹Ñ €ôô€À€ü9•ÑÝ½É­MÑ…Ñ”¹9½‘‘É•ÍÌ€è9•ÑÝ½É­MÑ…Ñ”¹U¹­¹½Ý¸ì4(€€€€€€€€€€€€€€€¥Ñ•´¹1…ÍÑ¡•­•‘UÑŒ€ô¹Õ±°ì4(€€€€€€€€€€€€€€€¥Ñ•´¹1…ÍÑ1…Ñ•¹å5Ì€ô¹Õ±°ì4(€€€€€€€€€€€€€€€¥Ñ•´¹1…ÍÑ9•ÑÝ½É­ÉÉ½È€ô¥À¹1•¹Ñ €ôô€À€üÍÑÉ¥¹œ¹µÁÑä€è€‰]…¥Ñ¥¹œ™½Èµ…¹Õ…°Ù•É¥™¥…Ñ¥½¸¸ˆì4(€€€€€€€€€€€€€€€¥Ñ•´¹=‰Í•ÉÙ•‘5…‘‘É•ÍÌ€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€€€€€€€€€¥Ñ•´¹5…Y•É¥™¥…Ñ¥½¹5•ÍÍ…”€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€€€€€€€€€¥Ñ•´¹!ÑÑÁA½ÉÑ=Á•¸€ô™…±Í”ì4(€€€€€€€€€€€€€€€¥Ñ•´¹!ÑÑÁÍA½ÉÑ=Á•¸€ô™…±Í”ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€¥¹Ñ•É™…•Ì¹‘¡¥Ñ•´¤ì4(€€€€€€€ô4(€€€€€€€}Í½ÕÉ”¹9•ÑÝ½É­%¹Ñ•É™…•Ì€ô¥¹Ñ•É™…•Ìì4(€€€€€€€}Í½ÕÉ”¹MÕ‰¹•Ð€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹MÕ‰¹•Ð¤¤ì4(€€€€€€€}Í½ÕÉ”¹…Ñ•Ý…ä€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹…Ñ•Ý…ä¤¤ì4(€€€€€€€}Í½ÕÉ”¹M•É¥…±½¹¹•Ñ¥½¸€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹M•É¥…±½¹¹•Ñ¥½¸¤¤ì4(€€€€€€€}Í½ÕÉ”¹UÍ•É¹…µ”€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹UÍ•É¹…µ”¤¤ì4(€€€€€€€}Í½ÕÉ”¹A…ÍÍÝ½É€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹A…ÍÍÝ½É¤¤ì4(€€€€€€€}Í½ÕÉ”¹M½ÕÉ•¥±”€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹M½ÕÉ•¥±”¤¤ì4(€€€€€€€}Í½ÕÉ”¹9½Ñ•Ì€ôY…±Õ”¡¹…µ•½˜¡ÅÕ¥Áµ•¹ÑI•½É¹9½Ñ•Ì¤¤ì4(€€€€€€€}Í½ÕÉ”¹½¹™¥ÕÉ…Ñ¥½¹¥±•Ì€ô}½¹™¥ÕÉ…Ñ¥½¹¥±•Ì¹M•±•Ð¡±½¹•½¹™¥ÕÉ…Ñ¥½¹¥±”¤¹Q½1¥ÍÐ ¤ì4(€€€€€€€}Í½ÕÉ”¹UÁ‘…Ñ•‘UÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üì4(4(€€€€€€€}Í½ÕÉ”¹Må¹1•…å9•ÑÝ½É­¥•±‘Ì ¤ì4(€€€€€€€}Í½ÕÉ”¹UÁ‘…Ñ•É•…Ñ•9•ÑÝ½É­MÑ…Ñ” ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑÉ¥¹œY…±Õ”¡ÍÑÉ¥¹œ­•ä¤€ôø}™¥•±‘Ím­•åt¹Q•áÐ¹QÉ¥´ ¤ì4(4(€€€ÁÉ¥Ù…Ñ”Í•…±•±…ÍÌ9•ÑÝ½É­%¹Ñ•É™…•‘¥Ñ½ÉI½Ü4(€€€ì4(€€€€€€€ÁÕ‰±¥Œ9•ÑÝ½É­%¹Ñ•É™…•I•½ÉM½ÕÉ”ì•Ðìô4(€€€€€€€ÁÕ‰±¥ŒA…¹•°½¹Ñ…¥¹•Èì•Ðìô€ô¹•Ü ¤ì4(€€€€€€€ÁÕ‰±¥Œ½µ‰½	½àQåÁ”ì•Ðìô€ô¹•Ü ¤ì4(€€€€€€€ÁÕ‰±¥ŒQ•áÑ	½à%Á‘‘É•ÍÌì•Ðìô€ô¹•Ü ¤ì4(€€€€€€€ÁÕ‰±¥ŒQ•áÑ	½à5…‘‘É•ÍÌì•Ðìô€ô¹•Ü ¤ì4(€€€€€€€ÁÕ‰±¥Œ	ÕÑÑ½¸I•µ½Ù•	ÕÑÑ½¸ì•Ðìô4(4(€€€€€€€ÁÕ‰±¥Œ9•ÑÝ½É­%¹Ñ•É™…•‘¥Ñ½ÉI½Ü¡9•ÑÝ½É­%¹Ñ•É™…•I•½ÉÍ½ÕÉ”¤4(€€€€€€€ì4(€€€€€€€€€€€M½ÕÉ”€ôÍ½ÕÉ”ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹M¥é”€ô¹•ÜM¥é” ÜÄÀ°€äÈ¤ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹5…É¥¸€ô¹•ÜA…‘‘¥¹œ Ð°€È°€Ð°€à¤ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹	…­½±½È€ôU¥Q¡•µ”¹!•…‘•ÉMÕÉ™…”ì4(4(€€€€€€€€€€€‘‘1…‰•° ‰QeAˆ°€ÄÐ°€ÄÀ°€ÄÌÈ¤ì4(€€€€€€€€€€€‘‘1…‰•° ‰%@IMLˆ°€ÄÔà°€ÄÀ°€ÈÈÀ¤ì4(€€€€€€€€€€€‘‘1…‰•° ‰5IMLˆ°€ÌäÀ°€ÄÀ°€ÈÐÐ¤ì4(4(€€€€€€€€€€€QåÁ”¹É½Á½Ý¹MÑå±”€ô½µ‰½	½áMÑå±”¹É½Á½Ý¹1¥ÍÐì4(€€€€€€€€€€€QåÁ”¹%Ñ•µÌ¹‘‘I…¹”¡¹Õ´¹•ÑY…±Õ•Ìñ9•ÑÝ½É­%¹Ñ•É™…•QåÁ”ø ¤¹…ÍÐñ½‰©•Ðø ¤¹Q½ÉÉ…ä ¤¤ì4(€€€€€€€€€€€QåÁ”¹M•±•Ñ•‘%Ñ•´€ôÍ½ÕÉ”¹QåÁ”ì4(€€€€€€€€€€€QåÁ”¹1½…Ñ¥½¸€ô¹•ÜA½¥¹Ð ÄÐ°€ÌÐ¤ì4(€€€€€€€€€€€QåÁ”¹M¥é”€ô¹•ÜM¥é” ÄÌÈ°€Èä¤ì4(€€€€€€€€€€€QåÁ”¹½¹Ð€ôU¥Q¡•µ”¹½¹Ð ä¸Õ˜¤ì4(€€€€€€€€€€€U¥Q¡•µ”¹½¹™¥ÕÉ•U¹¥™½Éµ½µ‰½	½à¡QåÁ”¤ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹½¹ÑÉ½±Ì¹‘¡QåÁ”¤ì4(4(€€€€€€€€€€€%Á‘‘É•ÍÌ¹Q•áÐ€ôÍ½ÕÉ”¹%Á‘‘É•ÍÌì4(€€€€€€€€€€€%Á‘‘É•ÍÌ¹A±…•¡½±‘•ÉQ•áÐ€ô€‰%@…‘‘É•ÍÌˆì4(€€€€€€€€€€€%Á‘‘É•ÍÌ¹•ÍÍ¥‰±•9…µ”€ô€‰%@…‘‘É•ÍÌˆì4(€€€€€€€€€€€%Á‘‘É•ÍÌ¹1½…Ñ¥½¸€ô¹•ÜA½¥¹Ð ÄÔà°€ÌÐ¤ì4(€€€€€€€€€€€%Á‘‘É•ÍÌ¹M¥é”€ô¹•ÜM¥é” ÈÈÀ°€Èä¤ì4(€€€€€€€€€€€%Á‘‘É•ÍÌ¹½¹Ð€ôU¥Q¡•µ”¹½¹Ð ä¸Õ˜¤ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹½¹ÑÉ½±Ì¹‘¡%Á‘‘É•ÍÌ¤ì4(4(€€€€€€€€€€€5…‘‘É•ÍÌ¹Q•áÐ€ôÍ½ÕÉ”¹5…‘‘É•ÍÌì4(€€€€€€€€€€€5…‘‘É•ÍÌ¹A±…•¡½±‘•ÉQ•áÐ€ô€‰5…‘‘É•ÍÌˆì4(€€€€€€€€€€€5…‘‘É•ÍÌ¹•ÍÍ¥‰±•9…µ”€ô€‰5…‘‘É•ÍÌˆì4(€€€€€€€€€€€5…‘‘É•ÍÌ¹1½…Ñ¥½¸€ô¹•ÜA½¥¹Ð ÌäÀ°€ÌÐ¤ì4(€€€€€€€€€€€5…‘‘É•ÍÌ¹M¥é”€ô¹•ÜM¥é” ÈÐÐ°€Èä¤ì4(€€€€€€€€€€€5…‘‘É•ÍÌ¹½¹Ð€ôU¥Q¡•µ”¹½¹Ð ä¸Õ˜¤ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹½¹ÑÉ½±Ì¹‘¡5…‘‘É•ÍÌ¤ì4(4(€€€€€€€€€€€I•µ½Ù•	ÕÑÑ½¸€ôU¥Q¡•µ”¹…¹•É	ÕÑÑ½¸ ‹\ˆ¤ì4(€€€€€€€€€€€I•µ½Ù•	ÕÑÑ½¸¹ÕÑ½M¥é”€ô™…±Í”ì4(€€€€€€€€€€€I•µ½Ù•	ÕÑÑ½¸¹M¥é”€ô¹•ÜM¥é” ÐÈ°€ÌÀ¤ì4(€€€€€€€€€€€I•µ½Ù•	ÕÑÑ½¸¹1½…Ñ¥½¸€ô¹•ÜA½¥¹Ð ØÔÀ°€ÌÌ¤ì4(€€€€€€€€€€€I•µ½Ù•	ÕÑÑ½¸¹½¹Ð€ôU¥Q¡•µ”¹½¹Ð ÄÌ°½¹ÑMÑå±”¹	½±¤ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹½¹ÑÉ½±Ì¹‘¡I•µ½Ù•	ÕÑÑ½¸¤ì4(€€€€€€€ô4(4(€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥‘‘1…‰•°¡ÍÑÉ¥¹œÑ•áÐ°¥¹Ð±•™Ð°¥¹ÐÑ½À°¥¹ÐÝ¥‘Ñ ¤4(€€€€€€€ì4(€€€€€€€€€€€½¹Ñ…¥¹•È¹½¹ÑÉ½±Ì¹‘¡¹•Ü1…‰•°4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Q•áÐ€ôÑ•áÐ°4(€€€€€€€€€€€€€€€ÕÑ½M¥é”€ô™…±Í”°4(€€€€€€€€€€€€€€€M¥é”€ô¹•ÜM¥é”¡Ý¥‘Ñ °€Äà¤°4(€€€€€€€€€€€€€€€1½…Ñ¥½¸€ô¹•ÜA½¥¹Ð¡±•™Ð°Ñ½À¤°4(€€€€€€€€€€€€€€€½¹Ð€ôU¥Q¡•µ”¹½¹Ð à°½¹ÑMÑå±”¹	½±¤°4(€€€€€€€€€€€€€€€½É•½±½È€ôU¥Q¡•µ”¹5ÕÑ•°4(€€€€€€€€€€€€€€€Q•áÑ±¥¸€ô½¹Ñ•¹Ñ±¥¹µ•¹Ð¹5¥‘‘±•1•™Ð4(€€€€€€€€€€€ô¤ì4(€€€€€€€ô4(€€€ô4)ô4(