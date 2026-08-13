using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace InNasc;

internal sealed class N8IpScannerForm : Form
{
    private readonly ComboBox _nicPicker = new();
    private readonly TextBox _cidr = new();
    private readonly DataGridView _grid = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _progressLabel = new();
    private readonly Button _scanButton;
    private readonly Button _cancelButton;
    private readonly TextBox _ipAddress = new();
    private readonly TextBox _subnetMask = new();
    private readonly TextBox _gateway = new();
    private readonly TextBox _primaryDns = new();
    private readonly TextBox _secondaryDns = new();
    private readonly Label _nicState = new();
    private readonly List<ScannerResult> _results = [];
    private CancellationTokenSource? _scanCancellation;
    private bool _loadingNics;

    public N8IpScannerForm()
    {
        Text = "N8's IP Scanner";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1320, 790);
        MinimumSize = new Size(1080, 680);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        _scanButton = UiTheme.PrimaryButton("Scan network");
        _scanButton.Click += async (_, _) => await StartScanAsync();
        _cancelButton = UiTheme.SecondaryButton("Cancel");
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => _scanCancellation?.Cancel();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildHeader(), 0, 0);
        shell.Controls.Add(BuildToolbar(), 0, 1);
        shell.Controls.Add(BuildBody(), 0, 2);
        Controls.Add(shell);

        UiTheme.ConfigureUniformComboBox(_nicPicker);
        UiTheme.ApplyTheme(this);
        LoadNetworkAdapters();
        FormClosing += ScannerForm_FormClosing;
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Navy,
            Padding = new Padding(22, 0, 22, 0)
        };
        var logo = N8Brand.CreateLogo(58, 44, onDarkBackground: true);
        logo.Location = new Point(22, 19);
        header.Controls.Add(logo);
        header.Controls.Add(new Label
        {
            Text = "N8's IP Scanner",
            AutoSize = true,
            ForeColor = Color.White,
            Font = UiTheme.Font(18, FontStyle.Bold),
            Location = new Point(92, 17)
        });
        header.Controls.Add(new Label
        {
            Text = "Selected-NIC discovery, web service detection, and adapter configuration",
            AutoSize = true,
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(94, 48)
        });
        return header;
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            WrapContents = false,
            Padding = new Padding(20, 13, 20, 8)
        };
        bar.Controls.Add(ToolbarLabel("Network adapter"));
        _nicPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        _nicPicker.Width = 250;
        _nicPicker.Margin = new Padding(0, 2, 14, 0);
        _nicPicker.SelectedIndexChanged += NicPicker_SelectedIndexChanged;
        bar.Controls.Add(_nicPicker);
        var refresh = UiTheme.SecondaryButton("Refresh NICs");
        refresh.Click += (_, _) => LoadNetworkAdapters();
        refresh.Margin = new Padding(0, 0, 18, 0);
        bar.Controls.Add(refresh);
        bar.Controls.Add(ToolbarLabel("CIDR range"));
        _cidr.Width = 155;
        _cidr.Font = UiTheme.Font(10);
        _cidr.Margin = new Padding(0, 4, 14, 0);
        bar.Controls.Add(_cidr);
        bar.Controls.Add(_scanButton);
        bar.Controls.Add(_cancelButton);
        var clear = UiTheme.SecondaryButton("Clear");
        clear.Click += (_, _) => ClearResults();
        bar.Controls.Add(clear);
        var export = UiTheme.SecondaryButton("Export CSV");
        export.Click += (_, _) => ExportCsv();
        bar.Controls.Add(export);
        return bar;
    }

    private static Label ToolbarLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.Muted,
        Font = UiTheme.Font(8.5f, FontStyle.Bold),
        Margin = new Padding(0, 10, 8, 0)
    };

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(20, 18, 20, 20)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        body.Controls.Add(BuildResultsPanel(), 0, 0);
        body.Controls.Add(BuildNicSettingsPanel(), 1, 0);
        return body;
    }

    private Control BuildResultsPanel()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 18, 0),
            Padding = new Padding(1)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(new Label
        {
            Text = "Discovered devices",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(14, FontStyle.Bold),
            Margin = new Padding(2, 7, 0, 0)
        }, 0, 0);

        ConfigureGrid();
        layout.Controls.Add(_grid, 0, 1);

        var progressPanel = new Panel { Dock = DockStyle.Fill };
        _progress.Location = new Point(2, 8);
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _progress.Width = 560;
        _progress.Height = 12;
        _progress.Style = ProgressBarStyle.Continuous;
        _progressLabel.AutoSize = true;
        _progressLabel.Text = "Ready to scan";
        _progressLabel.ForeColor = UiTheme.Muted;
        _progressLabel.Location = new Point(2, 28);
        progressPanel.Controls.AddRange([_progress, _progressLabel]);
        progressPanel.Resize += (_, _) => _progress.Width = Math.Max(100, progressPanel.ClientSize.Width - 4);
        layout.Controls.Add(progressPanel, 0, 2);
        panel.Controls.Add(layout);
        return panel;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = UiTheme.Surface;
        _grid.BorderStyle = BorderStyle.None;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowTemplate.Height = 39;
        _grid.ColumnHeadersHeight = 40;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiTheme.HeaderSurface,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            SelectionBackColor = UiTheme.HeaderSurface
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            ForeColor = UiTheme.Text,
            SelectionBackColor = UiTheme.Selection,
            SelectionForeColor = UiTheme.Text,
            Font = UiTheme.Font(9.2f)
        };
        _grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.AlternateSurface;
        _grid.GridColor = UiTheme.GridLine;
        AddColumn("IpAddress", "IP ADDRESS", 125);
        AddColumn("Hostname", "HOSTNAME", 175);
        AddColumn("MacAddress", "MAC ADDRESS", 145);
        AddColumn("Manufacturer", "MANUFACTURER", 145);
        AddColumn("Status", "STATUS", 125);
        AddColumn("Latency", "LATENCY", 85);
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        UiTheme.EnableDoubleBuffer(_grid);
    }

    private void AddColumn(string name, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
    }

    private Control BuildNicSettingsPanel()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(19)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        for (var index = 0; index < 5; index++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Panel { Dock = DockStyle.Fill };
        title.Controls.Add(new Label
        {
            Text = "Selected NIC Settings",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(14, FontStyle.Bold),
            Location = new Point(0, 0)
        });
        _nicState.AutoSize = true;
        _nicState.ForeColor = UiTheme.Muted;
        _nicState.Location = new Point(1, 31);
        title.Controls.Add(_nicState);
        table.Controls.Add(title, 0, 0);
        table.Controls.Add(SettingField("IP address", _ipAddress), 0, 1);
        table.Controls.Add(SettingField("Subnet mask", _subnetMask), 0, 2);
        table.Controls.Add(SettingField("Default gateway", _gateway), 0, 3);
        table.Controls.Add(SettingField("Primary DNS", _primaryDns), 0, 4);
        table.Controls.Add(SettingField("Secondary DNS", _secondaryDns), 0, 5);
        var apply = UiTheme.PrimaryButton("Apply static settings");
        apply.Dock = DockStyle.Fill;
        apply.Margin = new Padding(0, 6, 0, 6);
        apply.Click += async (_, _) => await ApplyStaticSettingsAsync();
        table.Controls.Add(apply, 0, 6);
        var dhcp = UiTheme.SecondaryButton("Set address and DNS to DHCP");
        dhcp.Dock = DockStyle.Fill;
        dhcp.Margin = new Padding(0, 6, 0, 6);
        dhcp.Click += async (_, _) => await SetDhcpAsync();
        table.Controls.Add(dhcp, 0, 7);
        table.Controls.Add(new Label
        {
            Text = "Changing adapter settings requires Windows administrator approval. The app uses the normal UAC prompt.",
            Dock = DockStyle.Top,
            Height = 60,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.5f),
            Padding = new Padding(0, 10, 0, 0)
        }, 0, 8);
        panel.Controls.Add(table);
        return panel;
    }

    private static Control SettingField(string label, TextBox textBox)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.3f, FontStyle.Bold),
            Location = new Point(0, 2)
        });
        textBox.Dock = DockStyle.Bottom;
        textBox.Height = 31;
        textBox.Font = UiTheme.Font(10);
        textBox.BorderStyle = BorderStyle.FixedSingle;
        panel.Controls.Add(textBox);
        return panel;
    }

    private void LoadNetworkAdapters()
    {
        _loadingNics = true;
        var previousId = (_nicPicker.SelectedItem as ScannerNicChoice)?.NicId;
        _nicPicker.Items.Clear();
        var choices = ScannerNicChoice.FindAll();
        foreach (var choice in choices) _nicPicker.Items.Add(choice);
        var selectedIndex = choices.FindIndex(choice => choice.NicId == previousId);
        _nicPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : choices.Count > 0 ? 0 : -1;
        _loadingNics = false;
        UpdateNicDetails();
    }

    private void ScannerForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_scanCancellation is null) return;
        e.Cancel = true;
        _scanCancellation.Cancel();
        _progressLabel.Text = "Canceling scanâ€¦ Close the window again when cancellation completes.";
    }

    private void NicPicker_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loadingNics) return;
        UpdateNicDetails();
    }

    private void UpdateNicDetails()
    {
        if (_nicPicker.SelectedItem is not ScannerNicChoice choice)
        {
            _nicState.Text = "No active IPv4 adapter found";
            _ipAddress.Clear();
            _subnetMask.Clear();
            _gateway.Clear();
            _primaryDns.Clear();
            _secondaryDns.Clear();
            return;
        }
        _nicState.Text = $"{choice.OperationalStatus}  â€¢  {(choice.DhcpEnabled ? "DHCP" : "Static")}";
        _ipAddress.Text = choice.Ipv4Address;
        _subnetMask.Text = CidrRange.PrefixToMask(choice.PrefixLength);
        _gateway.Text = choice.Gateway;
        _primaryDns.Text = choice.DnsAddresses.ElementAtOrDefault(0) ?? string.Empty;
        _secondaryDns.Text = choice.DnsAddresses.ElementAtOrDefault(1) ?? string.Empty;
        _cidr.Text = CidrRange.NetworkCidr(choice.Ipv4Address, choice.PrefixLength);
    }

    private async Task StartScanAsync()
    {
        if (_scanCancellation is not null) return;
        if (_nicPicker.SelectedItem is not ScannerNicChoice nic)
        {
            MessageBox.Show(this, "Choose an active IPv4 network adapter.", "N8's IP Scanner",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            ret×o|¶‰žËkºwµça•¹±°¡Á¥¹Q…Í¬°¡ÑÑÁQ…Í¬°¡ÑÑÁÍQ…Í¬¤ì4(€€€€€€€Ù…ÈÁ¥¹œ€ô…Ý…¥ÐÁ¥¹Q…Í¬ì4(€€€€€€€Ù…È¡ÑÑÀ€ô…Ý…¥Ð¡ÑÑÁQ…Í¬ì4(€€€€€€€Ù…È¡ÑÑÁÌ€ô…Ý…¥Ð¡ÑÑÁÍQ…Í¬ì4(€€€€€€€¥˜€ …Á¥¹œ¹MÕ•ÍÌ€˜˜€…¡ÑÑÀ€˜˜€…¡ÑÑÁÌ¤É•ÑÕÉ¸¹Õ±°ì4(4(€€€€€€€Ù…È¡½ÍÑ¹…µ”€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€Ù…È±½½­ÕÀ€ô¹Ì¹•Ñ!½ÍÑ¹ÑÉåÍå¹Œ¡…‘‘É•ÍÌ¤ì4(€€€€€€€€€€€¡½ÍÑ¹…µ”€ô€¡…Ý…¥Ð±½½­ÕÀ¹]…¥ÑÍå¹Œ¡Q¥µ•MÁ…¸¹É½µ5¥±±¥Í•½¹‘Ì ÜÀÀ¤°…¹•±±…Ñ¥½¹Q½­•¸¤¤¹!½ÍÑ9…µ”ì4(€€€€€€€ô4(€€€€€€€…Ñ 4(€€€€€€€ì4(€€€€€€€€€€€€¼¼I•Ù•ÉÍ”9L¥Ì½ÁÑ¥½¹…°¸4(€€€€€€€ô4(€€€€€€€Ù…ÈÍÑ…ÑÕÌ€ô€¡¡ÑÑÀ°¡ÑÑÁÌ¤ÍÝ¥Ñ 4(€€€€€€€ì4(€€€€€€€€€€€€¡ÑÉÕ”°ÑÉÕ”¤€ôø€‰!QQ@€¼!QQALˆ°4(€€€€€€€€€€€€¡ÑÉÕ”°™…±Í”¤€ôø€‰!QQ@ˆ°4(€€€€€€€€€€€€¡™…±Í”°ÑÉÕ”¤€ôø€‰!QQALˆ°4(€€€€€€€€€€€|€ôø€‰=¹±¥¹”ˆ4(€€€€€€€ôì4(€€€€€€€É•ÑÕÉ¸¹•ÜM…¹¹•ÉI•ÍÕ±Ð 4(€€€€€€€€€€€…‘‘É•ÍÌ¹Q½MÑÉ¥¹œ ¤°¡½ÍÑ¹…µ”°ÍÑÉ¥¹œ¹µÁÑä°ÍÑÉ¥¹œ¹µÁÑä°ÍÑ…ÑÕÌ°4(€€€€€€€€€€€Á¥¹œ¹MÕ•ÍÌ€ü€‰íÁ¥¹œ¹I½Õ¹‘ÑÉ¥Á5¥±±¥Í•½¹‘ÍôµÌˆ€èÍÑÉ¥¹œ¹µÁÑä°4(€€€€€€€€€€€¡ÑÑÀ°¡ÑÑÁÌ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ…Íå¹ŒQ…Í¬ñ‰½½°ø…¹½¹¹•ÑÍå¹Œ 4(€€€€€€€%A‘‘É•ÍÌÍ½ÕÉ”°4(€€€€€€€%A‘‘É•ÍÌ‘•ÍÑ¥¹…Ñ¥½¸°4(€€€€€€€¥¹ÐÁ½ÉÐ°4(€€€€€€€¥¹ÐÑ¥µ•½ÕÑ5¥±±¥Í•½¹‘Ì°4(€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸…¹•±±…Ñ¥½¹Q½­•¸¤4(€€€ì4(€€€€€€€ÕÍ¥¹œÙ…ÈÑ¥µ•½ÕÐ€ô…¹•±±…Ñ¥½¹Q½­•¹M½ÕÉ”¹É•…Ñ•1¥¹­•‘Q½­•¹M½ÕÉ”¡…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€Ñ¥µ•½ÕÐ¹…¹•±™Ñ•È¡Ñ¥µ•½ÕÑ5¥±±¥Í•½¹‘Ì¤ì4(€€€€€€€ÕÍ¥¹œÙ…ÈÍ½­•Ð€ô¹•ÜM½­•Ð¡‘‘É•ÍÍ…µ¥±ä¹%¹Ñ•É9•ÑÝ½É¬°M½­•ÑQåÁ”¹MÑÉ•…´°AÉ½Ñ½½±QåÁ”¹QÀ¤ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€Í½­•Ð¹	¥¹¡¹•Ü%A¹‘A½¥¹Ð¡Í½ÕÉ”°€À¤¤ì4(€€€€€€€€€€€…Ý…¥ÐÍ½­•Ð¹½¹¹•ÑÍå¹Œ¡¹•Ü%A¹‘A½¥¹Ð¡‘•ÍÑ¥¹…Ñ¥½¸°Á½ÉÐ¤°Ñ¥µ•½ÕÐ¹Q½­•¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ÑÉÕ”ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡=Á•É…Ñ¥½¹…¹•±•‘á•ÁÑ¥½¸¤Ý¡•¸€ ……¹•±±…Ñ¥½¹Q½­•¸¹%Í…¹•±±…Ñ¥½¹I•ÅÕ•ÍÑ•¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡M½­•Ñá•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥A½ÁÕ±…Ñ•É¥ ¤4(€€€ì4(€€€€€€€}É¥¹I½ÝÌ¹±•…È ¤ì4(€€€€€€€™½É•… €¡Ù…ÈÉ•ÍÕ±Ð¥¸}É•ÍÕ±ÑÌ¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÉ½Ý%¹‘•à€ô}É¥¹I½ÝÌ¹‘¡É•ÍÕ±Ð¹%Á‘‘É•ÍÌ°É•ÍÕ±Ð¹!½ÍÑ¹…µ”°É•ÍÕ±Ð¹5…‘‘É•ÍÌ°4(€€€€€€€€€€€€€€€É•ÍÕ±Ð¹5…¹Õ™…ÑÕÉ•È°É•ÍÕ±Ð¹MÑ…ÑÕÌ°É•ÍÕ±Ð¹1…Ñ•¹ä¤ì4(€€€€€€€€€€€}É¥¹I½ÝÍmÉ½Ý%¹‘•át¹Q…œ€ôÉ•ÍÕ±Ðì4(€€€€€€€€€€€}É¥¹I½ÝÍmÉ½Ý%¹‘•át¹•±±ÍlÑt¹MÑå±”¹½É•½±½È€ôÉ•ÍÕ±Ð¹MÑ…ÑÕÌ€ôô€‰=¹±¥¹”ˆ€üU¥Q¡•µ”¹É••¸€èU¥Q¡•µ”¹	±Õ”ì4(€€€€€€€€€€€}É¥¹I½ÝÍmÉ½Ý%¹‘•át¹•±±ÍlÑt¹MÑå±”¹½¹Ð€ôU¥Q¡•µ”¹½¹Ð ä°½¹ÑMÑå±”¹	½±¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥±•…ÉI•ÍÕ±ÑÌ ¤4(€€€ì4(€€€€€€€¥˜€¡}Í…¹…¹•±±…Ñ¥½¸¥Ì¹½Ð¹Õ±°¤É•ÑÕÉ¸ì4(€€€€€€€}É•ÍÕ±ÑÌ¹±•…È ¤ì4(€€€€€€€}É¥¹I½ÝÌ¹±•…È ¤ì4(€€€€€€€}ÁÉ½É•ÍÌ¹Y…±Õ”€ô€Àì4(€€€€€€€}ÁÉ½É•ÍÍ1…‰•°¹Q•áÐ€ô€‰I•…‘äÑ¼Í…¸ˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥É¥‘}•±±½Õ‰±•±¥¬¡½‰©•ÐüÍ•¹‘•È°…Ñ…É¥‘Y¥•Ý•±±Ù•¹ÑÉÌ”¤4(€€€ì4(€€€€€€€¥˜€¡”¹I½Ý%¹‘•à€ð€Àñð}É¥¹I½ÝÍm”¹I½Ý%¹‘•át¹Q…œ¥Ì¹½ÐM…¹¹•ÉI•ÍÕ±ÐÉ•ÍÕ±Ð¤É•ÑÕÉ¸ì4(€€€€€€€Ù…ÈÍ¡•µ”€ôÉ•ÍÕ±Ð¹!ÑÑÁÍ=Á•¸€ü€‰¡ÑÑÁÌˆ€èÉ•ÍÕ±Ð¹!ÑÑÁ=Á•¸€ü€‰¡ÑÑÀˆ€è¹Õ±°ì4(€€€€€€€¥˜€¡Í¡•µ”¥Ì¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰Q¡¥Ì‘•Ù¥”‘¥¹½Ð•áÁ½Í”!QQ@½È!QQAL‘ÕÉ¥¹œÑ¡”Í…¸¸ˆ°4(€€€€€€€€€€€€€€€€‰=Á•¸‘•Ù¥”ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€AÉ½•ÍÌ¹MÑ…ÉÐ¡¹•ÜAÉ½•ÍÍMÑ…ÉÑ%¹™¼ ‰íÍ¡•µ•ôè¼½íÉ•ÍÕ±Ð¹%Á‘‘É•ÍÍôˆ¤ìUÍ•M¡•±±á•ÕÑ”€ôÑÉÕ”ô¤ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°•á•ÁÑ¥½¸¹5•ÍÍ…”°€‰=Á•¸‘•Ù¥”ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Ù½¥áÁ½ÉÑÍØ ¤4(€€€ì4(€€€€€€€¥˜€¡}É•ÍÕ±ÑÌ¹½Õ¹Ð€ôô€À¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰IÕ¸„Í…¸‰•™½É”•áÁ½ÉÑ¥¹œÉ•ÍÕ±ÑÌ¸ˆ°€‰áÁ½ÉÐMXˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(€€€€€€€ÕÍ¥¹œÙ…È‘¥…±½œ€ô¹•ÜM…Ù•¥±•¥…±½œ4(€€€€€€€ì4(€€€€€€€€€€€¥±Ñ•È€ô€‰MX™¥±”€ ¨¹ÍØ¥ð¨¹ÍØˆ°4(€€€€€€€€€€€¥±•9…µ”€ô€‰8àµ%@µM…¸µí…Ñ•Q¥µ”¹9½Üéåååäµ54µ‘µ!!µµô¹ÍØˆ4(€€€€€€€ôì4(€€€€€€€¥˜€¡‘¥…±½œ¹M¡½Ý¥…±½œ¡Ñ¡¥Ì¤€„ô¥…±½I•ÍÕ±Ð¹=,¤É•ÑÕÉ¸ì4(€€€€€€€Ù…È±¥¹•Ì€ô¹•Ü1¥ÍÐñÍÑÉ¥¹œøì€‰%@‘‘É•ÍÌ±!½ÍÑ¹…µ”±5‘‘É•ÍÌ±5…¹Õ™…ÑÕÉ•È±MÑ…ÑÕÌ±1…Ñ•¹äˆôì4(€€€€€€€±¥¹•Ì¹‘‘I…¹”¡}É•ÍÕ±ÑÌ¹M•±•Ð¡¥Ñ•´€ôøÍÑÉ¥¹œ¹)½¥¸ œ°œ°4(€€€€€€€€€€€ÍØ¡¥Ñ•´¹%Á‘‘É•ÍÌ¤°ÍØ¡¥Ñ•´¹!½ÍÑ¹…µ”¤°ÍØ¡¥Ñ•´¹5…‘‘É•ÍÌ¤°ÍØ¡¥Ñ•´¹5…¹Õ™…ÑÕÉ•È¤°4(€€€€€€€€€€€ÍØ¡¥Ñ•´¹MÑ…ÑÕÌ¤°ÍØ¡¥Ñ•´¹1…Ñ•¹ä¤¤¤¤ì4(€€€€€€€¥±”¹]É¥Ñ•±±1¥¹•Ì¡‘¥…±½œ¹¥±•9…µ”°±¥¹•Ì°¹•ÜUQá¹½‘¥¹œ¡ÑÉÕ”¤¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œÍØ¡ÍÑÉ¥¹œÙ…±Õ”¤€ôø€‰p‰íÙ…±Õ”¹I•Á±…” ‰pˆˆ°€‰p‰pˆˆ¥õpˆˆì4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬ÁÁ±åMÑ…Ñ¥M•ÑÑ¥¹ÍÍå¹Œ ¤4(€€€ì4(€€€€€€€¥˜€¡}¹¥A¥­•È¹M•±•Ñ•‘%Ñ•´¥Ì¹½ÐM…¹¹•É9¥¡½¥”¹¥Œ¤É•ÑÕÉ¸ì4(€€€€€€€¥˜€ …Y…±¥‘%ÁØÐ¡}¥Á‘‘É•ÍÌ¹Q•áÐ°É•ÅÕ¥É•èÑÉÕ”¤ñð€…Y…±¥‘%ÁØÐ¡}ÍÕ‰¹•Ñ5…Í¬¹Q•áÐ°É•ÅÕ¥É•èÑÉÕ”¤ñð4(€€€€€€€€€€€€…Y…±¥‘%ÁØÐ¡}…Ñ•Ý…ä¹Q•áÐ°É•ÅÕ¥É•è™…±Í”¤ñð€…Y…±¥‘%ÁØÐ¡}ÁÉ¥µ…Éå¹Ì¹Q•áÐ°É•ÅÕ¥É•è™…±Í”¤ñð4(€€€€€€€€€€€€…Y…±¥‘%ÁØÐ¡}Í•½¹‘…Éå¹Ì¹Q•áÐ°É•ÅÕ¥É•è™…±Í”¤¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰I•Ù¥•ÜÑ¡”%@…‘‘É•ÍÌ°ÍÕ‰¹•Ðµ…Í¬°…Ñ•Ý…ä°…¹9LÙ…±Õ•Ì¸ˆ°4(€€€€€€€€€€€€€€€€‰%¹Ù…±¥¹•ÑÝ½É¬Í•ÑÑ¥¹œˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€ô4(€€€€€€€¥˜€ …M…™•%¹Ñ•É™…•9…µ”¡¹¥Œ¹9¥9…µ”¤¤É•ÑÕÉ¸ì4(€€€€€€€Ù…È…Ñ•Ý…ä€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}…Ñ•Ý…ä¹Q•áÐ¤€ü€‰¹½¹”ˆ€è}…Ñ•Ý…ä¹Q•áÐ¹QÉ¥´ ¤ì4(€€€€€€€Ù…È±¥¹•Ì€ô¹•Ü1¥ÍÐñÍÑÉ¥¹œø4(€€€€€€€ì4(€€€€€€€€€€€€‰¹•ÑÍ ¥¹Ñ•É™…”¥ÁØÐÍ•Ð…‘‘É•ÍÌ¹…µ”õp‰í¹¥Œ¹9¥9…µ•õpˆÍ½ÕÉ”õÍÑ…Ñ¥Œ…‘‘É•ÍÌõí}¥Á‘‘É•ÍÌ¹Q•áÐ¹QÉ¥´ ¥ôµ…Í¬õí}ÍÕ‰¹•Ñ5…Í¬¹Q•áÐ¹QÉ¥´ ¥ô…Ñ•Ý…äõí…Ñ•Ý…åôˆ4(€€€€€€€ôì4(€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}ÁÉ¥µ…Éå¹Ì¹Q•áÐ¤¤4(€€€€€€€ì4(€€€€€€€€€€€±¥¹•Ì¹‘ ‰¹•ÑÍ ¥¹Ñ•É™…”¥ÁØÐÍ•Ð‘¹ÍÍ•ÉÙ•ÉÌ¹…µ”õp‰í¹¥Œ¹9¥9…µ•õpˆÍ½ÕÉ”õÍÑ…Ñ¥Œ…‘‘É•ÍÌõí}ÁÉ¥µ…Éå¹Ì¹Q•áÐ¹QÉ¥´ ¥ôÙ…±¥‘…Ñ”õ¹¼ˆ¤ì4(€€€€€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡}Í•½¹‘…Éå¹Ì¹Q•áÐ¤¤4(€€€€€€€€€€€€€€€±¥¹•Ì¹‘ ‰¹•ÑÍ ¥¹Ñ•É™…”¥ÁØÐ…‘‘¹ÍÍ•ÉÙ•ÉÌ¹…µ”õp‰í¹¥Œ¹9¥9…µ•õpˆ…‘‘É•ÍÌõí}Í•½¹‘…Éå¹Ì¹Q•áÐ¹QÉ¥´ ¥ô¥¹‘•àôÈÙ…±¥‘…Ñ”õ¹¼ˆ¤ì4(€€€€€€€ô4(€€€€€€€¥˜€¡…Ý…¥ÐIÕ¹±•Ù…Ñ•‘½µµ…¹‘ÍÍå¹Œ¡±¥¹•Ì¤¤1½…‘9•ÑÝ½É­‘…ÁÑ•ÉÌ ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬M•Ñ¡ÁÍå¹Œ ¤4(€€€ì4(€€€€€€€¥˜€¡}¹¥A¥­•È¹M•±•Ñ•‘%Ñ•´¥Ì¹½ÐM…¹¹•É9¥¡½¥”¹¥Œñð€…M…™•%¹Ñ•É™…•9…µ”¡¹¥Œ¹9¥9…µ”¤¤É•ÑÕÉ¸ì4(€€€€€€€Ù…È±¥¹•Ì€ô¹•Ýmt4(€€€€€€€ì4(€€€€€€€€€€€€‰¹•ÑÍ ¥¹Ñ•É™…”¥ÁØÐÍ•Ð…‘‘É•ÍÌ¹…µ”õp‰í¹¥Œ¹9¥9…µ•õpˆÍ½ÕÉ”õ‘¡Àˆ°4(€€€€€€€€€€€€‰¹•ÑÍ ¥¹Ñ•É™…”¥ÁØÐÍ•Ð‘¹ÍÍ•ÉÙ•ÉÌ¹…µ”õp‰í¹¥Œ¹9¥9…µ•õpˆÍ½ÕÉ”õ‘¡Àˆ4(€€€€€€€ôì4(€€€€€€€¥˜€¡…Ý…¥ÐIÕ¹±•Ù…Ñ•‘½µµ…¹‘ÍÍå¹Œ¡±¥¹•Ì¤¤1½…‘9•ÑÝ½É­‘…ÁÑ•ÉÌ ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”…Íå¹ŒQ…Í¬ñ‰½½°øIÕ¹±•Ù…Ñ•‘½µµ…¹‘ÍÍå¹Œ¡%¹Õµ•É…‰±”ñÍÑÉ¥¹œø½µµ…¹‘Ì¤4(€€€ì4(€€€€€€€Ù…È½µµ…¹‘¥±”€ôA…Ñ ¹½µ‰¥¹”¡A…Ñ ¹•ÑQ•µÁA…Ñ  ¤°€‰¸àµ¥ÀµÍ…¹¹•ÈµíÕ¥¹9•ÝÕ¥ ¤é9ô¹µˆ¤ì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€¥±”¹]É¥Ñ•±±1¥¹•Ì¡½µµ…¹‘¥±”°l‰•¡¼½™˜ˆ°€¸¸½µµ…¹‘Ì°€‰•á¥Ð€½ˆ€••ÉÉ½É±•Ù•°”‰t°¹½‘¥¹œ¹M%$¤ì4(€€€€€€€€€€€Ù…ÈÁÉ½•ÍÌ€ôAÉ½•ÍÌ¹MÑ…ÉÐ¡¹•ÜAÉ½•ÍÍMÑ…ÉÑ%¹™¼¡½µµ…¹‘¥±”¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€UÍ•M¡•±±á•ÕÑ”€ôÑÉÕ”°4(€€€€€€€€€€€€€€€Y•Éˆ€ô€‰ÉÕ¹…Ìˆ°4(€€€€€€€€€€€€€€€]½É­¥¹¥É•Ñ½Éä€ôA…Ñ ¹•ÑQ•µÁA…Ñ  ¤°4(€€€€€€€€€€€€€€€]¥¹‘½ÝMÑå±”€ôAÉ½•ÍÍ]¥¹‘½ÝMÑå±”¹!¥‘‘•¸4(€€€€€€€€€€€ô¤ì4(€€€€€€€€€€€¥˜€¡ÁÉ½•ÍÌ¥Ì¹Õ±°¤É•ÑÕÉ¸™…±Í”ì4(€€€€€€€€€€€…Ý…¥ÐÁÉ½•ÍÌ¹]…¥Ñ½Éá¥ÑÍå¹Œ ¤ì4(€€€€€€€€€€€¥˜€¡ÁÉ½•ÍÌ¹á¥Ñ½‘”€„ô€À¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰]¥¹‘½ÝÌÉ•ÑÕÉ¹••á¥Ð½‘”íÁÉ½•ÍÌ¹á¥Ñ½‘•ôÝ¡¥±”¡…¹¥¹œÑ¡”…‘…ÁÑ•È¸ˆ°4(€€€€€€€€€€€€€€€€€€€€‰9%Í•ÑÑ¥¹Ìˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰Q¡”¹•ÑÝ½É¬…‘…ÁÑ•ÈÍ•ÑÑ¥¹ÌÝ•É”ÕÁ‘…Ñ•¸ˆ°€‰9%Í•ÑÑ¥¹Ìˆ°4(€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹%¹™½Éµ…Ñ¥½¸¤ì4(€€€€€€€€€€€É•ÑÕÉ¸ÑÉÕ”ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡]¥¸ÌÉá•ÁÑ¥½¸•á•ÁÑ¥½¸¤Ý¡•¸€¡•á•ÁÑ¥½¸¹9…Ñ¥Ù•ÉÉ½É½‘”€ôô€ÄÈÈÌ¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€ô4(€€€€€€€…Ñ €¡á•ÁÑ¥½¸•á•ÁÑ¥½¸¤4(€€€€€€€ì4(€€€€€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°•á•ÁÑ¥½¸¹5•ÍÍ…”°€‰9%Í•ÑÑ¥¹Ìˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€ô4(€€€€€€€™¥¹…±±ä4(€€€€€€€ì4(€€€€€€€€€€€ÑÉäì¥˜€¡¥±”¹á¥ÍÑÌ¡½µµ…¹‘¥±”¤¤¥±”¹•±•Ñ”¡½µµ…¹‘¥±”¤ìô…Ñ ìô4(€€€€€€€ô4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”‰½½°M…™•%¹Ñ•É™…•9…µ”¡ÍÑÉ¥¹œ¹…µ”¤4(€€€ì4(€€€€€€€¥˜€¡¹…µ”¹%¹‘•á=™¹ä¡lqÈœ°€q¸œ°€œˆœ°€œ˜œ°€ðœ°€œðœ°€œøt¤€ð€À¤É•ÑÕÉ¸ÑÉÕ”ì4(€€€€€€€5•ÍÍ…•	½à¹M¡½Ü¡Ñ¡¥Ì°€‰Q¡¥Ì¹•ÑÝ½É¬…‘…ÁÑ•È¹…µ”½¹Ñ…¥¹Ì¡…É…Ñ•ÉÌÑ¡…Ð…¹¹½Ð‰”Á…ÍÍ•Í…™•±äÑ¼]¥¹‘½ÝÌ¸ˆ°4(€€€€€€€€€€€€‰9%Í•ÑÑ¥¹Ìˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ‰½½°Y…±¥‘%ÁØÐ¡ÍÑÉ¥¹œÙ…±Õ”°‰½½°É•ÅÕ¥É•¤€ôø4(€€€€€€€ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Ù…±Õ”¤€ü€…É•ÅÕ¥É•€è%ÁØÑ‘‘É•ÍÍQ•áÐ¹QÉåA…ÉÍ”¡Ù…±Õ”°½ÕÐ|°½ÕÐ|¤ì4)ô4(4)¥¹Ñ•É¹…°Í•…±•É•½ÉM…¹¹•ÉI•ÍÕ±Ð 4(€€€ÍÑÉ¥¹œ%Á‘‘É•ÍÌ°4(€€€ÍÑÉ¥¹œ!½ÍÑ¹…µ”°4(€€€ÍÑÉ¥¹œ5…‘‘É•ÍÌ°4(€€€ÍÑÉ¥¹œ5…¹Õ™…ÑÕÉ•È°4(€€€ÍÑÉ¥¹œMÑ…ÑÕÌ°4(€€€ÍÑÉ¥¹œ1…Ñ•¹ä°4(€€€‰½½°!ÑÑÁ=Á•¸°4(€€€‰½½°!ÑÑÁÍ=Á•¸¤ì4(4)¥¹Ñ•É¹…°Í•…±•É•½ÉM…¹¹•É9¥¡½¥” 4(€€€ÍÑÉ¥¹œ9¥%°4(€€€ÍÑÉ¥¹œ9¥9…µ”°4(€€€ÍÑÉ¥¹œ•ÍÉ¥ÁÑ¥½¸°4(€€€ÍÑÉ¥¹œ%ÁØÑ‘‘É•ÍÌ°4(€€€¥¹ÐAÉ•™¥á1•¹Ñ °4(€€€ÍÑÉ¥¹œ…Ñ•Ý…ä°4(€€€%I•…‘=¹±å1¥ÍÐñÍÑÉ¥¹œø¹Í‘‘É•ÍÍ•Ì°4(€€€‰½½°¡Á¹…‰±•°4(€€€=Á•É…Ñ¥½¹…±MÑ…ÑÕÌ=Á•É…Ñ¥½¹…±MÑ…ÑÕÌ¤4)ì4(€€€ÁÕ‰±¥Œ½Ù•ÉÉ¥‘”ÍÑÉ¥¹œQ½MÑÉ¥¹œ ¤€ôø€‰í9¥9…µ•ô€ƒŠˆ€í%ÁØÑ‘‘É•ÍÍôˆì4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥Œ1¥ÍÐñM…¹¹•É9¥¡½¥”ø¥¹‘±° ¤4(€€€ì4(€€€€€€€Ù…ÈÉ•ÍÕ±ÑÌ€ô¹•Ü1¥ÍÐñM…¹¹•É9¥¡½¥”ø ¤ì4(€€€€€€€™½É•… €¡Ù…È¹¥Œ¥¸9•ÑÝ½É­%¹Ñ•É™…”¹•Ñ±±9•ÑÝ½É­%¹Ñ•É™…•Ì ¤4(€€€€€€€€€€€€€€€€€€€€€¹]¡•É”¡¥Ñ•´€ôø¥Ñ•´¹9•ÑÝ½É­%¹Ñ•É™…•QåÁ”€„ô4(€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€MåÍÑ•´¹9•Ð¹9•ÑÝ½É­%¹™½Éµ…Ñ¥½¸¹9•ÑÝ½É­%¹Ñ•É™…•QåÁ”¹1½½Á‰…¬¤4(€€€€€€€€€€€€€€€€€€€€€¹]¡•É”¡¥Ñ•´€ôø¥Ñ•´¹=Á•É…Ñ¥½¹…±MÑ…ÑÕÌ€ôô=Á•É…Ñ¥½¹…±MÑ…ÑÕÌ¹UÀ¤¤4(€€€€€€€ì4(€€€€€€€€€€€ÑÉä4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…ÈÁÉ½Á•ÉÑ¥•Ì€ô¹¥Œ¹•Ñ%AAÉ½Á•ÉÑ¥•Ì ¤ì4(€€€€€€€€€€€€€€€Ù…È…Ñ•Ý…ä€ôÁÉ½Á•ÉÑ¥•Ì¹…Ñ•Ý…å‘‘É•ÍÍ•Ì4(€€€€€€€€€€€€€€€€€€€€¹M•±•Ð¡¥Ñ•´€ôø¥Ñ•´¹‘‘É•ÍÌ¤4(€€€€€€€€€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±Ð¡¥Ñ•´€ôø¥Ñ•´¹‘‘É•ÍÍ…µ¥±ä€ôô‘‘É•ÍÍ…µ¥±ä¹%¹Ñ•É9•ÑÝ½É¬¤ü¹Q½MÑÉ¥¹œ ¤€üüÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€€€€€€€€€Ù…È‘¹Ì€ôÁÉ½Á•ÉÑ¥•Ì¹¹Í‘‘É•ÍÍ•Ì4(€€€€€€€€€€€€€€€€€€€€¹]¡•É”¡¥Ñ•´€ôø¥Ñ•´¹‘‘É•ÍÍ…µ¥±ä€ôô‘‘É•ÍÍ…µ¥±ä¹%¹Ñ•É9•ÑÝ½É¬¤4(€€€€€€€€€€€€€€€€€€€€¹M•±•Ð¡¥Ñ•´€ôø¥Ñ•´¹Q½MÑÉ¥¹œ ¤¤4(€€€€€€€€€€€€€€€€€€€€¹Q½1¥ÍÐ ¤ì4(€€€€€€€€€€€€€€€Ù…È‘¡À€ôÁÉ½Á•ÉÑ¥•Ì¹•Ñ%AØÑAÉ½Á•ÉÑ¥•Ì ¤ü¹%Í¡Á¹…‰±•€üü™…±Í”ì4(€€€€€€€€€€€€€€€™½É•… €¡Ù…È…‘‘É•ÍÌ¥¸ÁÉ½Á•ÉÑ¥•Ì¹U¹¥…ÍÑ‘‘É•ÍÍ•Ì4(€€€€€€€€€€€€€€€€€€€€€€€€€€€€€¹]¡•É”¡¥Ñ•´€ôø¥Ñ•´¹‘‘É•ÍÌ¹‘‘É•ÍÍ…µ¥±ä€ôô‘‘É•ÍÍ…µ¥±ä¹%¹Ñ•É9•ÑÝ½É¬¤4(€€€€€€€€€€€€€€€€€€€€€€€€€€€€€¹]¡•É”¡¥Ñ•´€ôø€…%A‘‘É•ÍÌ¹%Í1½½Á‰…¬¡¥Ñ•´¹‘‘É•ÍÌ¤¤¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€É•ÍÕ±ÑÌ¹‘¡¹•ÜM…¹¹•É9¥¡½¥”¡¹¥Œ¹%°¹¥Œ¹9…µ”°¹¥Œ¹•ÍÉ¥ÁÑ¥½¸°…‘‘É•ÍÌ¹‘‘É•ÍÌ¹Q½MÑÉ¥¹œ ¤°4(€€€€€€€€€€€€€€€€€€€€€€€…‘‘É•ÍÌ¹AÉ•™¥á1•¹Ñ °…Ñ•Ý…ä°‘¹Ì°‘¡À°¹¥Œ¹=Á•É…Ñ¥½¹…±MÑ…ÑÕÌ¤¤ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€ô4(€€€€€€€€€€€…Ñ €¡9•ÑÝ½É­%¹™½Éµ…Ñ¥½¹á•ÁÑ¥½¸¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€¼¼M­¥À…‘…ÁÑ•ÉÌÝ¡½Í”ÁÉ½Á•ÉÑ¥•Ì…¹¹½Ð‰”É•…¸4(€€€€€€€€€€€ô4(€€€€€€€ô4(€€€€€€€É•ÑÕÉ¸É•ÍÕ±ÑÌ¹=É‘•É	ä¡¥Ñ•´€ôø¥Ñ•´¹9¥9…µ”°MÑÉ¥¹½µÁ…É•È¹ÕÉÉ•¹ÑÕ±ÑÕÉ•%¹½É•…Í”¤¹Q½1¥ÍÐ ¤ì4(€€€ô4)ô4(4)¥¹Ñ•É¹…°Í•…±•É•½É¥‘ÉI…¹”¡Õ¥¹Ð9•ÑÝ½É¬°¥¹ÐAÉ•™¥á1•¹Ñ °¥¹Ð!½ÍÑ½Õ¹Ð¤4)ì4(€€€ÁÕ‰±¥Œ%¹Õµ•É…‰±”ñ%A‘‘É•ÍÌø‘‘É•ÍÍ•Ì ¤4(€€€ì4(€€€€€€€Ù…ÈÍÑ…ÉÐ€ôAÉ•™¥á1•¹Ñ €ðô€ÌÀ€ü€ÅÔ€è€ÁÔì4(€€€€€€€™½È€¡Õ¥¹Ð½™™Í•Ð€ô€Àì½™™Í•Ð€ð!½ÍÑ½Õ¹Ðì½™™Í•Ð¬¬¤4(€€€€€€€€€€€å¥•±É•ÑÕÉ¸É½µU%¹ÐÌÈ¡9•ÑÝ½É¬€¬ÍÑ…ÉÐ€¬½™™Í•Ð¤ì4(€€€ô4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥Œ‰½½°QÉåA…ÉÍ”¡ÍÑÉ¥¹œÙ…±Õ”°½ÕÐ¥‘ÉI…¹”É…¹”°½ÕÐÍÑÉ¥¹œ•ÉÉ½È¤4(€€€ì4(€€€€€€€É…¹”€ô¹•Ü¥‘ÉI…¹” À°€ÌÈ°€À¤ì4(€€€€€€€•ÉÉ½È€ô€‰¹Ñ•È„%HÉ…¹”ÍÕ …Ì€ÄäÈ¸ÄØà¸Ä¸À¼ÈÐ¸ˆì4(€€€€€€€Ù…ÈÁ…ÉÑÌ€ôÙ…±Õ”¹QÉ¥´ ¤¹MÁ±¥Ð œ¼œ¤ì4(€€€€€€€¥˜€¡Á…ÉÑÌ¹1•¹Ñ €„ô€Èñð€…%ÁØÑ‘‘É•ÍÍQ•áÐ¹QÉåA…ÉÍ”¡Á…ÉÑÍlÁt°½ÕÐÙ…È…‘‘É•ÍÌ°½ÕÐ|¤ñð4(€€€€€€€€€€€€…¥¹Ð¹QÉåA…ÉÍ”¡Á…ÉÑÍlÅt°½ÕÐÙ…ÈÁÉ•™¥à¤ñðÁÉ•™¥à¥Ì€ð€À½È€ø€ÌÈ¤4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€Ù…È…‘‘É•ÍÍY…±Õ”€ôQ½U%¹ÐÌÈ¡…‘‘É•ÍÌ¤ì4(€€€€€€€Ù…Èµ…Í¬€ôÁÉ•™¥à€ôô€À€ü€ÁÔ€èÕ¥¹Ð¹5…áY…±Õ”€ðð€ ÌÈ€´ÁÉ•™¥à¤ì4(€€€€€€€Ù…ÈÑ½Ñ…±‘‘É•ÍÍ½Õ¹Ð€ô€Å0€ðð€ ÌÈ€´ÁÉ•™¥à¤ì4(€€€€€€€Ù…ÈÕÍ…‰±•!½ÍÑ½Õ¹Ð€ôÁÉ•™¥à€ðô€ÌÀ€üÑ½Ñ…±‘‘É•ÍÍ½Õ¹Ð€´€È€èÑ½Ñ…±‘‘É•ÍÍ½Õ¹Ðì4(€€€€€€€¥˜€¡ÕÍ…‰±•!½ÍÑ½Õ¹Ð€ø¥¹Ð¹5…áY…±Õ”¤4(€€€€€€€ì4(€€€€€€€€€€€•ÉÉ½È€ô€‰Q¡”%HÉ…¹”¥ÌÑ½¼±…É”¸ˆì4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€ô4(€€€€€€€É…¹”€ô¹•Ü¥‘ÉI…¹”¡…‘‘É•ÍÍY…±Õ”€˜µ…Í¬°ÁÉ•™¥à°€¡¥¹Ð¥ÕÍ…‰±•!½ÍÑ½Õ¹Ð¤ì4(€€€€€€€•ÉÉ½È€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€É•ÑÕÉ¸ÑÉÕ”ì4(€€€ô4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÍÑÉ¥¹œ9•ÑÝ½É­¥‘È¡ÍÑÉ¥¹œ…‘‘É•ÍÌ°¥¹ÐÁÉ•™¥à¤4(€€€ì4(€€€€€€€¥˜€ …%A‘‘É•ÍÌ¹QÉåA…ÉÍ”¡…‘‘É•ÍÌ°½ÕÐÙ…ÈÁ…ÉÍ•¤¤É•ÑÕÉ¸€‰í…‘‘É•ÍÍô½íÁÉ•™¥áôˆì4(€€€€€€€Ù…Èµ…Í¬€ôÁÉ•™¥à€ôô€À€ü€ÁÔ€èÕ¥¹Ð¹5…áY…±Õ”€ðð€ ÌÈ€´ÁÉ•™¥à¤ì4(€€€€€€€É•ÑÕÉ¸€‰íÉ½µU%¹ÐÌÈ¡Q½U%¹ÐÌÈ¡Á…ÉÍ•¤€˜µ…Í¬¥ô½íÁÉ•™¥áôˆì4(€€€ô4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÍÑÉ¥¹œAÉ•™¥áQ½5…Í¬¡¥¹ÐÁÉ•™¥à¤4(€€€ì4(€€€€€€€Ù…Èµ…Í¬€ôÁÉ•™¥à€ôô€À€ü€ÁÔ€èÕ¥¹Ð¹5…áY…±Õ”€ðð€ ÌÈ€´ÁÉ•™¥à¤ì4(€€€€€€€É•ÑÕÉ¸É½µU%¹ÐÌÈ¡µ…Í¬¤¹Q½MÑÉ¥¹œ ¤ì4(€€€ô4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÕ¥¹ÐQ½U%¹ÐÌÈ¡%A‘‘É•ÍÌ…‘‘É•ÍÌ¤4(€€€ì4(€€€€€€€Ù…È‰åÑ•Ì€ô…‘‘É•ÍÌ¹•Ñ‘‘É•ÍÍ	åÑ•Ì ¤ì4(€€€€€€€É•ÑÕÉ¸€ ¡Õ¥¹Ð¥‰åÑ•ÍlÁt€ðð€ÈÐ¤ð€ ¡Õ¥¹Ð¥‰åÑ•ÍlÅt€ðð€ÄØ¤ð€ ¡Õ¥¹Ð¥‰åÑ•ÍlÉt€ðð€à¤ð‰åÑ•ÍlÍtì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ%A‘‘É•ÍÌÉ½µU%¹ÐÌÈ¡Õ¥¹ÐÙ…±Õ”¤€ôø¹•Ü¡¹•Ýmt4(€€€ì4(€€€€€€€€¡‰åÑ”¤¡Ù…±Õ”€øø€ÈÐ¤°€¡‰åÑ”¤¡Ù…±Õ”€øø€ÄØ¤°€¡‰åÑ”¤¡Ù…±Õ”€øø€à¤°€¡‰åÑ”¥Ù…±Õ”4(€€€ô¤ì4)ô4(4)¥¹Ñ•É¹…°ÍÑ…Ñ¥ŒÁ…ÉÑ¥…°±…ÍÌÉÁQ…‰±”4)ì4(€€€m•¹•É…Ñ•‘I••à¡ ‰yqÌ¨ üñ¥Àùq‘ìÄ°Íô üép¹q‘ìÄ°Íô¥ìÍô¥qÌ¬ üñµ…ŒùlÀ´å„µ™µµuìÄÝô¥qÌ¬ˆ°I••á=ÁÑ¥½¹Ì¹5Õ±Ñ¥±¥¹”¥t4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÁ…ÉÑ¥…°I••àÉÁ1¥¹•I••à ¤ì4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥Œ¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œøI•… ¤4(€€€ì4(€€€€€€€Ù…ÈÉ•ÍÕ±ÑÌ€ô¹•Ü¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œø¡MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤ì4(€€€€€€€¥˜€ …=Á•É…Ñ¥¹MåÍÑ•´¹%Í]¥¹‘½ÝÌ ¤¤É•ÑÕÉ¸É•ÍÕ±ÑÌì4(€€€€€€€ÑÉä4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÁÉ½•ÍÌ€ôAÉ½•ÍÌ¹MÑ…ÉÐ¡¹•ÜAÉ½•ÍÍMÑ…ÉÑ%¹™¼ ‰…ÉÀ¹•á”ˆ°€ˆµ„ˆ¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€UÍ•M¡•±±á•ÕÑ”€ô™…±Í”°4(€€€€€€€€€€€€€€€I•‘¥É•ÑMÑ…¹‘…É‘=ÕÑÁÕÐ€ôÑÉÕ”°4(€€€€€€€€€€€€€€€É•…Ñ•9½]¥¹‘½Ü€ôÑÉÕ”4(€€€€€€€€€€€ô¤ì4(€€€€€€€€€€€¥˜€¡ÁÉ½•ÍÌ¥Ì¹Õ±°¤É•ÑÕÉ¸É•ÍÕ±ÑÌì4(€€€€€€€€€€€Ù…È½ÕÑÁÕÐ€ôÁÉ½•ÍÌ¹MÑ…¹‘…É‘=ÕÑÁÕÐ¹I•…‘Q½¹ ¤ì4(€€€€€€€€€€€ÁÉ½•ÍÌ¹]…¥Ñ½Éá¥Ð ÈÀÀÀ¤ì4(€€€€€€€€€€€™½É•… €¡5…Ñ µ…Ñ ¥¸ÉÁ1¥¹•I••à ¤¹5…Ñ¡•Ì¡½ÕÑÁÕÐ¤¤4(€€€€€€€€€€€€€€€É•ÍÕ±ÑÍmµ…Ñ ¹É½ÕÁÍl‰¥À‰t¹Y…±Õ•t€ôµ…Ñ ¹É½ÕÁÍl‰µ…Œ‰t¹Y…±Õ”¹I•Á±…” œ´œ°€œèœ¤¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤ì4(€€€€€€€ô4(€€€€€€€…Ñ 4(€€€€€€€ì4(€€€€€€€€€€€€¼¼5‘¥Í½Ù•Éä¥ÌÍÕÁÁ±•µ•¹Ñ…°¸4(€€€€€€€ô4(€€€€€€€É•ÑÕÉ¸É•ÍÕ±ÑÌì4(€€€ô4)ô4(4)¥¹Ñ•É¹…°ÍÑ…Ñ¥Œ±…ÍÌ=Õ¥1½½­ÕÀ4)ì4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÉ•…‘½¹±ä¥Ñ¥½¹…ÉäñÍÑÉ¥¹œ°ÍÑÉ¥¹œø-¹½Ý¸€ô¹•Ü¡MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…±%¹½É•…Í”¤4(€€€ì4(€€€€€€€lˆÜàèÐÔèÀÄ‰t€ô€‰	¥…µÀˆ°4(€€€€€€€lˆÀÀèÐÔéÔ‰t€ô€‰	…É¼ˆ4(€€€ôì4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÍÑÉ¥¹œ¥¹¡ÍÑÉ¥¹œµ…Œ¤4(€€€ì4(€€€€€€€¥˜€¡µ…Œ¹1•¹Ñ €ð€à¤É•ÑÕÉ¸ÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€É•ÑÕÉ¸-¹½Ý¸¹•ÑY…±Õ•=É•™…Õ±Ð¡µ…l¸¸át°€‰U¹­¹½Ý¸ˆ¤ì4(€€€ô4)ô4(