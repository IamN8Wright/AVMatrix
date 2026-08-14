namespace InNasc;

internal sealed class InNascGlobalUserEditorForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _displayName = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _globalAdmin = new();
    private readonly Label _error = new();
    private readonly FlowLayoutPanel _companyRows = new();
    private readonly List<InNascCompanyMembershipRow> _rows = [];

    public string Username => _username.Text.Trim();
    public string DisplayName => _displayName.Text.Trim();
    public string Password => _password.Text;
    public bool IsGlobalAdmin => _globalAdmin.Checked;
    public List<InNascMembershipChoice> CompanyChoices => _rows.Select(row => row.Choice()).ToList();

    public InNascGlobalUserEditorForm(InNascGlobalCatalog? catalog = null)
    {
        Text = "Add InNasc user";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(820, 750);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Add user", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            "Create the login and assign company access now. Global Admin publishes this password securely into each assigned .nasc file.",
            30, 58, 748, 48));

        AddField("Username", _username, 118);
        AddField("Display name", _displayName, 186);
        AddField("Temporary password", _password, 254);
        AddField("Confirm password", _confirm, 322);

        _globalAdmin.Text = "Global Admin (Owner access to all companies)";
        _globalAdmin.AutoSize = true;
        _globalAdmin.Location = new Point(30, 394);
        _globalAdmin.ForeColor = UiTheme.Text;
        _globalAdmin.CheckedChanged += (_, _) =>
        {
            foreach (var row in _rows) row.SetGlobalAdmin(_globalAdmin.Checked);
        };
        Controls.Add(_globalAdmin);

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Company access", 30, 430, 11, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            "Select each company this user can open and choose the role to apply inside that company.",
            30, 454, 748, 28));

        _companyRows.Location = new Point(30, 486);
        _companyRows.Size = new Size(760, 180);
        _companyRows.AutoScroll = true;
        _companyRows.FlowDirection = FlowDirection.TopDown;
        _companyRows.WrapContents = false;
        _companyRows.Padding = new Padding(0, 0, 10, 0);
        _companyRows.BackColor = UiTheme.Canvas;
        Controls.Add(_companyRows);
        PopulateCompanies(catalog);

        _error.Location = new Point(30, 674);
        _error.Size = new Size(520, 44);
        _error.ForeColor = UiTheme.Red;
        _error.Font = UiTheme.Font(8.5f);
        Controls.Add(_error);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(86, 36);
        cancel.Location = new Point(608, 700);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var add = UiTheme.PrimaryButton("Add user");
        add.AutoSize = false;
        add.Size = new Size(92, 36);
        add.Location = new Point(700, 700);
        add.Click += (_, _) => ValidateAndClose();
        Controls.Add(add);
        AcceptButton = add;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private void PopulateCompanies(InNascGlobalCatalog? catalog)
    {
        var companies = catalog?.Companies
            .Where(company => company.Enabled)
            .OrderBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList() ?? [];

        if (companies.Count == 0)
        {
            _companyRows.Controls.Add(InNascGlobalSetupForm.Description(
                "No companies are available yet. You can create the user now and assign companies later.",
                6, 6, 700, 38));
            return;
        }

        foreach (var company in companies)
        {
            var row = new InNascCompanyMembershipRow(
                company,
                assigned: false,
                MasterUserRole.Tech);
            _rows.Add(row);
            _companyRows.Controls.Add(row);
        }
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(InNascGlobalSetupForm.TitleLabel(label, 30, top, 8.5f, true));
        box.Location = new Point(30, top + 22);
        box.Size = new Size(760, 30);
        box.Font = UiTheme.Font(10);
        Controls.Add(box);
    }

    private void ValidateAndClose()
    {
        _error.Text = string.Empty;
        if (Username.Length < 2)
        {
            _error.Text = "Enter a username with at least two characters.";
            return;
        }
        if (Password.Length < 8)
        {
            _error.Text = "Passwords must contain at least 8 characters.";
            return;
        }
        if (!string.Equals(Password, _confirm.Text, StringComparison.Ordinal))
        {
            _error.Text = "The passwords do not match.";
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class InNascPasswordResetForm : Form
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true };
    private readonly Label _error = new();
    public string Password => _password.Text;

    public InNascPasswordResetForm(string username)
    {
        Text = "Reset InNasc password";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(480, 280);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(InNascGlobalSetupForm.TitleLabel($"Reset {username}", 26, 22, 16, true));
        AddField("New password", _password, 78);
        AddField("Confirm password", _confirm, 142);
        _error.Location = new Point(26, 203);
        _error.Size = new Size(250, 42);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.Location = new Point(284, 226);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var save = UiTheme.PrimaryButton("Reset");
        save.Location = new Point(378, 226);
        save.Click += (_, _) => ValidateAndClose();
        Controls.Add(save);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(InNascGlobalSetupForm.TitleLabel(label, 26, top, 8.5f, true));
        box.Location = new Point(26, top + 21);
        box.Size = new Size(428, 29);
        Controls.Add(box);
    }

    private void ValidateAndClose()
    {
        if (Password.Length < 8) { _error.Text = "Use at least 8 characters."; return; }
        if (Password != _confirm.Text) { _error.Text = "The passwords do not match."; return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}
