using System.Reflection;

namespace InNasc;

internal static class WorkspaceCloneService
{
    public static EquipmentRecord CloneEquipment(EquipmentRecord source)
    {
        source.EnsureNetworkInterfaces();
        var now = DateTime.UtcNow;
        var clone = new EquipmentRecord
        {
            Description = source.Description,
            Manufacturer = source.Manufacturer,
            PartNumber = source.PartNumber,
            EquipmentId = source.EquipmentId,
            Hostname = source.Hostname,
            SerialNumber = source.SerialNumber,
            Firmware = source.Firmware,
            Subnet = source.Subnet,
            Gateway = source.Gateway,
            SerialConnection = source.SerialConnection,
            Username = source.Username,
            Password = source.Password,
            Notes = source.Notes,
            SourceFile = source.SourceFile,
            CreatedUtc = now,
            UpdatedUtc = now,
            NetworkInterfaces = source.NetworkInterfaces.Select(CloneInterface).ToList(),
            ConfigurationFiles = (source.ConfigurationFiles ?? [])
                .Select(CloneConfigurationFile)
                .ToList()
        };
        clone.EnsureNetworkInterfaces();
        clone.ResetNetworkVerification();
        return clone;
    }

    public static RoomRecord CloneRoom(RoomRecord source, string? name = null) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? source.Name : name.Trim(),
        Notes = source.Notes,
        Equipment = source.Equipment.Select(CloneEquipment).ToList()
    };

    public static IReadOnlyList<string> MissingConfigurationPayloads(IEnumerable<EquipmentRecord> equipment) =>
        equipment
            .SelectMany(device => (device.ConfigurationFiles ?? [])
                .Where(file => !file.ContentIncluded)
                .Select(file => $"{device.Description}: {file.FileName}"))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static NetworkInterfaceRecord CloneInterface(NetworkInterfaceRecord source) => new()
    {
        Type = source.Type,
        IpAddress = source.IpAddress,
        MacAddress = source.MacAddress,
        NetworkState = string.IsNullOrWhiteSpace(source.IpAddress)
            ? NetworkState.NoAddress
            : NetworkState.Unknown
    };

    private static DeviceConfigurationFile CloneConfigurationFile(DeviceConfigurationFile source) => new()
    {
        FileName = source.FileName,
        ContentType = source.ContentType,
        SizeBytes = source.SizeBytes,
        Sha256 = source.Sha256,
        ContentBase64 = source.ContentBase64,
        ContentIncluded = source.ContentIncluded,
        Notes = source.Notes,
        AddedBy = source.AddedBy,
        AddedUtc = source.AddedUtc
    };
}

internal sealed class WorkspaceCopyPasteFeature : IDisposable, IMessageFilter
{
    private const int WmRButtonUp = 0x0205;
    private readonly MainForm _main;
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly TreeView _tree;
    private readonly DataGridView _grid;
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _editorEnhancementTimer = new() { Interval = 450 };
    private readonly HashSet<ComboBox> _enhancedInterfacePickers = [];
    private readonly FieldInfo? _expandedEquipmentIdsField;
    private readonly FieldInfo? _gridSingleClickTimerField;
    private readonly FieldInfo? _pendingGridSingleClickField;
    private readonly FieldInfo? _statusLabelField;
    private readonly MethodInfo? _ensureWorkspaceWritableMethod;
    private readonly MethodInfo? _refreshTreeMethod;
    private readonly MethodInfo? _refreshGridMethod;
    private readonly MethodInfo? _refreshManufacturerFilterMethod;
    private readonly MethodInfo? _refreshSyncIndicatorMethod;
    private readonly MethodInfo? _selectEquipmentRowsMethod;
    private readonly MethodInfo? _moveSelectedRoomMethod;
    private readonly MethodInfo? _renameSelectedContainerMethod;
    private readonly MethodInfo? _deleteSelectedContainerMethod;
    private EquipmentClipboard? _equipmentClipboard;
    private RoomClipboard? _roomClipboard;
    private Guid? _chevronMouseDownEquipmentId;
    private bool _disposed;

