namespace AVMatrixStudio;

internal sealed class DeleteClientForm : Form
{
    private readonly ComboBox _clientPicker = new();

    public ClientRecord? SelectedClient => (_clientPicker.SelectedItem as ClientChoice)?.Client;

    public DeleteClientForm(IEnumerable<ClientRecord> clients)
    {
        Text = "Delete client";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 250);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = "Choose the client to delete",
            AutoSize = true,
            Font = UiTheme.Font(17, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        heading.Controls.Add(new Label
        {
            Text = "You will be asked to confirm before anything is removed.",
            AutoSize = true,
            Font = UiTheme.Font(9),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 37)
        });

        var field = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 15, 18, 12),
            Margin = Padding.Empty
        };
        field.Controls.Add(new Label
        {
            Text = "CLIENT",
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(18, 14)
        });
        _clientPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        _clientPicker.Font = UiTheme.Font(10);
        _clientPicker.Location = new Point(18, 39);
        _clientPicker.Width = 414;
        UiTheme.ConfigureUniformComboBox(_clientPicker);
        foreach (var client in clients.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            _clientPicker.Items.Add(new ClientChoice(client));
        if (_clientPicker.Items.Count > 0) _clientPicker.SelectedIndex = 0;
        field.Controls.Add(_clientPicker);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var select = UiTheme.DangerButton("Continue");
        select.AutoSize = false;
        select.Size = new Size(104, 36);
        select.DialogResult = DialogResult.OK;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(82, 36);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(select);
        buttons.Controls.Add(cancel);

        AcceptButton = select;
        CancelButton = cancel;
        shell.Controls.Add(heading, 0, 0);
        shell.Controls.Add(field, 0, 1);
        shell.Controls.Add(buttons, 0, 2);
        Controls.Add(shell);
        UiTheme.ApplyTheme(this);
    }

    private sealed record ClientChoice(ClientRecord Client)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Client.Address)
            ? Client.Name
            : $"{Client.Name}  •  {Client.Address}";
    }
}
