namespace AVMatrixStudio;

internal sealed class InNascGlobalSetupForm : Form
{
    private readonly TextBox _username = new() { Text = "admin" };
    private readonly TextBox _displayName = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true };
    private readonly Label _error = new();

    public string Username => _username.Text.Trim();
    public string DisplayName => _displayName.Text.Trim();
    public string Password => _password.Text;

    public InNascGlobalSetupForm()
    {
        Text = "Create InNasc Global Admin";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 470);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(TitleLabel("Create Global Admin", 28, 24, 19, true));
        Controls.Add(Description(
            "This account manages companies and company access. User passwords are never displayed or stored as readable text.",
            30, 62, 460, 48));
        AddField("Username", _username, 126);
        AddField("Display name", _displayName, 194);
        AddField("Password", _password, 262);
        AddField("Confirm password", _confirm, 330);

        _error.Location = new Point(30, 392);
        _error.Size = new Size(286, 46);
        _error.ForeColor = UiTheme.Red;
        _error.Font = UiTheme.Font(8.5f);
        Controls.Add(_error);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(318, 414);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        var create = UiTheme.PrimaryButton("Create");
        create.AutoSize = false;
        create.Size = new Size(86, 36);
        create.Location = new Point(414, 414);
        create.Click += (_, _) => ValidateAndClose();
        Controls.Add(create);

        AcceptButton = create;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
        Shown += (_, _) => _username.Focus();
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(TitleLabel(label, 30, top, 8.5f, true));
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
            _username.Focus();
            return;
        }
        if (Password.Length < 8)
        {
            _error.Text = "Passwords must contain at least 8 characters.";
            _password.Focus();
            return;
        }
        if (!string.Equals(Password, _confirm.Text, StringComparison.Ordinal))
        {
            _error.Text = "The passwords do not match.";
            _confirm.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    internal static Label TitleLabel(string text, int left, int top, float size = 9, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = UiTheme.Text,
        Location = new Point(left, top)
    };

    internal static Label Description(string text, int left, int top, int width, int height) => new()
    {
        Text = text,
        AutoSize = false,
        Font = UiTheme.Font(9),
        ForeColor = UiTheme.Muted,
        Location = new Point(left, top),
        Size = new Size(width, height)
    };
}
