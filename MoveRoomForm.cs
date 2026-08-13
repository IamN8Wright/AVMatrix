namespace AVMatrixStudio;

internal sealed record LocationDestination(
    ClientRecord Client,
    LocationRecord Location)
{
    public string SearchText => $"{Client.Name}|{Location.Name}";
}

internal sealed record RoomDragPayload(
    Guid RoomId,
    string RoomName,
    Guid SourceLocationId);

internal sealed class MoveRoomForm : Form
{
    private readonly List<LocationDestination> _destinations;
    private readonly TextBox _search = new();
    private readonly ListBox _locations = new();
    private readonly Button _moveButton = UiTheme.PrimaryButton("Move room");
    private readonly Label _resultCount = new();

    public LocationDestination? SelectedDestination =>
        _locations.SelectedItem as LocationDestination;

    public MoveRoomForm(
        RoomRecord room,
        IEnumerable<LocationDestination> destinations,
        Guid currentLocationId)
    {
        _destinations = destinations
            .Where(item => item.Location.Id != currentLocationId)
            .OrderBy(item => item.Client.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Location.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Text = "Move room";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(580, 460);
        Size = new Size(700, 570);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 20, 24, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = $"Move {room.Name}",
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(18, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0),
            Size = new Size(620, 34)
        });
        heading.Controls.Add(new Label
        {
            Text = room.Equipment.Count == 1
                ? "The room and its 1 equipment record will move together"
                : $"The room and its {room.Equipment.Count:N0} equipment records will move together",
            AutoSize = true,
            Font = UiTheme.Font(9),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 39)
        });

        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Search clients or locations…";
        _search.Font = UiTheme.Font(10);
        _search.Margin = new Padding(0, 0, 0, 10);
        _search.TextChanged += (_, _) => RefreshDestinations();

        _locations.Dock = DockStyle.Fill;
        _locations.DrawMode = DrawMode.OwnerDrawFixed;
        _locations.ItemHeight = 58;
        _locations.IntegralHeight = false;
        _locations.BorderStyle = BorderStyle.FixedSingle;
        _locations.BackColor = UiTheme.InputSurface;
        _locations.ForeColor = UiTheme.Text;
        _locations.Margin = Padding.Empty;
        _locations.DrawItem += Locations_DrawItem;
        _locations.SelectedIndexChanged += (_, _) =>
            _moveButton.Enabled = SelectedDestination is not null;
        _locations.DoubleClick += (_, _) =>
        {
            if (SelectedDestination is null) return;
            DialogResult = DialogResult.OK;
            Close();
        };

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _resultCount.Dock = DockStyle.Fill;
        _resultCount.TextAlign = ContentAlignment.MiddleLeft;
        _resultCount.ForeColor = UiTheme.Muted;

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _moveButton.Enabled = false;
        _moveButton.DialogResult = DialogResult.OK;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(_moveButton);
        actions.Controls.Add(cancel);
        footer.Controls.Add(_resultCount, 0, 0);
        footer.Controls.Add(actions, 1, 0);

        shell.Controls.Add(heading, 0, 0);
        shell.Controls.Add(_search, 0, 1);
        shell.Controls.Add(_locations, 0, 2);
        shell.Controls.Add(footer, 0, 3);
        Controls.Add(shell);
        AcceptButton = _moveButton;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
        RefreshDestinations();
    }

    private void RefreshDestinations()
    {
        var search = _search.Text.Trim();
        var filtered = search.Length == 0
            ? _destinations
            : _destinations.Where(item =>
                item.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();

        _locations.BeginUpdate();
        _locations.Items.Clear();
        foreach (var destination in filtered) _locations.Items.Add(destination);
        _locations.EndUpdate();
        if (_locations.Items.Count > 0) _locations.SelectedIndex = 0;
        _resultCount.Text = _locations.Items.Count == 1
            ? "1 available location"
            : $"{_locations.Items.Count:N0} available locations";
        _moveButton.Enabled = SelectedDestination is not null;
    }

    private void Locations_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _locations.Items.Count) return;
        var destination = (LocationDestination)_locations.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? UiTheme.Selection : UiTheme.InputSurface);
        e.Graphics.FillRectangle(background, e.Bounds);

        var locationBounds = new Rectangle(e.Bounds.Left + 16, e.Bounds.Top + 7, e.Bounds.Width - 32, 23);
        var clientBounds = new Rectangle(e.Bounds.Left + 16, e.Bounds.Top + 31, e.Bounds.Width - 32, 20);
        using var locationFont = UiTheme.Font(10, FontStyle.Bold);
        using var clientFont = UiTheme.Font(8.7f);
        TextRenderer.DrawText(e.Graphics, destination.Location.Name, locationFont, locationBounds, UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, destination.Client.Name, clientFont, clientBounds, UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}
