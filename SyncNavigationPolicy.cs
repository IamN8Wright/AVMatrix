namespace InNasc;

internal static class SyncNavigationPolicy
{
    internal static SyncTarget ForWorkspace(AppSettings settings, SyncTarget? signedInTarget = null)
    {
        if (settings.ActiveCheckoutClientId.HasValue && TryTarget(settings.ActiveCheckoutTarget, out var checkout))
            return checkout;
        if (signedInTarget.HasValue) return signedInTarget.Value;
        if (TryTarget(settings.LastMasterTarget, out var previous) && IsLinked(settings, previous)) return previous;
        return !string.IsNullOrWhiteSpace(settings.SharedMasterPath)
            ? SyncTarget.SharedFile : !string.IsNullOrWhiteSpace(settings.GoogleDriveFileId)
                ? SyncTarget.GoogleDrive : SyncTarget.SharedFile;
    }

    internal static bool CanApplyStatus(AppSettings settings, SyncTarget inspectedTarget, SyncTarget? signedInTarget)
    {
        if (signedInTarget.HasValue && signedInTarget.Value != inspectedTarget) return false;
        return !settings.ActiveCheckoutClientId.HasValue ||
            TryTarget(settings.ActiveCheckoutTarget, out var checkout) && checkout == inspectedTarget;
    }

    private static bool IsLinked(AppSettings settings, SyncTarget target) => target == SyncTarget.GoogleDrive
        ? !string.IsNullOrWhiteSpace(settings.GoogleDriveFileId)
        : !string.IsNullOrWhiteSpace(settings.SharedMasterPath);

    private static bool TryTarget(string value, out SyncTarget target) =>
        Enum.TryParse(value, out target) && Enum.IsDefined(target);
}
