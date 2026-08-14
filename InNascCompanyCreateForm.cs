namespace InNasc;

internal sealed class InNascCompanyCreateForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _path = new();
    private readonly InNascDeviceLimitPicker _limit = new();
    private readonly Label _error = new();

    public string EnteredCompanyName => _name.Text.Trim();
    public string CompanyPath => _path.Text.Trim();
    public int DeviceLimit => _limit.DeviceLimit;

    public InNascCompanyCreateForm()
    {
        Text = "Create InNasc company";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(610, 438);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Create company", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            "Create the first encrypted .nasc license for this company. Company users are assigned after the company is created.",
            30, 58, 550, 46));
        AddField("Company name", _name, 116);
        Controls.Add(InNascGlobalSetupForm.TitleLabel("Company .nasc file", 30, 184, 8.5f, true));
        _path.Location = new Point(30, 206);
        _path.Size = new Size(452, 30);
        Controls.Add(_path);
        var browse = UiTheme.SecondaryButton("Browse...");
        browse.AutoSize = false;
        browse.Size = new Size(90, 32);
        browse.Location = new Point(490, 205);
        browse.Click += (_, _) => Browse();
        Controls.Add(browse);

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Device tier for this .nasc", 30, 258, 8.5f, true));
        _limit.Location = new Point(30, 282);
        _limit.Size = new Size(550, 42);
        Controls.Add(_limit);

        _error.Location = new Point(30, 338);
        _error.Size = new Size(350, 52);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(86, 36);
        cancel.Location = new Point(398, 380);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var create = UiTheme.PrimaryButton("Create company");
        create.AutoSize = false;
        create.Size = new Size(96, 36);
        create.Location = new Point(490, 380);
        create.Click += (_, _) => ValidateAndClose();
        Controls.Add(create);
        AcceptButton = create;
        CancelButton = cancel;
        _name.TextChanged += (_, _) => SuggestFileName();
        UiTheme.ApplyTheme(this);
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(InNascGlobalSetupForm.TitleLabel(label, 30, top, 8.5f, true));
        box.Location = new Point(30, top + 22);
        box.Size = new Size(550, 30);
        Controls.Add(box);
    }

    private void Browse()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Create InNasc company file",
            Filter = "InNasc company (*.nasc)|*.nasc",
            DefaultExt = "nasc",
            AddExtension = true,
            FileName = SafeName(EnteredCompanyName) + ".nasc"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.FileName;
    }

    private void SuggestFileName()
    {
        if (_path.Focused || string.IsNullOrWhiteSpace(EnteredCompanyName)) return;
        var config = InNascGlobalConfigStore.Load();
        var directory = string.IsNullOrWhiteSpace(config.GlobalPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetDirectoryName(config.GlobalPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _path.Text = Path.Combine(directory, SafeName(EnteredCompanyName) + ".nasc");
    }

    private void ValidateAndClose()
    {
        _error.Text = string.Empty;
        if (EnteredCompanyName.Length == 0) { _error.Text = "Enter a company name."; return; }
        if (CompanyPath.Length == 0) { _error.Text = "Choose a .nasc file location."; return; }
        try { _path.Text = InNascFileTypes.ValidateNewCompanyPath(CompanyPath); }
        catch (Exception exception) { _error.Text = exception.Message; return; }
        DialogResult = DialogResult.OK;
        Close();
    }

    internal static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Company" : result;
    }
}

