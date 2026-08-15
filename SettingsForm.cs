namespace InNasc;

internal sealed class SettingsForm : Form
{
    private readonly AppData _data;
    private readonly TextBox _projectName = new();
    private readonly NumericUpDown _pingTimeout = new();
    private readonly ComboBox _themeMode = new();
    private readonly Label _transferStatus = new();
    private PortableImport? _pendingImport;

    public bool DataImported { get; private set; }
    public int ImportedClientCount { get; private set; }
    public int ImportedEquipmentCount { get; private set; }

    public SettingsForm(AppData data, string dataPath)
    {
        _data = data;
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(650, 575);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 235));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = "Application settings",
            AutoSize = true,
            Font = UiTheme.Font(18, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        heading.Controls.Add(new Label
        {
            Text = "General preferences for this workstation",
            AutoSize = true,
            Font = UiTheme.Font(9),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 36)
        });

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = UiTheme.Surface,
            Padding = new Padding(18, 16, 18, 12),
            Margin = Padding.Empty
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _projectName.Dock = DockStyle.Top;
        _projectName.Text = data.ProjectName;
        _projectName.Font = UiTheme.Font(10);
        fields.Controls.Add(FieldLabel("Project name"), 0, 0);
        fields.Controls.Add(_projectName, 1, 0);

        _pingTimeout.Minimum = 250;
        _pingTimeout.Maximum = 10000;
        _pingTimeout.Increment = 250;
        _pingTimeout.Value = Math.Clamp(data.Settings.PingTimeoutMilliseconds, 250, 10000);
        _pingTimeout.Dock = DockStyle.Top;
        _pingTimeout.Font = UiTheme.Font(10);
        _pingTimeout.ThousandsSeparator = true;
        fields.Controls.Add(FieldLabel("Ping timeout (ms)"), 0, 1);
        fields.Controls.Add(_pingTimeout, 1, 1);

        _themeMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeMode.Font = UiTheme.Font(10);
        _themeMode.Items.AddRange(["Light", "Dark"]);
        _themeMode.SelectedItem = data.Settings.DarkMode ? "Dark" : "Light";
        _themeMode.Dock = DockStyle.Top;
        UiTheme.ConfigureUniformComboBox(_themeMode);
        fields.Controls.Add(FieldLabel("Appearance"), 0, 2);
        fields.Controls.Add(_themeMode, 1, 2);

        var path = new TextBox
        {
            Dock = DockStyle.Top,
            ReadOnly = true,
            Text = dataPath,
            Font = UiTheme.Font(9),
            BackColor = UiTheme.HeaderSurface,
            ForeColor = UiTheme.Text
        };
        fields.Controls.Add(FieldLabel("Local data file"), 0, 3);
        fields.Controls.Add(path, 1, 3);

        var transfer = BuildTransferPanel();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var save = UiTheme.PrimaryButton("Save settings");
        save.AutoSize = false;
        save.Size = new Size(122, 36);
        save.Click += Save_Click;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(82, 36);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        AcceptButton = save;
        CancelButton = cancel;
        shell.Controls.Add(heading, 0, 0);
        shell.Controls.Add(fields, 0, 1);
        shell.Controls.Add(transfer, 0, 2);
        shell.Controls.Add(buttons, 0, 3);
        Controls.Add(shell);
        UiTheme.ApplyTheme(this);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(9, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        Margin = new Padding(0, 7, 12, 0)
    };

    private Control BuildTransferPanel()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
            Padding = new Padding(18, 14, 18, 12)
        };
        panel.Controls.Add(new Label
        {
            Text = "APP BACKUP & TRANSFER",
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(18, 14)
        });
        panel.Controls.Add(new Label
        {
            Text = "Creates an InNasc backup for another PC. This is not an Excel workbook.",
            AutoSize = true,
            Font = UiTheme.Font(9),
            ForeColor = UiTheme.Text,
            Location = new Point(18, 38)
        });
        panel.Controls.Add(new Label
        {
            Text = "Backups include credentials and configuration files by default; privacy options are offered before export.",
            AutoSize = true,
            Font = UiTheme.Font(8.5f),
            ForeColor = UiTheme.Amber,
            Location = new Point(18, 63)
        });

        var export = UiTheme.SecondaryButton("Back up all data (.nasc)");
        export.AutoSize = false;
        export.Size = new Size(210, 36);
        export.Location = new Point(18, 92);
        export.Click += ExportData_Click;
        panel.Controls.Add(export);

        var import = UiTheme.SecondaryButton("Restore backup");
        import.AutoSize = false;
        import.Size = new Size(150, 36);
        import.Location = new Point(238, 92);
        import.Click += ImportData_Click;
        panel.Controls.Add(import);

        var github = UiTheme.SecondaryButton("GitHub Master storage…");
        github.AutoSize = false;
        github.Size = new Size(180, 36);
        github.Location = new Point(398, 92);
        github.Click += (_, _) =>
        {
            using var form = new GitHubMasterStorageForm(_data, new DataStore());
            form.ShowDialog(this);
        };
        panel.Controls.Add(github);

        _transferStatus.AutoEllipsis = true;
        _transferStatus.Font = UiTheme.Font(8.7f);
        _transferStatus.ForeColor = UiTheme.Muted;
        _transferStatus.Location = new Point(18, 139);
        _transferStatus.Size = new Size(560, 23);
        _transferStatus.Text = "No transfer is pending.";
        panel.Controls.Add(_transferStatus);
        return panel;
    }

    private void ExportData_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Back up all InNasc data",
            Filter = "InNasc company backup (*.nasc)|*.nasc",
            DefaultExt = "nasc",
            AddExtension = true,
            FileName = $"InNasc-Company-{DateTime.Now:yyyy-MM-dd}.nasc"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var backupOptions = BackupPrivacyOptionsForm.Prompt(this);
        if (backupOptions is null) return;
        var protection = PasswordDialog.PromptForNewFile(this);
        if (protection is null) return;
        try
        {
            PortableDataService.Export(
                dialog.FileName, _data, protection.Password, backupOptions);
            _transferStatus.Text = protection.Password is null
                ? $"Exported {Path.GetFileName(dialog.FileName)}"
                : $"Exported password-protected {Path.GetFileName(dialog.FileName)}";
            MessageBox.Show(this,
                "The .nasc backup is ready" +
                (protection.Password is null ? ". " : " and encrypted as JWE. ") +
                "Copy it to the new PC and restore it from Settings. " +
                "For Excel, use the Excel button on a client card or Export Excel in its workspace.",
                "Backup complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"Client data could not be exported.\r\n\r\n{exception.Message}",
                "Backup failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ImportData_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Restore InNasc backup",
            Filter = "InNasc company backup (*.nasc)|*.nasc|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string? password = null;
            if (PortableDataService.IsPasswordProtected(dialog.FileName))
            {
                var prompt = PasswordDialog.PromptForProtectedFile(this);
                if (prompt is null) return;
                password = prompt.Password;
            }
            var imported = PortableDataService.Import(dialog.FileName, password);
            var answer = MessageBox.Show(this,
                $"This file contains {imported.ClientCount:N0} client(s) and " +
                $"{imported.EquipmentCount:N0} equipment record(s).\r\n\r\n" +
                "Importing will replace the client data currently stored on this PC. " +
                "The replacement will occur only after you click Save settings.\r\n\r\nContinue?",
                "Confirm client-data import",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            _pendingImport = imported;
            _projectName.Text = imported.Data.ProjectName;
            _transferStatus.Text =
                $"Ready to import {imported.ClientCount:N0} client(s) — click Save settings.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"Client data could not be imported.\r\n\r\n{exception.Message}",
                "Import failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        var projectName = _projectName.Text.Trim();
        if (projectName.Length == 0)
        {
            MessageBox.Show(this, "Enter a project name.", "Settings",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _projectName.Focus();
            return;
        }

        _data.ProjectName = projectName;
        _data.Settings.PingTimeoutMilliseconds = decimal.ToInt32(_pingTimeout.Value);
        _data.Settings.DarkMode = string.Equals(_themeMode.SelectedItem as string, "Dark",
            StringComparison.OrdinalIgnoreCase);
        if (_pendingImport is not null)
        {
            _data.Clients = _pendingImport.Data.Clients;
            DataImported = true;
            ImportedClientCount = _pendingImport.ClientCount;
            ImportedEquipmentCount = _pendingImport.EquipmentCount;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
