namespace AVMatrixStudio;

internal enum SyncIndicatorState
{
    Synced,
    NeedsSync
}

internal sealed record SyncIndicatorResult(
    SyncIndicatorState State,
    string Tooltip);

internal static class SyncIndicatorService
{
    public static SyncIndicatorResult Evaluate(AppData data, SyncTarget? activeTarget = null)
    {
        var settings = data.Settings;
        var localFingerprint = SyncContentFingerprint.Compute(data);
        var checkedOutClient = settings.ActiveCheckoutClientId.HasValue
            ? data.Clients.FirstOrDefault(client => client.Id == settings.ActiveCheckoutClientId.Value)
            : null;
        var checkoutFingerprint = checkedOutClient is null
            ? string.Empty
            : SyncContentFingerprint.ComputeClient(checkedOutClient);
        var linkedTargets = 0;
        var syncedTargets = 0;

        if ((activeTarget is null || activeTarget == SyncTarget.SharedFile) &&
            !string.IsNullOrWhiteSpace(settings.SharedMasterPath))
        {
            linkedTargets++;
            var localMatches = settings.ActiveCheckoutClientId.HasValue &&
                               settings.ActiveCheckoutTarget == nameof(SyncTarget.SharedFile)
                ? !string.IsNullOrWhiteSpace(settings.ActiveCheckoutBaselineFingerprint) &&
                  string.Equals(
                      checkoutFingerprint,
                      settings.ActiveCheckoutBaselineFingerprint,
                      StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrWhiteSpace(settings.SharedLocalContentFingerprint) &&
                  string.Equals(
                      localFingerprint,
                      settings.SharedLocalContentFingerprint,
                      StringComparison.OrdinalIgnoreCase);
            var masterMatches = false;
            try
            {
                masterMatches = File.Exists(settings.SharedMasterPath) &&
                    !string.IsNullOrWhiteSpace(settings.SharedMasterFingerprint) &&
                    string.Equals(
                        SharedSyncService.Fingerprint(settings.SharedMasterPath),
                        settings.SharedMasterFingerprint,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                masterMatches = false;
            }
            if (localMatches && masterMatches) syncedTargets++;
        }

        if ((activeTarget is null || activeTarget == SyncTarget.GoogleDrive) &&
            !string.IsNullOrWhiteSpace(settings.GoogleDriveFileId))
        {
            linkedTargets++;
            var localMatches = settings.ActiveCheckoutClientId.HasValue &&
                               settings.ActiveCheckoutTarget == nameof(SyncTarget.GoogleDrive)
                ? !string.IsNullOrWhiteSpace(settings.ActiveCheckoutBaselineFingerprint) &&
                  string.Equals(
                      checkoutFingerprint,
                      settings.ActiveCheckoutBaselineFingerprint,
                      StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrWhiteSpace(settings.GoogleDriveLocalContentFingerprint) &&
                  string.Equals(
                      localFingerprint,
                      settings.GoogleDriveLocalContentFingerprint,
                      StringComparison.OrdinalIgnoreCase);
            if (localMatches && !settings.GoogleDriveRemoteChangesDetected) syncedTargets++;
        }

        if (linkedTargets > 0 && syncedTargets == linkedTargets)
            return new SyncIndicatorResult(
                SyncIndicatorState.Synced,
                "Shared master sync — up to date");

        return new SyncIndicatorResult(
            SyncIndicatorState.NeedsSync,
            linkedTargets == 0
                ? "Shared master sync — not linked"
                : settings.ActiveCheckoutClientId.HasValue
                    ? "Client checkout — changes need to be checked in"
                    : "Shared master sync — changes need to be merged or pushed");
    }
}
