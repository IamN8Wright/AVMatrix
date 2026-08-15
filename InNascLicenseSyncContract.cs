namespace InNasc;

internal sealed record InNascLicenseSyncSnapshot(
    Guid CompanyId,
    Guid LicenseId,
    string LicenseName,
    int DeviceLimit,
    DateTime? ExpiresUtc,
    int DeviceCount,
    DateTime ObservedUtc,
    string RevisionId);

internal interface IInNascLicenseRemoteSyncTransport
{
    Task PushSnapshotAsync(
        InNascLicenseSyncSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

internal static class InNascLicenseSyncContract
{
    // 5.3 implements local .nasc <-> .nascglobal reconciliation. This transport-neutral
    // snapshot is the hook for a later authenticated server sync without another data-model rewrite.
    public static InNascLicenseSyncSnapshot Snapshot(
        InNascCompanyRecord company,
        InNascCompanyFileRecord file) =>
        new(
            company.Id,
            file.Id,
            file.Name,
            file.DeviceLimit,
            file.ExpiresUtc,
            file.LastObservedDeviceCount,
            file.LastCatalogSyncUtc ?? DateTime.UtcNow,
            file.LastObservedRevisionId);
}
