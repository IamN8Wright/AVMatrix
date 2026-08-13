using System.Security.Cryptography;

namespace InNasc;

internal static class GoogleDriveSyncService
{
    public static async Task<GoogleDriveFileMetadata> LinkAsync(
        string shareLink,
        AppData data,
        DataStore store,
        CancellationToken cancellationToken = default)
    {
        var fileId = GoogleDriveService.ParseFileId(shareLink);
        var previousLink = data.Settings.GoogleDriveShareLink;
        var previousId = data.Settings.GoogleDriveFileId;
        data.Settings.GoogleDriveShareLink = shareLink.Trim();
        data.Settings.GoogleDriveFileId = fileId;
        GoogleDriveFileMetadata metadata;
        try
        {
            metadata = await GoogleDriveService.GetMetadataAsync(data.Settings, cancellationToken);
        }
        catch
        {
            data.Settings.GoogleDriveShareLink = previousLink;
            data.Settings.GoogleDriveFileId = previousId;
            throw;
        }
        if (!metadata.CanEdit ||
            !(metadata.Name.EndsWith(InNascFileTypes.CompanyExtension, StringComparison.OrdinalIgnoreCase) ||
              metadata.Name.EndsWith(InNascFileTypes.LegacyCompanyExtension, StringComparison.OrdinalIgnoreCase)))
        {
            data.Settings.GoogleDriveShareLink = previousLink;
            data.Settings.GoogleDriveFileId = previousId;
            if (!metadata.CanEdit)
                throw new InvalidOperationException(
                    "The signed-in Google account does not have edit access to this Drive file.");
            throw new InvalidDataException(
                "The linked Google Drive company file must use .nasc. Legacy .avmatrix files remain readable for migration.");
        }
        data.Settings.GoogleDriveFingerprint = string.Empty;
        data.Settings.GoogleDriveLocalContentFingerprint = string.Empty;
        data.Settings.GoogleDriveRemoteChangesDetected = true;
        data.Settings.GoogleDriveLastSyncUtc = null;
        data.Settings.LastMasterTarget = nameof(SyncTarget.GoogleDrive);
        SyncBaselineStore.Delete(store, SyncTarget.GoogleDrive);
        store.Save(data);
        return metadata;
    }

    public static async Task<GoogleDriveSnapshot> InspectAsync(
        AppData data,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var file = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        var imported = PortableDataService.ImportBytes(file.Contents, password);
        return new GoogleDriveSnapshot(file.Metadata, Fingerprint(file.Contents), imported, file.Contents);
    }

    public static GoogleDriveSyncResult Pull(
        AppData data,
        DataStore store,
        GoogleDriveSnapshot snapshot,
        string? password,
        MasterSession? session = null)
    {
        MasterAccessService.RequireRead(snapshot.Contents.Data.MasterAccess, session);
        var recovery = CreateRecoveryBackup(data, store, password);
        data.ProjectName = snapshot.Contents.Data.ProjectName;
        data.Clients = snapshot.Contents.Data.Clients
            .Select(ClientSubmatrixService.MetadataOnly)
            .ToList();
        data.MasterAccess = snapshot.Contents.Data.MasterAccess;
        data.Settings.MasterWorkspaceReadOnly = session?.Role == MasterUserRole.ReadOnly;
        SharedSyncService.ClearActiveCheckout(data.Settings);
        data.Settings.GoogleDriveFingerprint = snapshot.Fingerprint;
        data.Settings.GoogleDriveLocalContentFingerprint = SyncContentFingerprint.Compute(data);
        data.Settings.GoogleDriveRemoteChangesDetected = false;
        data.Settings.GoogleDriveLastSyncUtc = DateTime.UtcNow;
        SyncBaselineStore.Save(store, SyncTarget.GoogleDrive, snapshot.RawContents);
        DataStore.Normalize(data);
        store.Save(data);
        return Result("Pulled", snapshot, recovery);
    }

