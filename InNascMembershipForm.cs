namespace InNasc;

internal sealed class InNascMembershipForm : Form
{
    private readonly InNascGlobalUserRecord _user;
    private readonly InNascGlobalCatalog _catalog;
    private readonly DataGridView _grid = new();

    public sealed record MembershipChoice(Guid CompanyId, bool Assigned, MasterUserRole Role);
    public List<MembershipChoice> Choices { get; private set; } = [];

    public InNascMembershipForm(InNascGlobalUserRecord user, InNascGlobalCatalog catalog)
    {
        _user = user;
        _catalog = catalog;
        Text = $"Company access - {user.DisplayName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 470);
        Size = new Size(820, 540);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var header = new Panel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(24, 18, 24, 8) };
        header.Controls.Add(InNascGlobalSetupForm.TitleLabel("Company access", 24, 14, 18, true));
        header.Controls.Add(InNascGlobalSetupForm.Description(
            user.IsGlobalAdmin
                ? "Global Admins automatically have Owner access to every company."
                : "Choose the companies this user can open and the role applied inside each company.",
            26, 50, 700, 28));
        Controls.Add(header);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = UiTheme.Canvas;
        _grid.BorderStyle = BorderStyle.None;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Assigned",
            HeaderText = "Assigned",
            Width = 78
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Company",
            HeaderText = "Company",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        var role = new DataGridViewComboBoxColumn
        {
            Name = "Role",
            HeaderText = "Role",
            Width = 140,
            DataSource = Enum.GetValues<MasterUserRole>()
        };
        _grid.Columns.Add(role);
        Controls.Add(_grid);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(12)
        };
        var save = UiTheme.PrimaryButton("Save access");
        save.Click += (_, _) => SaveAndClose();
        var cancel = UiTheme.SecondaryButton("Cancel");
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
        foreach (var company in _catalog.Companies.Where(x => x.Enabled)
                     .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var membership = _user.Companies.FirstOrDefault(x => x.CompanyId == company.Id);
            var row = _grid.Rows[_grid.Rows.Add(
                _user.IsGlobalAdmin || membership is not null,
                company.Name,
                _user.IsGlobalAdmin ? MasterUserRole.Owner : membership?.Role ?? MasterUserRole.Tech)];
            row.Tag = company.Id;
            if (_user.IsGlobalAdmin)
            {
                row.Cells["Assigned"].ReadOnly = true;
                row.Cells["Role"].ReadOnly = true;
            }
        }
    }

    private void SaveAndClose()
    {
        _grid.EndEdit();
        Choices = _grid.Rows.Cast<DataGridViewRow>().Select(row =>
        {
            var companyId = (Guid)row.Tag!;
            var assigned = row.Cells["Assigned"].Value is true;
            var role = row.Cells["Role"].Value is MasterUserRole typed
                ? typed
                : Enum.TryParse<MasterUserRole>(Convert.ToString(row.Cells["Role"].Value), out var parsed)
                    ? parsed
                    : MasterUserRole.Tech;
            return new MembershipChoice(companyId, assigned, role);
        }).ToList();
        DialogResult = DialogResult.OK;
        Close();
    }
}