    private WorkspaceCopyPasteFeature(MainForm main, AppData data, DataStore store)
    {
        _main = main;
        _data = data;
        _store = store;
        _tree = FindControl<TreeView>(_main, tree => tree.AllowDrop)
            ?? throw new InvalidOperationException("The InNasc room tree could not be found.");
        _grid = FindControl<DataGridView>(_main, grid => grid.Columns.Contains("Description"))
            ?? throw new InvalidOperationException("The InNasc equipment grid could not be found.");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainForm);
        _expandedEquipmentIdsField = type.GetField("_expandedEquipmentIds", flags);
        _gridSingleClickTimerField = type.GetField("_gridSingleClickTimer", flags);
        _pendingGridSingleClickField = type.GetField("_pendingGridSingleClick", flags);
        _statusLabelField = type.GetField("_statusLabel", flags);
        _ensureWorkspaceWritableMethod = type.GetMethod("EnsureWorkspaceWritable", flags);
        _refreshTreeMethod = type.GetMethod("RefreshTree", flags);
        _refreshGridMethod = type.GetMethod("RefreshGrid", flags);
        _refreshManufacturerFilterMethod = type.GetMethod("RefreshManufacturerFilter", flags);
        _refreshSyncIndicatorMethod = type.GetMethod("RefreshSyncIndicator", flags);
        _selectEquipmentRowsMethod = type.GetMethod("SelectEquipmentRows", flags);
        _moveSelectedRoomMethod = type.GetMethod("MoveSelectedRoom", flags);
        _renameSelectedContainerMethod = type.GetMethod("RenameSelectedContainer", flags);
        _deleteSelectedContainerMethod = type.GetMethod("DeleteSelectedContainer", flags);

        _main.KeyPreview = true;
        _main.KeyDown += Main_KeyDown;
        _grid.CellPainting += Grid_CellPainting;
        _grid.CellMouseDown += Grid_CellMouseDown;
        _grid.CellClick += Grid_CellClick;
        EnhanceEquipmentContextMenu();
        Application.AddMessageFilter(this);

