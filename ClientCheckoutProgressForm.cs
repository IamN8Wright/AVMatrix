namespace InNasc;

internal sealed class ClientCheckoutProgressForm : Form
{
    public ClientCheckoutProgressForm(
        string clientName,
        bool checkingIn,
        bool takingOver)
    {
        Text = checkingIn ? "Checking in client" : "Checking out client";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 230);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        UseWaitCursor = true;

        var title = takingOver
            ? $"Taking over {clientName}"
            : checkingIn
                ? $"Checking in {clientName}"
                : $"Checking out {clientName}";
        var detail = checkingIn
            ? "Uploading this client's records and configuration files, updating the company workspace, and releasing its lock."
            : "Locking this client in the company workspace and downloading its device records and configuration files.";

        Controls.Add(new Label
        {
            Text = title,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(18, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(28, 25),
            Size = new Size(464, 38)
        });
        Controls.Add(new Label
        {
            Text = detail,
            AutoSize = false,
            Font = UiTheme.Font(9.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(30, 72),
            Size = new Size(460, 58)
        });
        Controls.Add(new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24,
            Location = new Point(30, 145),
            Size = new Size(460, 18)
        });
        Controls.Add(new Label
        {
            Text = "Please keep InNasc open until this finishes.",
            AutoSize = false,
            Font = UiTheme.Font(8.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(30, 174),
            Size = new Size(460, 28),
            TextAlign = ContentAlignment.MiddleCenter
        });

        UiTheme.ApplyTheme(this);
    }
}
