namespace InNasc;

internal static class SyncPublishApplyService
{
    public static AppData Capture(AppData data) => new()
    {
        ProjectName = data.ProjectName,
        Clients = data.Clients.Select(ClientSubmatrixService.CloneClient).ToList()
    };

    public static void Apply(AppData local, AppData beforeUpload, AppData published)
    {
        // Network awaits leave the UI usable. An edit made after serialization
        // belongs to the next push and must not disappear when this push completes.
        var result = AppDataMergeService.Merge(
            beforeUpload, local, published, MergeConflictPreference.ThisPc).Data;
        local.ProjectName = result.ProjectName;
        local.Clients = result.Clients;
        local.MasterAccess = published.MasterAccess;
        local.Settings.GoogleDriveLocalContentFingerprint = SyncContentFingerprint.Compute(published);
    }
}
