namespace InNasc;

internal sealed class AboutControl : UserControl
{
    public event EventHandler? UpdateRequested;

    public AboutControl()
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Canvas;

        var centering = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(28)
        };

        var card = new RoundedPanel
        {
            Anchor = AnchorStyles.None,
            Size = new Size(720, 510),
            Padding = new Padding(28)
        };
        var appLogo = new PictureBox
        {
            Image = AppBrand.CreatePrimaryHorizontal(),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            Location = new Point(70, 16),
            Size = new Size(580, 183),
            TabStop = false
        };
        card.Controls.Add(appLogo);

        card.Controls.Add(new Panel
        {
            BackColor = UiTheme.Border,
            Location = new Point(54, 224),
            Size = new Size(612, 1)
        });

        var inN8Logo = new PictureBox
        {
            Image = AppBrand.CreateInN8LabsMascot(),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Location = new Point(44, 248),
            Size = new Size(220, 176),
            TabStop = false
        };
        card.Controls.Add(inN8Logo);

        card.Controls.Add(LeftLabel(
            "Created by InN8 Labs",
            UiTheme.Font(16, FontStyle.Bold),
            UiTheme.Text,
            new Rectangle(286, 252, 386, 35)));
        card.Controls.Add(LeftLabel(
            $"Revision {AppInfo.Revision}  •  © {AppInfo.CurrentYear} InN8 Labs",
            UiTheme.Font(10),
            UiTheme.Muted,
            new Rectangle(286, 292, 386, 26)));

        card.Controls.Add(LeftLabel(
            "Design & Development  •  InN8 Labs\r\nIntegrated Network Tool  •  N8's IP Scanner\r\nPlatform  •  Windows / .NET 8",
            UiTheme.Font(9.5f),
            UiTheme.Muted,
            new Rectangle(286, 328, 386, 74)));

        var website = new LinkLabel
        {
            Text = "InNasc.com",
            Font = UiTheme.Font(10, FontStyle.Bold),
            LinkColor = UiTheme.Blue,
            ActiveLinkColor = UiTheme.BlueHover,
            Location = new Point(286, 402),
            Size = new Size(160, 26),
            TextAlign = ContentAlignment.MiddleLeft
        };
        website.LinkClicked += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(AppInfo.Website) { UseShellExecute = true });
        card.Controls.Add(website);

        var update = UiTheme.PrimaryButton("Check for app update");
        update.AutoSize = false;
        update.Location = new Point(474, 432);
        update.Size = new Size(198, 40);
        update.Click += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        card.Controls.Add(update);

        centering.Controls.Add(card, 0, 0);
        Controls.Add(centering);
        UiTheme.ApplyTheme(this);
    }

    private static Label CenteredLabel(string text, Font font, Color color, Rectangle bounds) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        TextAlign = ContentAlignment.MiddleCenter,
        Location = bounds.Location,
        Size = bounds.Size
    };

    private static Label LeftLabel(string text, Font font, Color color, Rectangle bounds) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        TextAlign = ContentAlignment.MiddleLeft,
        Location = bounds.Location,
        Size = bounds.Size
    };
}
