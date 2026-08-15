namespace InNasc;

internal static class DeviceLimitPolicy
{
    public static int CountDevices(AppData data) =>
        data.Clients.Sum(client => client.Locations.Sum(location =>
            location.Rooms.Sum(room => room.Equipment.Count)));

    public static string LimitText(int deviceLimit) =>
        deviceLimit <= 0 ? "Unlimited" : $"{deviceLimit:N0}";

    public static string ExpirationText(DateTime? expiresUtc) =>
        expiresUtc is null ? "Never" : expiresUtc.Value.ToLocalTime().ToString("yyyy-MM-dd");

    public static bool IsExpired(MasterAccessControl access, DateTime? nowUtc = null) =>
        access.LicenseExpiresUtc is DateTime expires && expires <= (nowUtc ?? DateTime.UtcNow);

    public static bool IsOverLimit(MasterAccessControl access, AppData data) =>
        access.DeviceLimit > 0 && CountDevices(data) > access.DeviceLimit;

    public static string UsageText(MasterAccessControl access, AppData data)
    {
        var count = CountDevices(data);
        return access.DeviceLimit <= 0
            ? $"{count:N0} devices / Unlimited"
            : $"{count:N0} / {access.DeviceLimit:N0} devices";
    }

    public static string WarningText(MasterAccessControl access, AppData data)
    {
        var count = CountDevices(data);
        if (IsExpired(access))
            return $"LICENSE EXPIRED {ExpirationText(access.LicenseExpiresUtc)} - new client cards and devices are locked.";
        if (access.DeviceLimit > 0 && count > access.DeviceLimit)
            return $"LICENSE OVER DEVICE LIMIT: {count:N0} / {access.DeviceLimit:N0} - new client cards and devices are locked.";
        return string.Empty;
    }

    public static void RequireNewClientAllowed(MasterAccessControl access, AppData data)
    {
        if (IsExpired(access))
            throw new LicenseExpiredException(
                access.LicenseExpiresUtc,
                $"This InNasc license expired on {ExpirationText(access.LicenseExpiresUtc)}. Existing records remain available, but new client cards and devices are locked. Ask your InNasc Global Admin to renew the license.");
        if (IsOverLimit(access, data))
            throw new DeviceLimitExceededException(
                access.DeviceLimit,
                CountDevices(data),
                0,
                $"This .nasc contains {CountDevices(data):N0} devices, above its {access.DeviceLimit:N0}-device tier. Existing records remain available, but new client cards and devices are locked until usage is reduced or the tier is increased.");
    }

    public static void RequireCapacity(
        MasterAccessControl access,
        AppData data,
        int additionalDevices)
    {
        RequireNewClientAllowed(access, data);
        if (additionalDevices <= 0 || access.DeviceLimit <= 0) return;
        var current = CountDevices(data);
        if (current + additionalDevices <= access.DeviceLimit) return;
        var remaining = Math.Max(0, access.DeviceLimit - current);
        throw new DeviceLimitExceededException(
            access.DeviceLimit,
            current,
            additionalDevices,
            $"This .nasc license allows {access.DeviceLimit:N0} devices. It currently contains {current:N0}, leaving {remaining:N0} available. Ask your InNasc Global Admin to unlock a higher device tier.");
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
            $"This .nasc license allows {access.DeviceLimit:N0} devices, but the merged workspace would contain {count:N0}. Ask your InNasc Global Admin to unlock a higher tier.");
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

internal sealed class LicenseExpiredException(DateTime? expiresUtc, string message)
    : InvalidOperationException(message)
{
    public DateTime? ExpiresUtc { get; } = expiresUtc;
}
