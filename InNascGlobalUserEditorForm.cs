namespace InNasc;

internal sealed class InNascGlobalUserEditorForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _displayName = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _globalAdmin = new();
    private readonly Label _error = new();

    public string Username => _username.Text.Trim();
    public string DisplayName => _displayName.Text.Trim();
    public string Password => _password.Text;
    public bool IsGlobalAdmin => _globalAdmin.Checked;

    public InNascGlobalUserEditorForm()
    {
        Text = "Add InNasc user";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 500);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(InNascGlobalSetupForm.TitleLabel("Add user", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            "Create the login here, then assign the user to one or more companies. Passwords cannot be viewed later; a Global Admin can only reset them.",
            30, 58, 460, 58));
        AddField("Username", _username, 128);
        AddField("Display name", _displayName, 196);
        AddField("Temporary password", _password, 264);
        AddField("Confirm password", _confirm, 332);

        _globalAdmin.Text = "Global Admin (access to all companies)";
        _globalAdmin.AutoSize = true;
        _globalAdmin.Location = new Point(30, 402);
        _globalAdmin.ForeColor = UiTheme.Text;
        Controls.Add(_globalAdmin);

        _error.Location = new Point(30, 430);
        _error.Size = new Size(290, 44);
        _error.ForeColor = UiTheme.Red;
        _error.Font = UiTheme.Font(8.5f);
        Controls.Add(_error);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(86, 36);
        cancel.Location = new Point(320, 446);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        var add = UiTheme.PrimaryButton("Add user");
        add.AutoSize = false;
        add.Size = new Size(92, 36);
        add.Location = new Point(414, 446);
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
        box.Size = new Size(460, 30);
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
