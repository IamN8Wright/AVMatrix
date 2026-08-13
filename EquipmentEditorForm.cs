using System.Security.Cryptography;

namespace AVMatrixStudio;

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

        var add = UiTheme.SecondaryButton("＋  Add IP / MAC");
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
        var upload = UiTheme.PrimaryButton("＋ Add files…");
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
        _configurationGrid.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 ||
                _configurationGrid.Columns[eventArgs.ColumnIndex].Name == "Download")
                return;
            _configurationGrid.ClearSelection();
            _configurationGrid.Rows[eventArgs.RowIndex].Selected = true;
            SaveSelectedConfigurationFile();
        };
    }

    private void AddConfigurationFiles()
    {
        if (!_configurationFilesEditable) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Attach device configuration files",
            Filter = "All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 250L * 1024 * 1024)
                    throw new InvalidDataException(
                        $"'{info.Name}' is larger than the 250 MB per-file safety limit.");
                if (info.Length > 50L * 1024 * 1024 &&
                    MessageBox.Show(this,
                        $"{info.Name} is {FormatSize(info.Length)} and will make the AV Matrix considerably larger.\r\n\r\nAttach it anyway?",
                        "Large configuration file",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    continue;

                var existing = _configurationFiles.FirstOrDefault(file =>
                    string.Equals(file.FileName, info.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null && MessageBox.Show(this,
                        $"A file named '{info.Name}' is already attached. Replace it?",
                        "Replace configuration file",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    continue;
                var contents = File.ReadAllBytes(path);
                var attachment = existing ?? new DeviceConfigurationFile();
                attachment.FileName = info.Name;
                attachment.ContentType = ContentTypeFor(info.Extension);
                attachment.SizeBytes = contents.LongLength;
                attachment.Sha256 = Convert.ToHexString(SHA256.HashData(contents));
                attachment.ContentBase64 = Convert.ToBase64String(contents);
                attachment.ContentIncluded = true;
                attachment.AddedBy = $"{Environment.UserName} on {Environment.MachineName}";
                attachment.AddedUtc = DateTime.UtcNow;
                if (existing is null) _configurationFiles.Add(attachment);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this,
                    $"The file could not be attached.\r\n\r\n{exception.Message}",
                    "Configuration file",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        RefreshConfigurationFiles();
    }

    private void SaveSelectedConfigurationFile()
    {
        if (_configurationGrid.SelectedRows.Count != 1) return;
        var file = (DeviceConfigurationFile)_configurationGrid.SelectedRows[0].Tag!;
        if (!_configurationFilesEditable || MasterSessionContext.Current?.Session.CanWrite == false)
        {
            MessageBox.Show(this,
                "Configuration files can be downloaded only by an Owner or Tech after this client is checked out.",
                "Download configuration file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        if (!file.ContentIncluded)
        {
            MessageBox.Show(this,
                "Check out this client to download the configuration file.",
                "Download configuration file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Download device configuration file",
            FileName = file.FileName,
            Filter = "All files (*.*)|*.*",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, file.GetContents());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The file could not be saved.\r\n\r\n{exception.Message}",
                "Configuration file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void RemoveSelectedConfigurationFiles()
    {
        if (!_configurationFilesEditable) return;
        if (_configurationGrid.SelectedRows.Count == 0) return;
        var selected = _configurationGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => (DeviceConfigurationFile)row.Tag!)
            .ToList();
        if (MessageBox.Show(this,
                $"Remove {selected.Count:N0} attached configuration file(s) from this device?",
                "Remove configuration files",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        foreach (var file in selected) _configurationFiles.Remove(file);
        RefreshConfigurationFiles();
    }

    private void RefreshConfigurationFiles()
    {
        _configurationGrid.Rows.Clear();
        foreach (var file in _configurationFiles.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var canDownload = _configurationFilesEditable && file.ContentIncluded &&
                MasterSessionContext.Current?.Session.CanWrite != false;
            var actionText = canDownload
                ? "Download"
                : MasterSessionContext.Current?.Session.CanWrite == false
                    ? "Owner/Tech only"
                    : "Check out client";
            var rowIndex = _configurationGrid.Rows.Add(
                file.FileName,
                FormatSize(file.SizeBytes),
                file.AddedUtc.ToLocalTime().ToString("g"),
                file.Sha256.Length > 16 ? file.Sha256[..16] + "…" : file.Sha256,
                actionText);
            var row = _configurationGrid.Rows[rowIndex];
            row.Tag = file;
            if (!canDownload)
            {
                row.Cells["Download"] = new DataGridViewTextBoxCell { Value = actionText };
                row.Cells["Download"].Style.ForeColor = UiTheme.Muted;
                row.Cells["Download"].ToolTipText = MasterSessionContext.Current?.Session.CanWrite == false
                    ? "Only an Owner or Tech can download configuration files."
                    : "Metadata only — check out this client to download the file.";
            }
        }
    }

    private static DeviceConfigurationFile CloneConfigurationFile(DeviceConfigurationFile source) => new()
    {
        Id = source.Id,
        FileName = source.FileName,
        ContentType = source.ContentType,
        SizeBytes = source.SizeBytes,
        Sha256 = source.Sha256,
        ContentBase64 = source.ContentBase64,
        ContentIncluded = source.ContentIncluded,
        Notes = source.Notes,
        AddedBy = source.AddedBy,
        AddedUtc = source.AddedUtc
    };

    private static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".txt" or ".cfg" or ".conf" or ".ini" => "text/plain",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024L => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes:N0} B"
    };

    private static TabPage NewPage(string text) => new(text)
    {
        BackColor = UiTheme.Surface,
        Padding = new Padding(16)
    };

    private static TableLayoutPanel NewFormTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 6,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Padding = new Padding(4)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        return table;
    }

    private void AddField(
        TableLayoutPanel table,
        int row,
        string label,
        string key,
        string value,
        bool span = false,
        int column = 0,
        bool password = false,
        bool multiline = false)
    {
        while (table.RowStyles.Count <= row)
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var holder = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = multiline ? 170 : 76,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 4, 0, 7),
            Margin = new Padding(column == 0 ? 4 : 8, 5, column == 0 ? 8 : 4, 2)
        };
        holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        holder.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = new Label
        {
            Text = label.ToUpperInvariant(),
            AccessibleName = $"{label} field title",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Margin = Padding.Empty
        };
        var textBox = new TextBox
        {
            Text = value,
            PlaceholderText = string.Empty,
            AccessibleName = label,
            Dock = DockStyle.Fill,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            UseSystemPasswordChar = password,
            Font = UiTheme.Font(10),
            BackColor = UiTheme.InputSurface,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        holder.Controls.Add(caption, 0, 0);
        holder.Controls.Add(textBox, 0, 1);
        table.Controls.Add(holder, column, row);
        if (span)
            table.SetColumnSpan(holder, 2);
        _fields[key] = textBox;
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value(nameof(EquipmentRecord.Description))))
        {
            MessageBox.Show(this, "Enter an equipment description.", "Description required",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
            _fields[nameof(EquipmentRecord.Description)].Focus();
            return;
        }

        var pending = new List<(NetworkInterfaceEditorRow Row, string Ip, string Mac,
            NetworkInterfaceType Type)>();
        foreach (var row in _networkRows)
        {
            var rawIp = row.IpAddress.Text.Trim();
            var rawMac = row.MacAddress.Text.Trim();
            var normalizedIp = string.Empty;
            var normalizedMac = string.Empty;
            if (rawIp.Length > 0 && !Ipv4AddressText.TryParse(rawIp, out _, out normalizedIp))
            {
                MessageBox.Show(this, $"'{rawIp}' is not a valid IPv4 address.", "Check IP address",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                row.IpAddress.Focus();
                return;
            }
            if (rawMac.Length > 0 && !MacAddressText.TryParse(rawMac, out normalizedMac))
            {
                MessageBox.Show(this,
                    $"'{rawMac}' is not a valid MAC address. Use 12 hexadecimal digits; colons, hyphens, dots, and spaces are accepted.",
                    "Check MAC address", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                row.MacAddress.Focus();
                return;
            }

            var ip = rawIp.Length == 0 ? string.Empty : normalizedIp;
            var mac = rawMac.Length == 0 ? string.Empty : normalizedMac;
            var type = row.Type.SelectedItem is NetworkInterfaceType selectedType
                ? selectedType
                : NetworkInterfaceType.Main;
            pending.Add((row, ip, mac, type));
        }
        var duplicateIp = pending.Where(item => item.Ip.Length > 0)
            .GroupBy(item => item.Ip, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIp is not null)
        {
            MessageBox.Show(this, $"IP address {duplicateIp.Key} is listed more than once on this device.",
                "Duplicate IP address", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
            duplicateIp.First().Row.IpAddress.Focus();
            return;
        }

        _source.Description = Value(nameof(EquipmentRecord.Description));
        _source.Manufacturer = Value(nameof(EquipmentRecord.Manufacturer));
        _source.PartNumber = Value(nameof(EquipmentRecord.PartNumber));
        _source.EquipmentId = Value(nameof(EquipmentRecord.EquipmentId));
        _source.Hostname = Value(nameof(EquipmentRecord.Hostname));
        _source.SerialNumber = Value(nameof(EquipmentRecord.SerialNumber));
        _source.Firmware = Value(nameof(EquipmentRecord.Firmware));
        var interfaces = new List<NetworkInterfaceRecord>();
        foreach (var pendingItem in pending)
        {
            var item = pendingItem.Row.Source;
            var ip = pendingItem.Ip;
            var mac = pendingItem.Mac;
            var type = pendingItem.Type;
            var changed = item.Type != type ||
                !string.Equals(item.IpAddress, ip, StringComparison.OrdinalIgnoreCase) ||
                !MacAddressText.EqualsNormalized(item.MacAddress, mac) &&
                !(string.IsNullOrWhiteSpace(item.MacAddress) && string.IsNullOrWhiteSpace(mac));
            item.Type = type;
            item.IpAddress = ip;
            item.MacAddress = mac;
            if (changed)
            {
                item.NetworkState = ip.Length == 0 ? NetworkState.NoAddress : NetworkState.Unknown;
                item.LastCheckedUtc = null;
                item.LastLatencyMs = null;
                item.LastNetworkError = ip.Length == 0 ? string.Empty : "Waiting for manual verification.";
                item.ObservedMacAddress = string.Empty;
                item.MacVerificationMessage = string.Empty;
                item.HttpPortOpen = false;
                item.HttpsPortOpen = false;
            }
            interfaces.Add(item);
        }
        _source.NetworkInterfaces = interfaces;
        _source.Subnet = Value(nameof(EquipmentRecord.Subnet));
        _source.Gateway = Value(nameof(EquipmentRecord.Gateway));
        _source.SerialConnection = Value(nameof(EquipmentRecord.SerialConnection));
        _source.Username = Value(nameof(EquipmentRecord.Username));
        _source.Password = Value(nameof(EquipmentRecord.Password));
        _source.SourceFile = Value(nameof(EquipmentRecord.SourceFile));
        _source.Notes = Value(nameof(EquipmentRecord.Notes));
        _source.ConfigurationFiles = _configurationFiles.Select(CloneConfigurationFile).ToList();
        _source.UpdatedUtc = DateTime.UtcNow;

        _source.SyncLegacyNetworkFields();
        _source.UpdateAggregateNetworkState();
    }

    private string Value(string key) => _fields[key].Text.Trim();

    private sealed class NetworkInterfaceEditorRow
    {
        public NetworkInterfaceRecord Source { get; }
        public Panel Container { get; } = new();
        public ComboBox Type { get; } = new();
        public TextBox IpAddress { get; } = new();
        public TextBox MacAddress { get; } = new();
        public Button RemoveButton { get; }

        public NetworkInterfaceEditorRow(NetworkInterfaceRecord source)
        {
            Source = source;
            Container.Size = new Size(710, 92);
            Container.Margin = new Padding(4, 2, 4, 8);
            Container.BackColor = UiTheme.HeaderSurface;

            AddLabel("TYPE", 14, 10, 132);
            AddLabel("IP ADDRESS", 158, 10, 220);
            AddLabel("MAC ADDRESS", 390, 10, 244);

            Type.DropDownStyle = ComboBoxStyle.DropDownList;
            Type.Items.AddRange(Enum.GetValues<NetworkInterfaceType>().Cast<object>().ToArray());
            Type.SelectedItem = source.Type;
            Type.Location = new Point(14, 34);
            Type.Size = new Size(132, 29);
            Type.Font = UiTheme.Font(9.5f);
            UiTheme.ConfigureUniformComboBox(Type);
            Container.Controls.Add(Type);

            IpAddress.Text = source.IpAddress;
            IpAddress.PlaceholderText = "IP address";
            IpAddress.AccessibleName = "IP address";
            IpAddress.Location = new Point(158, 34);
            IpAddress.Size = new Size(220, 29);
            IpAddress.Font = UiTheme.Font(9.5f);
            Container.Controls.Add(IpAddress);

            MacAddress.Text = source.MacAddress;
            MacAddress.PlaceholderText = "MAC address";
            MacAddress.AccessibleName = "MAC address";
            MacAddress.Location = new Point(390, 34);
            MacAddress.Size = new Size(244, 29);
            MacAddress.Font = UiTheme.Font(9.5f);
            Container.Controls.Add(MacAddress);

            RemoveButton = UiTheme.DangerButton("×");
            RemoveButton.AutoSize = false;
            RemoveButton.Size = new Size(42, 30);
            RemoveButton.Location = new Point(650, 33);
            RemoveButton.Font = UiTheme.Font(13, FontStyle.Bold);
            Container.Controls.Add(RemoveButton);
        }

        private void AddLabel(string text, int left, int top, int width)
        {
            Container.Controls.Add(new Label
            {
                Text = text,
                AutoSize = false,
                Size = new Size(width, 18),
                Location = new Point(left, top),
                Font = UiTheme.Font(8, FontStyle.Bold),
                ForeColor = UiTheme.Muted,
                TextAlign = ContentAlignment.MiddleLeft
            });
        }
    }
}
