namespace InNasc;

internal sealed class InNascGlobalAdminUserEditorForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _displayName = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true };
    private readonly Label _error = new();

    public string Username => _username.Text.Trim();
    public string DisplayName => _displayName.Text.Trim();
    public string Password => _password.Text;

    public InNascGlobalAdminUserEditorForm()
    {
        Text = "Add Global Admin";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 430);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(InNascGlobalSetupForm.TitleLabel("Add Global Admin", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            "This account can open the .nascglobal catalog. It does not automatically become a user in any company .nasc file.",
            30, 58, 500, 48));
        AddField("Username", _username, 118);
        AddField("Display name", _displayName, 184);
        AddField("Password", _password, 250);
        AddField("Confirm password", _confirm, 316);
        _error.Location = new Point(30, 376);
        _error.Size = new Size(300, 40);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.Location = new Point(368, 376);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var add = UiTheme.PrimaryButton("Add admin");
        add.Location = new Point(458, 376);
        add.Click += (_, _) => ValidateAndClose();
        Controls.Add(add);
        AcceptButton = add;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(InNascGlobalSetupForm.TitleLabel(label, 30, top, 8.5f, true));
        box.Location = new Point(30, top + 22);
        box.Size = new Size(500, 30);
        Controls.Add(box);
    }

    private void ValidateAndClose()
    {
        _error.Text = string.Empty;
        if (Username.Length < 2) { _error.Text = "Use at least two characters for the username."; return; }
        if (Password.Length < 8) { _error.Text = "Use at least 8 password characters."; return; }
        if (Password != _confirm.Text) { _error.Text = "The passwords do not match."; return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class InNascCompanyUserEditorForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _displayName = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true };
    private readonly ComboBox _role = new();
    private readonly Label _error = new();
    private readonly bool _isEdit;

    public string Username => _username.Text.Trim();
    public string DisplayName => _displayName.Text.Trim();
    public string Password => _password.Text;
    public MasterUserRole Role => _role.SelectedItem is MasterUserRole role ? role : MasterUserRole.Tech;

    public InNascCompanyUserEditorForm(InNascCompanyUserRecord? user = null, bool firstUser = false)
    {
        _isEdit = user is not null;
        Text = _isEdit ? "Edit company user" : "Add company user";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(580, _isEdit ? 390 : 520);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(InNascGlobalSetupForm.TitleLabel(
            _isEdit ? "Edit company user" : "Add company user", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            _isEdit
                ? "Change the user's display name or access level. Their username remains fixed."
                : "This login is scoped to this company and is published into each of its active .nasc files.",
            30, 58, 520, 48));
        AddField("Username", _username, 118);
        AddField("Display name", _displayName, 184);
        Controls.Add(InNascGlobalSetupForm.TitleLabel("Access level", 30, 250, 8.5f, true));
        _role.DropDownStyle = ComboBoxStyle.DropDownList;
        _role.Location = new Point(30, 272);
        _role.Size = new Size(520, 32);
        _role.Items.AddRange(Enum.GetValues<MasterUserRole>().Cast<object>().ToArray());
        Controls.Add(_role);

        var nextTop = 318;
        if (!_isEdit)
        {
            AddField("Temporary password", _password, nextTop);
            AddField("Confirm password", _confirm, nextTop + 66);
            nextTop += 132;
        }
        _error.Location = new Point(30, nextTop);
        _error.Size = new Size(330, 42);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.Location = new Point(388, ClientSize.Height - 52);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var save = UiTheme.PrimaryButton(_isEdit ? "Save access" : "Add user");
        save.Location = new Point(478, ClientSize.Height - 52);
        save.Click += (_, _) => ValidateAndClose();
        Controls.Add(save);
        AcceptButton = save;
        CancelButton = cancel;

        if (user is not null)
        {
            _username.Text = user.Username;
            _username.ReadOnly = true;
            _displayName.Text = user.DisplayName;
            _role.SelectedItem = user.Role;
        }
        else
        {
            _role.SelectedItem = firstUser ? MasterUserRole.Owner : MasterUserRole.Tech;
        }
        UiTheme.ConfigureUniformComboBox(_role);
        UiTheme.ApplyTheme(this);
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(InNascGlobalSetupForm.TitleLabel(label, 30, top, 8.5f, true));
        box.Location = new Point(30, top + 22);
        box.Size = new Size(520, 30);
        Controls.Add(box);
    }

    private void ValidateAndClose()
    {
        _error.Text = string.Empty;
        if (Username.Length < 2) { _error.Text = "Use at least two characters for the username."; return; }
        if (!_isEdit && Password.Length < 8) { _error.Text = "Use at least 8 password characters."; return; }
        if (!_isEdit && Password != _confirm.Text) { _error.Text = "The passwords do not match."; return; }
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
        ClientSize = new Size(480, 290);
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
        cancel.Location = new Point(284, 236);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var save = UiTheme.PrimaryButton("Reset");
        save.Location = new Point(378, 236);
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
