namespace InNasc;

internal sealed class BackupPrivacyOptionsForm : Form
{
    private readonly CheckBox _credentials = new() { Checked = true };
    private readonly CheckBox _configs = new() { Checked = true };

    public PortableBackupOptions Options =>
        new(_credentials.Checked, _configs.Checked);

    private BackupPrivacyOptionsForm()
    {
        Text = "Backup privacy";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 315);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(MasterSignInForm.Heading("Choose backup contents", 26, 22));
        Controls.Add(MasterSignInForm.Description(
            "All data is included by default. Clear either option when sending a backup to someone who should not receive sensitive credentials or configuration files.",
            28, 60, 480, 60));

        _credentials.Text = "Include device usernames and passwords";
        _credentials.Location = new Point(30, 140);
        _credentials.Size = new Size(450, 28);
        _configs.Text = "Include device/client configuration files";
        _configs.Location = new Point(30, 180);
        _configs.Size = new Size(450, 28);
        Controls.AddRange([_credentials, _configs]);

        var note = new Label
        {
            Text = "Default: full backup. Clearing an option affects only the exported copy.",
            AutoSize = false,
            Location = new Point(30, 216),
            Size = new Size(475, 30),
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.5f)
        };
        Controls.Add(note);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(86, 36);
        cancel.Location = new Point(350, 260);
        cancel.DialogResult = DialogResult.Cancel;
        var next = UiTheme.PrimaryButton("Continue");
        next.AutoSize = false;
        next.Size = new Size(86, 36);
        next.Location = new Point(446, 260);
        next.DialogResult = DialogResult.OK;
        Controls.AddRange([cancel, next]);
        AcceptButton = next;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    public static PortableBackupOptions? Prompt(IWin32Window owner)
    {
        using var form = new BackupPrivacyOptionsForm();
        return form.ShowDialog(owner) == DialogResult.OK ? form.Options : null;
    }
}
