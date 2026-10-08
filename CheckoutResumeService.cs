namespace InNasc;

internal static class CheckoutResumeService
{
    // Refresh the company while retaining this PC's unfinished checked-out work.
    // The last downloaded master is the ancestor, not the current local checkout.
    public static void RefreshInventory(AppData local, AppData remote, AppData? baseline)
    {
        var clientId = local.Settings.ActiveCheckoutClientId
            ?? throw new InvalidOperationException("There is no checkout to resume.");
        var owned = local.Clients.SingleOrDefault(client => client.Id == clientId)
            ?? throw new InvalidOperationException("The local checked-out client is missing.");
        var remoteOwned = remote.Clients.SingleOrDefault(client => client.Id == clientId)
            ?? throw new InvalidOperationException("The checked-out client is missing from the company file.");
        var before = baseline?.Clients.SingleOrDefault(client => client.Id == clientId);
        var refreshedOwned = ClientSubmatrixService.CloneClient(owned);
        if (before is not null)
        {
            refreshedOwned = AppDataMergeService.Merge(
                new AppData { Clients = [ClientSubmatrixService.MetadataOnly(before)] },
                new AppData { Clients = [ClientSubmatrixService.MetadataOnly(owned)] },
                new AppData { Clients = [ClientSubmatrixService.MetadataOnly(remoteOwned)] },
                MergeConflictPreference.ThisPc).Data.Clients.Single();
            refreshedOwned = ClientSubmatrixService.CombineMetadataAndPayloads(refreshedOwned, owned);
        }
        // Without a usable ancestor, preserve the entire local checkout rather than
        // guessing whether a missing room was deleted or has never been downloaded.
        local.ProjectName = remote.ProjectName;
        local.Clients = remote.Clients.Select(client => client.Id == clientId
            ? refreshedOwned : ClientSubmatrixService.MetadataOnly(client)).ToList();
        if (before is not null)
            local.Settings.ActiveCheckoutBaselineFingerprint = SyncContentFingerprint.ComputeClient(remoteOwned);
    }
}
