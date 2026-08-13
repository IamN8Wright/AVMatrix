namespace InNasc;

internal sealed class InNascStartupForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly Label _globalStatus = new();
    private readonly Label _error = new();
    private readonly Panel _loginPanel = new();
    private readonly Panel _companiesPanel = new();
    private readonly FlowLayoutPanel _cards = new();
    private readonly Label _welcome = new();
    private InNascGlobalConfig _config;
    private InNascGlobalSession? _session;
    private InNascGlobalCatalog? _catalog;

    public InNascCompanySelection? Selection { get; private set; }

    public InNascStartupForm()
    {
        _config = InNascGlobalConfigStore.Load();
        Text = "InNasc";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(940, 620);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(new InNascBrandLogo(76, 76)
        {
            Location = new Point(302, 22)
        });
        Controls.Add(new Label
        {
            Text = "InNasc",
            AutoSize = true,
            Font = UiTheme.Font(30, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(390, 28)
        });
        Controls.Add(new Label
        {
            Text = "SYSTEMS IN CONTEXT.",
            AutoSize = true,
            Font = UiTheme.Font(9, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(397, 84)
        });
        _globalStatus.Location = new Point(90, 120);
        _globalStatus.Size = new Size(760, 27);
        _globalStatus.TextAlign = ContentAlignment.MiddleCenter;
        _globalStatus.AutoEllipsis = true;
        _globalStatus.ForeColor = UiTheme.Muted;
        _globalStatus.Font = UiTheme.Font(8.5f);
        Controls.Add(_globalStatus);

        BuildLoginPanel();
        BuildCompanyPanel();
        Controls.Add(_loginPanel);
        Controls.Add(_companiesPanel);
        _companiesPanel.Visible = false;
        UpdateGlobalStatus();
        UiTheme.ApplyTheme(this);
        Shown += (_, _) => _username.Focus();
    }

    private void BuildLoginPanel()
    {
        _loginPanel.Location = new Point(252, 164);
        _loginPanel.Size = new Size(436, 420);
        var card = new RoundedPanel
        {
            Location = new Point(0, 0),
            Size = new Size(436, 326),
            BackColor = UiTheme.Surface
        };
        card.Controls.Add(InNascGlobalSetupForm.TitleLabel("Sign in", 24, 20, 20, true));
        card.Controls.Add(InNascGlobalSetupForm.Description(
            "Enter your username and password. InNasc will then show the companies assigned to your account.",
            26, 58, 384, 48));
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
        _error.Location = new Point(26, 248);
        _error.Size = new Size(250, 55);
        _error.ForeColor = UiTheme.Red;
        _error.Font = UiTheme.Font(8.5f);
        card.Controls.Add(_error);
        var signIn = UiTheme.PrimaryButton("Sign in");
        signIn.AutoSize = false;
        signIn.Size = new Size(116, 38);
        signIn.Location = new Point(294, 270);
        signIn.Click += (_, _) => SignIn();
        card.Controls.Add(signIn);
        _loginPanel.Controls.Add(card);
        AcceptButton = signIn;

        var choose = UiTheme.SecondaryButton("Choose Global…");
        choose.AutoSize = false;
        choose.Size = new Size(134, 36);
        choose.Location = new Point(76, 350);
        choose.Click += (_, _) => ChooseGlobal();
        _loginPanel.Controls.Add(choose);
        var create = UiTheme.SecondaryButton("Create Global…");
        create.AutoSize = false;
        create.Size = new Size(134, 36);
        create.Location = new Point(226, 350);
        create.Click += (_, _) => CreateGlobal();
        _loginPanel.Controls.Add(create);
    }

    private void BuildCompanyPanel()
    {
        _companiesPanel.Location = new Point(70, 164);
        _companiesPanel.Size = new Size(800, 430);
        _welcome.Location = new Point(0, 0);
        _welcome.Size = new Size(650, 42);
        _welcome.Font = UiTheme.Font(18, FontStyle.Bold);
        _welcome.ForeColor = UiTheme.Text;
        _companiesPanel.Controls.Add(_welcome);
        _companiesPanel.Controls.Add(InNascGlobalSetupForm.Description(
            "Choose a company workspace. Your assigned role and client permissions are applied when it opens.",
            2, 42, 650, 32));
        var signOut = UiTheme.SecondaryButton("Sign out");
        signOut.AutoSize = false;
        signOut.Size = new Size(90, 34);
        signOut.Location = new Point(708, 2);
        signOut.Click += (_, _) => SignOut();
        _companiesPanel.Controls.Add(signOut);
        _cards.Location = new Point(0, 84);
        _cards.Size = new Size(800, 340);
        _cards.AutoScroll = true;
        _cards.WrapContents = true;
        _cards.FlowDirection = FlowDirection.LeftToRight;
        _companiesPanel.Controls.Add(_cards);
    }

    private void ChooseGlobal()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose InNasc Global",
            Filter = InNascFileTypes.GlobalFilter
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _config.GlobalPath = Path.GetFullPath(dialog.FileName);
        _config.LastCompanyId = null;
        InNascGlobalConfigStore.Save(_config);
        _error.Text = string.Empty;
        UpdateGlobalStatus();
        _username.Focus();
    }

    private void CreateGlobal()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Create InNasc Global",
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
            _config.LastCompanyId = null;
            InNascGlobalConfigStore.Save(_config);
            _session = login.Session;
            _catalog = login.Catalog;
            InNascGlobalSessionContext.Set(_session, _config.GlobalPath);
            UpdateGlobalStatus();
            ShowCompanies();
        }
        catch (Exception exception) { _error.Text = exception.Message; }
    }

    private void SignIn()
    {
        _error.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(_config.GlobalPath) || !File.Exists(_config.GlobalPath))
        {
            _error.Text = "Choose or create an InNasc Global file first.";
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
            _session = login.Session;
            _catalog = login.Catalog;
            _password.Clear();
            InNascGlobalSessionContext.Set(_session, _config.GlobalPath);
            ShowCompanies();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
            _password.SelectAll();
            _password.Focus();
        }
    }

    private void ShowCompanies()
    {
        if (_session is null || _catalog is null) return;
        _loginPanel.Visible = false;
        _companiesPanel.Visible = true;
        _welcome.Text = $"Welcome, {_session.DisplayName}";
        RefreshCards();
    }

    private void RefreshCards()
    {
        if (_session is null || _catalog is null) return;
        foreach (Control control in _cards.Controls) control.Dispose();
        _cards.Controls.Clear();
        if (_session.IsGlobalAdmin) _cards.Controls.Add(AdminCard());
        foreach (var company in InNascGlobalCoreService.CompaniesFor(_catalog, _session))
            _cards.Controls.Add(CompanyCard(company));
        if (_cards.Controls.Count == 0)
            _cards.Controls.Add(InNascGlobalSetupForm.Description(
                "No companies are assigned to this account. Ask your Global Admin for access.",
                12, 12, 620, 44));
    }

    private Control AdminCard()
    {
        var card = BaseCard(UiTheme.NavySoft);
        card.Controls.Add(CardTitle("GLOBAL ADMIN", Color.White));
        card.Controls.Add(CardSubtitle("Companies & users", UiTheme.SidebarMuted));
        var open = UiTheme.PrimaryButton("Open Global Admin");
        open.AutoSize = false;
        open.Size = new Size(150, 36);
        open.Location = new Point(16, 94);
        open.Click += (_, _) => OpenAdmin();
        card.Controls.Add(open);
        return card;
    }

    private Control CompanyCard(InNascCompanyRecord company)
    {
        var card = BaseCard(UiTheme.Surface);
        card.Controls.Add(CardTitle(company.Name, UiTheme.Text));
        card.Controls.Add(CardSubtitle(Path.GetFileName(company.FilePath), UiTheme.Muted));
        var open = UiTheme.PrimaryButton("Open company");
        open.AutoSize = false;
        open.Size = new Size(124, 36);
        open.Location = new Point(16, 94);
        open.Click += (_, _) => ChooseCompany(company);
        card.Controls.Add(open);
        return card;
    }

    private static RoundedPanel BaseCard(Color backColor) => new()
    {
        Size = new Size(246, 144),
        Margin = new Padding(0, 0, 16, 16),
        BackColor = backColor
    };

    private static Label CardTitle(string text, Color color) => new()
    {
        Text = text,
        AutoSize = false,
        AutoEllipsis = true,
        Font = UiTheme.Font(13.5f, FontStyle.Bold),
        ForeColor = color,
        Location = new Point(16, 16),
        Size = new Size(214, 33)
    };

    private static Label CardSubtitle(string text, Color color) => new()
    {
        Text = text,
        AutoSize = false,
        AutoEllipsis = true,
        Font = UiTheme.Font(8.5f),
        ForeColor = color,
        Location = new Point(16, 54),
        Size = new Size(214, 26)
    };

    private void OpenAdmin()
    {
        if (_session is null || _catalog is null || !_session.IsGlobalAdmin) return;
        using var form = new InNascGlobalAdminForm(_config.GlobalPath, _catalog, _session);
        form.ShowDialog(this);
        try { _catalog = InNascGlobalCoreService.Load(_config.GlobalPath, _session); }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "InNasc Global",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshCards();
    }

    private void ChooseCompany(InNascCompanyRecord company)
    {
        if (_session is null || _catalog is null) return;
        try
        {
            if (!File.Exists(company.FilePath))
                throw new FileNotFoundException($"The company file for {company.Name} could not be found.", company.FilePath);
            var companySession = InNascGlobalCoreService.CreateCompanySession(_session, _catalog, company);
            Selection = new InNascCompanySelection(_session, company, companySession);
            _config.LastCompanyId = company.Id;
            InNascGlobalConfigStore.Save(_config);
            InNascGlobalSessionContext.SelectCompany(company.Id);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Open company",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SignOut()
    {
        _session = null;
        _catalog = null;
        Selection = null;
        InNascGlobalSessionContext.Clear();
        _username.Clear();
        _password.Clear();
        _companiesPanel.Visible = false;
        _loginPanel.Visible = true;
        _error.Text = string.Empty;
        _username.Focus();
    }

    private void UpdateGlobalStatus()
    {
        _globalStatus.Text = string.IsNullOrWhiteSpace(_config.GlobalPath)
            ? "No InNasc Global file selected"
            : File.Exists(_config.GlobalPath)
                ? $"Global: {_config.GlobalPath}"
                : $"Global file missing: {_config.GlobalPath}";
    }
}
