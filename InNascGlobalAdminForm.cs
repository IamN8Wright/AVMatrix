namespace InNasc;

internal sealed class InNascGlobalAdminForm : Form
{
    private readonly string _globalPath;
    private readonly InNascGlobalSession _session;
    private InNascGlobalCatalog _catalog;
    private readonly FlowLayoutPanel _companies = new();
    private readonly ListView _users = new();
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
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1000, 640);
        Size = new Size(1180, 720);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(22, 18, 22, 18)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(InNascGlobalSetupForm.TitleLabel("Global Admin", 0, 0, 20, true));
        header.Controls.Add(InNascGlobalSetupForm.Description(
            "Companies are .nasc files. Users sign in once, then choose from the companies assigned to them.",
            2, 40, 820, 30));
        shell.Controls.Add(header, 0, 0);
        shell.SetColumnSpan(header, 2);

        shell.Controls.Add(BuildCompanyPanel(), 0, 1);
        shell.Controls.Add(BuildUserPanel(), 1, 1);

        var footer = new Panel { Dock = DockStyle.Fill };
        _status.AutoSize = false;
        _status.Location = new Point(0, 18);
        _status.Size = new Size(760, 30);
        _status.ForeColor = UiTheme.Muted;
        _status.Text = "Global catalog loaded.";
        footer.Controls.Add(_status);
        var close = UiTheme.SecondaryButton("Close");
        close.AutoSize = false;
        close.Size = new Size(88, 36);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(1008, 10);
        close.DialogResult = DialogResult.OK;
        footer.Controls.Add(close);
        footer.Resize += (_, _) => close.Left = footer.ClientSize.Width - close.Width;
        shell.Controls.Add(footer, 0, 2);
        shell.SetColumnSpan(footer, 2);
        Controls.Add(shell);

        RefreshAll();
        UiTheme.ApplyTheme(this);
    }

    private Control BuildCompanyPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
        var title = InNascGlobalSetupForm.TitleLabel("Companies", 0, 4, 14, true);
        panel.Controls.Add(title);
        var add = UiTheme.PrimaryButton("＋ Create company");
        add.AutoSize = false;
        add.Size = new Size(148, 36);
        add.Location = new Point(0, 40);
        add.Click += (_, _) => CreateCompany();
        panel.Controls.Add(add);
        var migrate = UiTheme.SecondaryButton("Migrate .avmatrix…");
        migrate.AutoSize = false;
        migrate.Size = new Size(156, 36);
        migrate.Location = new Point(158, 40);
        migrate.Click += (_, _) => MigrateLegacyCompany();
        panel.Controls.Add(migrate);
        _companies.Location = new Point(0, 91);
        _companies.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _companies.Size = new Size(660, 430);
        _companies.AutoScroll = true;
        _companies.WrapContents = true;
        _companies.FlowDirection = FlowDirection.LeftToRight;
        panel.Controls.Add(_companies);
        panel.Resize += (_, _) => _companies.Size = new Size(panel.ClientSize.Width, panel.ClientSize.Height - 92);
        return panel;
    }

    private Control BuildUserPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 0, 0) };
        panel.Controls.Add(InNascGlobalSetupForm.TitleLabel("Users", 14, 4, 14, true));
        var add = UiTheme.PrimaryButton("＋ Add user");
        add.AutoSize = false;
        add.Size = new Size(112, 36);
        add.Location = new Point(14, 40);
        add.Click += (_, _) => AddUser();
        panel.Controls.Add(add);
        var access = UiTheme.SecondaryButton("Company access…");
        access.AutoSize = false;
        access.Size = new Size(138, 36);
        access.Location = new Point(134, 40);
        access.Click += (_, _) => EditAccess();
        panel.Controls.Add(access);
        var reset = UiTheme.SecondaryButton("Reset password");
        reset.AutoSize = false;
        reset.Size = new Size(126, 36);
        reset.Location = new Point(280, 40);
        reset.Click += (_, _) => ResetPassword();
        panel.Controls.Add(reset);

        _users.Location = new Point(14, 91);
        _users.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _users.Size = new Size(420, 430);
        _users.View = View.Details;
        _users.FullRowSelect = true;
        _users.MultiSelect = false;
        _users.HideSelection = false;
        _users.Columns.Add("User", 150);
        _users.Columns.Add("Role", 100);
        _users.Columns.Add("Companies", 90);
        panel.Controls.Add(_users);
        panel.Resize += (_, _) => _users.Size = new Size(panel.ClientSize.Width - 14, panel.ClientSize.Height - 92);
        return panel;
    }

    private void RefreshAll()
    {
        RefreshCompanies();
        RefreshUsers();
    }

    private void RefreshCompanies()
    {
        foreach (Control c in _companies.Controls) c.Dispose();
        _companies.Controls.Clear();
        foreach (var company in _catalog.Companies.Where(x => x.Enabled)
                     .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            _companies.Controls.Add(CompanyCard(company));
        if (_companies.Controls.Count == 0)
            _companies.Controls.Add(InNascGlobalSetupForm.Description(
                "No companies yet. Create the first company to produce its .nasc file.",
                10, 10, 500, 42));
    }

    private Control CompanyCard(InNascCompanyRecord company)
    {
        var card = new RoundedPanel
        {
            Size = new Size(292, 150),
            Margin = new Padding(0, 0, 14, 14),
            BackColor = UiTheme.Surface
        };
        card.Controls.Add(new Label
        {
            Text = company.Name,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(14, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(16, 15),
            Size = new Size(258, 32)
        });
        card.Controls.Add(new Label
        {
            Text = Path.GetFileName(company.FilePath),
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(8.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(16, 52),
            Size = new Size(258, 25)
        });
        var count = _catalog.Users.Count(user => user.IsGlobalAdmin ||
            user.Companies.Any(x => x.CompanyId == company.Id));
        card.Controls.Add(new Label
        {
            Text = $"{count:N0} user{(count == 1 ? string.Empty : "s")}",
            AutoSize = true,
            Font = UiTheme.Font(8.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(16, 82)
        });
        var sync = UiTheme.SecondaryButton("Sync users");
        sync.AutoSize = false;
        sync.Size = new Size(104, 34);
        sync.Location = new Point(16, 106);
        sync.Click += (_, _) => SyncCompany(company);
        card.Controls.Add(sync);
        return card;
    }

    private void RefreshUsers(Guid? selectId = null)
    {
        _users.BeginUpdate();
        _users.Items.Clear();
        foreach (var user in _catalog.Users.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var companies = user.IsGlobalAdmin
                ? "All"
                : user.Companies.Count(x => _catalog.Companies.Any(c => c.Id == x.CompanyId && c.Enabled)).ToString();
            var item = new ListViewItem(user.DisplayName);
            item.SubItems.Add(user.IsGlobalAdmin ? "Global Admin" : "User");
            item.SubItems.Add(companies);
            item.Tag = user.Id;
            _users.Items.Add(item);
            if (selectId == user.Id) item.Selected = true;
        }
        _users.EndUpdate();
    }

    private InNascGlobalUserRecord? SelectedUser()
    {
        if (_users.SelectedItems.Count == 0) return null;
        var id = (Guid)_users.SelectedItems[0].Tag!;
        return _catalog.Users.FirstOrDefault(x => x.Id == id);
    }

    private void CreateCompany()
    {
        using var form = new InNascCompanyCreateForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var company = InNascGlobalCoreService.CreateCompany(
                _globalPath, _catalog, _session, form.EnteredCompanyName, form.CompanyPath);
            _status.Text = $"Created {company.Name}: {company.FilePath}";
            RefreshAll();
        }
        catch (Exception exception) { ShowError("Create company", exception); }
    }

    private void AddUser()
    {
        using var form = new InNascGlobalUserEditorForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var user = InNascGlobalCoreService.AddUser(
                _globalPath, _catalog, _session,
                form.Username, form.DisplayName, form.Password, form.IsGlobalAdmin);
            InNascCompanyAccessSyncService.SyncAll(_catalog, _session);
            _status.Text = $"Added {user.DisplayName}. The password is stored only as a verifier and encrypted key wrapper.";
            RefreshAll();
        }
        catch (Exception exception) { ShowError("Add user", exception); }
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
            var companyName = InputDialog.Show(
                this,
                "Migrate legacy company",
                "Company name",
                suggestedName);
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
                _globalPath,
                _catalog,
                _session,
                companyName,
                sourceDialog.FileName,
                destinationDialog.FileName,
                legacyPasswordOrKey);
            _status.Text = $"Migrated {company.Name} to {company.FilePath}. The original .avmatrix file was not changed.";
            RefreshAll();
        }
        catch (Exception exception)
        {
            ShowError("Migrate legacy company", exception);
        }
    }

    private void EditAccess()
    {
        var user = SelectedUser();
        if (user is null) return;
        using var form = new InNascMembershipForm(user, _catalog);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            foreach (var choice in form.Choices)
                InNascGlobalCoreService.SetMembership(
                    _globalPath, _catalog, _session,
                    user.Id, choice.CompanyId, choice.Assigned, choice.Role);
            InNascCompanyAccessSyncService.SyncAll(_catalog, _session);
            _status.Text = $"Updated company access for {user.DisplayName}.";
            RefreshAll();
        }
        catch (Exception exception) { ShowError("Company access", exception); }
    }

    private void ResetPassword()
    {
        var user = SelectedUser();
        if (user is null) return;
        using var form = new InNascPasswordResetForm(user.Username);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            InNascGlobalCoreService.ResetPassword(
                _globalPath, _catalog, _session, user.Id, form.Password);
            _status.Text = $"Password reset for {user.DisplayName}. The previous password remains unrecoverable.";
        }
        catch (Exception exception) { ShowError("Reset password", exception); }
    }

    private void SyncCompany(InNascCompanyRecord company)
    {
        try
        {
            InNascCompanyAccessSyncService.SyncCompany(_catalog, _session, company);
            _status.Text = $"Synced company access into {company.Name}.";
        }
        catch (Exception exception) { ShowError("Sync company", exception); }
    }

    private void ShowError(string title, Exception exception)
    {
        _status.Text = exception.Message;
        MessageBox.Show(this, exception.Message, title,
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