internal sealed class InNascCompanyFileForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _path = new();
    private readonly InNascDeviceLimitPicker _limit = new();
    private readonly Label _error = new();

    public string FileName => _name.Text.Trim();
    public string CompanyPath => _path.Text.Trim();
    public int DeviceLimit => _limit.DeviceLimit;

    public InNascCompanyFileForm(string companyName)
    {
        Text = "Grant additional .nasc";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(610, 420);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(InNascGlobalSetupForm.TitleLabel("Grant additional .nasc", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            $"Add another independent encrypted license for {companyName}. It shares the company's users but has its own device tier.",
            30, 58, 550, 48));
        AddField("License name", _name, 120);
        Controls.Add(InNascGlobalSetupForm.TitleLabel("New .nasc file", 30, 188, 8.5f, true));
        _path.Location = new Point(30, 210);
        _path.Size = new Size(452, 30);
        Controls.Add(_path);
        var browse = UiTheme.SecondaryButton("Browse...");
        browse.AutoSize = false;
        browse.Size = new Size(90, 32);
        browse.Location = new Point(490, 209);
        browse.Click += (_, _) => Browse(companyName);
        Controls.Add(browse);
        Controls.Add(InNascGlobalSetupForm.TitleLabel("Device tier", 30, 260, 8.5f, true));
        _limit.Location = new Point(30, 284);
        _limit.Size = new Size(550, 42);
        Controls.Add(_limit);
        _error.Location = new Point(30, 340);
        _error.Size = new Size(350, 44);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.Location = new Point(398, 366);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var grant = UiTheme.PrimaryButton("Grant .nasc");
        grant.Location = new Point(490, 366);
        grant.Click += (_, _) => ValidateAndClose();
        Controls.Add(grant);
        AcceptButton = grant;
        CancelButton = cancel;
        _name.TextChanged += (_, _) =>
        {
            if (_path.Focused || string.IsNullOrWhiteSpace(FileName)) return;
            var directory = Path.GetDirectoryName(InNascGlobalConfigStore.Load().GlobalPath)
                            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _path.Text = Path.Combine(directory, InNascCompanyCreateForm.SafeName(FileName) + ".nasc");
        };
        UiTheme.ApplyTheme(this);
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(InNascGlobalSetupForm.TitleLabel(label, 30, top, 8.5f, true));
        box.Location = new Point(30, top + 22);
        box.Size = new Size(550, 30);
        Controls.Add(box);
    }

    private void Browse(string companyName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Grant additional InNasc company file",
            Filter = "InNasc company (*.nasc)|*.nasc",
            DefaultExt = "nasc",
            AddExtension = true,
            FileName = InNascCompanyCreateForm.SafeName(
                string.IsNullOrWhiteSpace(FileName) ? companyName : FileName) + ".nasc"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.FileName;
    }

    private void ValidateAndClose()
    {
        _error.Text = string.Empty;
        if (FileName.Length == 0) { _error.Text = "Enter a license name."; return; }
        if (CompanyPath.Length == 0) { _error.Text = "Choose a .nasc file location."; return; }
        try { _path.Text = InNascFileTypes.ValidateNewCompanyPath(CompanyPath); }
        catch (Exception exception) { _error.Text = exception.Message; return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class InNascDeviceLimitForm : Form
{
    private readonly InNascDeviceLimitPicker _picker;
    public int DeviceLimit => _picker.DeviceLimit;

    public InNascDeviceLimitForm(string licenseName, int currentLimit, int deviceCount)
    {
        Text = "Change device tier";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 250);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(InNascGlobalSetupForm.TitleLabel("Change device tier", 26, 22, 17, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            $"{licenseName} currently contains {deviceCount:N0} devices. A new limit cannot be lower than current usage.",
            28, 58, 484, 44));
        _picker = new InNascDeviceLimitPicker(currentLimit) { Location = new Point(28, 118), Size = new Size(484, 42) };
        Controls.Add(_picker);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.Location = new Point(350, 194);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var save = UiTheme.PrimaryButton("Save tier");
        save.Location = new Point(438, 194);
        save.Click += (_, _) =>
        {
            if (DeviceLimit > 0 && DeviceLimit < deviceCount)
            {
                MessageBox.Show(this, $"Choose at least {deviceCount:N0} devices, or Unlimited.",
                    "Device tier", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(save);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }
}

internal sealed class InNascDeviceLimitPicker : UserControl
{
    private readonly ComboBox _preset = new();
    private readonly NumericUpDown _custom = new();
    private sealed record Option(string Text, int Value) { public override string ToString() => Text; }

    public int DeviceLimit => _preset.SelectedItem is Option option && option.Value >= 0
        ? option.Value
        : decimal.ToInt32(_custom.Value);

    public InNascDeviceLimitPicker(int initial = 250)
    {
        BackColor = UiTheme.Canvas;
        _preset.DropDownStyle = ComboBoxStyle.DropDownList;
        _preset.Location = new Point(0, 0);
        _preset.Size = new Size(220, 32);
        _preset.Items.AddRange([
            new Option("250 devices", 250),
            new Option("500 devices", 500),
            new Option("1,000 devices", 1000),
            new Option("Unlimited", 0),
            new Option("Custom...", -1)
        ]);
        _custom.Location = new Point(232, 0);
        _custom.Size = new Size(150, 32);
        _custom.Minimum = 1;
        _custom.Maximum = 10000000;
        _custom.ThousandsSeparator = true;
        _custom.Visible = false;
        Controls.Add(_preset);
        Controls.Add(_custom);
        var matching = _preset.Items.Cast<Option>().FirstOrDefault(option => option.Value == initial);
        _preset.SelectedItem = matching ?? _preset.Items.Cast<Option>().Last();
        if (matching is null)
        {
            _custom.Value = Math.Clamp(initial, 1, decimal.ToInt32(_custom.Maximum));
            _custom.Visible = true;
        }
        _preset.SelectedIndexChanged += (_, _) =>
        {
            _custom.Visible = _preset.SelectedItem is Option option && option.Value < 0;
        };
        UiTheme.ApplyTheme(this);
    }
}
