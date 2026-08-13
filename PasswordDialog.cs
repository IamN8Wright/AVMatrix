namespace AVMatrixStudio;

internal sealed record PasswordPromptResult(string? Password, bool RememberForSession);

internal sealed class PasswordDialog : Form
{
    private readonly bool _creating;
    private readonly CheckBox _protect = new();
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly CheckBox _remember = new();
    private readonly Label _validation = new();

    public PasswordPromptResult Result { get; private set; } = new(null, false);

    private PasswordDialog(bool creating, bool allowUnprotected, bool allowRemember)
    {
        _creating = creating;
        Text = creating ? "Protect InNasc file" : "Unlock InNasc file";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(490, creating ? 390 : 290);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var heading = new Label
        {
            Text = creating ? "Password protection" : "Password required",
            AutoSize = true,
            Font = UiTheme.Font(18, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(26, 22)
        };
        Controls.Add(heading);
        Controls.Add(new Label
        {
            Text = creating
                ? "Protected files are automatically encrypted as compact JWE.\r\nThe password cannot be recovered if it is lost."
                : "This .avmatrix file is encrypted. Enter its password to continue.",
            AutoSize = true,
            Font = UiTheme.Font(9.2f),
            ForeColor = UiTheme.Muted,
            Location = new Point(28, 61)
        });

        var top = creating ? 112 : 100;
        _protect.Text = "Password protect this file";
        _protect.AutoSize = true;
        _protect.Checked = !allowUnprotected;
        _protect.Visible = creating && allowUnprotected;
        _protect.Location = new Point(28, top);
        _protect.CheckedChanged += (_, _) => UpdateEnabledState();
        Controls.Add(_protect);
        if (_protect.Visible) top += 40;

        Controls.Add(FieldLabel("Password", top));
        ConfigurePasswordBox(_password, top + 21);
        Controls.Add(_password);
        top += 66;

        if (creating)
        {
            Controls.Add(FieldLabel("Confirm password", top));
            ConfigurePasswordBox(_confirm, top + 21);
            Controls.Add(_confirm);
            top += 66;
        }
        else if (allowRemember)
        {
            _remember.Text = "Remember for this app session";
            _remember.AutoSize = true;
            _remember.Location = new Point(28, top + 2);
            Controls.Add(_remember);
            top += 38;
        }

        _validation.AutoSize = false;
        _validation.Size = new Size(430, 24);
        _validation.Location = new Point(28, top - 4);
        _validation.ForeColor = UiTheme.Red;
        Controls.Add(_validation);

        var ok = UiTheme.PrimaryButton(creating ? "Continue" : "Unlock");
        ok.AutoSize = false;
        ok.Size = new Size(105, 36);
        ok.Location = new Point(347, ClientSize.Height - 56);
        ok.Click += Ok_Click;
        Controls.Add(ok);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(84, 36);
        cancel.Location = new Point(253, ClientSize.Height - 56);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        UpdateEnabledState();
        UiTheme.ApplyTheme(this);
        Shown += (_, _) =>
        {
            if (_protect.Visible && !_protect.Checked) _protect.Focus();
            else _password.Focus();
        };
    }

    public static PasswordPromptResult? PromptForNewFile(IWin32Window owner)
    {
        using var dialog = new PasswordDialog(creating: true, allowUnprotected: true, allowRemember: false);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    public static PasswordPromptResult? PromptForProtectedFile(
        IWin32Window owner,
        bool allowRememberForSession = false)
    {
        using var dialog = new PasswordDialog(creating: false, allowUnprotected: false,
            allowRemember: allowRememberForSession);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    private static Label FieldLabel(string text, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(8.5f, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        Location = new Point(28, top)
    };

    private static void ConfigurePasswordBox(TextBox box, int top)
    {
        box.Location = new Point(28, top);
        box.Size = new Size(424, 29);
        box.Font = UiTheme.Font(10);
        box.UseSystemPasswordChar = true;
    }

    private void UpdateEnabledState()
    {
        var enabled = !_creating || !_protect.Visible || _protect.Checked;
        _password.Enabled = enabled;
        _confirm.Enabled = enabled;
        _validation.Text = string.Empty;
    }

    private void Ok_Click(object? sender, EventArgs e)
    {
        if (_creating && _protect.Visible && !_protect.Checked)
        {
            Result = new PasswordPromptResult(null, false);
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        var password = _password.Text;
        if (password.Length < (_creating ? 10 : 1))
        {
            _validation.Text = _creating
                ? "Use at least 10 characters. A longer passphrase is recommended."
                : "Enter the file password.";
            _password.Focus();
            return;
        }
        if (_creating && !string.Equals(password, _confirm.Text, StringComparison.Ordinal))
        {
            _validation.Text = "The passwords do not match.";
            _confirm.Focus();
            return;
        }

        Result = new PasswordPromptResult(password, _remember.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }
}