    public static async Task<GoogleDriveSyncResult> PushAsync(
        AppData data,
        DataStore store,
        string? password,
        MergeConflictPreference? conflictPreference = null,
        MasterSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var expectedFingerprint = data.Settings.GoogleDriveFingerprint;
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
            throw new SharedMasterConflictException(
                "Pull the Google Drive company file before your first push. This prevents overwriting existing work.");

        string recoveryPath = string.Empty;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var remote = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
            var currentFingerprint = Fingerprint(remote.Contents);
            if (PortableDataService.IsPasswordProtected(remote.Contents) && string.IsNullOrEmpty(password))
                throw new PasswordRequiredException();
            var remoteData = PortableDataService.ImportBytes(remote.Contents, password).Data;
            MasterAccessService.RequireWrite(remoteData.MasterAccess, session);

            var localForMerge = MasterCheckoutPolicy.PreserveProtectedClients(
                data, remoteData, remoteData.MasterAccess, session);
            var dataToPush = localForMerge;
            var action = "Pushed";
            if (!string.Equals(currentFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                var baseline = SyncBaselineStore.Load(
                    store, SyncTarget.GoogleDrive, expectedFingerprint, password);
                var merge = AppDataMergeService.Merge(
                    baseline, localForMerge, remoteData, conflictPreference);
                if (merge.Conflicts.Count > 0 && conflictPreference is null)
                    throw new MergeResolutionRequiredException(merge.Conflicts);
                dataToPush = merge.Data;
                action = "Merged";
                if (string.IsNullOrEmpty(recoveryPath))
                    recoveryPath = SaveRemoteRecoveryBackup(store, remote.Contents);
            }

            var masterData = ClientSubmatrixService.MasterMetadataOnly(dataToPush);
            masterData.MasterAccess = remoteData.MasterAccess;
            var writingSession = session ?? throw new MasterAuthorizationException(
                "Sign in to push this master.");
            var bytes = PortableDataService.ExportMasterBytes(
                masterData, writingSession, out var exportInfo);

            // Narrow the cloud race window: verify the source revision again immediately
            // before upload, then verify that our exact bytes are still current afterward.
            var latest = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
            if (!string.Equals(
                    Fingerprint(latest.Contents), currentFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            await GoogleDriveService.UploadAsync(data.Settings, bytes, cancellationToken);
            var verified = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
            var uploadedFingerprint = Fingerprint(bytes);
            if (!string.Equals(
                    Fingerprint(verified.Contents), uploadedFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            data.ProjectName = masterData.ProjectName;
            data.Clients = masterData.Clients;
            data.MasterAccess = masterData.MasterAccess;
            data.Settings.GoogleDriveFingerprint = uploadedFingerprint;
            data.Settings.GoogleDriveLocalContentFingerprint = SyncContentFingerprint.Compute(data);
            data.Settings.GoogleDriveRemoteChangesDetected = false;
            data.Settings.GoogleDriveLastSyncUtc = DateTime.UtcNow;
            SyncBaselineStore.Save(store, SyncTarget.GoogleDrive, verified.Contents);
            store.Save(data);
            return new GoogleDriveSyncResult(
                action, verified.Metadata, uploadedFingerprint, exportInfo.ExportedUtc,
                exportInfo.RevisionId, exportInfo.SavedBy, data.Clients.Count,
                data.Clients.Sum(client => client.Locations.Sum(location =>
                    location.Rooms.Sum(room => room.Equipment.Count))), recoveryPath);
        }

        throw new SharedMasterConflictException(
            "The Google Drive company file kept changing while the merge was being uploaded. " +
            "No unverified overwrite was accepted. Wait a moment and try Merge & push again.");
    }

    public static void Unlink(AppData data, DataStore store)
    {
        data.Settings.GoogleDriveShareLink = string.Empty;
        data.Settings.GoogleDriveFileId = string.Empty;
        data.Settings.GoogleDriveFingerprint = string.Empty;
        data.Settings.GoogleDriveLocalContentFingerprint = string.Empty;
        data.Settings.GoogleDriveRemoteChangesDetected = false;
        data.Settings.GoogleDriveLastSyncUtc = null;
        data.Settings.MasterWorkspaceReadOnly = false;
        SyncBaselineStore.Delete(store, SyncTarget.GoogleDrive);
        store.Save(data);
    }

    public static async Task<GoogleDriveSyncResult> SaveAccessControlAsync(
        AppData data,
        DataStore store,
        MasterAccessControl updatedAccess,
        MasterSession? session,
        bool initialSetup,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var remote = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        var remoteFingerprint = Fingerprint(remote.Contents);
        var remoteData = PortableDataService.ImportBytes(remote.Contents, password).Data;
        if (initialSetup)
        {
            if (remoteData.MasterAccess.IsConfigured)
                throw new MasterAuthorizationException(
                    "Another Owner configured this master first. Refresh and sign in.");
        }
        else
        {
            _ = session ?? throw new MasterAuthorizationException("Sign in to update account access.");
        }
        var accessToSave = initialSetup || session?.IsOwner == true
            ? MasterAccessService.Clone(updatedAccess)
            : MasterAccessService.ApplyOwnPasswordChange(
                remoteData.MasterAccess,
                updatedAccess,
                session!);
        MasterAccessService.ValidateForSave(accessToSave);
        remoteData.MasterAccess = accessToSave;
        var masterData = ClientSubmatrixService.MasterMetadataOnly(remoteData);
        var writingSession = session ?? throw new MasterAuthorizationException(
            "The Owner session is required to secure the master account list.");
        var bytes = PortableDataService.ExportMasterBytes(
            masterData, writingSession, out var exportInfo);
        var latest = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        if (!string.Equals(
                Fingerprint(latest.Contents), remoteFingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new SharedMasterConflictException(
                "The Google Drive company file changed while accounts were being updated. Refresh and try again.");
        await GoogleDriveService.UploadAsync(data.Settings, bytes, cancellationToken);
        var saved = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        var savedFingerprint = Fingerprint(saved.Contents);
        if (!string.Equals(savedFingerprint, Fingerprint(bytes), StringComparison.OrdinalIgnoreCase))
            throw new SharedMasterConflictException(
                "The Google Drive company file changed immediately after the account update. Refresh before continuing.");
        data.MasterAccess = MasterAccessService.Clone(accessToSave);
        data.Settings.GoogleDriveFingerprint = savedFingerprint;
        data.Settings.GoogleDriveRemoteChangesDetected = false;
        data.Settings.GoogleDriveLastSyncUtc = DateTime.UtcNow;
        SyncBaselineStore.Save(store, SyncTarget.GoogleDrive, saved.Contents);
        store.Save(data);
        return new GoogleDriveSyncResult(
            initialSetup ? "Owner configured" : "Accounts updated",
            saved.Metadata,
            savedFingerprint,
            exportInfo.ExportedUtc,
            exportInfo.RevisionId,
            exportInfo.SavedBy,
            masterData.Clients.Count,
            masterData.Clients.Sum(client => client.Locations.Sum(location =>
                location.Rooms.Sum(room => room.Equipment.Count))),
            string.Empty);
    }

    public static async Task<int> MigrateClientSubmatricesAsync(
        AppData data,
        AppData legacyMaster,
        DataStore store,
        string? legacyPassword,
        MasterSession session,
        CancellationToken cancellationToken = default)
    {
        var migrated = 0;
        foreach (var client in legacyMaster.Clients)
        {
            var reference = legacyMaster.MasterAccess.ClientSubmatrices
                .FirstOrDefault(item => item.ClientId == client.Id &&
                    !string.IsNullOrWhiteSpace(item.GoogleDriveFileId));
            var name = ClientSubmatrixService.GoogleDriveClientFileName(
                legacyMaster.MasterAccess.MasterId,
                client.Id);
            var metadata = reference is null
                ? await GoogleDriveService.FindSiblingAsync(data.Settings, name, cancellationToken)
                : null;
            var fileId = reference?.GoogleDriveFileId ?? metadata?.Id;
            if (string.IsNullOrWhiteSpace(fileId)) continue;
            var file = await GoogleDriveService.DownloadByIdAsync(
                data.Settings, fileId, cancellationToken);
            var package = PortableDataService.ImportBytes(file.Contents, legacyPassword).Data;
            SaveRemoteRecoveryBackup(store, file.Contents);
            var secured = PortableDataService.ExportBytes(
                package, session.MasterKey, out _);
            await GoogleDriveService.UploadByIdAsync(
                data.Settings, fileId, secured, cancellationToken);
            migrated++;
        }
        return migrated;
    }

    public static async Task<ClientCheckoutResult> CheckoutClientAsync(
        AppData data,
        DataStore store,
        Guid clientId,
        MasterSession session,
        bool force,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var remote = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        var remoteFingerprint = Fingerprint(remote.Contents);
        var remoteData = PortableDataService.ImportBytes(remote.Contents, password).Data;
        MasterAccessService.RequireClientWrite(remoteData.MasterAccess, session, clientId);
        var clientMetadata = remoteData.Clients.FirstOrDefault(client => client.Id == clientId)
            ?? throw new InvalidOperationException("The selected client no longer exists in the master.");
        var existing = remoteData.MasterAccess.Checkouts
            .FirstOrDefault(checkout => checkout.ClientId == clientId);
        var continuingOwnCheckout = existing is not null &&
            existing.UserId == session.UserId &&
            data.Settings.ActiveCheckoutToken == existing.CheckoutToken &&
            data.Settings.ActiveCheckoutTarget == nameof(SyncTarget.GoogleDrive);
        if (existing is not null && !continuingOwnCheckout && !force)
            throw new ClientLockedException(clientMetadata.Name, existing);

        var recovery = CreateRecoveryBackup(data, store, password);
        if (existing is not null) remoteData.MasterAccess.Checkouts.Remove(existing);
        var checkout = continuingOwnCheckout
            ? existing!
            : new ClientCheckoutRecord
            {
                ClientId = clientId,
                UserId = session.UserId,
                Username = session.Username,
                DisplayName = session.DisplayName,
                MachineName = Environment.MachineName
            };
        checkout.LastActivityUtc = DateTime.UtcNow;
        remoteData.MasterAccess.Checkouts.Add(checkout);

        var submatrixName = ClientSubmatrixService.GoogleDriveClientFileName(
            remoteData.MasterAccess.MasterId, clientId);
        var submatrixReference = remoteData.MasterAccess.ClientSubmatrices
            .FirstOrDefault(reference => reference.ClientId == clientId &&
                !string.IsNullOrWhiteSpace(reference.GoogleDriveFileId));
        var submatrixMetadata = submatrixReference is null
            ? await GoogleDriveService.FindSiblingAsync(
                data.Settings, submatrixName, cancellationToken)
            : null;
        ClientRecord? payloadClient = null;
        var submatrixFileId = submatrixReference?.GoogleDriveFileId ?? submatrixMetadata?.Id;
        if (!string.IsNullOrWhiteSpace(submatrixFileId))
        {
            var submatrix = await GoogleDriveService.DownloadByIdAsync(
                data.Settings, submatrixFileId, cancellationToken);
            payloadClient = ClientSubmatrixService.ReadClientPackage(
                submatrix.Contents, clientId, password);
        }
        var checkedOutClient = ClientSubmatrixService.CombineMetadataAndPayloads(
            clientMetadata, payloadClient);
        var saved = await WriteMasterIfCurrentAsync(
            data.Settings, remoteFingerprint, remoteData, session, cancellationToken);

        data.ProjectName = remoteData.ProjectName;
        data.Clients = remoteData.Clients
            .Select(client => client.Id == clientId
                ? checkedOutClient
                : ClientSubmatrixService.MetadataOnly(client))
            .ToList();
        data.MasterAccess = MasterAccessService.Clone(saved.Data.MasterAccess);
        data.Settings.GoogleDriveFingerprint = saved.Fingerprint;
        data.Settings.GoogleDriveLocalContentFingerprint = string.Empty;
        data.Settings.GoogleDriveRemoteChangesDetected = false;
        data.Settings.GoogleDriveLastSyncUtc = DateTime.UtcNow;
        data.Settings.ActiveCheckoutClientId = clientId;
        data.Settings.ActiveCheckoutToken = checkout.CheckoutToken;
        data.Settings.ActiveCheckoutUsername = session.Username;
        data.Settings.ActiveCheckoutBaselineFingerprint =
            SyncContentFingerprint.ComputeClient(checkedOutClient);
        data.Settings.ActiveCheckoutTarget = nameof(SyncTarget.GoogleDrive);
        data.Settings.MasterWorkspaceReadOnly = false;
        SyncBaselineStore.Save(store, SyncTarget.GoogleDrive, saved.File.Contents);
        store.Save(data);
        return new ClientCheckoutResult(
            clientId,
            checkedOutClient.Name,
            checkout,
            existing is not null && !continuingOwnCheckout,
            recovery,
            $"Google Drive: {submatrixName}");
    }

    public static async Task<GoogleDriveSyncResult> CheckInClientAsync(
        AppData data,
        DataStore store,
        MasterSession session,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (data.Settings.ActiveCheckoutTarget != nameof(SyncTarget.GoogleDrive))
            throw new InvalidOperationException("The active checkout is not linked to Google Drive.");
        var clientId = data.Settings.ActiveCheckoutClientId
            ?? throw new InvalidOperationException("No client is checked out on this PC.");
        var token = data.Settings.ActiveCheckoutToken
            ?? throw new InvalidOperationException("The local checkout token is missing.");
        var localClient = data.Clients.SingleOrDefault(client => client.Id == clientId)
            ?? throw new InvalidOperationException("The checked-out client is not available locally.");

        var initial = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        var initialData = PortableDataService.ImportBytes(initial.Contents, password).Data;
        MasterAccessService.RequireClientWrite(initialData.MasterAccess, session, clientId);
        RequireOwnedCheckout(initialData.MasterAccess, clientId, token, session);
        var recovery = SaveRemoteRecoveryBackup(store, initial.Contents);

        var submatrixName = ClientSubmatrixService.GoogleDriveClientFileName(
            initialData.MasterAccess.MasterId, clientId);
        var submatrixBytes = PortableDataService.ExportBytes(
            ClientSubmatrixService.ClientPackage(localClient), password, out _);
        var knownReference = initialData.MasterAccess.ClientSubmatrices
            .FirstOrDefault(reference => reference.ClientId == clientId &&
                !string.IsNullOrWhiteSpace(reference.GoogleDriveFileId));
        var submatrix = knownReference is null
            ? await GoogleDriveService.FindSiblingAsync(
                data.Settings, submatrixName, cancellationToken)
            : null;
        string submatrixFileId;
        if (knownReference is null && submatrix is null)
        {
            var created = await GoogleDriveService.CreateSiblingAsync(
                data.Settings, submatrixName, submatrixBytes, cancellationToken);
            submatrixFileId = created.Id;
        }
        else
        {
            submatrixFileId = knownReference?.GoogleDriveFileId ?? submatrix!.Id;
            var previous = await GoogleDriveService.DownloadByIdAsync(
                data.Settings, submatrixFileId, cancellationToken);
            SaveRemoteRecoveryBackup(store, previous.Contents);
            await GoogleDriveService.UploadByIdAsync(
                data.Settings, submatrixFileId, submatrixBytes, cancellationToken);
        }

        // Re-read after the sub-matrix upload so an intervening boot is detected
        // before any main-master inventory or lock state is changed.
        var latest = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        var latestData = PortableDataService.ImportBytes(latest.Contents, password).Data;
        MasterAccessService.RequireClientWrite(latestData.MasterAccess, session, clientId);
        var checkout = RequireOwnedCheckout(
            latestData.MasterAccess, clientId, token, session);
        var latestReference = latestData.MasterAccess.ClientSubmatrices
            .FirstOrDefault(reference => reference.ClientId == clientId);
        if (latestReference is null)
        {
            latestReference = new ClientSubmatrixReference { ClientId = clientId };
            latestData.MasterAccess.ClientSubmatrices.Add(latestReference);
        }
        latestReference.GoogleDriveFileId = submatrixFileId;
        latestReference.FileName = submatrixName;
        latestReference.UpdatedUtc = DateTime.UtcNow;
        var index = latestData.Clients.FindIndex(client => client.Id == clientId);
        if (index < 0)
            throw new InvalidOperationException("The checked-out client was deleted from the master.");
        latestData.Clients[index] = ClientSubmatrixService.MetadataOnly(localClient);
        latestData.MasterAccess.Checkouts.Remove(checkout);
        var saved = await WriteMasterIfCurrentAsync(
            data.Settings,
            Fingerprint(latest.Contents),
            latestData,
            session,
            cancellationToken);

        data.ProjectName = saved.Data.ProjectName;
        data.Clients = saved.Data.Clients.Select(ClientSubmatrixService.MetadataOnly).ToList();
        data.MasterAccess = MasterAccessService.Clone(saved.Data.MasterAccess);
        SharedSyncService.ClearActiveCheckout(data.Settings);
        data.Settings.GoogleDriveFingerprint = saved.Fingerprint;
        data.Settings.GoogleDriveLocalContentFingerprint = SyncContentFingerprint.Compute(data);
        data.Settings.GoogleDriveRemoteChangesDetected = false;
        data.Settings.GoogleDriveLastSyncUtc = DateTime.UtcNow;
        SyncBaselineStore.Save(store, SyncTarget.GoogleDrive, saved.File.Contents);
        store.Save(data);
        return Result("Client checked in", new GoogleDriveSnapshot(
            saved.File.Metadata,
            saved.Fingerprint,
            new PortableImport(
                saved.Data,
                saved.ExportInfo.ExportedUtc,
                AppInfo.Revision,
                saved.ExportInfo.RevisionId,
                saved.ExportInfo.SavedBy,
                !string.IsNullOrEmpty(password)),
            saved.File.Contents), recovery);
    }

    public static async Task<GoogleDriveSyncResult> ReleaseCheckoutAsync(
        AppData data,
        DataStore store,
        MasterSession session,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (data.Settings.ActiveCheckoutTarget != nameof(SyncTarget.GoogleDrive))
            throw new InvalidOperationException("The active checkout is not linked to Google Drive.");
        var clientId = data.Settings.ActiveCheckoutClientId
            ?? throw new InvalidOperationException("No client is checked out on this PC.");
        var token = data.Settings.ActiveCheckoutToken
            ?? throw new InvalidOperationException("The local checkout token is missing.");
        var remote = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        var remoteData = PortableDataService.ImportBytes(remote.Contents, password).Data;
        MasterAccessService.RequireClientWrite(remoteData.MasterAccess, session, clientId);
        var checkout = RequireOwnedCheckout(remoteData.MasterAccess, clientId, token, session);
        remoteData.MasterAccess.Checkouts.Remove(checkout);
        var saved = await WriteMasterIfCurrentAsync(
            data.Settings,
            Fingerprint(remote.Contents),
            remoteData,
            session,
            cancellationToken);
        data.ProjectName = saved.Data.ProjectName;
        data.Clients = saved.Data.Clients.Select(ClientSubmatrixService.MetadataOnly).ToList();
        data.MasterAccess = MasterAccessService.Clone(saved.Data.MasterAccess);
        SharedSyncService.ClearActiveCheckout(data.Settings);
        data.Settings.GoogleDriveFingerprint = saved.Fingerprint;
        data.Settings.GoogleDriveLocalContentFingerprint = SyncContentFingerprint.Compute(data);
        data.Settings.GoogleDriveRemoteChangesDetected = false;
        data.Settings.GoogleDriveLastSyncUtc = DateTime.UtcNow;
        SyncBaselineStore.Save(store, SyncTarget.GoogleDrive, saved.File.Contents);
        store.Save(data);
        return Result("Checkout released", new GoogleDriveSnapshot(
            saved.File.Metadata,
            saved.Fingerprint,
            new PortableImport(
                saved.Data,
                saved.ExportInfo.ExportedUtc,
                AppInfo.Revision,
                saved.ExportInfo.RevisionId,
                saved.ExportInfo.SavedBy,
                !string.IsNullOrEmpty(password)),
            saved.File.Contents), string.Empty);
    }

    public static bool HasExternalChanges(AppData data, GoogleDriveSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(data.Settings.GoogleDriveFingerprint) &&
        !string.Equals(data.Settings.GoogleDriveFingerprint, snapshot.Fingerprint,
            StringComparison.OrdinalIgnoreCase);

    public static bool EnsureBaselineIfSafe(
        AppData data,
        DataStore store,
        GoogleDriveSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(data.Settings.GoogleDriveFingerprint) ||
            !string.Equals(
                data.Settings.GoogleDriveFingerprint,
                snapshot.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
            return false;
        var localFingerprint = SyncContentFingerprint.Compute(data);
        var remoteFingerprint = SyncContentFingerprint.Compute(snapshot.Contents.Data);
        if (!string.Equals(localFingerprint, remoteFingerprint, StringComparison.OrdinalIgnoreCase))
            return false;
        SyncBaselineStore.Save(store, SyncTarget.GoogleDrive, snapshot.RawContents);
        data.Settings.GoogleDriveLocalContentFingerprint = localFingerprint;
        data.Settings.GoogleDriveRemoteChangesDetected = false;
        store.Save(data);
        return true;
    }

    private static GoogleDriveSyncResult Result(
        string action,
        GoogleDriveSnapshot snapshot,
        string recoveryPath) => new(
        action,
        snapshot.Metadata,
        snapshot.Fingerprint,
        snapshot.Contents.ExportedUtc,
        snapshot.Contents.RevisionId,
        snapshot.Contents.SavedBy,
        snapshot.Contents.ClientCount,
        snapshot.Contents.EquipmentCount,
        recoveryPath);

    private static string CreateRecoveryBackup(AppData data, DataStore store, string? password)
    {
        var directory = Path.Combine(store.DataDirectory, "SharedSyncBackups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"Before-Google-Pull-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.nasc");
        PortableDataService.Export(path, data, password);
        return path;
    }

    private static async Task<GoogleMasterWriteResult> WriteMasterIfCurrentAsync(
        AppSettings settings,
        string expectedFingerprint,
        AppData data,
        MasterSession session,
        CancellationToken cancellationToken)
    {
        var masterData = ClientSubmatrixService.MasterMetadataOnly(data);
        var bytes = PortableDataService.ExportMasterBytes(masterData, session, out var exportInfo);
        var latest = await GoogleDriveService.DownloadAsync(settings, cancellationToken);
        if (!string.Equals(
                Fingerprint(latest.Contents), expectedFingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new SharedMasterConflictException(
                "The Google Drive company file changed during this checkout operation. Refresh and try again.");
        await GoogleDriveService.UploadAsync(settings, bytes, cancellationToken);
        var saved = await GoogleDriveService.DownloadAsync(settings, cancellationToken);
        var savedFingerprint = Fingerprint(saved.Contents);
        if (!string.Equals(savedFingerprint, Fingerprint(bytes), StringComparison.OrdinalIgnoreCase))
            throw new SharedMasterConflictException(
                "Another Google Drive update landed at the same time. The checkout operation was not confirmed; refresh before continuing.");
        var savedData = PortableDataService.ImportBytes(saved.Contents, session.MasterKey).Data;
        return new GoogleMasterWriteResult(saved, savedData, savedFingerprint, exportInfo);
    }

    private static ClientCheckoutRecord RequireOwnedCheckout(
        MasterAccessControl access,
        Guid clientId,
        Guid token,
        MasterSession session) =>
        access.Checkouts.FirstOrDefault(checkout =>
            checkout.ClientId == clientId &&
            checkout.CheckoutToken == token &&
            checkout.UserId == session.UserId)
        ?? throw new CheckoutOwnershipLostException(
            "This checkout was released or booted by another technician. " +
            "Your local work remains on this PC. Check out the client again before pushing it.");

    private static string Fingerprint(byte[] contents) =>
        Convert.ToHexString(SHA256.HashData(contents));

    private static string SaveRemoteRecoveryBackup(DataStore store, byte[] contents)
    {
        var directory = Path.Combine(store.DataDirectory, "SharedSyncBackups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"Before-Google-Merge-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.nasc");
        File.WriteAllBytes(path, contents);
        return path;
    }

    private sealed record GoogleMasterWriteResult(
        GoogleDriveFile File,
        AppData Data,
        string Fingerprint,
        PortableExportInfo ExportInfo);
}

internal sealed record GoogleDriveSnapshot(
    GoogleDriveFileMetadata Metadata,
    string Fingerprint,
    PortableImport Contents,
    byte[] RawContents);

internal sealed record GoogleDriveSyncResult(
    string Action,
    GoogleDriveFileMetadata Metadata,
    string Fingerprint,
    DateTime ExportedUtc,
    string RevisionId,
    string SavedBy,
    int ClientCount,
    int EquipmentCount,
    string RecoveryBackupPath);
