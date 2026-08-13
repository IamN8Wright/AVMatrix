namespace InNasc;

internal sealed class LegacyMasterMigrationForm : Form
{
    private readonly TextBox _password = new();

    private LegacyMasterMigrationForm()
    {
        Text = "Migrate legacy company workspace";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 280);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(MasterSignInForm.Heading("One-time legacy migration", 26, 20));
        Controls.Add(MasterSignInForm.Description(
            "This master uses the older shared file password. Enter it once so the Owner account can migrate the file to user-based unlocking.",
            28, 62, 460, 54));
        Controls.Add(MasterSignInForm.FieldLabel("Existing file password", 28, 132));
        MasterSignInForm.ConfigureBox(_password, 28, 154, 460);
        _password.UseSystemPasswordChar = true;
        Controls.Add(_password);
        var migrate = UiTheme.PrimaryButton("Continue");
        migrate.AutoSize = false;
        migrate.Size = new Size(108, 36);
        migrate.Location = new Point(380, 226);
        migrate.DialogResult = DialogResult.OK;
        Controls.Add(migrate);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(282, 226);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        AcceptButton = migrate;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    public static string? Prompt(IWin32Window owner)
    {
        using var dialog = new LegacyMasterMigrationForm();
        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog._password.Text
            : null;
    }
}
