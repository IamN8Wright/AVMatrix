namespace InNasc;

internal sealed class InNascGlobalAdminForm : Form
{
    private readonly string _globalPath;
    private readonly InNascGlobalSession _session;
    private readonly InNascGlobalCatalog _catalog;
    private readonly FlowLayoutPanel _companies = new();
    private readonly TextBox _search = new();
    private readonly Label _status = new();

    public InNascGlobalAdminForm(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session)
    {
        if (!session.IsGlobalAdmin)
            throw new MasterAuthorizationException("Global Admin access is required.");
        _globalPath = globalPath;
        _catalog = catalog;
        _session = session;
        Text = "InNasc Global Admin";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 680);
        Size = new Size(1260, 780);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(30, 22, 30, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        shell.Controls.Add(BuildHeader(), 0, 0);
        shell.Controls.Add(BuildSearch(), 0, 1);
        _companies.Dock = DockStyle.Fill;
        _companies.AutoScroll = true;
        _companies.FlowDirection = FlowDirection.LeftToRight;
        _companies.WrapContents = true;
        _companies.Padding = new Padding(0, 8, 0, 8);
        _companies.BackColor = UiTheme.Canvas;
        shell.Controls.Add(_companies, 0, 2);
        shell.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(shell);
        _search.TextChanged += (_, _) => RefreshCompanies();
        RefreshCompanies();
        UiTheme.ApplyTheme(this);
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new InNascBrandLogo(58, 58) { Location = new Point(0, 4) });
        panel.Controls.Add(InNascGlobalSetupForm.TitleLabel("Company Directory", 74, 2, 22, true));
        panel.Controls.Add(InNascGlobalSetupForm.Description(
            "Create companies, issue .nasc licenses, set device tiers, and manage company users.",
            76, 43, 650, 30));
        var admins = UiTheme.SecondaryButton("Global Admins");
        admins.AutoSize = false;
        admins.Size = new Size(130, 38);
        admins.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        admins.Location = new Point(790, 14);
        admins.Click += (_, _) => ManageGlobalAdmins();
        panel.Controls.Add(admins);
        var migrate = UiTheme.SecondaryButton("Migrate .avmatrix");
        migrate.AutoSize = false;
        migrate.Size = new Size(148, 38);
        migrate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        migrate.Location = new Point(930, 14);
        migrate.Click += (_, _) => MigrateLegacyCompany();
        panel.Controls.Add(migrate);
        var create = UiTheme.PrimaryButton("+ Create company");
        create.AutoSize = false;
        create.Size = new Size(144, 38);
        create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        create.Location = new Point(1088, 14);
        create.Click += (_, _) => CreateCompany();
        panel.Controls.Add(create);
        panel.Resize += (_, _) =>
        {
            create.Left = panel.ClientSize.Width - create.Width;
            migrate.Left = create.Left - migrate.Width - 10;
            admins.Left = migrate.Left - admins.Width - 10;
        };
        return panel;
    }

    private Control BuildSearch()
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 10) };
        var label = InNascGlobalSetupForm.TitleLabel("Search companies", 18, 8, 8.5f, true);
        card.Controls.Add(label);
        _search.Location = new Point(142, 10);
        _search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _search.Size = new Size(760, 30);
        _search.PlaceholderText = "Company name or .nasc license...";
        card.Controls.Add(_search);
        var summary = new Label
        {
            Name = "CompanySummary",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = UiTheme.Muted,
            Location = new Point(920, 8),
            Size = new Size(280, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        card.Controls.Add(summary);
        card.Resize += (_, _) =>
        {
            summary.Left = card.ClientSize.Width - summary.Width - 18;
            _search.Width = Math.Max(240, summary.Left - _search.Left - 18);
        };
        return card;
    }

    private Control BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        _status.AutoSize = false;
        _status.Location = new Point(0, 14);
        _status.Size = new Size(900, 28);
        _status.ForeColor = UiTheme.Muted;
        _status.Text = $"Signed in as {_session.DisplayName} - Global Admin accounts are separate from company users.";
        panel.Controls.Add(_status);
        var close = UiTheme.SecondaryButton("Close");
        close.AutoSize = false;
        close.Size = new Size(86, 36);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(1120, 7);
        close.DialogResult = DialogResult.OK;
        panel.Controls.Add(close);
        panel.Resize += (_, _) => close.Left = panel.ClientSize.Width - close.Width;
        return panel;
    }

    private void RefreshCompanies()
    {
        foreach (Control control in _companies.Controls) control.Dispose();
        _companies.Controls.Clear();
        var query = _search.Text.Trim();
        var companies = _catalog.Companies
            .Where(company => company.Enabled &&
                (query.Length == 0 || company.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 company.Files.Any(file => file.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           file.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var company in companies) _companies.Controls.Add(CompanyCard(company));
        if (companies.Count == 0)
        {
            _companies.Controls.Add(InNascGlobalSetupForm.Description(
                query.Length == 0
                    ? "No companies yet. Create the first company to issue its first .nasc license."
                    : "No companies match your search.",
                12, 12, 720, 50));
        }
        var summary = Controls.Find("CompanySummary", true).OfType<Label>().FirstOrDefault();
        if (summary is not null)
        {
            var files = _catalog.Companies.Where(company => company.Enabled).Sum(company => company.Files.Count(file => file.Enabled));
            summary.Text = $"{_catalog.Companies.Count(company => company.Enabled):N0} companies  -  {files:N0} .nasc licenses";
        }
    }

    private Control CompanyCard(InNascCompanyRecord company)
    {
        var card = new RoundedPanel
        {
            Size = new Size(350, 286),
            Margin = new Padding(0, 0, 18, 18),
            BackColor = UiTheme.Surface,
            Cursor = Cursors.Hand
        };
        var initials = new Label
        {
            Text = Initials(company.Name),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = UiTheme.LogoTile,
            ForeColor = UiTheme.Blue,
            Font = UiTheme.Font(16, FontStyle.Bold),
            Location = new Point(18, 18),
            Size = new Size(64, 64)
        };
        card.Controls.Add(initials);
        card.Controls.Add(new Label
        {
            Text = company.Name,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(14, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(98, 18),
            Size = new Size(230, 30)
        });
        card.Controls.Add(new Label
        {
            Text = $"{company.Files.Count(file => file.Enabled):N0} .nasc license(s)  -  {company.Users.Count(user => user.Enabled):N0} user(s)",
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(8.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(98, 51),
            Size = new Size(230, 26)
        });
        var counts = company.Files.Where(file => file.Enabled).Select(SafeDeviceCount).ToList();
        var totalDevices = counts.Sum();
        card.Controls.Add(new Label
        {
            Text = totalDevices.ToString("N0"),
            AutoSize = false,
            Font = UiTheme.Font(23, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(18, 105),
            Size = new Size(104, 42)
        });
        card.Controls.Add(new Label
        {
            Text = "DEVICES ACROSS LICENSES",
            AutoSize = false,
            Font = UiTheme.Font(7.5f, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(124, 118),
            Size = new Size(204, 24)
        });
        var tiers = string.Join("  |  ", company.Files.Where(file => file.Enabled)
            .Select(file => DeviceLimitPolicy.LimitText(file.DeviceLimit)).Distinct());
        card.Controls.Add(new Label
        {
            Text = tiers.Length == 0 ? "No active licenses" : tiers,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(9, FontStyle.Bold),
            ForeColor = UiTheme.Green,
            Location = new Point(18, 158),
            Size = new Size(310, 26)
        });
        var open = UiTheme.PrimaryButton("Open company");
        open.AutoSize = false;
        open.Size = new Size(136, 36);
        open.Location = new Point(194, 199);
        open.Click += (_, _) => OpenCompany(company);
        card.Controls.Add(open);
        var sync = UiTheme.SecondaryButton("Sync licenses");
        sync.AutoSize = false;
        sync.Size = new Size(158, 36);
        sync.Location = new Point(18, 199);
        sync.Click += (_, _) => SyncCompany(company);
        card.Controls.Add(sync);
        var delete = UiTheme.SecondaryButton("Delete company");
        delete.AutoSize = false;
        delete.Size = new Size(312, 34);
        delete.Location = new Point(18, 242);
        delete.ForeColor = UiTheme.Red;
        delete.Click += (_, _) => DeleteCompany(company);
        card.Controls.Add(delete);
        foreach (Control control in card.Controls.Cast<Control>().Where(control => control is not Button))
            control.Click += (_, _) => OpenCompany(company);
        card.Click += (_, _) => OpenCompany(company);
        return card;
    }

    private static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private static int SafeDeviceCount(InNascCompanyFileRecord file)
    {
        try { return InNascGlobalCoreService.GetDeviceCount(file); }
        catch { return 0; }
    }

    private void CreateCompany()
    {
        using var form = new InNascCompanyCreateForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var company = InNascGlobalCoreService.CreateCompany(
                _globalPath, _catalog, _session, form.EnteredCompanyName, form.CompanyPath, form.DeviceLimit);
            _status.Text = $"Created {company.Name} with a {DeviceLimitPolicy.LimitText(form.DeviceLimit)} tier.";
            RefreshCompanies();
            OpenCompany(company);
        }
        catch (Exception exception) { ShowError("Create company", exception); }
    }

    private void OpenCompany(InNascCompanyRecord company)
    {
        using var form = new InNascCompanyAdminForm(_globalPath, _catalog, _session, company);
        form.ShowDialog(this);
        RefreshCompanies();
    }

    private void SyncCompany(InNascCompanyRecord company)
    {
        try
        {
            InNascCompanyAccessSyncService.SyncCompany(_globalPath, _catalog, _session, company);
            _status.Text = $"Published {company.Users.Count(user => user.Enabled && user.CredentialReady):N0} users to {company.Name}'s active .nasc files.";
            RefreshCompanies();
        }
        catch (Exception exception) { ShowError("Sync company", exception); }
    }

    private void DeleteCompany(InNascCompanyRecord company)
    {
        var result = MessageBox.Show(this,
            $"Delete {company.Name} from Global Admin?\r\n\r\nIts {company.Files.Count:N0} physical .nasc file(s) will remain on disk and can be recovered. Company users and license grants will be removed from this catalog.",
            "Delete company", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;
        try
        {
            var retained = InNascGlobalCoreService.DeleteCompany(
                _globalPath, _catalog, _session, company.Id);
            _status.Text = $"Deleted {company.Name} from Global Admin. Retained {retained.Count:N0} .nasc file(s) on disk.";
            RefreshCompanies();
        }
        catch (Exception exception) { ShowError("Delete company", exception); }
    }

    private void ManageGlobalAdmins()
    {
        using var form = new InNascGlobalAdminUsersForm(_globalPath, _catalog, _session);
        form.ShowDialog(this);
    }

    private void MigrateLegacyCompany()
    {
        using var sourceDialog = new OpenFileDialog
        {
            Title = "Migrate legacy AV Matrix company",
            Filter = "Legacy AV Matrix company (*.avmatrix)|*.avmatrix|All files (*.*)|*.*"
        };
        if (sourceDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var sourceBytes = File.ReadAllBytes(sourceDialog.FileName);
            string? legacyPasswordOrKey = null;
            if (PortableDataService.IsAccountProtected(sourceBytes))
            {
                var access = PortableDataService.ReadMasterAccess(sourceBytes);
                var legacySession = MasterSignInForm.Prompt(this, access);
                if (legacySession is null) return;
                legacyPasswordOrKey = legacySession.MasterKey;
            }
            else if (PortableDataService.IsPasswordProtected(sourceBytes))
            {
                legacyPasswordOrKey = LegacyMasterMigrationForm.Prompt(this);
                if (legacyPasswordOrKey is null) return;
            }
            var suggestedName = Path.GetFileNameWithoutExtension(sourceDialog.FileName);
            var companyName = InputDialog.Show(this, "Migrate legacy company", "Company name", suggestedName);
            if (companyName is null) return;
            using var destinationDialog = new SaveFileDialog
            {
                Title = "Save migrated InNasc company",
                Filter = "InNasc company (*.nasc)|*.nasc",
                DefaultExt = "nasc",
                AddExtension = true,
                FileName = suggestedName + ".nasc",
                InitialDirectory = Path.GetDirectoryName(sourceDialog.FileName)
            };
            if (destinationDialog.ShowDialog(this) != DialogResult.OK) return;
            var company = InNascGlobalCoreService.MigrateLegacyCompany(
                _globalPath, _catalog, _session, companyName, sourceDialog.FileName,
                destinationDialog.FileName, legacyPasswordOrKey);
            _status.Text = $"Migrated {company.Name}. The original .avmatrix file was not changed; its first tier is Unlimited.";
            RefreshCompanies();
        }
        catch (Exception exception) { ShowError("Migrate legacy company", exception); }
    }

    private void ShowError(string title, Exception exception)
    {
        _status.Text = exception.Message;
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

internal sealed class InNascCompanyAdminForm : Form
{
    private readonly string _globalPath;
    private readonly InNascGlobalCatalog _catalog;
    private readonly InNascGlobalSession _session;
    private readonly InNascCompanyRecord _company;
    private readonly ListView _licenses = new();
    private readonly ListView _users = new();
    private readonly Label _summary = new();
    private readonly Label _status = new();

    public InNascCompanyAdminForm(string globalPath, InNascGlobalCatalog catalog,
        InNascGlobalSession session, InNascCompanyRecord company)
    {
        _globalPath = globalPath;
        _catalog = catalog;
        _session = session;
        _company = company;
        Text = $"{company.Name} - InNasc Global Admin";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1020, 650);
        Size = new Size(1180, 720);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(28, 22, 28, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 53));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        shell.Controls.Add(BuildHeader(), 0, 0);
        shell.Controls.Add(BuildLicenses(), 0, 1);
        shell.Controls.Add(BuildUsers(), 0, 2);
        shell.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(shell);
        RefreshAll();
        UiTheme.ApplyTheme(this);
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var tile = new Label
        {
            Text = string.Concat(_company.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0]))),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UiTheme.Font(17, FontStyle.Bold),
            BackColor = UiTheme.LogoTile,
            ForeColor = UiTheme.Blue,
            Location = new Point(0, 4),
            Size = new Size(66, 66)
        };
        panel.Controls.Add(tile);
        panel.Controls.Add(InNascGlobalSetupForm.TitleLabel(_company.Name, 82, 3, 22, true));
        _summary.Location = new Point(84, 44);
        _summary.Size = new Size(820, 30);
        _summary.ForeColor = UiTheme.Muted;
        panel.Controls.Add(_summary);
        var close = UiTheme.SecondaryButton("Back to companies");
        close.AutoSize = false;
        close.Size = new Size(144, 38);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(950, 15);
        close.DialogResult = DialogResult.OK;
        panel.Controls.Add(close);
        panel.Resize += (_, _) => close.Left = panel.ClientSize.Width - close.Width;
        return panel;
    }

    private Control BuildLicenses()
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        card.Controls.Add(InNascGlobalSetupForm.TitleLabel(".nasc licenses and device tiers", 18, 14, 13, true));
        var grant = Button("+ Grant .nasc", 18, GrantFile, true);
        var limit = Button("Change tier", 144, ChangeLimit);
        var sync = Button("Sync selected", 264, SyncSelectedFile);
        var remove = Button("Remove grant", 404, RemoveFile);
        card.Controls.AddRange([grant, limit, sync, remove]);
        ConfigureList(_licenses);
        _licenses.Location = new Point(18, 92);
        _licenses.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _licenses.Size = new Size(1040, 130);
        _licenses.Columns.Add("License", 190);
        _licenses.Columns.Add("Devices", 90);
        _licenses.Columns.Add("Tier", 130);
        _licenses.Columns.Add("File", 560);
        card.Controls.Add(_licenses);
        card.Resize += (_, _) => _licenses.Size = new Size(card.ClientSize.Width - 36, card.ClientSize.Height - 108);
        return card;
    }

    private Control BuildUsers()
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 14, 0, 0) };
        card.Controls.Add(InNascGlobalSetupForm.TitleLabel("Company users", 18, 14, 13, true));
        card.Controls.Add(InNascGlobalSetupForm.Description(
            "These accounts open this company's .nasc files only. They are separate from Global Admin accounts.",
            18, 42, 720, 26));
        var add = Button("+ Add user", 18, AddUser, true);
        var edit = Button("Edit access", 128, EditUser);
        var reset = Button("Reset password", 242, ResetUserPassword);
        var delete = Button("Delete user", 390, DeleteUser);
        add.Top = edit.Top = reset.Top = delete.Top = 72;
        card.Controls.AddRange([add, edit, reset, delete]);
        ConfigureList(_users);
        _users.Location = new Point(18, 118);
        _users.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _users.Size = new Size(1040, 140);
        _users.Columns.Add("Display name", 230);
        _users.Columns.Add("Username", 190);
        _users.Columns.Add("Access level", 160);
        _users.Columns.Add("Company login", 180);
        card.Controls.Add(_users);
        card.Resize += (_, _) => _users.Size = new Size(card.ClientSize.Width - 36, card.ClientSize.Height - 134);
        return card;
    }

    private Control BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        _status.Location = new Point(0, 14);
        _status.Size = new Size(960, 28);
        _status.ForeColor = UiTheme.Muted;
        panel.Controls.Add(_status);
        return panel;
    }

    private Button Button(string text, int left, Action action, bool primary = false)
    {
        var button = primary ? UiTheme.PrimaryButton(text) : UiTheme.SecondaryButton(text);
        button.AutoSize = false;
        button.Size = new Size(primary ? 110 : 128, 34);
        button.Location = new Point(left, 50);
        button.Click += (_, _) => action();
        return button;
    }

    private static void ConfigureList(ListView list)
    {
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.GridLines = true;
    }

    private void RefreshAll(Guid? licenseId = null, Guid? userId = null)
    {
        _licenses.BeginUpdate();
        _licenses.Items.Clear();
        var total = 0;
        foreach (var file in _company.Files.Where(file => file.Enabled).OrderBy(file => file.Name))
        {
            var devices = SafeDeviceCount(file);
            total += devices;
            var item = new ListViewItem(file.Name);
            item.SubItems.Add(devices.ToString("N0"));
            item.SubItems.Add(DeviceLimitPolicy.LimitText(file.DeviceLimit));
            item.SubItems.Add(file.FilePath);
            item.Tag = file.Id;
            _licenses.Items.Add(item);
            if (file.Id == licenseId) item.Selected = true;
        }
        _licenses.EndUpdate();
        _users.BeginUpdate();
        _users.Items.Clear();
        foreach (var user in _company.Users.Where(user => user.Enabled).OrderBy(user => user.DisplayName))
        {
            var item = new ListViewItem(user.DisplayName);
            item.SubItems.Add(user.Username);
            item.SubItems.Add(RoleText(user.Role));
            item.SubItems.Add(user.CredentialReady ? "Ready" : "Password reset needed");
            item.Tag = user.Id;
            if (!user.CredentialReady) item.ForeColor = UiTheme.Amber;
            _users.Items.Add(item);
            if (user.Id == userId) item.Selected = true;
        }
        _users.EndUpdate();
        _summary.Text = $"{_company.Files.Count(file => file.Enabled):N0} .nasc licenses  -  {_company.Users.Count(user => user.Enabled):N0} company users  -  {total:N0} total devices";
        _status.Text = "Select a license to change its tier, or select a company user to manage access.";
    }

    private InNascCompanyFileRecord? SelectedFile() => _licenses.SelectedItems.Count == 0
        ? null
        : _company.Files.FirstOrDefault(file => file.Id == (Guid)_licenses.SelectedItems[0].Tag!);

    private InNascCompanyUserRecord? SelectedUser() => _users.SelectedItems.Count == 0
        ? null
        : _company.Users.FirstOrDefault(user => user.Id == (Guid)_users.SelectedItems[0].Tag!);

    private static int SafeDeviceCount(InNascCompanyFileRecord file)
    {
        try { return InNascGlobalCoreService.GetDeviceCount(file); }
        catch { return 0; }
    }

    private void GrantFile()
    {
        using var form = new InNascCompanyFileForm(_company.Name);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Grant .nasc", () =>
        {
            var file = InNascGlobalCoreService.AddCompanyFile(
                _globalPath, _catalog, _session, _company.Id,
                form.FileName, form.CompanyPath, form.DeviceLimit);
            _status.Text = $"Granted {file.Name} with a {DeviceLimitPolicy.LimitText(file.DeviceLimit)} tier.";
            RefreshAll(file.Id);
        });
    }

    private void ChangeLimit()
    {
        var file = SelectedFile();
        if (file is null) { SelectMessage("Select a .nasc license first."); return; }
        var count = SafeDeviceCount(file);
        using var form = new InNascDeviceLimitForm(file.Name, file.DeviceLimit, count);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Change device tier", () =>
        {
            InNascGlobalCoreService.SetDeviceLimit(
                _globalPath, _catalog, _session, _company.Id, file.Id, form.DeviceLimit);
            InNascCompanyAccessSyncService.SyncFile(_globalPath, _catalog, _session, _company, file);
            _status.Text = $"Changed {file.Name} to {DeviceLimitPolicy.LimitText(file.DeviceLimit)}.";
            RefreshAll(file.Id);
        });
    }

    private void SyncSelectedFile()
    {
        var file = SelectedFile();
        if (file is null) { SelectMessage("Select a .nasc license first."); return; }
        Try("Sync .nasc", () =>
        {
            InNascCompanyAccessSyncService.SyncFile(_globalPath, _catalog, _session, _company, file);
            _status.Text = $"Published company users and the current device tier to {file.Name}.";
            RefreshAll(file.Id);
        });
    }

    private void RemoveFile()
    {
        var file = SelectedFile();
        if (file is null) { SelectMessage("Select a .nasc license first."); return; }
        if (MessageBox.Show(this,
                $"Remove the grant for {file.Name}?\r\n\r\nThe physical .nasc file will remain on disk.",
                "Remove .nasc grant", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Try("Remove .nasc grant", () =>
        {
            var retained = InNascGlobalCoreService.RemoveCompanyFile(
                _globalPath, _catalog, _session, _company.Id, file.Id);
            RefreshAll();
            _status.Text = $"Removed the license grant. File retained: {retained}";
        });
    }

    private void AddUser()
    {
        var first = !_company.Users.Any(user => user.Enabled && user.Role == MasterUserRole.Owner);
        using var form = new InNascCompanyUserEditorForm(firstUser: first);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Add company user", () =>
        {
            var user = InNascGlobalCoreService.AddCompanyUser(
                _globalPath, _catalog, _session, _company.Id,
                form.Username, form.DisplayName, form.Password, form.Role);
            SyncAllFiles();
            RefreshAll(userId: user.Id);
            _status.Text = $"Added {user.DisplayName} and published the login to all active .nasc files.";
        });
    }

    private void EditUser()
    {
        var user = SelectedUser();
        if (user is null) { SelectMessage("Select a company user first."); return; }
        using var form = new InNascCompanyUserEditorForm(user);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Edit company user", () =>
        {
            InNascGlobalCoreService.UpdateCompanyUser(
                _globalPath, _catalog, _session, _company.Id, user.Id, form.DisplayName, form.Role);
            SyncAllFiles();
            RefreshAll(userId: user.Id);
            _status.Text = $"Updated {user.DisplayName}'s company access.";
        });
    }

    private void ResetUserPassword()
    {
        var user = SelectedUser();
        if (user is null) { SelectMessage("Select a company user first."); return; }
        using var form = new InNascPasswordResetForm(user.Username);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Reset company password", () =>
        {
            InNascGlobalCoreService.ResetCompanyUserPassword(
                _globalPath, _catalog, _session, _company.Id, user.Id, form.Password);
            SyncAllFiles();
            RefreshAll(userId: user.Id);
            _status.Text = $"Reset {user.DisplayName}'s password and republished it to all active .nasc files.";
        });
    }

    private void DeleteUser()
    {
        var user = SelectedUser();
        if (user is null) { SelectMessage("Select a company user first."); return; }
        if (MessageBox.Show(this, $"Delete {user.DisplayName} from {_company.Name}?",
                "Delete company user", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Try("Delete company user", () =>
        {
            InNascGlobalCoreService.DeleteCompanyUser(
                _globalPath, _catalog, _session, _company.Id, user.Id);
            SyncAllFiles();
            RefreshAll();
            _status.Text = $"Deleted {user.DisplayName} and removed the login from active .nasc files.";
        });
    }

    private void SyncAllFiles() => InNascCompanyAccessSyncService.SyncCompany(
        _globalPath, _catalog, _session, _company);

    private void SelectMessage(string text) => MessageBox.Show(
        this, text, "InNasc Global Admin", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void Try(string title, Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string RoleText(MasterUserRole role) => role switch
    {
        MasterUserRole.Owner => "Company Owner",
        MasterUserRole.ReadOnly => "Read only",
        _ => "Technician"
    };
}

internal sealed class InNascGlobalAdminUsersForm : Form
{
    private readonly string _globalPath;
    private readonly InNascGlobalCatalog _catalog;
    private readonly InNascGlobalSession _session;
    private readonly ListView _admins = new();

    public InNascGlobalAdminUsersForm(string globalPath, InNascGlobalCatalog catalog, InNascGlobalSession session)
    {
        _globalPath = globalPath;
        _catalog = catalog;
        _session = session;
        Text = "Global Admin accounts";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 520);
        MinimumSize = new Size(680, 460);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(InNascGlobalSetupForm.TitleLabel("Global Admin accounts", 28, 22, 18, true));
        Controls.Add(InNascGlobalSetupForm.Description(
            "These accounts can open the .nascglobal catalog. They are completely separate from company users and receive no automatic .nasc access.",
            30, 58, 690, 48));
        var add = UiTheme.PrimaryButton("+ Add admin");
        add.Location = new Point(30, 116);
        add.Click += (_, _) => AddAdmin();
        Controls.Add(add);
        var reset = UiTheme.SecondaryButton("Reset password");
        reset.Location = new Point(150, 116);
        reset.Click += (_, _) => ResetPassword();
        Controls.Add(reset);
        var delete = UiTheme.SecondaryButton("Delete admin");
        delete.Location = new Point(294, 116);
        delete.Click += (_, _) => DeleteAdmin();
        Controls.Add(delete);
        _admins.Location = new Point(30, 170);
        _admins.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _admins.Size = new Size(684, 260);
        _admins.View = View.Details;
        _admins.FullRowSelect = true;
        _admins.MultiSelect = false;
        _admins.HideSelection = false;
        _admins.Columns.Add("Display name", 260);
        _admins.Columns.Add("Username", 220);
        _admins.Columns.Add("Status", 140);
        Controls.Add(_admins);
        var close = UiTheme.SecondaryButton("Close");
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Location = new Point(630, 444);
        close.DialogResult = DialogResult.OK;
        Controls.Add(close);
        RefreshAdmins();
        UiTheme.ApplyTheme(this);
    }

    private InNascGlobalAdminRecord? Selected() => _admins.SelectedItems.Count == 0
        ? null
        : _catalog.GlobalAdmins.FirstOrDefault(admin => admin.Id == (Guid)_admins.SelectedItems[0].Tag!);

    private void RefreshAdmins(Guid? selectedId = null)
    {
        _admins.Items.Clear();
        foreach (var admin in _catalog.GlobalAdmins.Where(admin => admin.Enabled).OrderBy(admin => admin.DisplayName))
        {
            var item = new ListViewItem(admin.DisplayName);
            item.SubItems.Add(admin.Username);
            item.SubItems.Add(admin.Id == _session.UserId ? "Signed in" : "Enabled");
            item.Tag = admin.Id;
            _admins.Items.Add(item);
            if (admin.Id == selectedId) item.Selected = true;
        }
    }

    private void AddAdmin()
    {
        using var form = new InNascGlobalAdminUserEditorForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Add Global Admin", () =>
        {
            var admin = InNascGlobalCoreService.AddGlobalAdmin(
                _globalPath, _catalog, _session, form.Username, form.DisplayName, form.Password);
            RefreshAdmins(admin.Id);
        });
    }

    private void ResetPassword()
    {
        var admin = Selected();
        if (admin is null) { SelectAdmin(); return; }
        using var form = new InNascPasswordResetForm(admin.Username);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Reset Global Admin password", () =>
        {
            InNascGlobalCoreService.ResetGlobalAdminPassword(
                _globalPath, _catalog, _session, admin.Id, form.Password);
            RefreshAdmins(admin.Id);
        });
    }

    private void DeleteAdmin()
    {
        var admin = Selected();
        if (admin is null) { SelectAdmin(); return; }
        if (MessageBox.Show(this, $"Delete Global Admin {admin.DisplayName}?",
                "Delete Global Admin", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Try("Delete Global Admin", () =>
        {
            InNascGlobalCoreService.DeleteGlobalAdmin(
                _globalPath, _catalog, _session, admin.Id);
            RefreshAdmins();
        });
    }

    private void SelectAdmin() => MessageBox.Show(this, "Select a Global Admin first.",
        "Global Admin accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void Try(string title, Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
