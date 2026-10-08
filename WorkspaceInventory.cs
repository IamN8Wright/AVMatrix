namespace InNasc;

// UI objects survive asynchronous sync; inventory object instances do not.
internal static class WorkspaceInventory
{
    public static object? Resolve(AppData data, object? selected) => selected switch
    {
        ClientRecord client => data.Clients.FirstOrDefault(item => item.Id == client.Id),
        LocationRecord location => data.Clients.SelectMany(item => item.Locations)
            .FirstOrDefault(item => item.Id == location.Id),
        RoomRecord room => data.Clients.SelectMany(item => item.Locations)
            .SelectMany(item => item.Rooms).FirstOrDefault(item => item.Id == room.Id),
        _ => null
    };

    public static IEnumerable<EquipmentContext> Scope(
        IEnumerable<EquipmentContext> contexts, object? selected) => selected switch
    {
        ClientRecord client => contexts.Where(item => item.Client.Id == client.Id),
        LocationRecord location => contexts.Where(item => item.Location.Id == location.Id),
        RoomRecord room => contexts.Where(item => item.Room.Id == room.Id),
        _ => contexts
    };

    public static string CountSummary(int visible, int scoped, int company) =>
        $"Showing {visible:N0} of {scoped:N0} devices in this view • Company total: {company:N0} devices";
}
