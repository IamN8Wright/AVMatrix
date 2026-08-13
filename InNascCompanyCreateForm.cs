namespace InNasc;

internal sealed class InNascCompanyCreateForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _path = new();
    private readonly Label _error = new();

    public string EnteredCompanyName => _name.Text.Trim();
    public string CompanyPath => _path.Text.Trim();

    public InNascCompanyCreateForm()
    {
        Text = "Create InNasc company";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(570, 330);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Create company", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            "Each company is stored in its own encrypted .nasc file with its clients, locations, rooms, equipment, checkouts, and sync history.",
            30, 58, 510, 58));

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Company name", 30, 130, 8.5f, true));
        _name.Location = new Point(30, 152);
        _name.Size = new Size(510, 30);
        Controls.Add(_name);

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Company .nasc file", 30, 198, 8.5f, true));
        _path.Location = new Point(30, 220);
        _path.Size = new Size(414, 30);
        Controls.Add(_path);
        var browse = UiTheme.SecondaryButton("Browse…");
        browse.AutoSize = false;
        browse.Size = new Size(88, 32);
        browse.Location = new Point(452, 219);
        browse.Click += (_, _) => Browse();
        Controls.Add(browse);

        _error.Location = new Point(30, 260);
        _error.Size = new Size(310, 42);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(84, 36);
        cancel.Location = new Point(360, 276);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var create = UiTheme.PrimaryButton("Create");
        create.AutoSize = false;
        create.Size = new Size(88, 36);
        create.Location = new Point(452, 276);
        create.Click += (_, _) => ValidateAndClose();
        Controls.Add(create);
        AcceptButton = create;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
        _name.TextChanged += (_, _) => SuggestFileName();
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

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Company" : result;
    }
}
