using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AVMatrixStudio;

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
        _progressLabel.Text = "Canceling scan… Close the window again when cancellation completes.";
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
        _nicState.Text = $"{choice.OperationalStatus}  •  {(choice.DhcpEnabled ? "DHCP" : "Static")}";
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
            return;
        }
        if (!CidrRange.TryParse(_cidr.Text, out var range, out var error))
        {
            MessageBox.Show(this, error, "Invalid CIDR range", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (range.HostCount > 65_536)
        {
            MessageBox.Show(this, "This build scans up to 65,536 addresses at once. Choose a /16 or smaller range.",
                "Range too large", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ClearResults();
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        _scanButton.Enabled = false;
        _cancelButton.Enabled = true;
        _nicPicker.Enabled = false;
        _progress.Minimum = 0;
        _progress.Maximum = Math.Max(1, range.HostCount);
        _progress.Value = 0;
        _progressLabel.Text = $"Scanning {range.HostCount:N0} addresses from {nic.Ipv4Address}…";
        var found = new ConcurrentBag<ScannerResult>();
        var completed = 0;
        var stride = Math.Max(1, range.HostCount / 250);
        using var limit = new SemaphoreSlim(128);

        try
        {
            var tasks = range.Addresses().Select(async address =>
            {
                await limit.WaitAsync(token);
                try
                {
                    var result = await ScanAddressAsync(address, IPAddress.Parse(nic.Ipv4Address), token);
                    if (result is not null) found.Add(result);
                }
                finally
                {
                    limit.Release();
                    var done = Interlocked.Increment(ref completed);
                    if (done % stride == 0 || done == range.HostCount)
                        BeginInvoke(new Action(() =>
                        {
                            _progress.Value = Math.Min(_progress.Maximum, done);
                            _progressLabel.Text = $"Scanned {done:N0} of {range.HostCount:N0}  •  Found {found.Count:N0}";
                        }));
                }
            }).ToList();
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            _progressLabel.Text = $"Scan canceled after {completed:N0} addresses";
        }
        finally
        {
            var arp = ArpTable.Read();
            _results.Clear();
            foreach (var result in found.OrderBy(item => CidrRange.ToUInt32(IPAddress.Parse(item.IpAddress))))
            {
                var mac = arp.GetValueOrDefault(result.IpAddress, string.Empty);
                _results.Add(result with
                {
                    MacAddress = mac,
                    Manufacturer = OuiLookup.Find(mac)
                });
            }
            PopulateGrid();
            if (!token.IsCancellationRequested)
            {
                _progress.Value = _progress.Maximum;
                _progressLabel.Text = $"Scan complete  •  {_results.Count:N0} device(s) found";
            }
            _scanCancellation.Dispose();
            _scanCancellation = null;
            _scanButton.Enabled = true;
            _cancelButton.Enabled = false;
            _nicPicker.Enabled = true;
        }
    }

    private static async Task<ScannerResult?> ScanAddressAsync(
        IPAddress address,
        IPAddress source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pingTask = Task.Run(() => SourceBoundIcmp.Ping(source, address, 700), cancellationToken);
        var httpTask = CanConnectAsync(source, address, 80, 650, cancellationToken);
        var httpsTask = CanConnectAsync(source, address, 443, 650, cancellationToken);
        await Task.WhenAll(pingTask, httpTask, httpsTask);
        var ping = await pingTask;
        var http = await httpTask;
        var https = await httpsTask;
        if (!ping.Success && !http && !https) return null;

        var hostname = string.Empty;
        try
        {
            var lookup = Dns.GetHostEntryAsync(address);
            hostname = (await lookup.WaitAsync(TimeSpan.FromMilliseconds(700), cancellationToken)).HostName;
        }
        catch
        {
            // Reverse DNS is optional.
        }
        var status = (http, https) switch
        {
            (true, true) => "HTTP / HTTPS",
            (true, false) => "HTTP",
            (false, true) => "HTTPS",
            _ => "Online"
        };
        return new ScannerResult(
            address.ToString(), hostname, string.Empty, string.Empty, status,
            ping.Success ? $"{ping.RoundtripMilliseconds} ms" : string.Empty,
            http, https);
    }

    private static async Task<bool> CanConnectAsync(
        IPAddress source,
        IPAddress destination,
        int port,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMilliseconds);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(source, 0));
            await socket.ConnectAsync(new IPEndPoint(destination, port), timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var result in _results)
        {
            var rowIndex = _grid.Rows.Add(result.IpAddress, result.Hostname, result.MacAddress,
                result.Manufacturer, result.Status, result.Latency);
            _grid.Rows[rowIndex].Tag = result;
            _grid.Rows[rowIndex].Cells[4].Style.ForeColor = result.Status == "Online" ? UiTheme.Green : UiTheme.Blue;
            _grid.Rows[rowIndex].Cells[4].Style.Font = UiTheme.Font(9, FontStyle.Bold);
        }
    }

    private void ClearResults()
    {
        if (_scanCancellation is not null) return;
        _results.Clear();
        _grid.Rows.Clear();
        _progress.Value = 0;
        _progressLabel.Text = "Ready to scan";
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not ScannerResult result) return;
        var scheme = result.HttpsOpen ? "https" : result.HttpOpen ? "http" : null;
        if (scheme is null)
        {
            MessageBox.Show(this, "This device did not expose HTTP or HTTPS during the scan.",
                "Open device", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo($"{scheme}://{result.IpAddress}") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Open device", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCsv()
    {
        if (_results.Count == 0)
        {
            MessageBox.Show(this, "Run a scan before exporting results.", "Export CSV",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"N8-IP-Scan-{DateTime.Now:yyyy-MM-dd-HHmm}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var lines = new List<string> { "IP Address,Hostname,MAC Address,Manufacturer,Status,Latency" };
        lines.AddRange(_results.Select(item => string.Join(',',
            Csv(item.IpAddress), Csv(item.Hostname), Csv(item.MacAddress), Csv(item.Manufacturer),
            Csv(item.Status), Csv(item.Latency))));
        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private async Task ApplyStaticSettingsAsync()
    {
        if (_nicPicker.SelectedItem is not ScannerNicChoice nic) return;
        if (!ValidIpv4(_ipAddress.Text, required: true) || !ValidIpv4(_subnetMask.Text, required: true) ||
            !ValidIpv4(_gateway.Text, required: false) || !ValidIpv4(_primaryDns.Text, required: false) ||
            !ValidIpv4(_secondaryDns.Text, required: false))
        {
            MessageBox.Show(this, "Review the IP address, subnet mask, gateway, and DNS values.",
                "Invalid network setting", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!SafeInterfaceName(nic.NicName)) return;
        var gateway = string.IsNullOrWhiteSpace(_gateway.Text) ? "none" : _gateway.Text.Trim();
        var lines = new List<string>
        {
            $"netsh interface ipv4 set address name=\"{nic.NicName}\" source=static address={_ipAddress.Text.Trim()} mask={_subnetMask.Text.Trim()} gateway={gateway}"
        };
        if (!string.IsNullOrWhiteSpace(_primaryDns.Text))
        {
            lines.Add($"netsh interface ipv4 set dnsservers name=\"{nic.NicName}\" source=static address={_primaryDns.Text.Trim()} validate=no");
            if (!string.IsNullOrWhiteSpace(_secondaryDns.Text))
                lines.Add($"netsh interface ipv4 add dnsservers name=\"{nic.NicName}\" address={_secondaryDns.Text.Trim()} index=2 validate=no");
        }
        if (await RunElevatedCommandsAsync(lines)) LoadNetworkAdapters();
    }

    private async Task SetDhcpAsync()
    {
        if (_nicPicker.SelectedItem is not ScannerNicChoice nic || !SafeInterfaceName(nic.NicName)) return;
        var lines = new[]
        {
            $"netsh interface ipv4 set address name=\"{nic.NicName}\" source=dhcp",
            $"netsh interface ipv4 set dnsservers name=\"{nic.NicName}\" source=dhcp"
        };
        if (await RunElevatedCommandsAsync(lines)) LoadNetworkAdapters();
    }

    private async Task<bool> RunElevatedCommandsAsync(IEnumerable<string> commands)
    {
        var commandFile = Path.Combine(Path.GetTempPath(), $"n8-ip-scanner-{Guid.NewGuid():N}.cmd");
        try
        {
            File.WriteAllLines(commandFile, ["@echo off", .. commands, "exit /b %errorlevel%"], Encoding.ASCII);
            var process = Process.Start(new ProcessStartInfo(commandFile)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetTempPath(),
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return false;
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                MessageBox.Show(this, $"Windows returned exit code {process.ExitCode} while changing the adapter.",
                    "NIC settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            MessageBox.Show(this, "The network adapter settings were updated.", "NIC settings",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return false;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "NIC settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            try { if (File.Exists(commandFile)) File.Delete(commandFile); } catch { }
        }
    }

    private bool SafeInterfaceName(string name)
    {
        if (name.IndexOfAny(['\r', '\n', '"', '&', '|', '<', '>']) < 0) return true;
        MessageBox.Show(this, "This network adapter name contains characters that cannot be passed safely to Windows.",
            "NIC settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    private static bool ValidIpv4(string value, bool required) =>
        string.IsNullOrWhiteSpace(value) ? !required : Ipv4AddressText.TryParse(value, out _, out _);
}

internal sealed record ScannerResult(
    string IpAddress,
    string Hostname,
    string MacAddress,
    string Manufacturer,
    string Status,
    string Latency,
    bool HttpOpen,
    bool HttpsOpen);

internal sealed record ScannerNicChoice(
    string NicId,
    string NicName,
    string Description,
    string Ipv4Address,
    int PrefixLength,
    string Gateway,
    IReadOnlyList<string> DnsAddresses,
    bool DhcpEnabled,
    OperationalStatus OperationalStatus)
{
    public override string ToString() => $"{NicName}  •  {Ipv4Address}";

    public static List<ScannerNicChoice> FindAll()
    {
        var results = new List<ScannerNicChoice>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.NetworkInterfaceType !=
                                    System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                     .Where(item => item.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var properties = nic.GetIPProperties();
                var gateway = properties.GatewayAddresses
                    .Select(item => item.Address)
                    .FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? string.Empty;
                var dns = properties.DnsAddresses
                    .Where(item => item.AddressFamily == AddressFamily.InterNetwork)
                    .Select(item => item.ToString())
                    .ToList();
                var dhcp = properties.GetIPv4Properties()?.IsDhcpEnabled ?? false;
                foreach (var address in properties.UnicastAddresses
                             .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                             .Where(item => !IPAddress.IsLoopback(item.Address)))
                {
                    results.Add(new ScannerNicChoice(nic.Id, nic.Name, nic.Description, address.Address.ToString(),
                        address.PrefixLength, gateway, dns, dhcp, nic.OperationalStatus));
                }
            }
            catch (NetworkInformationException)
            {
                // Skip adapters whose properties cannot be read.
            }
        }
        return results.OrderBy(item => item.NicName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

internal sealed record CidrRange(uint Network, int PrefixLength, int HostCount)
{
    public IEnumerable<IPAddress> Addresses()
    {
        var start = PrefixLength <= 30 ? 1u : 0u;
        for (uint offset = 0; offset < HostCount; offset++)
            yield return FromUInt32(Network + start + offset);
    }

    public static bool TryParse(string value, out CidrRange range, out string error)
    {
        range = new CidrRange(0, 32, 0);
        error = "Enter a CIDR range such as 192.168.1.0/24.";
        var parts = value.Trim().Split('/');
        if (parts.Length != 2 || !Ipv4AddressText.TryParse(parts[0], out var address, out _) ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            return false;
        var addressValue = ToUInt32(address);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var totalAddressCount = 1L << (32 - prefix);
        var usableHostCount = prefix <= 30 ? totalAddressCount - 2 : totalAddressCount;
        if (usableHostCount > int.MaxValue)
        {
            error = "The CIDR range is too large.";
            return false;
        }
        range = new CidrRange(addressValue & mask, prefix, (int)usableHostCount);
        error = string.Empty;
        return true;
    }

    public static string NetworkCidr(string address, int prefix)
    {
        if (!IPAddress.TryParse(address, out var parsed)) return $"{address}/{prefix}";
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return $"{FromUInt32(ToUInt32(parsed) & mask)}/{prefix}";
    }

    public static string PrefixToMask(int prefix)
    {
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return FromUInt32(mask).ToString();
    }

    public static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new(new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    });
}

internal static partial class ArpTable
{
    [GeneratedRegex(@"^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>[0-9a-fA-F-]{17})\s+", RegexOptions.Multiline)]
    private static partial Regex ArpLineRegex();

    public static Dictionary<string, string> Read()
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return results;
        try
        {
            var process = Process.Start(new ProcessStartInfo("arp.exe", "-a")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (process is null) return results;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            foreach (Match match in ArpLineRegex().Matches(output))
                results[match.Groups["ip"].Value] = match.Groups["mac"].Value.Replace('-', ':').ToUpperInvariant();
        }
        catch
        {
            // MAC discovery is supplemental.
        }
        return results;
    }
}

internal static class OuiLookup
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["78:45:01"] = "Biamp",
        ["00:45:A5"] = "Barco"
    };

    public static string Find(string mac)
    {
        if (mac.Length < 8) return string.Empty;
        return Known.GetValueOrDefault(mac[..8], "Unknown");
    }
}
