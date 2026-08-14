namespace InNasc;

internal static class DeviceLimitPolicy
{
    public static int CountDevices(AppData data) =>
        data.Clients.Sum(client => client.Locations.Sum(location =>
            location.Rooms.Sum(room => room.Equipment.Count)));

    public static string LimitText(int deviceLimit) =>
        deviceLimit <= 0 ? "Unlimited" : $"{deviceLimit:N0}";

    public static string UsageText(MasterAccessControl access, AppData data)
    {
        var count = CountDevices(data);
        return access.DeviceLimit <= 0
            ? $"{count:N0} devices / Unlimited"
            : $"{count:N0} / {access.DeviceLimit:N0} devices";
    }

    public static void RequireCapacity(
        MasterAccessControl access,
        AppData data,
        int additionalDevices)
    {
        if (additionalDevices <= 0 || access.DeviceLimit <= 0) return;
        var current = CountDevices(data);
        if (current + additionalDevices <= access.DeviceLimit) return;
        var remaining = Math.Max(0, access.DeviceLimit - current);
        throw new DeviceLimitExceededException(
            access.DeviceLimit,
            current,
            additionalDevices,
            $"This .nasc license allows {access.DeviceLimit:N0} devices. " +
            $"It currently contains {current:N0}, leaving {remaining:N0} available. " +
            "Ask your InNasc Global Admin to unlock a higher device tier.");
    }

    public static void RequireWithinLimit(MasterAccessControl access, AppData data)
    {
        if (access.DeviceLimit <= 0) return;
        var count = CountDevices(data);
        if (count <= access.DeviceLimit) return;
        throw new DeviceLimitExceededException(
            access.DeviceLimit,
            count,
            0,
            $"This .nasc license allows {access.DeviceLimit:N0} devices, but the merged workspace " +
            $"would contain {count:N0}. Ask your InNasc Global Admin to unlock a higher tier.");
    }
}

internal sealed class DeviceLimitExceededException(
    int limit,
    int currentCount,
    int requestedAdditional,
    string message) : InvalidOperationException(message)
{
    public int Limit { get; } = limit;
    public int CurrentCount { get; } = currentCount;
    public int RequestedAdditional { get; } = requestedAdditional;
}
