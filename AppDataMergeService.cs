using System.Reflection;
using System.Text.Json;

namespace InNasc;

internal enum MergeConflictPreference
{
    ThisPc,
    Master
}

internal sealed record SyncMergeConflict(
    string Item,
    string Field,
    string ThisPcValue,
    string MasterValue);

internal sealed record AppDataMergeResult(
    AppData Data,
    IReadOnlyList<SyncMergeConflict> Conflicts);

internal static class AppDataMergeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> EquipmentExcluded =
    [
        nameof(EquipmentRecord.Id),
        nameof(EquipmentRecord.NetworkInterfaces),
        nameof(EquipmentRecord.ConfigurationFiles),
        nameof(EquipmentRecord.NetworkState),
        nameof(EquipmentRecord.LastCheckedUtc),
        nameof(EquipmentRecord.LastLatencyMs),
        nameof(EquipmentRecord.LastNetworkError),
        nameof(EquipmentRecord.CreatedUtc),
        nameof(EquipmentRecord.UpdatedUtc)
    ];

    private static readonly HashSet<string> InterfaceExcluded =
    [
        nameof(NetworkInterfaceRecord.Id),
        nameof(NetworkInterfaceRecord.NetworkState),
        nameof(NetworkInterfaceRecord.LastCheckedUtc),
        nameof(NetworkInterfaceRecord.LastLatencyMs),
        nameof(NetworkInterfaceRecord.LastNetworkError),
        nameof(NetworkInterfaceRecord.ObservedMacAddress),
        nameof(NetworkInterfaceRecord.MacVerificationMessage),
        nameof(NetworkInterfaceRecord.HttpPortOpen),
        nameof(NetworkInterfaceRecord.HttpsPortOpen),
        nameof(NetworkInterfaceRecord.HasAddress),
        nameof(NetworkInterfaceRecord.PortalUrl)
    ];

    public static AppDataMergeResult Merge(
        AppData baseline,
        AppData thisPc,
        AppData master,
        MergeConflictPreference? preference = null)
    {
        var conflicts = new List<SyncMergeConflict>();
        var merged = new AppData
        {
            ProjectName = MergeValue(
                baseline.ProjectName,
                thisPc.ProjectName,
                master.ProjectName,
                "Project",
                "Project name",
                preference,
                conflicts),
            Settings = thisPc.Settings,
            MasterAccess = Clone(master.MasterAccess),
            Clients = MergeCollection(
                baseline.Clients,
                thisPc.Clients,
                master.Clients,
                item => item.Id,
                item => $"Client: {Display(item.Name, item.Id)}",
                (baseItem, localItem, remoteItem, path) =>
                    MergeClient(baseItem, localItem, remoteItem, path, preference, conflicts),
                preference,
                conflicts)
        };
        DataStore.Normalize(merged);
        return new AppDataMergeResult(merged, conflicts);
    }

    private static ClientRecord MergeClient(
        ClientRecord baseline,
        ClientRecord thisPc,
        ClientRecord master,
        string path,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts)
    {
        var merged = MergeFields(
            baseline, thisPc, master, path, preference, conflicts,
            [nameof(ClientRecord.Id), nameof(ClientRecord.Locations)]);
        merged.Locations = MergeCollection(
            baseline.Locations,
            thisPc.Locations,
            master.Locations,
            item => item.Id,
            item => $"{path} / Location: {Display(item.Name, item.Id)}",
            (baseItem, localItem, remoteItem, childPath) =>
                MergeLocation(baseItem, localItem, remoteItem, childPath, preference, conflicts),
            preference,
            conflicts);
        return merged;
    }

    private static LocationRecord MergeLocation(
        LocationRecord baseline,
        LocationRecord thisPc,
        LocationRecord master,
        string path,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts)
    {
        var merged = MergeFields(
            baseline, thisPc, master, path, preference, conflicts,
            [nameof(LocationRecord.Id), nameof(LocationRecord.Rooms)]);
        merged.Rooms = MergeCollection(
            baseline.Rooms,
            thisPc.Rooms,
            master.Rooms,
            item => item.Id,
            item => $"{path} / Room: {Display(item.Name, item.Id)}",
            (baseItem, localItem, remoteItem, childPath) =>
                MergeRoom(baseItem, localItem, remoteItem, childPath, preference, conflicts),
            preference,
            conflicts);
        return merged;
    }

    private static RoomRecord MergeRoom(
        RoomRecord baseline,
        RoomRecord thisPc,
        RoomRecord master,
        string path,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts)
    {
        var merged = MergeFields(
            baseline, thisPc, master, path, preference, conflicts,
            [nameof(RoomRecord.Id), nameof(RoomRecord.Equipment)]);
        merged.Equipment = MergeCollection(
            baseline.Equipment,
            thisPc.Equipment,
            master.Equipment,
            item => item.Id,
            item => $"{path} / Device: {Display(item.Description, item.Id)}",
            (baseItem, localItem, remoteItem, childPath) =>
                MergeEquipment(baseItem, localItem, remoteItem, childPath, preference, conflicts),
            preference,
            conflicts);
        return merged;
    }

    private static EquipmentRecord MergeEquipment(
        EquipmentRecord baseline,
        EquipmentRecord thisPc,
        EquipmentRecord master,
        string path,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts)
    {
        var merged = MergeFields(
            baseline, thisPc, master, path, preference, conflicts, EquipmentExcluded);
        merged.CreatedUtc = Earliest(baseline.CreatedUtc, thisPc.CreatedUtc, master.CreatedUtc);
        merged.UpdatedUtc = Latest(baseline.UpdatedUtc, thisPc.UpdatedUtc, master.UpdatedUtc);
        merged.NetworkInterfaces = MergeCollection(
            baseline.NetworkInterfaces,
            thisPc.NetworkInterfaces,
            master.NetworkInterfaces,
            item => item.Id,
            item => $"{path} / {item.Type} IP {Display(item.IpAddress, item.Id)}",
            (baseItem, localItem, remoteItem, childPath) =>
                MergeInterface(baseItem, localItem, remoteItem, childPath, preference, conflicts),
            preference,
            conflicts);
        merged.ConfigurationFiles = MergeCollection(
            baseline.ConfigurationFiles,
            thisPc.ConfigurationFiles,
            master.ConfigurationFiles,
            item => item.Id,
            item => $"{path} / Config: {Display(item.FileName, item.Id)}",
            (baseItem, localItem, remoteItem, childPath) =>
                MergeFields(
                    baseItem,
                    localItem,
                    remoteItem,
                    childPath,
                    preference,
                    conflicts,
                    [
                        nameof(DeviceConfigurationFile.Id),
                        nameof(DeviceConfigurationFile.ContentBase64),
                        nameof(DeviceConfigurationFile.ContentIncluded)
                    ]),
            preference,
            conflicts);
        return merged;
    }

    private static NetworkInterfaceRecord MergeInterface(
        NetworkInterfaceRecord baseline,
        NetworkInterfaceRecord thisPc,
        NetworkInterfaceRecord master,
        string path,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts)
    {
        var merged = MergeFields(
            baseline, thisPc, master, path, preference, conflicts, InterfaceExcluded);
        var newest = (master.LastCheckedUtc ?? DateTime.MinValue) >
                     (thisPc.LastCheckedUtc ?? DateTime.MinValue)
            ? master
            : thisPc;
        merged.NetworkState = newest.NetworkState;
        merged.LastCheckedUtc = newest.LastCheckedUtc;
        merged.LastLatencyMs = newest.LastLatencyMs;
        merged.LastNetworkError = newest.LastNetworkError;
        merged.ObservedMacAddress = newest.ObservedMacAddress;
        merged.MacVerificationMessage = newest.MacVerificationMessage;
        merged.HttpPortOpen = newest.HttpPortOpen;
        merged.HttpsPortOpen = newest.HttpsPortOpen;
        return merged;
    }

    private static List<T> MergeCollection<T>(
        IReadOnlyList<T> baseline,
        IReadOnlyList<T> thisPc,
        IReadOnlyList<T> master,
        Func<T, Guid> id,
        Func<T, string> path,
        Func<T, T, T, string, T> mergeExisting,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts)
        where T : class
    {
        var baseItems = baseline.ToDictionary(id);
        var localItems = thisPc.ToDictionary(id);
        var remoteItems = master.ToDictionary(id);
        var orderedIds = master.Select(id)
            .Concat(thisPc.Select(id))
            .Concat(baseline.Select(id))
            .Distinct()
            .ToList();
        var merged = new List<T>();

        foreach (var itemId in orderedIds)
        {
            baseItems.TryGetValue(itemId, out var baseItem);
            localItems.TryGetValue(itemId, out var localItem);
            remoteItems.TryGetValue(itemId, out var remoteItem);
            var displayItem = localItem ?? remoteItem ?? baseItem!;
            var itemPath = path(displayItem);

            if (baseItem is null)
            {
                if (localItem is null && remoteItem is not null)
                    merged.Add(Clone(remoteItem));
                else if (remoteItem is null && localItem is not null)
                    merged.Add(Clone(localItem));
                else if (localItem is not null && remoteItem is not null)
                {
                    if (EquivalentForMerge(localItem, remoteItem))
                        merged.Add(Clone(localItem));
                    else
                    {
                        conflicts.Add(new SyncMergeConflict(
                            itemPath,
                            "New record added differently",
                            Summarize(localItem),
                            Summarize(remoteItem)));
                        merged.Add(Clone(preference == MergeConflictPreference.Master
                            ? remoteItem
                            : localItem));
                    }
                }
                continue;
            }

            if (localItem is null && remoteItem is null) continue;
            if (localItem is null && remoteItem is not null)
            {
                if (EquivalentForMerge(baseItem, remoteItem)) continue;
                conflicts.Add(new SyncMergeConflict(
                    itemPath,
                    "Deleted here / changed in master",
                    "Deleted",
                    Summarize(remoteItem)));
                if (preference == MergeConflictPreference.Master) merged.Add(Clone(remoteItem));
                continue;
            }
            if (remoteItem is null && localItem is not null)
            {
                if (EquivalentForMerge(baseItem, localItem)) continue;
                conflicts.Add(new SyncMergeConflict(
                    itemPath,
                    "Changed here / deleted in master",
                    Summarize(localItem),
                    "Deleted"));
                if (preference != MergeConflictPreference.Master) merged.Add(Clone(localItem));
                continue;
            }

            merged.Add(mergeExisting(baseItem, localItem!, remoteItem!, itemPath));
        }
        return merged;
    }

    private static T MergeFields<T>(
        T baseline,
        T thisPc,
        T master,
        string path,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts,
        HashSet<string> excluded)
        where T : class
    {
        var merged = Clone(thisPc);
        foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || excluded.Contains(property.Name)) continue;
            var baseValue = property.GetValue(baseline);
            var localValue = property.GetValue(thisPc);
            var remoteValue = property.GetValue(master);
            object? value;
            if (Equals(localValue, remoteValue))
                value = localValue;
            else if (Equals(localValue, baseValue))
                value = remoteValue;
            else if (Equals(remoteValue, baseValue))
                value = localValue;
            else
            {
                conflicts.Add(new SyncMergeConflict(
                    path,
                    FriendlyName(property.Name),
                    DisplayValue(localValue),
                    DisplayValue(remoteValue)));
                value = preference == MergeConflictPreference.Master ? remoteValue : localValue;
            }
            property.SetValue(merged, value);
        }
        return merged;
    }

    private static string MergeValue(
        string baseline,
        string thisPc,
        string master,
        string item,
        string field,
        MergeConflictPreference? preference,
        List<SyncMergeConflict> conflicts)
    {
        if (thisPc == master) return thisPc;
        if (thisPc == baseline) return master;
        if (master == baseline) return thisPc;
        conflicts.Add(new SyncMergeConflict(item, field, thisPc, master));
        return preference == MergeConflictPreference.Master ? master : thisPc;
    }

    private static bool EquivalentForMerge<T>(T left, T right)
    {
        var leftClone = Clone(left);
        var rightClone = Clone(right);
        ClearTransient(leftClone);
        ClearTransient(rightClone);
        return JsonSerializer.Serialize(leftClone, JsonOptions) ==
               JsonSerializer.Serialize(rightClone, JsonOptions);
    }

    private static void ClearTransient(object? value)
    {
        switch (value)
        {
            case ClientRecord client:
                foreach (var location in client.Locations) ClearTransient(location);
                break;
            case LocationRecord location:
                foreach (var room in location.Rooms) ClearTransient(room);
                break;
            case RoomRecord room:
                foreach (var equipment in room.Equipment) ClearTransient(equipment);
                break;
            case EquipmentRecord equipment:
                equipment.NetworkState = NetworkState.Unknown;
                equipment.LastCheckedUtc = null;
                equipment.LastLatencyMs = null;
                equipment.LastNetworkError = string.Empty;
                foreach (var networkInterface in equipment.NetworkInterfaces)
                    ClearTransient(networkInterface);
                break;
            case NetworkInterfaceRecord networkInterface:
                networkInterface.NetworkState = NetworkState.Unknown;
                networkInterface.LastCheckedUtc = null;
                networkInterface.LastLatencyMs = null;
                networkInterface.LastNetworkError = string.Empty;
                networkInterface.ObservedMacAddress = string.Empty;
                networkInterface.MacVerificationMessage = string.Empty;
                networkInterface.HttpPortOpen = false;
                networkInterface.HttpsPortOpen = false;
                break;
        }
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)
        ?? throw new InvalidOperationException("A record could not be copied for merging.");

    private static string Display(string? text, Guid id) =>
        string.IsNullOrWhiteSpace(text) ? id.ToString("N")[..8].ToUpperInvariant() : text.Trim();

    private static string DisplayValue(object? value)
    {
        var text = value switch
        {
            null => "(blank)",
            string stringValue when string.IsNullOrWhiteSpace(stringValue) => "(blank)",
            DateTime date => date.ToLocalTime().ToString("g"),
            _ => value.ToString() ?? "(blank)"
        };
        return text.Length > 180 ? text[..177] + "..." : text;
    }

    private static string Summarize<T>(T value)
    {
        var text = value switch
        {
            ClientRecord client => client.Name,
            LocationRecord location => location.Name,
            RoomRecord room => room.Name,
            EquipmentRecord equipment => equipment.Description,
            NetworkInterfaceRecord networkInterface =>
                $"{networkInterface.Type}: {networkInterface.IpAddress}",
            DeviceConfigurationFile configurationFile =>
                $"{configurationFile.FileName} ({configurationFile.SizeBytes:N0} bytes)",
            _ => value?.ToString() ?? "(blank)"
        };
        return string.IsNullOrWhiteSpace(text) ? "(blank)" : text;
    }

    private static string FriendlyName(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
                result.Append(' ');
            result.Append(value[index]);
        }
        return result.ToString();
    }

    private static DateTime Earliest(params DateTime[] values) =>
        values.Where(value => value != default).DefaultIfEmpty(DateTime.UtcNow).Min();

    private static DateTime Latest(params DateTime[] values) =>
        values.DefaultIfEmpty(DateTime.UtcNow).Max();
}

internal sealed class MergeResolutionRequiredException : InvalidOperationException
{
    public IReadOnlyList<SyncMergeConflict> Conflicts { get; }

    public MergeResolutionRequiredException(IReadOnlyList<SyncMergeConflict> conflicts)
        : base($"{conflicts.Count:N0} overlapping change(s) need a merge decision.")
    {
        Conflicts = conflicts;
    }
}
