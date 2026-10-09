namespace InNasc;

internal sealed record CheckoutInventoryRecovery(ClientRecord Client, IReadOnlyList<string> AddedRecords);

internal static class CheckoutInventoryService
{
    public static string LocalFingerprint(AppData data)
    {
        var contents = new
        {
            Metadata = SyncContentFingerprint.Compute(data),
            Files = data.Clients.SelectMany(client => client.Locations).SelectMany(location => location.Rooms)
                .SelectMany(room => room.Equipment).SelectMany(equipment => equipment.ConfigurationFiles)
                .Select(file => new { file.Id, file.ContentIncluded, file.ContentBase64 })
        };
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(contents)));
    }

    public static void RequireOwnership(AppData local, AppData remote, MasterSession session, SyncTarget target)
    {
        var clientId = local.Settings.ActiveCheckoutClientId
            ?? throw new InvalidOperationException("No client is checked out on this PC.");
        if (local.Settings.ActiveCheckoutTarget != target.ToString())
            throw new InvalidOperationException("This checkout belongs to a different company connection.");
        MasterAccessService.RequireClientWrite(remote.MasterAccess, session, clientId);
        if (!remote.MasterAccess.Checkouts.Any(item => item.ClientId == clientId &&
                item.CheckoutToken == local.Settings.ActiveCheckoutToken && item.UserId == session.UserId))
            throw new InvalidOperationException("This checkout is no longer held by this PC. Local work was kept.");
    }

    public static void ApplyRecovery(AppData local, AppData remote, DataStore store,
        SyncTarget target, byte[] remoteContents, CheckoutInventoryRecovery recovery,
        string expectedLocalFingerprint, string? password)
    {
        if (LocalFingerprint(local) != expectedLocalFingerprint)
            throw new InvalidOperationException("The local inventory changed during the recovery preview. Review it again.");
        var clientId = local.Settings.ActiveCheckoutClientId;
        if (clientId != recovery.Client.Id)
            throw new InvalidOperationException("The active checkout changed. Review recovery again.");
        // Preserve an exact local recovery copy before adopting the new ancestor.
        var directory = Path.Combine(store.DataDirectory, "CheckoutRecoveryBackups");
        Directory.CreateDirectory(directory);
        PortableDataService.Export(Path.Combine(directory, $"Before-Recovery-{Guid.NewGuid():N}.nasc"),
            local, password);
        SyncBaselineStore.Save(store, target, remoteContents);
        local.Clients = remote.Clients.Select(client => client.Id == clientId
            ? recovery.Client : ClientSubmatrixService.MetadataOnly(client)).ToList();
        local.ProjectName = remote.ProjectName;
        local.MasterAccess = MasterAccessService.Clone(remote.MasterAccess);
        local.Settings.ActiveCheckoutBaselineFingerprint = SyncContentFingerprint.ComputeClient(
            remote.Clients.Single(client => client.Id == clientId));
        if (target == SyncTarget.GoogleDrive)
        {
            local.Settings.GoogleDriveFingerprint = SyncBaselineStore.Fingerprint(remoteContents);
            local.Settings.GoogleDriveLocalContentFingerprint = SyncContentFingerprint.Compute(remote);
            local.Settings.GoogleDriveRemoteChangesDetected = false;
        }
        else
        {
            local.Settings.SharedMasterFingerprint = SyncBaselineStore.Fingerprint(remoteContents);
            local.Settings.SharedLocalContentFingerprint = SyncContentFingerprint.Compute(remote);
        }
        store.Save(local);
    }

    public static ClientRecord MergeForCheckIn(
        ClientRecord local, ClientRecord remote, ClientRecord? baseline,
        string checkoutFingerprint, MergeConflictPreference? preference = null)
    {
        if (baseline is null)
        {
            if (!string.Equals(SyncContentFingerprint.ComputeClient(remote), checkoutFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new SharedMasterConflictException(
                    "The checked-out inventory changed, and this PC has no matching merge baseline. " +
                    "Use Recover missing records to review the company inventory before checking in. " +
                    "Your local work and checkout have been kept.");
            return ClientSubmatrixService.CloneClient(local);
        }
        var merged = AppDataMergeService.Merge(
            new AppData { Clients = [ClientSubmatrixService.MetadataOnly(baseline)] },
            new AppData { Clients = [ClientSubmatrixService.MetadataOnly(local)] },
            new AppData { Clients = [ClientSubmatrixService.MetadataOnly(remote)] }, preference);
        if (merged.Conflicts.Count > 0 && preference is null)
            throw new MergeResolutionRequiredException(merged.Conflicts);
        if (merged.Data.Clients.Count != 1)
            throw new InvalidOperationException("The checked-out client could not be merged.");
        return ClientSubmatrixService.CombineMetadataAndPayloads(merged.Data.Clients[0], local);
    }

    // Explicit recovery only: without an ancestor, an absent record may be a
    // deliberate local deletion. The caller must show these additions for review.
    public static CheckoutInventoryRecovery PrepareRecovery(ClientRecord local, ClientRecord remote)
    {
        if (local.Id != remote.Id)
            throw new InvalidOperationException("Recovery must use the same client.");
        var result = ClientSubmatrixService.CloneClient(local);
        var added = new List<string>();
        foreach (var location in remote.Locations)
        {
            var destination = result.Locations.SingleOrDefault(item => item.Id == location.Id);
            if (destination is null)
            {
                destination = new LocationRecord
                {
                    Id = location.Id, Name = location.Name, Address = location.Address, Notes = location.Notes
                };
                result.Locations.Add(destination);
                added.Add($"Location: {location.Name}");
            }
            foreach (var room in location.Rooms)
            {
                // A locally moved entity already exists; recover into its current parent.
                var targetRoom = result.Locations.SelectMany(item => item.Rooms)
                    .SingleOrDefault(item => item.Id == room.Id);
                if (targetRoom is null)
                {
                    targetRoom = new RoomRecord { Id = room.Id, Name = room.Name, Notes = room.Notes };
                    destination.Rooms.Add(targetRoom);
                    added.Add($"Room: {location.Name} / {room.Name}");
                }
                foreach (var equipment in room.Equipment)
                {
                    if (result.Locations.SelectMany(item => item.Rooms).SelectMany(item => item.Equipment)
                            .Any(item => item.Id == equipment.Id)) continue;
                    targetRoom.Equipment.Add(ClientSubmatrixService.CloneEquipment(equipment));
                    added.Add($"Device: {location.Name} / {room.Name} / {equipment.Description}");
                }
            }
        }
        return new CheckoutInventoryRecovery(result, added);
    }
}
