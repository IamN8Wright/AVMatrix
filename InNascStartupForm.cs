namespace InNasc;

internal sealed class InNascStartupForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly Label _globalStatus = new();
    private readonly Label _error = new();
    private InNascGlobalConfig _config;

    public InNascGlobalAdminSelection? Selection { get; private set; }

    public InNascStartupForm()
    {
        _config = InNascGlobalConfigStore.Load();
        Text = "InNasc Global Admin";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(680, 610);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(new InNascBrandLogo(82, 82) { Location = new Point(174, 26) });
        Controls.Add(new Label
        {
            Text = "InNasc Global Admin",
            AutoSize = true,
            Font = UiTheme.Font(25, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(266, 35)
        });
        Controls.Add(new Label
        {
            Text = "CREATE COMPANIES. PUBLISH ACCESS.",
            AutoSize = true,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(270, 82)
        });

        _globalStatus.Location = new Point(50, 126);
        _globalStatus.Size = new Size(580, 28);
        _globalStatus.TextAlign = ContentAlignment.MiddleCenter;
        _globalStatus.AutoEllipsis = true;
        _globalStatus.ForeColor = UiTheme.Muted;
        _globalStatus.Font = UiTheme.Font(8.5f);
        Controls.Add(_globalStatus);

        var card = new RoundedPanel
        {
            Location = new Point(122, 170),
            Size = new Size(436, 318),
            BackColor = UiTheme.Surface
        };
        card.Controls.Add(InNascGlobalSetupForm.TitleLabel("Global Admin sign in", 24, 20, 19, true));
        card.Controls.Add(InNascGlobalSetupForm.Description(
            "Open the encrypted .nascglobal catalog used to generate company .nasc files and publish company logins.",
            26, 58, 384, 52));
        card.Controls.Add(InNascGlobalSetupForm.TitleLabel("Username", 26, 121, 8.5f, true));
        _username.Location = new Point(26, 143);
        _username.Size = new Size(384, 30);
        _username.Font = UiTheme.Font(10.5f);
        card.Controls.Add(_username);
        card.Controls.Add(InNascGlobalSetupForm.TitleLabel("Password", 26, 190, 8.5f, true));
        _password.Location = new Point(26, 212);
        _password.Size = new Size(384, 30);
        _password.Font = UiTheme.Font(10.5f);
        card.Controls.Add(_password);
        _error.Location = new Point(26, 250);
        _error.Size = new Size(254, 52);
        _error.ForeColor = UiTheme.Red;
        _error.Font = UiTheme.Font(8.5f);
        card.Controls.Add(_error);
        var signIn = UiTheme.PrimaryButton("Open Admin");
        signIn.AutoSize = false;
        signIn.Size = new Size(116, 38);
        signIn.Location = new Point(294, 266);
        signIn.Click += (_, _) => SignIn();
        card.Controls.Add(signIn);
        Controls.Add(card);

        var choose = UiTheme.SecondaryButton("Choose Global…");
        choose.AutoSize = false;
        choose.Size = new Size(142, 38);
        choose.Location = new Point(187, 518);
        choose.Click += (_, _) => ChooseGlobal();
        Controls.Add(choose);
        var create = UiTheme.SecondaryButton("Create Global…");
        create.AutoSize = false;
        create.Size = new Size(142, 38);
        create.Location = new Point(351, 518);
        create.Click += (_, _) => CreateGlobal();
        Controls.Add(create);

        AcceptButton = signIn;
        UpdateGlobalStatus();
        UiTheme.ApplyTheme(this);
        Shown += (_, _) => _username.Focus();
    }

    private void ChooseGlobal()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose InNasc Global catalog",
            Filter = InNascFileTypes.GlobalFilter
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _config.GlobalPath = Path.GetFullPath(dialog.FileName);
        InNascGlobalConfigStore.Save(_config);
        _error.Text = string.Empty;
        UpdateGlobalStatus();
        _username.Focus();
    }

    private void CreateGlobal()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Create InNasc Global catalog",
            Filter = "InNasc Global (*.nascglobal)|*.nascglobal",
            DefaultExt = "nascglobal",
            AddExtension = true,
            FileName = "InNasc-Global.nascglobal"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var setup = new InNascGlobalSetupForm();
        if (setup.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var login = InNascGlobalCoreService.Create(
                dialog.FileName, setup.Username, setup.DisplayName, setup.Password);
            _config.GlobalPath = Path.GetFullPath(dialog.FileName);
            InNascGlobalConfigStore.Save(_config);
            Selection = new InNascGlobalAdminSelection(
                _config.GlobalPath, login.Session, login.Catalog);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }

    private void SignIn()
    {
        _error.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(_config.GlobalPath) || !File.Exists(_config.GlobalPath))
        {
            _error.Text = "Choose or create an InNasc Global catalog first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrEmpty(_password.Text))
        {
            _error.Text = "Enter both a username and password.";
            return;
        }
        try
        {
            var login = InNascGlobalCoreService.SignIn(
                _config.GlobalPath, _username.Text, _password.Text);
            if (!login.Session.IsGlobalAdmin)
                throw new MasterAuthorizationException(
                    "This application is for Global Admin accounts only.");
            Selection = new InNascGlobalAdminSelection(
                _config.GlobalPath, login.Session, login.Catalog);
            _password.Clear();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
            _password.SelectAll();
            _password.Focus();
        }
    }

    private void UpdateGlobalStatus()
    {
        _globalStatus.Text = string.IsNullOrWhiteSpace(_config.GlobalPath)
            ? "No .nascglobal catalog selected"
            : File.Exists(_config.GlobalPath)
                ? $"Catalog: {_config.GlobalPath}"
                : $"Catalog missing: {_config.GlobalPath}";
    }
}
