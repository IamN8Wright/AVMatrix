namespace AVMatrixStudio;

internal sealed record LiveCoauthoringResult(
    bool DataChanged,
    string Status);

internal static class LiveCoauthoringService
{
    public static async Task<LiveCoauthoringResult> SynchronizeOnceAsync(
        AppData data,
        DataStore store,
        ActiveMasterSession active,
        CancellationToken cancellationToken = default)
    {
        if (active.Target != SyncTarget.GoogleDrive)
            return new LiveCoauthoringResult(false, "Live coauthoring requires Google Drive");

        var snapshot = await GoogleDriveSyncService.InspectAsync(
            data,
            active.Session.MasterKey,
            cancellationToken);
        var remoteChanged = !string.Equals(
            snapshot.Fingerprint,
            data.Settings.GoogleDriveFingerprint,
            StringComparison.OrdinalIgnoreCase);
        if (data.Settings.ActiveCheckoutClientId.HasValue)
        {
            var clientId = data.Settings.ActiveCheckoutClientId.Value;
            var stillOwned = snapshot.Contents.Data.MasterAccess.Checkouts.Any(checkout =>
                checkout.ClientId == clientId &&
                checkout.CheckoutToken == data.Settings.ActiveCheckoutToken &&
                checkout.UserId == active.Session.UserId);
            if (!stillOwned)
            {
                var recovery = CheckoutRecoveryService.PreserveLostCheckout(
                    data,
                    store,
                    snapshot.Contents.Data.MasterAccess);
                _ = GoogleDriveSyncService.Pull(
                    data,
                    store,
                    snapshot,
                    active.Session.MasterKey,
                    active.Session);
                return new LiveCoauthoringResult(
                    true,
                    $"Checkout taken over by {recovery.NewHolder} — local work preserved");
            }

            data.MasterAccess = MasterAccessService.Clone(
                snapshot.Contents.Data.MasterAccess);
            data.Settings.GoogleDriveRemoteChangesDetected = !string.Equals(
                snapshot.Fingerprint,
                data.Settings.GoogleDriveFingerprint,
                StringComparison.OrdinalIgnoreCase);
            store.Save(data);
            return new LiveCoauthoringResult(
                remoteChanged,
                "Checkout active — takeover status connected");
        }

        var localFingerprint = SyncContentFingerprint.Compute(data);
        var localChanged = !string.Equals(
            localFingerprint,
            data.Settings.GoogleDriveLocalContentFingerprint,
            StringComparison.OrdinalIgnoreCase);

        if (!remoteChanged && !localChanged)
            return new LiveCoauthoringResult(false, "Live coauthoring connected");

        if (!active.Session.CanWrite)
        {
            if (!remoteChanged)
                return new LiveCoauthoringResult(false, "Live coauthoring connected — read-only");
            _ = GoogleDriveSyncService.Pull(
                data,
                store,
                snapshot,
                active.Session.MasterKey,
                active.Session);
            return new LiveCoauthoringResult(true, "Live changes received");
        }

        if (localChanged)
        {
            _ = await GoogleDriveSyncService.PushAsync(
                data,
                store,
                active.Session.MasterKey,
                MergeConflictPreference.ThisPc,
                active.Session,
                cancellationToken);
            return new LiveCoauthoringResult(
                true,
                remoteChanged
                    ? "Live changes merged"
                    : "Live changes published");
        }

        _ = GoogleDriveSyncService.Pull(
            data,
            store,
            snapshot,
            active.Session.MasterKey,
            active.Session);
        return new LiveCoauthoringResult(true, "Live changes received");
    }
}
