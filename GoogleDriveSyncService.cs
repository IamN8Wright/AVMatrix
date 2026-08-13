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
        var remoteFinger×N¼¶‰žËkºwµçh€€€€€€€€€€€€¹Q½1¥ÍÐ ¤ì4(€€€€€€€‘…Ñ„¹5…ÍÑ•É•ÍÌ€ô5…ÍÑ•É•ÍÍM•ÉÙ¥”¹±½¹”¡Í…Ù•¹…Ñ„¹5…ÍÑ•É•ÍÌ¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð€ôÍ…Ù•¹¥¹•ÉÁÉ¥¹Ðì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•1½…±½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•I•µ½Ñ•¡…¹•Í•Ñ•Ñ•€ô™…±Í”ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•1…ÍÑMå¹UÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%€ô±¥•¹Ñ%ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ½­•¸€ô¡•­½ÕÐ¹¡•­½ÕÑQ½­•¸ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑUÍ•É¹…µ”€ôÍ•ÍÍ¥½¸¹UÍ•É¹…µ”ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ	…Í•±¥¹•¥¹•ÉÁÉ¥¹Ð€ô4(€€€€€€€€€€€Må¹½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð¹½µÁÕÑ•±¥•¹Ð¡¡•­•‘=ÕÑ±¥•¹Ð¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ…É•Ð€ô¹…µ•½˜¡Må¹Q…É•Ð¹½½±•É¥Ù”¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹5…ÍÑ•É]½É­ÍÁ…•I•…‘=¹±ä€ô™…±Í”ì4(€€€€€€€Må¹	…Í•±¥¹•MÑ½É”¹M…Ù”¡ÍÑ½É”°Må¹Q…É•Ð¹½½±•É¥Ù”°Í…Ù•¹¥±”¹½¹Ñ•¹ÑÌ¤ì4(€€€€€€€ÍÑ½É”¹M…Ù”¡‘…Ñ„¤ì4(€€€€€€€É•ÑÕÉ¸¹•Ü±¥•¹Ñ¡•­½ÕÑI•ÍÕ±Ð 4(€€€€€€€€€€€±¥•¹Ñ%°4(€€€€€€€€€€€¡•­•‘=ÕÑ±¥•¹Ð¹9…µ”°4(€€€€€€€€€€€¡•­½ÕÐ°4(€€€€€€€€€€€•á¥ÍÑ¥¹œ¥Ì¹½Ð¹Õ±°€˜˜€…½¹Ñ¥¹Õ¥¹=Ý¹¡•­½ÕÐ°4(€€€€€€€€€€€É•½Ù•Éä°4(€€€€€€€€€€€€‰½½±”É¥Ù”èíÍÕ‰µ…ÑÉ¥á9…µ•ôˆ¤ì4(€€€ô4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥Œ…Íå¹ŒQ…Í¬ñ½½±•É¥Ù•Må¹I•ÍÕ±Ðø¡•­%¹±¥•¹ÑÍå¹Œ 4(€€€€€€€ÁÁ…Ñ„‘…Ñ„°4(€€€€€€€…Ñ…MÑ½É”ÍÑ½É”°4(€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¸Í•ÍÍ¥½¸°4(€€€€€€€ÍÑÉ¥¹œüÁ…ÍÍÝ½É°4(€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸…¹•±±…Ñ¥½¹Q½­•¸€ô‘•™…Õ±Ð¤4(€€€ì4(€€€€€€€¥˜€¡‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ…É•Ð€„ô¹…µ•½˜¡Må¹Q…É•Ð¹½½±•É¥Ù”¤¤4(€€€€€€€€€€€Ñ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰Q¡”…Ñ¥Ù”¡•­½ÕÐ¥Ì¹½Ð±¥¹­•Ñ¼½½±”É¥Ù”¸ˆ¤ì4(€€€€€€€Ù…È±¥•¹Ñ%€ô‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%4(€€€€€€€€€€€€üüÑ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰9¼±¥•¹Ð¥Ì¡•­•½ÕÐ½¸Ñ¡¥ÌA¸ˆ¤ì4(€€€€€€€Ù…ÈÑ½­•¸€ô‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ½­•¸4(€€€€€€€€€€€€üüÑ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰Q¡”±½…°¡•­½ÕÐÑ½­•¸¥Ìµ¥ÍÍ¥¹œ¸ˆ¤ì4(€€€€€€€Ù…È±½…±±¥•¹Ð€ô‘…Ñ„¹±¥•¹ÑÌ¹M¥¹±•=É•™…Õ±Ð¡±¥•¹Ð€ôø±¥•¹Ð¹%€ôô±¥•¹Ñ%¤4(€€€€€€€€€€€€üüÑ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰Q¡”¡•­•µ½ÕÐ±¥•¹Ð¥Ì¹½Ð…Ù…¥±…‰±”±½…±±ä¸ˆ¤ì4(4(€€€€€€€Ù…È¥¹¥Ñ¥…°€ô…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹½Ý¹±½…‘Íå¹Œ¡‘…Ñ„¹M•ÑÑ¥¹Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€Ù…È¥¹¥Ñ¥…±…Ñ„€ôA½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%µÁ½ÉÑ	åÑ•Ì¡¥¹¥Ñ¥…°¹½¹Ñ•¹ÑÌ°Á…ÍÍÝ½É¤¹…Ñ„ì4(€€€€€€€5…ÍÑ•É•ÍÍM•ÉÙ¥”¹I•ÅÕ¥É•±¥•¹Ñ]É¥Ñ”¡¥¹¥Ñ¥…±…Ñ„¹5…ÍÑ•É•ÍÌ°Í•ÍÍ¥½¸°±¥•¹Ñ%¤ì4(€€€€€€€I•ÅÕ¥É•=Ý¹•‘¡•­½ÕÐ¡¥¹¥Ñ¥…±…Ñ„¹5…ÍÑ•É•ÍÌ°±¥•¹Ñ%°Ñ½­•¸°Í•ÍÍ¥½¸¤ì4(€€€€€€€Ù…ÈÉ•½Ù•Éä€ôM…Ù•I•µ½Ñ•I•½Ù•Éå	…­ÕÀ¡ÍÑ½É”°¥¹¥Ñ¥…°¹½¹Ñ•¹ÑÌ¤ì4(4(€€€€€€€Ù…ÈÍÕ‰µ…ÑÉ¥á9…µ”€ô±¥•¹ÑMÕ‰µ…ÑÉ¥áM•ÉÙ¥”¹½½±•É¥Ù•±¥•¹Ñ¥±•9…µ” 4(€€€€€€€€€€€¥¹¥Ñ¥…±…Ñ„¹5…ÍÑ•É•ÍÌ¹5…ÍÑ•É%°±¥•¹Ñ%¤ì4(€€€€€€€Ù…ÈÍÕ‰µ…ÑÉ¥á	åÑ•Ì€ôA½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹áÁ½ÉÑ	åÑ•Ì 4(€€€€€€€€€€€±¥•¹ÑMÕ‰µ…ÑÉ¥áM•ÉÙ¥”¹±¥•¹ÑA…­…”¡±½…±±¥•¹Ð¤°Á…ÍÍÝ½É°½ÕÐ|¤ì4(€€€€€€€Ù…È­¹½Ý¹I•™•É•¹”€ô¥¹¥Ñ¥…±…Ñ„¹5…ÍÑ•É•ÍÌ¹±¥•¹ÑMÕ‰µ…ÑÉ¥•Ì4(€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±Ð¡É•™•É•¹”€ôøÉ•™•É•¹”¹±¥•¹Ñ%€ôô±¥•¹Ñ%€˜˜4(€€€€€€€€€€€€€€€€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡É•™•É•¹”¹½½±•É¥Ù•¥±•%¤¤ì4(€€€€€€€Ù…ÈÍÕ‰µ…ÑÉ¥à€ô­¹½Ý¹I•™•É•¹”¥Ì¹Õ±°4(€€€€€€€€€€€€ü…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹¥¹‘M¥‰±¥¹Íå¹Œ 4(€€€€€€€€€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì°ÍÕ‰µ…ÑÉ¥á9…µ”°…¹•±±…Ñ¥½¹Q½­•¸¤4(€€€€€€€€€€€€è¹Õ±°ì4(€€€€€€€ÍÑÉ¥¹œÍÕ‰µ…ÑÉ¥á¥±•%ì4(€€€€€€€¥˜€¡­¹½Ý¹I•™•É•¹”¥Ì¹Õ±°€˜˜ÍÕ‰µ…ÑÉ¥à¥Ì¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÉ•…Ñ•€ô…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹É•…Ñ•M¥‰±¥¹Íå¹Œ 4(€€€€€€€€€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì°ÍÕ‰µ…ÑÉ¥á9…µ”°ÍÕ‰µ…ÑÉ¥á	åÑ•Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€€€€€ÍÕ‰µ…ÑÉ¥á¥±•%€ôÉ•…Ñ•¹%ì4(€€€€€€€ô4(€€€€€€€•±Í”4(€€€€€€€ì4(€€€€€€€€€€€ÍÕ‰µ…ÑÉ¥á¥±•%€ô­¹½Ý¹I•™•É•¹”ü¹½½±•É¥Ù•¥±•%€üüÍÕ‰µ…ÑÉ¥à„¹%ì4(€€€€€€€€€€€Ù…ÈÁÉ•Ù¥½ÕÌ€ô…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹½Ý¹±½…‘	å%‘Íå¹Œ 4(€€€€€€€€€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì°ÍÕ‰µ…ÑÉ¥á¥±•%°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€€€€€M…Ù•I•µ½Ñ•I•½Ù•Éå	…­ÕÀ¡ÍÑ½É”°ÁÉ•Ù¥½ÕÌ¹½¹Ñ•¹ÑÌ¤ì4(€€€€€€€€€€€…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹UÁ±½…‘	å%‘Íå¹Œ 4(€€€€€€€€€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì°ÍÕ‰µ…ÑÉ¥á¥±•%°ÍÕ‰µ…ÑÉ¥á	åÑ•Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€ô4(4(€€€€€€€€¼¼I”µÉ•……™Ñ•ÈÑ¡”ÍÕˆµµ…ÑÉ¥àÕÁ±½…Í¼…¸¥¹Ñ•ÉÙ•¹¥¹œ‰½½Ð¥Ì‘•Ñ•Ñ•4(€€€€€€€€¼¼‰•™½É”…¹äµ…¥¸µµ…ÍÑ•È¥¹Ù•¹Ñ½Éä½È±½¬ÍÑ…Ñ”¥Ì¡…¹•¸4(€€€€€€€Ù…È±…Ñ•ÍÐ€ô…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹½Ý¹±½…‘Íå¹Œ¡‘…Ñ„¹M•ÑÑ¥¹Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€Ù…È±…Ñ•ÍÑ…Ñ„€ôA½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%µÁ½ÉÑ	åÑ•Ì¡±…Ñ•ÍÐ¹½¹Ñ•¹ÑÌ°Á…ÍÍÝ½É¤¹…Ñ„ì4(€€€€€€€5…ÍÑ•É•ÍÍM•ÉÙ¥”¹I•ÅÕ¥É•±¥•¹Ñ]É¥Ñ”¡±…Ñ•ÍÑ…Ñ„¹5…ÍÑ•É•ÍÌ°Í•ÍÍ¥½¸°±¥•¹Ñ%¤ì4(€€€€€€€Ù…È¡•­½ÕÐ€ôI•ÅÕ¥É•=Ý¹•‘¡•­½ÕÐ 4(€€€€€€€€€€€±…Ñ•ÍÑ…Ñ„¹5…ÍÑ•É•ÍÌ°±¥•¹Ñ%°Ñ½­•¸°Í•ÍÍ¥½¸¤ì4(€€€€€€€Ù…È±…Ñ•ÍÑI•™•É•¹”€ô±…Ñ•ÍÑ…Ñ„¹5…ÍÑ•É•ÍÌ¹±¥•¹ÑMÕ‰µ…ÑÉ¥•Ì4(€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±Ð¡É•™•É•¹”€ôøÉ•™•É•¹”¹±¥•¹Ñ%€ôô±¥•¹Ñ%¤ì4(€€€€€€€¥˜€¡±…Ñ•ÍÑI•™•É•¹”¥Ì¹Õ±°¤4(€€€€€€€ì4(€€€€€€€€€€€±…Ñ•ÍÑI•™•É•¹”€ô¹•Ü±¥•¹ÑMÕ‰µ…ÑÉ¥áI•™•É•¹”ì±¥•¹Ñ%€ô±¥•¹Ñ%ôì4(€€€€€€€€€€€±…Ñ•ÍÑ…Ñ„¹5…ÍÑ•É•ÍÌ¹±¥•¹ÑMÕ‰µ…ÑÉ¥•Ì¹‘¡±…Ñ•ÍÑI•™•É•¹”¤ì4(€€€€€€€ô4(€€€€€€€±…Ñ•ÍÑI•™•É•¹”¹½½±•É¥Ù•¥±•%€ôÍÕ‰µ…ÑÉ¥á¥±•%ì4(€€€€€€€±…Ñ•ÍÑI•™•É•¹”¹¥±•9…µ”€ôÍÕ‰µ…ÑÉ¥á9…µ”ì4(€€€€€€€±…Ñ•ÍÑI•™•É•¹”¹UÁ‘…Ñ•‘UÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üì4(€€€€€€€Ù…È¥¹‘•à€ô±…Ñ•ÍÑ…Ñ„¹±¥•¹ÑÌ¹¥¹‘%¹‘•à¡±¥•¹Ð€ôø±¥•¹Ð¹%€ôô±¥•¹Ñ%¤ì4(€€€€€€€¥˜€¡¥¹‘•à€ð€À¤4(€€€€€€€€€€€Ñ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰Q¡”¡•­•µ½ÕÐ±¥•¹ÐÝ…Ì‘•±•Ñ•™É½´Ñ¡”µ…ÍÑ•È¸ˆ¤ì4(€€€€€€€±…Ñ•ÍÑ…Ñ„¹±¥•¹ÑÍm¥¹‘•át€ô±¥•¹ÑMÕ‰µ…ÑÉ¥áM•ÉÙ¥”¹5•Ñ…‘…Ñ…=¹±ä¡±½…±±¥•¹Ð¤ì4(€€€€€€€±…Ñ•ÍÑ…Ñ„¹5…ÍÑ•É•ÍÌ¹¡•­½ÕÑÌ¹I•µ½Ù”¡¡•­½ÕÐ¤ì4(€€€€€€€Ù…ÈÍ…Ù•€ô…Ý…¥Ð]É¥Ñ•5…ÍÑ•É%™ÕÉÉ•¹ÑÍå¹Œ 4(€€€€€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì°4(€€€€€€€€€€€¥¹•ÉÁÉ¥¹Ð¡±…Ñ•ÍÐ¹½¹Ñ•¹ÑÌ¤°4(€€€€€€€€€€€±…Ñ•ÍÑ…Ñ„°4(€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸¤ì4(4(€€€€€€€‘…Ñ„¹AÉ½©•Ñ9…µ”€ôÍ…Ù•¹…Ñ„¹AÉ½©•Ñ9…µ”ì4(€€€€€€€‘…Ñ„¹±¥•¹ÑÌ€ôÍ…Ù•¹…Ñ„¹±¥•¹ÑÌ¹M•±•Ð¡±¥•¹ÑMÕ‰µ…ÑÉ¥áM•ÉÙ¥”¹5•Ñ…‘…Ñ…=¹±ä¤¹Q½1¥ÍÐ ¤ì4(€€€€€€€‘…Ñ„¹5…ÍÑ•É•ÍÌ€ô5…ÍÑ•É•ÍÍM•ÉÙ¥”¹±½¹”¡Í…Ù•¹…Ñ„¹5…ÍÑ•É•ÍÌ¤ì4(€€€€€€€M¡…É•‘Må¹M•ÉÙ¥”¹±•…ÉÑ¥Ù•¡•­½ÕÐ¡‘…Ñ„¹M•ÑÑ¥¹Ì¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð€ôÍ…Ù•¹¥¹•ÉÁÉ¥¹Ðì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•1½…±½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð€ôMå¹½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð¹½µÁÕÑ”¡‘…Ñ„¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•I•µ½Ñ•¡…¹•Í•Ñ•Ñ•€ô™…±Í”ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•1…ÍÑMå¹UÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üì4(€€€€€€€Må¹	…Í•±¥¹•MÑ½É”¹M…Ù”¡ÍÑ½É”°Må¹Q…É•Ð¹½½±•É¥Ù”°Í…Ù•¹¥±”¹½¹Ñ•¹ÑÌ¤ì4(€€€€€€€ÍÑ½É”¹M…Ù”¡‘…Ñ„¤ì4(€€€€€€€É•ÑÕÉ¸I•ÍÕ±Ð ‰±¥•¹Ð¡•­•¥¸ˆ°¹•Ü½½±•É¥Ù•M¹…ÁÍ¡½Ð 4(€€€€€€€€€€€Í…Ù•¹¥±”¹5•Ñ…‘…Ñ„°4(€€€€€€€€€€€Í…Ù•¹¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€€€€€¹•ÜA½ÉÑ…‰±•%µÁ½ÉÐ 4(€€€€€€€€€€€€€€€Í…Ù•¹…Ñ„°4(€€€€€€€€€€€€€€€Í…Ù•¹áÁ½ÉÑ%¹™¼¹áÁ½ÉÑ•‘UÑŒ°4(€€€€€€€€€€€€€€€ÁÁ%¹™¼¹I•Ù¥Í¥½¸°4(€€€€€€€€€€€€€€€Í…Ù•¹áÁ½ÉÑ%¹™¼¹I•Ù¥Í¥½¹%°4(€€€€€€€€€€€€€€€Í…Ù•¹áÁ½ÉÑ%¹™¼¹M…Ù•‘	ä°4(€€€€€€€€€€€€€€€€…ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡Á…ÍÍÝ½É¤¤°4(€€€€€€€€€€€Í…Ù•¹¥±”¹½¹Ñ•¹ÑÌ¤°É•½Ù•Éä¤ì4(€€€ô4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥Œ…Íå¹ŒQ…Í¬ñ½½±•É¥Ù•Må¹I•ÍÕ±ÐøI•±•…Í•¡•­½ÕÑÍå¹Œ 4(€€€€€€€ÁÁ…Ñ„‘…Ñ„°4(€€€€€€€…Ñ…MÑ½É”ÍÑ½É”°4(€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¸Í•ÍÍ¥½¸°4(€€€€€€€ÍÑÉ¥¹œüÁ…ÍÍÝ½É°4(€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸…¹•±±…Ñ¥½¹Q½­•¸€ô‘•™…Õ±Ð¤4(€€€ì4(€€€€€€€¥˜€¡‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ…É•Ð€„ô¹…µ•½˜¡Må¹Q…É•Ð¹½½±•É¥Ù”¤¤4(€€€€€€€€€€€Ñ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰Q¡”…Ñ¥Ù”¡•­½ÕÐ¥Ì¹½Ð±¥¹­•Ñ¼½½±”É¥Ù”¸ˆ¤ì4(€€€€€€€Ù…È±¥•¹Ñ%€ô‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑ±¥•¹Ñ%4(€€€€€€€€€€€€üüÑ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰9¼±¥•¹Ð¥Ì¡•­•½ÕÐ½¸Ñ¡¥ÌA¸ˆ¤ì4(€€€€€€€Ù…ÈÑ½­•¸€ô‘…Ñ„¹M•ÑÑ¥¹Ì¹Ñ¥Ù•¡•­½ÕÑQ½­•¸4(€€€€€€€€€€€€üüÑ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‰Q¡”±½…°¡•­½ÕÐÑ½­•¸¥Ìµ¥ÍÍ¥¹œ¸ˆ¤ì4(€€€€€€€Ù…ÈÉ•µ½Ñ”€ô…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹½Ý¹±½…‘Íå¹Œ¡‘…Ñ„¹M•ÑÑ¥¹Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€Ù…ÈÉ•µ½Ñ•…Ñ„€ôA½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%µÁ½ÉÑ	åÑ•Ì¡É•µ½Ñ”¹½¹Ñ•¹ÑÌ°Á…ÍÍÝ½É¤¹…Ñ„ì4(€€€€€€€5…ÍÑ•É•ÍÍM•ÉÙ¥”¹I•ÅÕ¥É•±¥•¹Ñ]É¥Ñ”¡É•µ½Ñ•…Ñ„¹5…ÍÑ•É•ÍÌ°Í•ÍÍ¥½¸°±¥•¹Ñ%¤ì4(€€€€€€€Ù…È¡•­½ÕÐ€ôI•ÅÕ¥É•=Ý¹•‘¡•­½ÕÐ¡É•µ½Ñ•…Ñ„¹5…ÍÑ•É•ÍÌ°±¥•¹Ñ%°Ñ½­•¸°Í•ÍÍ¥½¸¤ì4(€€€€€€€É•µ½Ñ•…Ñ„¹5…ÍÑ•É•ÍÌ¹¡•­½ÕÑÌ¹I•µ½Ù”¡¡•­½ÕÐ¤ì4(€€€€€€€Ù…ÈÍ…Ù•€ô…Ý…¥Ð]É¥Ñ•5…ÍÑ•É%™ÕÉÉ•¹ÑÍå¹Œ 4(€€€€€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì°4(€€€€€€€€€€€¥¹•ÉÁÉ¥¹Ð¡É•µ½Ñ”¹½¹Ñ•¹ÑÌ¤°4(€€€€€€€€€€€É•µ½Ñ•…Ñ„°4(€€€€€€€€€€€Í•ÍÍ¥½¸°4(€€€€€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€‘…Ñ„¹AÉ½©•Ñ9…µ”€ôÍ…Ù•¹…Ñ„¹AÉ½©•Ñ9…µ”ì4(€€€€€€€‘…Ñ„¹±¥•¹ÑÌ€ôÍ…Ù•¹…Ñ„¹±¥•¹ÑÌ¹M•±•Ð¡±¥•¹ÑMÕ‰µ…ÑÉ¥áM•ÉÙ¥”¹5•Ñ…‘…Ñ…=¹±ä¤¹Q½1¥ÍÐ ¤ì4(€€€€€€€‘…Ñ„¹5…ÍÑ•É•ÍÌ€ô5…ÍÑ•É•ÍÍM•ÉÙ¥”¹±½¹”¡Í…Ù•¹…Ñ„¹5…ÍÑ•É•ÍÌ¤ì4(€€€€€€€M¡…É•‘Må¹M•ÉÙ¥”¹±•…ÉÑ¥Ù•¡•­½ÕÐ¡‘…Ñ„¹M•ÑÑ¥¹Ì¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð€ôÍ…Ù•¹¥¹•ÉÁÉ¥¹Ðì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•1½…±½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð€ôMå¹½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð¹½µÁÕÑ”¡‘…Ñ„¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•I•µ½Ñ•¡…¹•Í•Ñ•Ñ•€ô™…±Í”ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•1…ÍÑMå¹UÑŒ€ô…Ñ•Q¥µ”¹UÑ9½Üì4(€€€€€€€Må¹	…Í•±¥¹•MÑ½É”¹M…Ù”¡ÍÑ½É”°Må¹Q…É•Ð¹½½±•É¥Ù”°Í…Ù•¹¥±”¹½¹Ñ•¹ÑÌ¤ì4(€€€€€€€ÍÑ½É”¹M…Ù”¡‘…Ñ„¤ì4(€€€€€€€É•ÑÕÉ¸I•ÍÕ±Ð ‰¡•­½ÕÐÉ•±•…Í•ˆ°¹•Ü½½±•É¥Ù•M¹…ÁÍ¡½Ð 4(€€€€€€€€€€€Í…Ù•¹¥±”¹5•Ñ…‘…Ñ„°4(€€€€€€€€€€€Í…Ù•¹¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€€€€€¹•ÜA½ÉÑ…‰±•%µÁ½ÉÐ 4(€€€€€€€€€€€€€€€Í…Ù•¹…Ñ„°4(€€€€€€€€€€€€€€€Í…Ù•¹áÁ½ÉÑ%¹™¼¹áÁ½ÉÑ•‘UÑŒ°4(€€€€€€€€€€€€€€€ÁÁ%¹™¼¹I•Ù¥Í¥½¸°4(€€€€€€€€€€€€€€€Í…Ù•¹áÁ½ÉÑ%¹™¼¹I•Ù¥Í¥½¹%°4(€€€€€€€€€€€€€€€Í…Ù•¹áÁ½ÉÑ%¹™¼¹M…Ù•‘	ä°4(€€€€€€€€€€€€€€€€…ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡Á…ÍÍÝ½É¤¤°4(€€€€€€€€€€€Í…Ù•¹¥±”¹½¹Ñ•¹ÑÌ¤°ÍÑÉ¥¹œ¹µÁÑä¤ì4(€€€ô4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥Œ‰½½°!…ÍáÑ•É¹…±¡…¹•Ì¡ÁÁ…Ñ„‘…Ñ„°½½±•É¥Ù•M¹…ÁÍ¡½ÐÍ¹…ÁÍ¡½Ð¤€ôø4(€€€€€€€€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð¤€˜˜4(€€€€€€€€…ÍÑÉ¥¹œ¹ÅÕ…±Ì¡‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð°Í¹…ÁÍ¡½Ð¹¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€€€€€MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ì4(4(€€€ÁÕ‰±¥ŒÍÑ…Ñ¥Œ‰½½°¹ÍÕÉ•	…Í•±¥¹•%™M…™” 4(€€€€€€€ÁÁ…Ñ„‘…Ñ„°4(€€€€€€€…Ñ…MÑ½É”ÍÑ½É”°4(€€€€€€€½½±•É¥Ù•M¹…ÁÍ¡½ÐÍ¹…ÁÍ¡½Ð¤4(€€€ì4(€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð¤ñð4(€€€€€€€€€€€€…ÍÑÉ¥¹œ¹ÅÕ…±Ì 4(€€€€€€€€€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€€€€€€€€€Í¹…ÁÍ¡½Ð¹¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€€€€€€€€€MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€Ù…È±½…±¥¹•ÉÁÉ¥¹Ð€ôMå¹½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð¹½µÁÕÑ”¡‘…Ñ„¤ì4(€€€€€€€Ù…ÈÉ•µ½Ñ•¥¹•ÉÁÉ¥¹Ð€ôMå¹½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð¹½µÁÕÑ”¡Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹…Ñ„¤ì4(€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹ÅÕ…±Ì¡±½…±¥¹•ÉÁÉ¥¹Ð°É•µ½Ñ•¥¹•ÉÁÉ¥¹Ð°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€Må¹	…Í•±¥¹•MÑ½É”¹M…Ù”¡ÍÑ½É”°Må¹Q…É•Ð¹½½±•É¥Ù”°Í¹…ÁÍ¡½Ð¹I…Ý½¹Ñ•¹ÑÌ¤ì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•1½…±½¹Ñ•¹Ñ¥¹•ÉÁÉ¥¹Ð€ô±½…±¥¹•ÉÁÉ¥¹Ðì4(€€€€€€€‘…Ñ„¹M•ÑÑ¥¹Ì¹½½±•É¥Ù•I•µ½Ñ•¡…¹•Í•Ñ•Ñ•€ô™…±Í”ì4(€€€€€€€ÍÑ½É”¹M…Ù”¡‘…Ñ„¤ì4(€€€€€€€É•ÑÕÉ¸ÑÉÕ”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ½½±•É¥Ù•Må¹I•ÍÕ±ÐI•ÍÕ±Ð 4(€€€€€€€ÍÑÉ¥¹œ…Ñ¥½¸°4(€€€€€€€½½±•É¥Ù•M¹…ÁÍ¡½ÐÍ¹…ÁÍ¡½Ð°4(€€€€€€€ÍÑÉ¥¹œÉ•½Ù•ÉåA…Ñ ¤€ôø¹•Ü 4(€€€€€€€…Ñ¥½¸°4(€€€€€€€Í¹…ÁÍ¡½Ð¹5•Ñ…‘…Ñ„°4(€€€€€€€Í¹…ÁÍ¡½Ð¹¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹áÁ½ÉÑ•‘UÑŒ°4(€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹I•Ù¥Í¥½¹%°4(€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹M…Ù•‘	ä°4(€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹±¥•¹Ñ½Õ¹Ð°4(€€€€€€€Í¹…ÁÍ¡½Ð¹½¹Ñ•¹ÑÌ¹ÅÕ¥Áµ•¹Ñ½Õ¹Ð°4(€€€€€€€É•½Ù•ÉåA…Ñ ¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œÉ•…Ñ•I•½Ù•Éå	…­ÕÀ¡ÁÁ…Ñ„‘…Ñ„°…Ñ…MÑ½É”ÍÑ½É”°ÍÑÉ¥¹œüÁ…ÍÍÝ½É¤4(€€€ì4(€€€€€€€Ù…È‘¥É•Ñ½Éä€ôA…Ñ ¹½µ‰¥¹”¡ÍÑ½É”¹…Ñ…¥É•Ñ½Éä°€‰M¡…É•‘Må¹	…­ÕÁÌˆ¤ì4(€€€€€€€¥É•Ñ½Éä¹É•…Ñ•¥É•Ñ½Éä¡‘¥É•Ñ½Éä¤ì4(€€€€€€€Ù…ÈÁ…Ñ €ôA…Ñ ¹½µ‰¥¹”¡‘¥É•Ñ½Éä°4(€€€€€€€€€€€€‰	•™½É”µ½½±”µAÕ±°µí…Ñ•Q¥µ”¹9½Üéåååäµ54µ‘µ!!µµÍÌµ™™™ô¹¹…ÍŒˆ¤ì4(€€€€€€€A½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹áÁ½ÉÐ¡Á…Ñ °‘…Ñ„°Á…ÍÍÝ½É¤ì4(€€€€€€€É•ÑÕÉ¸Á…Ñ ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ…Íå¹ŒQ…Í¬ñ½½±•5…ÍÑ•É]É¥Ñ•I•ÍÕ±Ðø]É¥Ñ•5…ÍÑ•É%™ÕÉÉ•¹ÑÍå¹Œ 4(€€€€€€€ÁÁM•ÑÑ¥¹ÌÍ•ÑÑ¥¹Ì°4(€€€€€€€ÍÑÉ¥¹œ•áÁ•Ñ•‘¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€ÁÁ…Ñ„‘…Ñ„°4(€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¸Í•ÍÍ¥½¸°4(€€€€€€€…¹•±±…Ñ¥½¹Q½­•¸…¹•±±…Ñ¥½¹Q½­•¸¤4(€€€ì4(€€€€€€€Ù…Èµ…ÍÑ•É…Ñ„€ô±¥•¹ÑMÕ‰µ…ÑÉ¥áM•ÉÙ¥”¹5…ÍÑ•É5•Ñ…‘…Ñ…=¹±ä¡‘…Ñ„¤ì4(€€€€€€€Ù…È‰åÑ•Ì€ôA½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹áÁ½ÉÑ5…ÍÑ•É	åÑ•Ì¡µ…ÍÑ•É…Ñ„°Í•ÍÍ¥½¸°½ÕÐÙ…È•áÁ½ÉÑ%¹™¼¤ì4(€€€€€€€Ù…È±…Ñ•ÍÐ€ô…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹½Ý¹±½…‘Íå¹Œ¡Í•ÑÑ¥¹Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹ÅÕ…±Ì 4(€€€€€€€€€€€€€€€¥¹•ÉÁÉ¥¹Ð¡±…Ñ•ÍÐ¹½¹Ñ•¹ÑÌ¤°•áÁ•Ñ•‘¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€€€€€€€€€MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€€€€€Ñ¡É½Ü¹•ÜM¡…É•‘5…ÍÑ•É½¹™±¥Ñá•ÁÑ¥½¸ 4(€€€€€€€€€€€€€€€€‰Q¡”½½±”É¥Ù”½µÁ…¹ä™¥±”¡…¹•‘ÕÉ¥¹œÑ¡¥Ì¡•­½ÕÐ½Á•É…Ñ¥½¸¸I•™É•Í …¹ÑÉä……¥¸¸ˆ¤ì4(€€€€€€€…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹UÁ±½…‘Íå¹Œ¡Í•ÑÑ¥¹Ì°‰åÑ•Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€Ù…ÈÍ…Ù•€ô…Ý…¥Ð½½±•É¥Ù•M•ÉÙ¥”¹½Ý¹±½…‘Íå¹Œ¡Í•ÑÑ¥¹Ì°…¹•±±…Ñ¥½¹Q½­•¸¤ì4(€€€€€€€Ù…ÈÍ…Ù•‘¥¹•ÉÁÉ¥¹Ð€ô¥¹•ÉÁÉ¥¹Ð¡Í…Ù•¹½¹Ñ•¹ÑÌ¤ì4(€€€€€€€¥˜€ …ÍÑÉ¥¹œ¹ÅÕ…±Ì¡Í…Ù•‘¥¹•ÉÁÉ¥¹Ð°¥¹•ÉÁÉ¥¹Ð¡‰åÑ•Ì¤°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€€€€€Ñ¡É½Ü¹•ÜM¡…É•‘5…ÍÑ•É½¹™±¥Ñá•ÁÑ¥½¸ 4(€€€€€€€€€€€€€€€€‰¹½Ñ¡•È½½±”É¥Ù”ÕÁ‘…Ñ”±…¹‘•…ÐÑ¡”Í…µ”Ñ¥µ”¸Q¡”¡•­½ÕÐ½Á•É…Ñ¥½¸Ý…Ì¹½Ð½¹™¥Éµ•ìÉ•™É•Í ‰•™½É”½¹Ñ¥¹Õ¥¹œ¸ˆ¤ì4(€€€€€€€Ù…ÈÍ…Ù•‘…Ñ„€ôA½ÉÑ…‰±•…Ñ…M•ÉÙ¥”¹%µÁ½ÉÑ	åÑ•Ì¡Í…Ù•¹½¹Ñ•¹ÑÌ°Í•ÍÍ¥½¸¹5…ÍÑ•É-•ä¤¹…Ñ„ì4(€€€€€€€É•ÑÕÉ¸¹•Ü½½±•5…ÍÑ•É]É¥Ñ•I•ÍÕ±Ð¡Í…Ù•°Í…Ù•‘…Ñ„°Í…Ù•‘¥¹•ÉÁÉ¥¹Ð°•áÁ½ÉÑ%¹™¼¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ±¥•¹Ñ¡•­½ÕÑI•½ÉI•ÅÕ¥É•=Ý¹•‘¡•­½ÕÐ 4(€€€€€€€5…ÍÑ•É•ÍÍ½¹ÑÉ½°…•ÍÌ°4(€€€€€€€Õ¥±¥•¹Ñ%°4(€€€€€€€Õ¥Ñ½­•¸°4(€€€€€€€5…ÍÑ•ÉM•ÍÍ¥½¸Í•ÍÍ¥½¸¤€ôø4(€€€€€€€…•ÍÌ¹¡•­½ÕÑÌ¹¥ÉÍÑ=É•™…Õ±Ð¡¡•­½ÕÐ€ôø4(€€€€€€€€€€€¡•­½ÕÐ¹±¥•¹Ñ%€ôô±¥•¹Ñ%€˜˜4(€€€€€€€€€€€¡•­½ÕÐ¹¡•­½ÕÑQ½­•¸€ôôÑ½­•¸€˜˜4(€€€€€€€€€€€¡•­½ÕÐ¹UÍ•É%€ôôÍ•ÍÍ¥½¸¹UÍ•É%¤4(€€€€€€€€üüÑ¡É½Ü¹•Ü¡•­½ÕÑ=Ý¹•ÉÍ¡¥Á1½ÍÑá•ÁÑ¥½¸ 4(€€€€€€€€€€€€‰Q¡¥Ì¡•­½ÕÐÝ…ÌÉ•±•…Í•½È‰½½Ñ•‰ä…¹½Ñ¡•ÈÑ•¡¹¥¥…¸¸€ˆ€¬4(€€€€€€€€€€€€‰e½ÕÈ±½…°Ý½É¬É•µ…¥¹Ì½¸Ñ¡¥ÌA¸¡•¬½ÕÐÑ¡”±¥•¹Ð……¥¸‰•™½É”ÁÕÍ¡¥¹œ¥Ð¸ˆ¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ¥¹•ÉÁÉ¥¹Ð¡‰åÑ•mt½¹Ñ•¹ÑÌ¤€ôø4(€€€€€€€½¹Ù•ÉÐ¹Q½!•áMÑÉ¥¹œ¡M!ÈÔØ¹!…Í¡…Ñ„¡½¹Ñ•¹ÑÌ¤¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œM…Ù•I•µ½Ñ•I•½Ù•Éå	…­ÕÀ¡…Ñ…MÑ½É”ÍÑ½É”°‰åÑ•mt½¹Ñ•¹ÑÌ¤4(€€€ì4(€€€€€€€Ù…È‘¥É•Ñ½Éä€ôA…Ñ ¹½µ‰¥¹”¡ÍÑ½É”¹…Ñ…¥É•Ñ½Éä°€‰M¡…É•‘Må¹	…­ÕÁÌˆ¤ì4(€€€€€€€¥É•Ñ½Éä¹É•…Ñ•¥É•Ñ½Éä¡‘¥É•Ñ½Éä¤ì4(€€€€€€€Ù…ÈÁ…Ñ €ôA…Ñ ¹½µ‰¥¹”¡‘¥É•Ñ½Éä°4(€€€€€€€€€€€€‰	•™½É”µ½½±”µ5•É”µí…Ñ•Q¥µ”¹9½Üéåååäµ54µ‘µ!!µµÍÌµ™™™ô¹¹…ÍŒˆ¤ì4(€€€€€€€¥±”¹]É¥Ñ•±±	åÑ•Ì¡Á…Ñ °½¹Ñ•¹ÑÌ¤ì4(€€€€€€€É•ÑÕÉ¸Á…Ñ ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½É½½±•5…ÍÑ•É]É¥Ñ•I•ÍÕ±Ð 4(€€€€€€€½½±•É¥Ù•¥±”¥±”°4(€€€€€€€ÁÁ…Ñ„…Ñ„°4(€€€€€€€ÍÑÉ¥¹œ¥¹•ÉÁÉ¥¹Ð°4(€€€€€€€A½ÉÑ…‰±•áÁ½ÉÑ%¹™¼áÁ½ÉÑ%¹™¼¤ì4)ô4(4)¥¹Ñ•É¹…°Í•…±•É•½É½½±•É¥Ù•M¹…ÁÍ¡½Ð 4(€€€½½±•É¥Ù•¥±•5•Ñ…‘…Ñ„5•Ñ…‘…Ñ„°4(€€€ÍÑÉ¥¹œ¥¹•ÉÁÉ¥¹Ð°4(€€€A½ÉÑ…‰±•%µÁ½ÉÐ½¹Ñ•¹ÑÌ°4(€€€‰åÑ•mtI…Ý½¹Ñ•¹ÑÌ¤ì4(4)¥¹Ñ•É¹…°Í•…±•É•½É½½±•É¥Ù•Må¹I•ÍÕ±Ð 4(€€€ÍÑÉ¥¹œÑ¥½¸°4(€€€½½±•É¥Ù•¥±•5•Ñ…‘…Ñ„5•Ñ…‘…Ñ„°4(€€€ÍÑÉ¥¹œ¥¹•ÉÁÉ¥¹Ð°4(€€€…Ñ•Q¥µ”áÁ½ÉÑ•‘UÑŒ°4(€€€ÍÑÉ¥¹œI•Ù¥Í¥½¹%°4(€€€ÍÑÉ¥¹œM…Ù•‘	ä°4(€€€¥¹Ð±¥•¹Ñ½Õ¹Ð°4(€€€¥¹ÐÅÕ¥Áµ•¹Ñ½Õ¹Ð°4(€€€ÍÑÉ¥¹œI•½Ù•Éå	…­ÕÁA…Ñ ¤ì4(