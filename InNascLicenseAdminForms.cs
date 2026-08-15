namespace InNasc;

internal sealed class InNascLicenseExpirationForm : Form
{
    private readonly ComboBox _term = new();
    public DateTime? ExpiresUtc { get; private set; }

    private sealed record Option(string Text, int Years)
    {
        public override string ToString() => Text;
    }

    public InNascLicenseExpirationForm(DateTime? current = null)
    {
        Text = "License expiration";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 250);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(MasterSignInForm.Heading("License term", 26, 22));
        Controls.Add(MasterSignInForm.Description(
            current is null
                ? "Choose how long this license remains active. The term begins when you save it."
                : $"Current expiration: {DeviceLimitPolicy.ExpirationText(current)}. Choose a new term from today.",
            28, 60, 444, 48));

        _term.DropDownStyle = ComboBoxStyle.DropDownList;
        _term.Location = new Point(28, 120);
        _term.Size = new Size(310, 32);
        _term.Items.AddRange([
            new Option("1 year", 1),
            new Option("2 years", 2),
            new Option("3 years", 3),
            new Option("Unlimited / no expiration", 0)
        ]);
        _term.SelectedIndex = current is null ? 3 : 0;
        Controls.Add(_term);
        UiTheme.ConfigureUniformComboBox(_term);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(300, 194);
        cancel.DialogResult = DialogResult.Cancel;
        var save = UiTheme.PrimaryButton("Set term");
        save.AutoSize = false;
        save.Size = new Size(88, 36);
        save.Location = new Point(398, 194);
        save.Click += (_, _) =>
        {
            var option = (Option)_term.SelectedItem!;
            ExpiresUtc = option.Years <= 0 ? null : DateTime.UtcNow.AddYears(option.Years);
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.AddRange([cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }
}

internal sealed class InNascRecoveryPasswordForm : Form
{
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly Label _error = new();

    public string Password => _password.Text;

    private InNascRecoveryPasswordForm(string licenseName)
    {
        Text = "Root recovery";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(525, 345);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(MasterSignInForm.Heading("Set break-glass root access", 26, 22));
        Controls.Add(MasterSignInForm.Description(
            $"{licenseName}: the recovery username is root. The password is not stored as readable text; it is hashed and used to encrypt a credential that unlocks this .nasc.",
            28, 60, 468, 58));

        Controls.Add(MasterSignInForm.FieldLabel("Root password", 28, 132));
        MasterSignInForm.ConfigureBox(_password, 28, 154, 468);
        _password.UseSystemPasswordChar = true;
        Controls.Add(_password);
        Controls.Add(MasterSignInForm.FieldLabel("Confirm password", 28, 196));
        MasterSignInForm.ConfigureBox(_confirm, 28, 218, 468);
        _confirm.UseSystemPasswordChar = true;
        Controls.Add(_confirm);

        _error.Location = new Point(28, 256);
        _error.Size = new Size(320, 40);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(326, 296);
        cancel.DialogResult = DialogResult.Cancel;
        var save = UiTheme.PrimaryButton("Set root");
        save.AutoSize = false;
        save.Size = new Size(82, 36);
        save.Location = new Point(424, 296);
        save.Click += (_, _) => Save();
        Controls.AddRange([cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private void Save()
    {
        if (_password.Text.Length < 8)
        {
            _error.Text = "Use at least 8 characters.";
            return;
        }
        if (_password.Text != _confirm.Text)
        {
            _error.Text = "Passwords do not match.";
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    public static string? Prompt(IWin32Window owner, string licenseName)
    {
        using var form = new InNascRecoveryPasswordForm(licenseName);
        return form.ShowDialog(owner) == DialogResult.OK ? form.Password : null;
    }
}
