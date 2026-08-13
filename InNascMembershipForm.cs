namespace InNasc;

internal sealed record InNascMembershipChoice(Guid CompanyId, bool Assigned, MasterUserRole Role);

internal sealed class InNascCompanyMembershipRow : Panel
{
    private readonly CheckBox _assigned = new();
    private readonly ComboBox _role = new();

    public Guid CompanyId { get; }
    public bool Assigned => _assigned.Checked;
    public MasterUserRole Role => _role.SelectedItem is MasterUserRole role
        ? role
        : MasterUserRole.Tech;

    public InNascCompanyMembershipRow(
        InNascCompanyRecord company,
        bool assigned,
        MasterUserRole role,
        bool globalAdmin = false)
    {
        CompanyId = company.Id;
        Size = new Size(724, 62);
        Margin = new Padding(0, 0, 0, 10);
        BackColor = UiTheme.Surface;

        _assigned.Text = "Assigned";
        _assigned.AutoSize = true;
        _assigned.Checked = globalAdmin || assigned;
        _assigned.Location = new Point(16, 21);
        _assigned.ForeColor = UiTheme.Text;
        _assigned.CheckedChanged += (_, _) => RefreshRoleState();
        Controls.Add(_assigned);

        Controls.Add(new Label
        {
            Text = company.Name,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(10.5f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(124, 10),
            Size = new Size(380, 24),
            TextAlign = ContentAlignment.MiddleLeft
        });
        Controls.Add(new Label
        {
            Text = Path.GetFileName(company.FilePath),
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(8.25f),
            ForeColor = UiTheme.Muted,
            Location = new Point(124, 33),
            Size = new Size(380, 20),
            TextAlign = ContentAlignment.MiddleLeft
        });

        _role.DropDownStyle = ComboBoxStyle.DropDownList;
        _role.Location = new Point(548, 16);
        _role.Size = new Size(156, 30);
        _role.Font = UiTheme.Font(9.5f);
        foreach (var value in Enum.GetValues<MasterUserRole>()) _role.Items.Add(value);
        _role.SelectedItem = globalAdmin ? MasterUserRole.Owner : role;
        Controls.Add(_role);

        if (globalAdmin)
        {
            _assigned.Enabled = false;
            _role.Enabled = false;
        }
        else
        {
            RefreshRoleState();
        }

        UiTheme.ApplyTheme(this);
    }

    public InNascMembershipChoice Choice() => new(CompanyId, Assigned, Role);

    public void SetGlobalAdmin(bool globalAdmin)
    {
        if (globalAdmin)
        {
            _assigned.Checked = true;
            _assigned.Enabled = false;
            _role.SelectedItem = MasterUserRole.Owner;
            _role.Enabled = false;
        }
        else
        {
            _assigned.Enabled = true;
            RefreshRoleState();
        }
    }

    private void RefreshRoleState()
    {
        _role.Enabled = _assigned.Checked;
    }
}

internal sealed class InNascMembershipForm : Form
{
    private readonly InNascGlobalUserRecord _user;
    private readonly InNascGlobalCatalog _catalog;
    private readonly FlowLayoutPanel _companyRows = new();
    private readonly List<InNascCompanyMembershipRow> _rows = [];

    public List<InNascMembershipChoice> Choices { get; private set; } = [];

    public InNascMembershipForm(InNascGlobalUserRecord user, InNascGlobalCatalog catalog)
    {
        _user = user;
        _catalog = catalog;
        Text = $"Company access - {user.DisplayName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 500);
        Size = new Size(820, 560);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(24, 18, 24, 8),
            BackColor = UiTheme.Canvas
        };
        header.Controls.Add(InNascGlobalSetupForm.TitleLabel("Company access", 24, 14, 18, true));
        header.Controls.Add(InNascGlobalSetupForm.Description(
            user.IsGlobalAdmin
                ? "Global Admins automatically have Owner access to every company."
                : "Choose the companies this user can open and the role applied inside each company.",
            26, 50, 700, 28));
        Controls.Add(header);

        _companyRows.Dock = DockStyle.Fill;
        _companyRows.AutoScroll = true;
        _companyRows.FlowDirection = FlowDirection.TopDown;
        _companyRows.WrapContents = false;
        _companyRows.Padding = new Padding(24, 12, 24, 12);
        _companyRows.BackColor = UiTheme.Canvas;
        Controls.Add(_companyRows);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(12),
            BackColor = UiTheme.Canvas
        };
        var save = UiTheme.PrimaryButton("Save access");
        save.AutoSize = false;
        save.Size = new Size(106, 36);
        save.Click += (_, _) => SaveAndClose();
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(82, 36);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;

        Populate();
        UiTheme.ApplyTheme(this);
    }

    private void Populate()
    {
        var companies = _catalog.Companies
            .Where(x => x.Enabled)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (companies.Count == 0)
        {
            _companyRows.Controls.Add(InNascGlobalSetupForm.Description(
                "No companies are available in this Global catalog yet.",
                8, 8, 650, 34));
            return;
        }

        foreach (var company in companies)
        {
            var membership = _user.Companies.FirstOrDefault(x => x.CompanyId == company.Id);
            var row = new InNascCompanyMembershipRow(
                company,
                membership is not null,
                membership?.Role ?? MasterUserRole.Tech,
                _user.IsGlobalAdmin);
            _rows.Add(row);
            _companyRows.Controls.Add(row);
        }
    }

    private void SaveAndClose()
    {
        Choices = _rows.Select(row => row.Choice()).ToList();
        DialogResult = DialogResult.OK;
        Close();
    }
}
