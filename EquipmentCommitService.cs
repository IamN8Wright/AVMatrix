namespace InNasc;

internal static class EquipmentCommitService
{
    public static RoomRecord? FindCurrentRoom(AppData data, Guid roomId) =>
        data.Clients
            .SelectMany(client => client.Locations)
            .SelectMany(location => location.Rooms)
            .FirstOrDefault(room => room.Id == roomId);

    public static ClientRecord? FindCurrentClientForRoom(AppData data, Guid roomId) =>
        data.Clients.FirstOrDefault(client =>
            client.Locations.Any(location => location.Rooms.Any(room => room.Id == roomId)));

    public static EquipmentRecord? FindCurrentEquipment(AppData data, Guid equipmentId) =>
        data.Clients
            .SelectMany(client => client.Locations)
            .SelectMany(location => location.Rooms)
            .SelectMany(room => room.Equipment)
            .FirstOrDefault(equipment => equipment.Id == equipmentId);

    public static bool TryAddToCurrentRoom(
        AppData data,
        Guid roomId,
        EquipmentRecord equipment,
        out RoomRecord? currentRoom)
    {
        currentRoom = FindCurrentRoom(data, roomId);
        if (currentRoom is null) return false;

        if (currentRoom.Equipment.All(item => item.Id != equipment.Id))
            currentRoom.Equipment.Add(equipment);
        return true;
    }

    public static bool TryApplyToCurrentEquipment(
        AppData data,
        Guid equipmentId,
        EquipmentRecord edited,
        out EquipmentRecord? currentEquipment)
    {
        currentEquipment = FindCurrentEquipment(data, equipmentId);
        if (currentEquipment is null) return false;
        if (!ReferenceEquals(currentEquipment, edited))
            CopyEditableState(edited, currentEquipment);
        return true;
    }

    internal static void CopyEditableState(EquipmentRecord source, EquipmentRecord target)
    {
        target.Description = source.Description;
        target.Manufacturer = source.Manufacturer;
        target.PartNumber = source.PartNumber;
        target.EquipmentId = source.EquipmentId;
        target.Hostname = source.Hostname;
        target.SerialNumber = source.SerialNumber;
        target.Firmware = source.Firmware;
        target.PrimaryIp = source.PrimaryIp;
        target.SecondaryIp = source.SecondaryIp;
        target.TargetIp = source.TargetIp;
        target.DanteIp = source.DanteIp;
        target.Subnet = source.Subnet;
        target.Gateway = source.Gateway;
        target.Mac1 = source.Mac1;
        target.Mac2 = source.Mac2;
        target.Mac3 = source.Mac3;
        target.NetworkInterfaces = (source.NetworkInterfaces ?? [])
            .Select(CloneNetworkInterfaceExact)
            .ToList();
        target.ConfigurationFiles = (source.ConfigurationFiles ?? [])
            .Select(CloneConfigurationFileExact)
            .ToList();
        target.SerialConnection = source.SerialConnection;
        target.Username = source.Username;
        target.Password = source.Password;
        target.Notes = source.Notes;
        target.SourceFile = source.SourceFile;
        target.NetworkState = source.NetworkState;
        target.LastCheckedUtc = source.LastCheckedUtc;
        target.LastLatencyMs = source.LastLatencyMs;
        target.LastNetworkError = source.LastNetworkError;
        target.UpdatedUtc = source.UpdatedUtc;
        target.SyncLegacyNetworkFields();
        target.UpdateAggregateNetworkState();
    }

    private static NetworkInterfaceRecord CloneNetworkInterfaceExact(NetworkInterfaceRecord source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        IpAddress = source.IpAddress,
        MacAddress = source.MacAddress,
        NetworkState = source.NetworkState,
        LastCheckedUtc = source.LastCheckedUtc,
        LastLatencyMs = source.LastLatencyMs,
        LastNetworkError = source.LastNetworkError,
        ObservedMacAddress = source.ObservedMacAddress,
        MacVerificationMessage = source.MacVerificationMessage,
        HttpPortOpen = source.HttpPortOpen,
        HttpsPortOpen = source.HttpsPortOpen
    };

    private static DeviceConfigurationFile CloneConfigurationFileExact(DeviceConfigurationFile source) => new()
    {
        Id = source.Id,
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
