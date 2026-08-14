namespace InNasc;

internal sealed class MasterWelcomeControl : UserControl
{
    private readonly AppData _data;
    private readonly Label _welcomeTitle = new();
    private readonly Label _welcomeSubtitle = new();
    private readonly Label _tier = new();
    private readonly Label _selection = new();
    private readonly Label _message = new();
    private InNascBrandLogo _defaultLogo = null!;
    private PictureBox _companyLogo = null!;
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private Button _signIn = null!;
    private Button _local = null!;
    private Button _google = null!;

    public event Action? LocalMasterRequested;
    public event Action? GoogleDriveRequested;
    public event Action<string, string>? SignInRequested;

    public SyncTarget SelectedTarget { get; private set; }

    public MasterWelcomeControl(AppData data)
    {
        _data = data;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Canvas;
        AutoScroll = true;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(44, 24, 44, 34)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // A 50 pt WinForms font needs more than a 70 px row at common Windows
        // DPI settings. Reserve the heading's full measured height so the
        // subtitle cannot overlap or clip the title.
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        shell.Controls.Add(BuildHeading(), 0, 0);
        shell.Controls.Add(BuildMasterOptions(), 0, 1);
        shell.Controls.Add(BuildSignInCard(), 0, 2);
        Controls.Add(shell);

        _local = (Button)Controls.Find("LocalMasterButton", true).Single();
        _google = (Button)Controls.Find("GoogleMasterButton", true).Single();
        _signIn = (Button)Controls.Find("MasterSignInButton", true).Single();

        _password.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter || !_signIn.Enabled) return;
            eventArgs.SuppressKeyPress = true;
            SignInRequested?.Invoke(_username.Text, _password.Text);
        };

        SelectInitialTarget();
        UiTheme.ApplyTheme(this);
    }

    private Control BuildHeading()
    {
        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var logoHost = new Panel
        {
            Size = new Size(124, 112),
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        _defaultLogo = new InNascBrandLogo(124, 112) { Dock = DockStyle.Fill };
        _companyLogo = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            Visible = false
        };
        _companyLogo.Disposed += (_, _) => _companyLogo.Image?.Dispose();
        logoHost.Controls.Add(_defaultLogo);
        logoHost.Controls.Add(_companyLogo);
        heading.Controls.Add(logoHost, 0, 0);

        _welcomeTitle.Name = "CompanyWelcomeTitle";
        _welcomeTitle.Text = "Welcome to InNasc";
        _welcomeTitle.Dock = DockStyle.Fill;
        _welcomeTitle.Font = UiTheme.Font(42, FontStyle.Bold);
        _welcomeTitle.ForeColor = UiTheme.Text;
        _welcomeTitle.TextAlign = ContentAlignment.MiddleCenter;
        _welcomeTitle.AutoEllipsis = true;
        _welcomeTitle.Margin = new Padding(0, 0, 0, 2);
        heading.Controls.Add(_welcomeTitle, 0, 1);

        _tier.Name = "CompanyTierLabel";
        _tier.Dock = DockStyle.Fill;
        _tier.Font = UiTheme.Font(10, FontStyle.Bold);
        _tier.ForeColor = UiTheme.Blue;
        _tier.TextAlign = ContentAlignment.MiddleCenter;
        _tier.Margin = Padding.Empty;
        _tier.Visible = false;
        heading.Controls.Add(_tier, 0, 2);

        _welcomeSubtitle.Text = "Choose a company workspace, then sign in once for this app session.";
        _welcomeSubtitle.Dock = DockStyle.Fill;
        _welcomeSubtitle.Font = UiTheme.Font(10.5f);
        _welcomeSubtitle.ForeColor = UiTheme.Muted;
        _welcomeSubtitle.TextAlign = ContentAlignment.MiddleCenter;
        _welcomeSubtitle.Margin = Padding.Empty;
        heading.Controls.Add(_welcomeSubtitle, 0, 3);
        return heading;
    }

    private Control BuildMasterOptions()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(22)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "COMPANY FILE",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);

        var choices = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var google = UiTheme.SecondaryButton("Google Drive company");
        google.Name = "GoogleMasterButton";
        google.AutoSize = false;
        google.Size = new Size(182, 40);
        google.Margin = new Padding(5, 0, 5, 0);
        google.Click += (_, _) =>
        {
            SelectTarget(SyncTarget.GoogleDrive);
            GoogleDriveRequested?.Invoke();
        };
        choices.Controls.Add(google);

        var local = UiTheme.PrimaryButton("Choose company .nasc");
        local.Name = "LocalMasterButton";
        local.AutoSize = false;
        local.Size = new Size(174, 40);
        local.Margin = new Padding(5, 0, 5, 0);
        local.Click += (_, _) =>
        {
            SelectTarget(SyncTarget.SharedFile);
            LocalMasterRequested?.Invoke();
        };
        choices.Controls.Add(local);
        layout.Controls.Add(choices, 0, 1);

        _selection.Dock = DockStyle.Fill;
        _selection.Font = UiTheme.Font(9.5f, FontStyle.Bold);
        _selection.ForeColor = UiTheme.Text;
        _selection.TextAlign = ContentAlignment.MiddleCenter;
        _selection.AutoEllipsis = true;
        _selection.Margin = new Padding(10, 0, 10, 0);
        layout.Controls.Add(_selection, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildSignInCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(24)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "SIGN IN TO COMPANY",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);

        var fields = new TableLayoutPanel
        {
            Size = new Size(680, 72),
            Anchor = AnchorStyles.None,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        fields.Controls.Add(CenteredFieldLabel("Username"), 0, 0);
        fields.Controls.Add(CenteredFieldLabel("Password"), 1, 0);
        MasterSignInForm.ConfigureBox(_username, 0, 0, 0);
        _username.Dock = DockStyle.Fill;
        _username.Margin = new Padding(8, 0, 8, 6);
        _username.TextAlign = HorizontalAlignment.Center;
        fields.Controls.Add(_username, 0, 1);
        MasterSignInForm.ConfigureBox(_password, 0, 0, 0);
        _password.Dock = DockStyle.Fill;
        _password.Margin = new Padding(8, 0, 8, 6);
        _password.UseSystemPasswordChar = true;
        _password.TextAlign = HorizontalAlignment.Center;
        fields.Controls.Add(_password, 1, 1);
        layout.Controls.Add(fields, 0, 1);

        var signIn = UiTheme.PrimaryButton("Sign in and open company");
        signIn.Name = "MasterSignInButton";
        signIn.AutoSize = false;
        signIn.Size = new Size(220, 42);
        signIn.Anchor = AnchorStyles.None;
        signIn.Click += (_, _) => SignInRequested?.Invoke(_username.Text, _password.Text);
        layout.Controls.Add(signIn, 0, 2);

        _message.Dock = DockStyle.Fill;
        _message.Font = UiTheme.Font(9.2f, FontStyle.Bold);
        _message.ForeColor = UiTheme.Muted;
        _message.TextAlign = ContentAlignment.TopCenter;
        _message.AutoEllipsis = true;
        _message.Margin = new Padding(10, 2, 10, 0);
        layout.Controls.Add(_message, 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    private static Label CenteredFieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = UiTheme.Font(9, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = Padding.Empty
    };

    public void RefreshState()
    {
        var localLinked = !string.IsNullOrWhiteSpace(_data.Settings.SharedMasterPath);
        var googleLinked = !string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId);
        CompanyFileSummary? summary = null;
        if (SelectedTarget == SyncTarget.GoogleDrive)
        {
            _selection.Text = googleLinked
                ? $"Selected: Google Drive  •  {_data.Settings.GoogleDriveShareLink}"
                : "Google Drive is not linked yet. Choose Google Drive online to connect it.";
            _signIn.Enabled = googleLinked;
            if (googleLinked &&
                !string.IsNullOrWhiteSpace(_data.ProjectName) &&
                (_data.MasterAccess.LicenseId != Guid.Empty ||
                 !string.IsNullOrWhiteSpace(_data.MasterAccess.LicenseName)))
            {
                summary = new CompanyFileSummary(
                    _data.ProjectName,
                    _data.MasterAccess.LicenseName,
                    _data.MasterAccess.DeviceLimit,
                    _data.MasterAccess.LicenseId != Guid.Empty);
            }
        }
        else
        {
            _selection.Text = localLinked
                ? $"Selected company file: {_data.Settings.SharedMasterPath}"
                : "Choose the .nasc file generated by InNasc Global Admin.";
            _signIn.Enabled = localLinked;
            if (localLinked && File.Exists(_data.Settings.SharedMasterPath))
            {
                try
                {
                    summary = PortableDataService.ReadCompanySummary(
                        _data.Settings.SharedMasterPath);
                }
                catch
                {
                    // Keep the selected path usable so sign-in can show the
                    // specific file/password error. Identity falls back to InNasc.
                }
            }
        }
        ShowCompanyIdentity(summary);
        _local.BackColor = SelectedTarget == SyncTarget.SharedFile ? UiTheme.Blue : UiTheme.Surface;
        _local.ForeColor = SelectedTarget == SyncTarget.SharedFile ? Color.White : UiTheme.Text;
        _google.BackColor = SelectedTarget == SyncTarget.GoogleDrive ? UiTheme.Blue : UiTheme.Surface;
        _google.ForeColor = SelectedTarget == SyncTarget.GoogleDrive ? Color.White : UiTheme.Text;
    }

    private void ShowCompanyIdentity(CompanyFileSummary? summary)
    {
        ShowCompanyLogo(summary?.CompanyLogoBase64);
        if (summary is null || string.IsNullOrWhiteSpace(summary.CompanyName))
        {
            _welcomeTitle.Text = "Welcome to InNasc";
            _tier.Text = string.Empty;
            _tier.Visible = false;
            _welcomeSubtitle.Text =
                "Choose a company workspace, then sign in once for this app session.";
            return;
        }

        _welcomeTitle.Text = $"Welcome to {summary.CompanyName}";
        _tier.Text = summary.IsLicensed
            ? $"{DeviceLimitPolicy.LimitText(summary.DeviceLimit).ToUpperInvariant()} DEVICE TIER"
            : "LEGACY COMPANY FILE  -  MIGRATE IN GLOBAL ADMIN";
        _tier.ForeColor = summary.IsLicensed ? UiTheme.Blue : UiTheme.Amber;
        _tier.Visible = true;
        _welcomeSubtitle.Text = summary.IsLicensed &&
                                !string.IsNullOrWhiteSpace(summary.LicenseName) &&
                                !string.Equals(
                                    summary.LicenseName,
                                    summary.CompanyName,
                                    StringComparison.OrdinalIgnoreCase)
            ? $"License: {summary.LicenseName}  -  Sign in to open this company workspace."
            : "Sign in to open this company workspace.";
    }

    private void ShowCompanyLogo(string? logoBase64)
    {
        var image = InNascCompanyEnvelopeMetadataService.DecodeLogo(logoBase64);
        var previous = _companyLogo.Image;
        _companyLogo.Image = image;
        _companyLogo.Visible = image is not null;
        _defaultLogo.Visible = image is null;
        previous?.Dispose();
    }

    public void SelectTarget(SyncTarget target)
    {
        SelectedTarget = target;
        _data.Settings.LastMasterTarget = target.ToString();
        _message.Text = string.Empty;
        RefreshState();
    }

    public void SetBusy(bool busy, string message)
    {
        _signIn.Enabled = !busy;
        _username.Enabled = !busy;
        _password.Enabled = !busy;
        if (busy || message.Length > 0)
        {
            _message.ForeColor = UiTheme.Muted;
            _message.Text = message;
        }
        if (!busy) RefreshState();
    }

    public void ShowError(string message)
    {
        _message.ForeColor = UiTheme.Red;
        _message.Text = message;
        _password.SelectAll();
        _password.Focus();
    }

    public void ResetForSignIn()
    {
        _username.Enabled = true;
        _password.Enabled = true;
        _password.Clear();
        _message.Text = string.Empty;
        _message.ForeColor = UiTheme.Muted;
        RefreshState();
        _username.Focus();
    }

    public void ClearCredentials()
    {
        _password.Clear();
        _username.Focus();
    }

    private void SelectInitialTarget()
    {
        var hasGoogle = !string.IsNullOrWhiteSpace(_data.Settings.GoogleDriveFileId);
        var hasLocal = !string.IsNullOrWhiteSpace(_data.Settings.SharedMasterPath);
        var target = hasLocal
            ? SyncTarget.SharedFile
            : hasGoogle
                ? SyncTarget.GoogleDrive
                : SyncTarget.SharedFile;
        SelectTarget(target);
    }
}
