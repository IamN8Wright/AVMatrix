namespace InNasc;

internal sealed class ClientCheckoutSelectionForm : Form
{
    private readonly ListView _clients = new();

    public Guid? SelectedClientId { get; private set; }

    public ClientCheckoutSelectionForm(
        IReadOnlyList<ClientRecord> clients,
        IReadOnlyList<ClientCheckoutRecord> checkouts)
    {
        Text = "Check out a client";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 500);
        Size = new Size(900, 590);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 20, 24, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(MasterSignInForm.Heading("Choose a client sub-matrix", 0, 0));
        heading.Controls.Add(MasterSignInForm.Description(
            "Checkout pulls this client's complete device data and configuration-file contents. " +
            "Other technicians can still view its inventory, but cannot check it out without explicitly booting the holder.",
            2, 38, 790, 42));
        shell.Controls.Add(heading, 0, 0);

        _clients.Dock = DockStyle.Fill;
        _clients.View = View.Details;
        _clients.FullRowSelect = true;
        _clients.MultiSelect = false;
        _clients.HideSelection = false;
        _clients.Columns.Add("Client", 245);
        _clients.Columns.Add("Locations", 85);
        _clients.Columns.Add("Devices", 85);
        _clients.Columns.Add("Checkout status", 340);
        _clients.DoubleClick += (_, _) => SelectClient();
        var locks = checkouts.ToDictionary(checkout => checkout.ClientId);
        foreach (var client in clients.OrderBy(client => client.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(client.Name) { Tag = client.Id };
            item.SubItems.Add(client.Locations.Count.ToString("N0"));
            item.SubItems.Add(client.Locations.Sum(location =>
                location.Rooms.Sum(room => room.Equipment.Count)).ToString("N0"));
            if (locks.TryGetValue(client.Id, out var checkout))
            {
                var holder = string.IsNullOrWhiteSpace(checkout.DisplayName)
                    ? checkout.Username
                    : checkout.DisplayName;
                item.SubItems.Add(
                    $"Checked out by {holder} on {checkout.MachineName} since " +
                    checkout.CheckedOutUtc.ToLocalTime().ToString("g"));
                item.ForeColor = UiTheme.Amber;
            }
            else
            {
                item.SubItems.Add("Available");
            }
            _clients.Items.Add(item);
        }
        shell.Controls.Add(_clients, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var checkoutButton = UiTheme.PrimaryButton("Check out selected client");
        checkoutButton.Click += (_, _) => SelectClient();
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(checkoutButton);
        actions.Controls.Add(cancel);
        shell.Controls.Add(actions, 0, 2);
        Controls.Add(shell);
        AcceptButton = checkoutButton;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private void SelectClient()
    {
        if (_clients.SelectedItems.Count != 1) return;
        SelectedClientId = (Guid)_clients.SelectedItems[0].Tag!;
        DialogResult = DialogResult.OK;
        Close();
    }
}
