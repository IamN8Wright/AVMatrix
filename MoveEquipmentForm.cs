namespace AVMatrixStudio;

internal sealed record RoomDestination(
    ClientRecord Client,
    LocationRecord Location,
    RoomRecord Room)
{
    public string SearchText => $"{Client.Name}|{Location.Name}|{Room.Name}";
}

internal sealed record EquipmentDragPayload(
    Guid[] EquipmentIds,
    string Description,
    Guid[] SourceRoomIds);

internal sealed class MoveEquipmentForm : Form
{
    private readonly List<RoomDestination> _destinations;
    private readonly TextBox _search = new();
    private readonly ListBox _rooms = new();
    private readonly Button _moveButton = UiTheme.PrimaryButton("Move equipment");
    private readonly Label _resultCount = new();

    public RoomDestination? SelectedDestination => _rooms.SelectedItem as RoomDestination;

    public MoveEquipmentForm(
        IReadOnlyCollection<string> equipmentNames,
        IEnumerable<RoomDestination> destinations,
        IEnumerable<Guid> currentRoomIds)
    {
        var sourceRooms = currentRoomIds.Distinct().ToHashSet();
        var onlySourceRoom = sourceRooms.Count == 1 ? sourceRooms.First() : (Guid?)null;
        _destinations = destinations
            .Where(item => item.Room.Id != onlySourceRoom)
            .OrderBy(item => item.Client.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Location.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Room.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Text = "Move equipment";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(600, 480);
        Size = new Size(720, 600);
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
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var heading = new Panel { Dock = DockStyle.Fill };
        var equipmentCount = equipmentNames.Count;
        heading.Controls.Add(new Label
        {
            Text = equipmentCount == 1
                ? $"Move {equipmentNames.FirstOrDefault() ?? "equipment"}"
                : $"Move {equipmentCount:N0} equipment records",
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiTheme.Font(18, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0),
            Size = new Size(640, 34)
        });
        heading.Controls.Add(new Label
        {
            Text = equipmentCount == 1
                ? "Choose a room in any client or location"
                : "The selected equipment will move together into one destination room",
            AutoSize = true,
            Font = UiTheme.Font(9),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 38)
        });

        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Search clients, locations, or rooms…";
        _search.Font = UiTheme.Font(10);
        _search.Margin = new Padding(0, 0, 0, 10);
        _search.TextChanged += (_, _) => RefreshDestinations();

        _rooms.Dock = DockStyle.Fill;
        _rooms.DrawMode = DrawMode.OwnerDrawFixed;
        _rooms.ItemHeight = 58;
        _rooms.IntegralHeight = false;
        _rooms.BorderStyle = BorderStyle.FixedSingle;
        _rooms.BackColor = UiTheme.InputSurface;
        _rooms.ForeColor = UiTheme.Text;
        _rooms.Margin = Padding.Empty;
        _rooms.DrawItem += Rooms_DrawItem;
        _rooms.SelectedIndexChanged += (_, _) => _moveButton.Enabled = SelectedDestination is not null;
        _rooms.DoubleClick += (_, _) =>
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
        _moveButton.Text = equipmentCount == 1 ? "Move equipment" : $"Move {equipmentCount:N0} items";
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
        shell.Controls.Add(_rooms, 0, 2);
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

        _rooms.BeginUpdate();
        _rooms.Items.Clear();
        foreach (var destination in filtered) _rooms.Items.Add(destination);
        _rooms.EndUpdate();
        if (_rooms.Items.Count > 0) _rooms.SelectedIndex = 0;
        _resultCount.Text = _rooms.Items.Count == 1
            ? "1 available room"
            : $"{_rooms.Items.Count:N0} available rooms";
        _moveButton.Enabled = SelectedDestination is not null;
    }

    private void Rooms_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _rooms.Items.Count) return;
        var destination = (RoomDestination)_rooms.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? UiTheme.Selection : UiTheme.InputSurface);
        e.Graphics.FillRectangle(background, e.Bounds);

        var roomBounds = new Rectangle(e.Bounds.Left + 16, e.Bounds.Top + 7, e.Bounds.Width - 32, 23);
        var pathBounds = new Rectangle(e.Bounds.Left + 16, e.Bounds.Top + 31, e.Bounds.Width - 32, 20);
        using var roomFont = UiTheme.Font(10, FontStyle.Bold);
        using var pathFont = UiTheme.Font(8.7f);
        TextRenderer.DrawText(e.Graphics, destination.Room.Name, roomFont, roomBounds, UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics,
            $"{destination.Client.Name}  ›  {destination.Location.Name}",
            pathFont,
            pathBounds,
            UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}