        _editorEnhancementTimer.Tick += (_, _) => EnhanceOpenEquipmentEditors();
        _editorEnhancementTimer.Start();
    }

    public static WorkspaceCopyPasteFeature Attach(MainForm main, AppData data, DataStore store) =>
        new(main, data, store);

    public bool PreFilterMessage(ref Message m)
    {
        if (_disposed || m.Msg != WmRButtonUp || m.HWnd != _tree.Handle) return false;
        var packed = m.LParam.ToInt64();
        var point = new Point(unchecked((short)(packed & 0xFFFF)), unchecked((short)((packed >> 16) & 0xFFFF)));
        var node = _tree.GetNodeAt(point);
        if (node?.Tag is RoomRecord room)
        {
            _tree.SelectedNode = node;
            ShowRoomMenu(room, point);
            return true;
        }
        if (node?.Tag is LocationRecord location && _roomClipboard is not null)
        {
            _tree.SelectedNode = node;
            ShowLocationMenu(location, point);
            return true;
        }
        return false;
    }

    private void EnhanceEquipmentContextMenu()
    {
        var menu = _grid.ContextMenuStrip;
        if (menu is null) return;
        var duplicate = menu.Items.Cast<ToolStripItem>()
            .FirstOrDefault(item => string.Equals(item.Text, "Duplicate", StringComparison.OrdinalIgnoreCase));
        var insertAt = duplicate is null ? Math.Max(0, menu.Items.Count - 2) : menu.Items.IndexOf(duplicate);
        if (duplicate is not null) menu.Items.Remove(duplicate);

        var copy = new ToolStripMenuItem("Copy") { ShortcutKeyDisplayString = "Ctrl+C" };
        copy.Click += (_, _) => CopySelectedEquipment();
        var copyTo = new ToolStripMenuItem("Copy to room…");
        copyTo.Click += (_, _) => CopySelectedEquipmentToRoom();
        var duplicateHere = new ToolStripMenuItem("Duplicate in this room");
        duplicateHere.Click += (_, _) => DuplicateSelectedEquipmentInPlace();

        menu.Items.Insert(insertAt++, copy);
        menu.Items.Insert(insertAt++, copyTo);
        menu.Items.Insert(insertAt, duplicateHere);
        menu.Opening += (_, _) =>
        {
            var count = SelectedEquipmentContexts().Count;
            copy.Enabled = count > 0;
            copyTo.Enabled = count > 0;
            duplicateHere.Enabled = count > 0;
            copy.Text = count > 1 ? $"Copy {count:N0} devices" : "Copy";
            copyTo.Text = count > 1 ? $"Copy {count:N0} devices to room…" : "Copy to room…";
            duplicateHere.Text = count > 1
                ? $"Duplicate {count:N0} devices in this room"
                : "Duplicate in this room";
        };
    }

    private void Main_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            if (_grid.ContainsFocus && SelectedEquipmentContexts().Count > 0)
                CopySelectedEquipment();
            else if (_tree.ContainsFocus && _tree.SelectedNode?.Tag is RoomRecord room)
                CopyRoom(room);
            else
                return;
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }
        if (!e.Control || e.KeyCode != Keys.V) return;
        if (_tree.SelectedNode?.Tag is RoomRecord targetRoom && _equipmentClipboard is not null)
            PasteEquipment(targetRoom);
        else if (_roomClipboard is not null && ResolvePasteLocation() is { } targetLocation)
            PasteRoom(targetLocation);
        else
            return;
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private void ShowRoomMenu(RoomRecord room, Point point)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Clone room in this location…", null, (_, _) => CloneRoomInPlace(room));
        var copyRoom = menu.Items.Add("Copy room", null, (_, _) => CopyRoom(room));
        copyRoom.ShortcutKeyDisplayString = "Ctrl+C";
        if (_roomClipboard is not null)
            menu.Items.Add("Paste copied room into this location", null, (_, _) =>
            {
                if (FindLocation(room) is { } location) PasteRoom(location);
            });
        if (_equipmentClipboard is not null)
            menu.Items.Add(
                _equipmentClipboard.Items.Count == 1
                    ? "Paste copied device into this room"
                    : $"Paste {_equipmentClipboard.Items.Count:N0} copied devices into this room",
                null,
                (_, _) => PasteEquipment(room));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Move room…", null, (_, _) => _moveSelectedRoomMethod?.Invoke(_main, null));
        menu.Items.Add("Rename", null, (_, _) => _renameSelectedContainerMethod?.Invoke(_main, null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => _deleteSelectedContainerMethod?.Invoke(_main, null));
        menu.Show(_tree, point);
    }

    private void ShowLocationMenu(LocationRecord location, Point point)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Paste copied room here", null, (_, _) => PasteRoom(location));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Rename", null, (_, _) => _renameSelectedContainerMethod?.Invoke(_main, null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => _deleteSelectedContainerMethod?.Invoke(_main, null));
        menu.Show(_tree, point);
    }

    private void CopySelectedEquipment()
    {
        var selected = SelectedEquipmentContexts();
        if (selected.Count == 0) return;
        var clientIds = selected.Select(item => item.Client.Id).Distinct().ToList();
        if (clientIds.Count != 1)
        {
            MessageBox.Show(_main, "Copy equipment from one client at a time.", "Copy equipment",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!EnsureConfigurationPayloadsAvailable(selected.Select(item => item.Equipment))) return;
        _equipmentClipboard = new EquipmentClipboard(
            clientIds[0],
            selected.Select(item => WorkspaceCloneService.CloneEquipment(item.Equipment)).ToList());
        SetStatus(selected.Count == 1
            ? $"Copied {selected[0].Equipment.Description}. Choose a room and press Ctrl+V, or use Copy to room."
            : $"Copied {selected.Count:N0} devices. Choose a room and press Ctrl+V, or use Copy to room.");
    }

    private void CopySelectedEquipmentToRoom()
    {
        var selected = SelectedEquipmentContexts();
        if (selected.Count == 0) return;
        var sourceClient = selected[0].Client;
        if (selected.Any(item => item.Client.Id != sourceClient.Id))
        {
            MessageBox.Show(_main, "Copy equipment from one client at a time.", "Copy equipment",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!EnsureConfigurationPayloadsAvailable(selected.Select(item => item.Equipment))) return;
        var destinations = sourceClient.Locations
            .SelectMany(location => location.Rooms.Select(room => new RoomDestination(sourceClient, location, room)))
            .ToList();
        if (destinations.Count == 0) return;
        using var picker = new CopyEquipmentDestinationForm(selected.Count, destinations);
        if (picker.ShowDialog(_main) != DialogResult.OK || picker.SelectedDestination is not { } destination) return;
        if (!EnsureWritable(sourceClient) || !EnsureCapacity(selected.Count)) return;
        var clones = selected.Select(item => WorkspaceCloneService.CloneEquipment(item.Equipment)).ToList();
        destination.Room.Equipment.AddRange(clones);
        SaveAndRefresh(destination.Room.Id, clones.Select(item => item.Id));
        SetStatus(clones.Count == 1
            ? $"Copied {clones[0].Description} to {destination.Location.Name} › {destination.Room.Name}"
            : $"Copied {clones.Count:N0} devices to {destination.Location.Name} › {destination.Room.Name}");
    }

    private void DuplicateSelectedEquipmentInPlace()
    {
        var selected = SelectedEquipmentContexts();
        if (selected.Count == 0) return;
        var client = selected[0].Client;
        if (selected.Any(item => item.Client.Id != client.Id)) return;
        if (!EnsureWritable(client) || !EnsureConfigurationPayloadsAvailable(selected.Select(item => item.Equipment)) ||
            !EnsureCapacity(selected.Count)) return;
        var added = new List<EquipmentRecord>();
        foreach (var item in selected)
        {
            var clone = WorkspaceCloneService.CloneEquipment(item.Equipment);
            item.Room.Equipment.Add(clone);
            added.Add(clone);
        }
        SaveAndRefresh(selected[0].Room.Id, added.Select(item => item.Id));
        SetStatus(added.Count == 1
            ? $"Duplicated {added[0].Description} with the same documented IP/MAC values and new internal IDs."
            : $"Duplicated {added.Count:N0} devices with the same documented IP/MAC values and new internal IDs.");
    }

    private void PasteEquipment(RoomRecord targetRoom)
    {
        if (_equipmentClipboard is null) return;
        var targetClient = FindClient(targetRoom);
        if (targetClient is null) return;
        if (targetClient.Id != _equipmentClipboard.ClientId)
        {
            MessageBox.Show(_main,
                "For safety, copied equipment can currently be pasted only within the same Client Card. " +
                "Use Move to room for an intentional cross-client transfer.",
                "Paste equipment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!EnsureWritable(targetClient) || !EnsureCapacity(_equipmentClipboard.Items.Count)) return;
        var clones = _equipmentClipboard.Items.Select(WorkspaceCloneService.CloneEquipment).ToList();
        targetRoom.Equipment.AddRange(clones);
        SaveAndRefresh(targetRoom.Id, clones.Select(item => item.Id));
        SetStatus(clones.Count == 1
            ? $"Pasted {clones[0].Description} into {targetRoom.Name}."
            : $"Pasted {clones.Count:N0} devices into {targetRoom.Name}.");
    }

    private void CopyRoom(RoomRecord room)
    {
        var client = FindClient(room);
        if (client is null) return;
        if (!EnsureConfigurationPayloadsAvailable(room.Equipment)) return;
        _roomClipboard = new RoomClipboard(client.Id, WorkspaceCloneService.CloneRoom(room));
        SetStatus($"Copied room {room.Name} with {room.Equipment.Count:N0} device(s). Select a location and press Ctrl+V.");
    }

    private void CloneRoomInPlace(RoomRecord room)
    {
        var location = FindLocation(room);
        var client = location is null ? null : FindClient(location);
        if (location is null || client is null) return;
        if (!EnsureWritable(client) || !EnsureConfigurationPayloadsAvailable(room.Equipment) ||
            !EnsureCapacity(room.Equipment.Count)) return;
        var defaultName = UniqueRoomName(location, room.Name);
        var name = InputDialog.Show(_main, "Clone room", "Name for the cloned room", defaultName);
        if (name is null) return;
        var clone = WorkspaceCloneService.CloneRoom(room, name);
        location.Rooms.Add(clone);
        SaveAndRefresh(clone.Id, clone.Equipment.Select(item => item.Id));
        SetStatus($"Cloned {room.Name} as {clone.Name}. IP/MAC values were preserved; internal record IDs are new.");
    }

    private void PasteRoom(LocationRecord targetLocation)
    {
        if (_roomClipboard is null) return;
        var targetClient = FindClient(targetLocation);
        if (targetClient is null) return;
        if (targetClient.Id != _roomClipboard.ClientId)
        {
            MessageBox.Show(_main,
                "For safety, copied rooms can currently be pasted only within the same Client Card.",
                "Paste room", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!EnsureWritable(targetClient) || !EnsureCapacity(_roomClipboard.Template.Equipment.Count)) return;
        var defaultName = UniqueRoomName(targetLocation, _roomClipboard.Template.Name);
        var name = InputDialog.Show(_main, "Paste room", "Name for the pasted room", defaultName);
        if (name is null) return;
        var clone = WorkspaceCloneService.CloneRoom(_roomClipboard.Template, name);
        targetLocation.Rooms.Add(clone);
        SaveAndRefresh(clone.Id, clone.Equipment.Select(item => item.Id));
        SetStatus($"Pasted room {clone.Name} with {_roomClipboard.Template.Equipment.Count:N0} device(s).");
    }

    private LocationRecord? ResolvePasteLocation() => _tree.SelectedNode?.Tag switch
    {
        LocationRecord location => location,
        RoomRecord room => FindLocation(room),
        _ => null
    };

    private bool EnsureConfigurationPayloadsAvailable(IEnumerable<EquipmentRecord> equipment)
    {
        var missing = WorkspaceCloneService.MissingConfigurationPayloads(equipment);
        if (missing.Count == 0) return true;
        var preview = string.Join("\r\n", missing.Take(5).Select(item => $"• {item}"));
        if (missing.Count > 5) preview += $"\r\n• and {missing.Count - 5:N0} more";
        MessageBox.Show(_main,
            "One or more attached configuration files are not downloaded on this PC. " +
            "Check out this client first so InNasc can make an exact copy instead of a broken metadata-only copy.\r\n\r\n" + preview,
            "Check out client before copying", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private bool EnsureCapacity(int additionalDevices)
    {
        try
        {
            DeviceLimitPolicy.RequireCapacity(_data.MasterAccess, _data, additionalDevices);
            return true;
        }
        catch (DeviceLimitExceededException exception)
        {
            MessageBox.Show(_main, exception.Message, "Device limit reached",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
    }

    private bool EnsureWritable(ClientRecord client)
    {
        try
        {
            return _ensureWorkspaceWritableMethod?.Invoke(_main, [client]) as bool? ?? true;
        }
        catch (TargetInvocationException exception)
        {
            MessageBox.Show(_main, exception.InnerException?.Message ?? exception.Message,
                "Workspace access", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void SaveAndRefresh(Guid? treeSelection, IEnumerable<Guid>? equipmentSelection = null)
    {
        try
        {
            _store.Save(_data);
            _refreshTreeMethod?.Invoke(_main, [treeSelection]);
            _refreshManufacturerFilterMethod?.Invoke(_main, null);
            _refreshGridMethod?.Invoke(_main, [null]);
            if (equipmentSelection is not null)
                _selectEquipmentRowsMethod?.Invoke(_main, [equipmentSelection]);
            _refreshSyncIndicatorMethod?.Invoke(_main, null);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static string UniqueRoomName(LocationRecord location, string sourceName)
    {
        var baseName = sourceName.Trim();
        var candidate = $"{baseName} Copy";
        if (!location.Rooms.Any(room => string.Equals(room.Name, candidate, StringComparison.CurrentCultureIgnoreCase)))
            return candidate;
        for (var number = 2; ; number++)
        {
            candidate = $"{baseName} Copy {number}";
            if (!location.Rooms.Any(room => string.Equals(room.Name, candidate, StringComparison.CurrentCultureIgnoreCase)))
                return candidate;
        }
    }

    private List<EquipmentContext> SelectedEquipmentContexts()
    {
        var selected = _grid.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Selected)
            .Select(row => EquipmentContextFromTag(row.Tag))
            .Where(item => item is not null)
            .Select(item => item!)
            .DistinctBy(item => item.Equipment.Id)
            .ToList();
        if (selected.Count == 0 && EquipmentContextFromTag(_grid.CurrentRow?.Tag) is { } current)
            selected.Add(current);
        return selected;
    }

    private static EquipmentContext? EquipmentContextFromTag(object? tag) => tag switch
    {
        EquipmentContext context => context,
        NetworkInterfaceContext interfaceContext => interfaceContext.EquipmentContext,
        _ => null
    };

    private ClientRecord? FindClient(RoomRecord room) =>
        _data.Clients.FirstOrDefault(client => client.Locations.Any(location => location.Rooms.Contains(room)));

    private ClientRecord? FindClient(LocationRecord location) =>
        _data.Clients.FirstOrDefault(client => client.Locations.Contains(location));

    private LocationRecord? FindLocation(RoomRecord room) =>
        _data.Clients.SelectMany(client => client.Locations)
            .FirstOrDefault(location => location.Rooms.Contains(room));

    private void Grid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        _chevronMouseDownEquipmentId = null;
        if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0 || e.X > 42 ||
            _grid.Columns[e.ColumnIndex].Name != "Description" ||
            _grid.Rows[e.RowIndex].Tag is not EquipmentContext context ||
            !HasInterfaceDetails(context.Equipment))
            return;
        _chevronMouseDownEquipmentId = context.Equipment.Id;
    }

    private void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (_chevronMouseDownEquipmentId is not { } id || e.RowIndex < 0 ||
            _grid.Rows[e.RowIndex].Tag is not EquipmentContext context || context.Equipment.Id != id)
            return;
        _chevronMouseDownEquipmentId = null;
        if (_gridSingleClickTimerField?.GetValue(_main) is System.Windows.Forms.Timer timer) timer.Stop();
        _pendingGridSingleClickField?.SetValue(_main, null);
        ToggleInterfaces(context.Equipment.Id);
    }

    private void ToggleInterfaces(Guid equipmentId)
    {
        if (_expandedEquipmentIdsField?.GetValue(_main) is not HashSet<Guid> expanded) return;
        if (!expanded.Remove(equipmentId)) expanded.Add(equipmentId);
        _refreshGridMethod?.Invoke(_main, [equipmentId]);
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
            _grid.Columns[e.ColumnIndex].Name != "Description" ||
            _grid.Rows[e.RowIndex].Tag is not EquipmentContext context ||
            !HasInterfaceDetails(context.Equipment))
            return;
        e.PaintBackground(e.CellBounds, true);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        var expanded = _expandedEquipmentIdsField?.GetValue(_main) is HashSet<Guid> ids && ids.Contains(context.Equipment.Id);
        var hit = new Rectangle(e.CellBounds.Left + 5, e.CellBounds.Top + 6, 30, Math.Max(24, e.CellBounds.Height - 12));
        using var fill = new SolidBrush(UiTheme.HeaderSurface);
        using var border = new Pen(UiTheme.Border);
        e.Graphics.FillRectangle(fill, hit);
        e.Graphics.DrawRectangle(border, hit);
        TextRenderer.DrawText(e.Graphics, expanded ? "▼" : "▶", UiTheme.Font(9.5f, FontStyle.Bold), hit,
            UiTheme.Blue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var textBounds = new Rectangle(hit.Right + 7, e.CellBounds.Top, Math.Max(0, e.CellBounds.Right - hit.Right - 12), e.CellBounds.Height);
        var foreground = (e.State & DataGridViewElementStates.Selected) != 0
            ? e.CellStyle.SelectionForeColor
            : e.CellStyle.ForeColor;
        TextRenderer.DrawText(e.Graphics, context.Equipment.Description, e.CellStyle.Font ?? _grid.Font,
            textBounds, foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.Handled = true;
    }

    private static bool HasInterfaceDetails(EquipmentRecord equipment)
    {
        equipment.EnsureNetworkInterfaces();
        return equipment.NetworkInterfaces.Any(item =>
            !string.IsNullOrWhiteSpace(item.IpAddress) || !string.IsNullOrWhiteSpace(item.MacAddress));
    }

    private void EnhanceOpenEquipmentEditors()
    {
        foreach (var editor in Application.OpenForms.Cast<Form>().Where(form => form is EquipmentEditorForm))
        foreach (var picker in Descendants(editor).OfType<ComboBox>().Where(IsInterfaceTypePicker))
        {
            if (!_enhancedInterfacePickers.Add(picker)) continue;
            picker.Font = UiTheme.Font(10.8f);
            picker.Width = Math.Max(picker.Width, 140);
            picker.DropDownWidth = Math.Max(picker.DropDownWidth, 190);
            picker.IntegralHeight = false;
            picker.DropDownHeight = 240;
            picker.MaxDropDownItems = 10;
            _toolTip.SetToolTip(picker, "Interface type. Click anywhere in this larger field to open the list.");
        }
    }

    private static bool IsInterfaceTypePicker(ComboBox picker) =>
        picker.Items.Cast<object>().Any(item => item is NetworkInterfaceType);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static T? FindControl<T>(Control root, Func<T, bool> predicate) where T : Control
    {
        foreach (var control in Descendants(root).OfType<T>())
            if (predicate(control)) return control;
        return null;
    }

    private void SetStatus(string text)
    {
        if (_statusLabelField?.GetValue(_main) is Label label) label.Text = text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Application.RemoveMessageFilter(this);
        _editorEnhancementTimer.Stop();
        _editorEnhancementTimer.Dispose();
        _toolTip.Dispose();
        _main.KeyDown -= Main_KeyDown;
        _grid.CellPainting -= Grid_CellPainting;
        _grid.CellMouseDown -= Grid_CellMouseDown;
        _grid.CellClick -= Grid_CellClick;
    }

    private sealed record EquipmentClipboard(Guid ClientId, List<EquipmentRecord> Items);
    private sealed record RoomClipboard(Guid ClientId, RoomRecord Template);
}

internal sealed class CopyEquipmentDestinationForm : Form
{
    private readonly List<RoomDestination> _destinations;
    private readonly TextBox _search = new();
    private readonly ListBox _rooms = new();
    private readonly Button _copy = UiTheme.PrimaryButton("Copy here");

    public RoomDestination? SelectedDestination => (_rooms.SelectedItem as Choice)?.Destination;

    public CopyEquipmentDestinationForm(int equipmentCount, IEnumerable<RoomDestination> destinations)
    {
        _destinations = destinations.OrderBy(item => item.Location.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Room.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Text = "Copy equipment to room";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(580, 460);
        Size = new Size(680, 560);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(24, 20, 24, 18) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        shell.Controls.Add(new Label
        {
            Text = equipmentCount == 1 ? "Copy device to room" : $"Copy {equipmentCount:N0} devices to room",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(18, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Search locations or rooms…";
        _search.Font = UiTheme.Font(10);
        _search.TextChanged += (_, _) => RefreshDestinations();
        shell.Controls.Add(_search, 0, 1);
        _rooms.Dock = DockStyle.Fill;
        _rooms.Font = UiTheme.Font(10);
        _rooms.IntegralHeight = false;
        _rooms.SelectedIndexChanged += (_, _) => _copy.Enabled = SelectedDestination is not null;
        _rooms.DoubleClick += (_, _) => { if (SelectedDestination is not null) { DialogResult = DialogResult.OK; Close(); } };
        shell.Controls.Add(_rooms, 0, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
        _copy.Enabled = false;
        _copy.DialogResult = DialogResult.OK;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(_copy);
        footer.Controls.Add(cancel);
        shell.Controls.Add(footer, 0, 3);
        Controls.Add(shell);
        AcceptButton = _copy;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
        RefreshDestinations();
    }

    private void RefreshDestinations()
    {
        var term = _search.Text.Trim();
        var filtered = term.Length == 0 ? _destinations : _destinations.Where(item =>
            $"{item.Location.Name}|{item.Room.Name}".Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _rooms.BeginUpdate();
        _rooms.Items.Clear();
        foreach (var destination in filtered) _rooms.Items.Add(new Choice(destination));
        _rooms.EndUpdate();
        if (_rooms.Items.Count > 0) _rooms.SelectedIndex = 0;
    }

    private sealed record Choice(RoomDestination Destination)
    {
        public override string ToString() => $"{Destination.Location.Name}  ›  {Destination.Room.Name}";
    }
}
