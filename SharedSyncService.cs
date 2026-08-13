using System.Security.Cryptography;
using System.Text;

namespace InNasc;

internal static class SharedSyncService
{
    public static SharedMasterSnapshot Inspect(string path, string? password = null)
    {
        var fullPath = ValidatePath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The company file could not be found.", fullPath);
        var contents = File.ReadAllBytes(fullPath);
        var imported = PortableDataService.ImportBytes(contents, password);
        return new SharedMasterSnapshot(
            fullPath,
            SyncBaselineStore.Fingerprint(contents),
            imported,
            contents.LongLength,
            contents);
    }

    public static SharedSyncResult Pull(
        AppData data,
        DataStore store,
        string? password = null,
        MasterSession? session = null)
    {
        var path = ValidatePath(data.Settings.SharedMasterPath);
        using var masterLock = AcquireLock(path);
        var snapshot = Inspect(path, password);
        MasterAccessService.RequireRead(snapshot.Contents.Data.MasterAccess, session);
        var recoveryBackup = CreateRecoveryBackup(data, store, password);

        data.ProjectName = snapshot.Contents.Data.ProjectName;
        data.Clients = snapshot.Contents.Data.Clients
            .Select(ClientSubmatrixService.MetadataOnly)
            .ToList();
        data.MasterAccess = snapshot.Contents.Data.MasterAccess;
        data.Settings.MasterWorkspaceReadOnly = session?.Role == MasterUserRole.ReadOnly;
        ClearActiveCheckout(data.Settings);
        data.Settings.SharedMasterPath = path;
        data.Settings.SharedMasterFingerprint = snapshot.Fingerprint;
        data.Settings.SharedLocalContentFingerprint = SyncContentFingerprint.Compute(data);
        data.Settings.SharedMasterLastSyncUtc = DateTime.UtcNow;
        SyncBaselineStore.Save(store, SyncTarget.SharedFile, snapshot.RawContents);
        DataStore.Normalize(data);
        store.Save(data);
        return Result(snapshot, "Pulled") with { RecoveryBackupPath = recoveryBackup };
    }

    public static SharedSyncResult Push(
        AppData data,
        DataStore store,
        string? password = null,
        MergeConflictPreference? conflictPreference = null,
        MasterSession? session = null)
    {
        var path = ValidatePath(data.Settings.SharedMasterPath);
        using var masterLock = AcquireLock(path);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "The linked master file is missing. Create a new master or link another file.", path);

        var remoteBytes = File.ReadAllBytes(path);
        var currentFingerprint = SyncBaselineStore.Fingerprint(remoteBytes);
        var remoteData = PortableDataService.ImportBytes(remoteBytes, password).Data;
        MasterAccessService.RequireWrite(remoteData.MasterAccess, session);
        var expectedFingerprint = data.Settings.SharedMasterFingerprint;
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
            throw new SharedMasterConflictException(
                "Pull the company file before your first push. This prevents overwriting work already in the file.");
        var localForMerge = MasterCheckoutPolicy.PreserveProtectedClients(
            data, remoteData, remoteData.MasterAccess, session);
        var dataToPush = localForMerge;
        var action = "Pushed";
        var recoveryBackup = string.Empty;
        if (!string.Equals(currentFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            var baseline = SyncBaselineStore.Load(
                store, SyncTarget.SharedFile, expectedFingerprint, password);
            var merge = AppDataMergeService.Merge(
                baseline, localForMerge, remoteData, conflictPreference);
            if (merge.Conflicts.Count > 0 && conflictPreference is null)
                throw new MergeResolutionRequiredException(merge.Conflicts);
            dataToPush = merge.Data;
            action = "Merged";
            recoveryBackup = SaveRemoteRecoveryBackup(store, remoteBytes, "Before-Shared-Merge");
        }

        var masterData = ClientSubmatrixService.MasterMetadataOnly(dataToPush);
        masterData.MasterAccess = remoteData.MasterAccess;
        PortableDataService.ExportMaster(
            path,
            masterData,
            session ?? throw new MasterAuthorizationException("Sign in to push this master."));
        var snapshot = Inspect(path, password);
        data.ProjectName = masterData.ProjectName;
        data.Clients = masterData.Clients;
        data.MasterAccess = masterData.MasterAccess;
        RememberSync(data, store, path, snapshot.Fingerprint, snapshot.RawContents);
        return Result(snapshot, action) with { RecoveryBackupPath = recoveryBackup };
    }

    public static SharedSyncResult CreateMaster(
        string path,
        AppData data,
        DataStore store,
        MasterSession session)
    {
        var fullPath = ValidatePath(path);
        using var masterLock = AcquireLock(fullPath);
        foreach (var client in data.Clients.Where(client => client.Locations
                     .SelectMany(location => location.Rooms)
                     .SelectMany(room => room.Equipment)
                     .SelectMany(equipment => equipment.ConfigurationFiles)
                     .Any(file => file.ContentIncluded)))
        {
            var submatrixPath = ClientSubmatrixService.SharedClientPath(fullPath, client.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(submatrixPath)!);
            PortableDataService.Export(
                submatrixPath,
                ClientSubmatrixService.ClientPackage(client),
                session.MasterKey);
        }
        PortableDataService.ExportMaster(
            fullPath,
            ClientSubmatrixService.MasterMetadataOnly(data),
            session);
        var snapshot = Inspect(fullPath, session.MasterKey);
        data.Clients = snapshot.Contents.Data.Clients
            .Select(ClientSubmatrixService.MetadataOnly)
            .ToList();
        data.MasterAccess = MasterAccessService.Clone(snapshot.Contents.Data.MasterAccess);
        RememberSync(data, store, fullPath, snapshot.Fingerprint, snapshot.RawContents);
        return Result(snapshot, "Created");
    }

    public static SharedMasterSnapshot LinkExisting(
        string path,
        AppData data,
        DataStore store,
        string? password = null)
    {
        var snapshot = Inspect(path, password);
        data.Settings.SharedMasterPath = snapshot.Path;
        data.Settings.SharedMasterFingerprint = string.Empty;
        data.Settings.SharedLocalContentFingerprint = string.Empty;
        data.Settings.SharedMasterLastSyncUtc = null;
        data.Settings.MasterWorkspaceReadOnly = false;
        SyncBaselineStore.Delete(store, SyncTarget.SharedFile);
        store.Save(data);
        return snapshot;
    }

    public static MasterAccessControl LinkPath(
        string path,
        AppData data,
        DataStore store,
        string? legacyPassword = null)
    {
        var fullPath = ValidatePath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The company file could not be found.", fullPath);
        var access = PortableDataService.ReadMasterAccess(fullPath, legacyPassword);
        data.Settings.SharedMasterPath = fullPath;
        data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
        data.Settings.SharedMasterFingerprint = string.Empty;
        data.Settings.SharedLocalContentFingerprint = string.Empty;
        data.Settings.SharedMasterLastSyncUtc = null;
        data.Settings.MasterWorkspaceReadOnly = false;
        SyncBaselineStore.Delete(store, SyncTarget.SharedFile);
        store.Save(data);
        return access;
    }

    public static int MigrateClientSubmatrices(
        AppData data,
        DataStore store,
        string? legacyPassword,
        MasterSession session)
    {
        var path = ValidatePath(data.Settings.SharedMasterPath);
        var snapshot = Inspect(path, legacyPassword);
        var migrated = 0;
        foreach (var client in snapshot.Contents.Data.Clients)
        {
            var submatrixPath = ClientSubmatrixService.SharedClientPath(path, client.Id);
            if (!File.Exists(submatrixPath)) continue;
            var previous = File.ReadAllBytes(submatrixPath);
            var package = PortableDataService.ImportBytes(previous, legacyPassword).Data;
            SaveRemoteRecoveryBackup(
                store,
                previous,
                "Before-Legacy-Submatrix-Migration");
            PortableDataService.Export(submatrixPath, package, session.MasterKey);
            migrated++;
        }
        return migrated;
    }

    public static void Unlink(AppData data, DataStore store)
    {
        data.Settings.SharedMasterPath = string.Empty;
        data.Settings.SharedMasterFingerprint = string.Empty;
        data.Settings.SharedLocalContentFingerprint = string.Empty;
        data.Settings.SharedMasterLastSyncUtc = null;
        SyncBaselineStore.Delete(store, SyncTarget.SharedFile);
        store.Save(data);
    }

    public static SharedSyncResult SaveAccessControl(
        AppData data,
        DataStore store,
        MasterAccessControl updatedAccess,
        MasterSession? session,
        bool initialSetup,
        string? password = null)
    {
        var path = ValidatePath(data.Settings.SharedMasterPath);
        using var masterLock = AcquireLock(path);
        var snapshot = Inspect(path, password);
        var remoteData = snapshot.Contents.Data;
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
        var writingSession = session ?? throw new MasterAuthorizationException(
            "The Owner session is required to secure the master account list.");
        PortableDataService.ExportMaster(
            path,
            ClientSubmatrixService.MasterMetadataOnly(remoteData),
            writingSession);
        var saved = Inspect(path, writingSession.MasterKey);
        data.MasterAccess = MasterAccessService.Clone(saved.Contents.Data.MasterAccess);
        data.Settings.SharedMasterFingerprint = saved.Fingerprint;
        data.Settings.SharedMasterLastSyncUtc = DateTime.UtcNow;
        SyncBaselineStore.Save(store, SyncTarget.SharedFile, saved.RawContents);
        store.Save(data);
        return Result(saved, initialSetup ? "Owner configured" : "Accounts updated");
    }

    public static ClientCheckoutResult CheckoutClient(
        AppData data,
        DataStore store,
        Guid clientId,
        MasterSession session,
        bool force,
        string? password = null)
    {
        var path = ValidatePath(data.Settings.SharedMasterPath);
        using var masterLock = AcquireLock(path);
        var snapshot = Inspect(path, password);
        var remoteData = snapshot.Contents.Data;
        MasterAccessService.RequireClientWrite(remoteData.MasterAccess, session, clientId);
        var clientMetadata = remoteData.Clients.FirstOrDefault(client => client.Id == clientId)
            ?? throw new InvalidOperationException("The selected client no longer exists in the master.");
        var existing = remoteData.MasterAccess.Checkouts
            .FirstOrDefault(checkout => checkout.ClientId == clientId);
        var continuingOwnCheckout = existing is not null &&
            existing.UserId == session.UserId &&
            data.Settings.ActiveCheckoutToken == existing.CheckoutToken;
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

        ClientRecord? payloadClient = null;
        var submatrixPath = ClientSubmatrixService.SharedClientPath(path, clientId);
        if (File.Exists(submatrixPath))
            payloadClient = ClientSubmatrixService.ReadClientPackage(
                File.ReadAllBytes(submatrixPath), clientId, password);
        var checkedOutClient = ClientSubmatrixService.CombineMetadataAndPayloads(
            clientMetadata, payloadClient);

        PortableDataService.ExportMaster(
            path,
            ClientSubmatrixService.MasterMetadataOnly(remoteData),
            session);
        var saved = Inspect(path, session.MasterKey);
        data.ProjectName = remoteData.ProjectName;
        data.Clients = remoteData.Clients
            .Select(client => client.Id == clientId
                ? checkedOutClient
                : ClientSubmatrixService.MetadataOnly(client))
            .ToList();
        data.MasterAccess = MasterAccessService.Clone(saved.Contents.Data.MasterAccess);
        data.Settings.SharedMasterFingerprint = saved.Fingerprint;
        data.Settings.SharedLocalContentFingerprint = string.Empty;
        data.Settings.SharedMasterLastSyncUtc = DateTime.UtcNow;
        data.Settings.ActiveCheckoutClientId = clientId;
        data.Settings.ActiveCheckoutToken = checkout.CheckoutToken;
        data.Settings.ActiveCheckoutUsername = session.Username;
        data.Settings.ActiveCheckoutBaselineFingerprint =
            SyncContentFingerprint.ComputeClient(checkedOutClient);
        data.Settings.ActiveCheckoutTarget = nameof(SyncTarget.SharedFile);
        data.Settings.MasterWorkspaceReadOnly = false;
        SyncBaselineStore.Save(store, SyncTarget.SharedFile, saved.RawContents);
        store.Save(data);
        return new ClientCheckoutResult(
            clientId,
            checkedOutClient.Name,
            checkout,
            existing is not null && !continuingOwnCheckout,
            recovery,
            submatrixPath);
    }

    public static SharedSyncResult CheckInClient(
        AppData data,
        DataStore store,
        MasterSession session,
        string? password = null)
    {
        var clientId = data.Settings.ActiveCheckoutClientId
            ?? throw new InvalidOperationException("No client is checked out on this PC.");
        var checkoutToken = data.Settings.ActiveCheckoutToken
            ?? throw new InvalidOperationException("The local checkout token is missing.");
        var localClient = data.Clients.SingleOrDefault(client => client.Id == clientId)
            ?? throw new InvalidOperationException("The checked-out client is not available locally.");
        var path = ValidatePath(data.Settings.SharedMasterPath);
        using var masterLock = AcquireLock(path);
        var snapshot = Inspect(path, password);
        var remoteData = snapshot.Contents.Data;
        MasterAccessService.RequireClientWrite(remoteData.MasterAccess, session, clientId);
        var checkout = remoteData.MasterAccess.Checkouts.FirstOrDefault(item =>
            item.ClientId == clientId && item.CheckoutToken == checkoutToken &&
            item.UserId == session.UserId);
        if (checkout is null)
            throw new CheckoutOwnershipLostException(
                "This checkout was released or booted by another technician. " +
                "Your local work was not overwritten. Check out the client again to merge it safely.");

        var remoteRecovery = SaveRemoteRecoveryBackup(
            store, snapshot.RawContents, "Before-Client-Check-In");
        var submatrixPath = ClientSubmatrixService.SharedClientPath(path, clientId);
        Directory.CreateDirectory(Path.GetDirectoryName(submatrixPath)!);
        if (File.Exists(submatrixPath))
            SaveRemoteRecoveryBackup(
                store, File.ReadAllBytes(submatrixPath), "Before-Client-Submatrix-Check-In");
        PortableDataService.Export(
            submatrixPath,
            ClientSubmatrixService.ClientPackage(localClient),
            password);

        var index = remoteData.Clients.FindIndex(client => client.Id == clientId);
        if (index < 0)
            throw new InvalidOperationException("The checked-out client was deleted from the master.");
        remoteData.Clients[index] = ClientSubmatrixService.MetadataOnly(localClient);
        remoteData.MasterAccess.Checkouts.Remove(checkout);
        PortableDataService.ExportMaster(
            path,
            ClientSubmatrixService.MasterMetadataOnly(remoteData),
            session);
        var saved = Inspect(path, session.MasterKey);
        data.ProjectName = saved.Contents.Data.ProjectName;
        data.Clients = saved.Contents.Data.Clients
            .Select(ClientSubmatrixService.MetadataOnly)
            .ToList();
        data.MasterAccess = MasterAccessService.Clone(saved.Contents.Data.MasterAccess);
        ClearActiveCheckout(data.Settings);
        RememberSync(data, store, path, saved.Fingerprint, saved.RawContents);
        return Result(saved, "Client checked in") with { RecoveryBackupPath = remoteRecovery };
    }

    public static SharedSyncResult ReleaseCheckout(
        AppData data,
        DataStore store,
        MasterSession session,
        string? password = null)
    {
        var clientId = data.Settings.ActiveCheckoutClientId
            ?? throw new InvalidOperationException("No client is checked out on this PC.");
        var checkoutToken = data.Settings.ActiveCheckoutToken
            ?? throw new InvalidOperationException("The local checkout token is missing.");
        var path = ValidatePath(data.Settings.SharedMasterPath);
        using var masterLock = AcquireLock(path);
        var snapshot = Inspect(path, password);
        var remoteData = snapshot.Contents.Data;
        MasterAccessService.RequireClientWrite(remoteData.MasterAccess, session, clientId);
        var checkout = remoteData.MasterAccess.Checkouts.FirstOrDefault(item =>
            item.ClientId == clientId && item.CheckoutToken == checkoutToken &&
            item.UserId == session.UserId);
        if (checkout is null)
            throw new CheckoutOwnershipLostException("This checkout is no longer held by this PC.");
        remoteData.MasterAccess.Checkouts.Remove(checkout);
        PortableDataService.ExportMaster(
            path,
            ClientSubmatrixService.MasterMetadataOnly(remoteData),
            session);
        var saved = Inspect(path, session.MasterKey);
        data.ProjectName = saved.Contents.Data.ProjectName;
        data.Clients = saved.Contents.Data.Clients
            .Select(ClientSubmatrixService.MetadataOnly)
            .ToList();
        data.MasterAccess = MasterAccessService.Clone(saved.Contents.Data.MasterAccess);
        ClearActiveCheckout(data.Settings);
        RememberSync(data, store, path, saved.Fingerprint, saved.RawContents);
        return Result(saved, "Checkout released");
    }

    public static bool HasExternalChanges(AppData data, SharedMasterSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(data.Settings.SharedMasterFingerprint) &&
        !string.Equals(
            data.Settings.SharedMasterFingerprint,
            snapshot.Fingerprint,
            StringComparison.OrdinalIgnoreCase);

    public static bool EnsureBaselineIfSafe(
        AppData data,
        DataStore store,
        SharedMasterSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(data.Settings.SharedMasterFingerprint) ||
            !string.Equals(
                data.Settings.SharedMasterFingerprint,
                snapshot.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
            return false;
        var localFingerprint = SyncContentFingerprint.Compute(data);
        var remoteFingerprint = SyncContentFingerprint.Compute(snapshot.Contents.Data);
        if (!string.Equals(localFingerprint, remoteFingerprint, StringComparison.OrdinalIgnoreCase))
            return false;
        SyncBaselineStore.Save(store, SyncTarget.SharedFile, snapshot.RawContents);
        data.Settings.SharedLocalContentFingerprint = localFingerprint;
        store.Save(data);
        return true;
    }

    private static SharedSyncResult Result(SharedMasterSnapshot snapshot, string action) => new(
        action,
        snapshot.Path,
        snapshot.Fingerprint,
        snapshot.Contents.ExportedUtc,
        snapshot.Contents.RevisionId,
        snapshot.Contents.SavedBy,
        snapshot.Contents.ClientCount,
        snapshot.Contents.EquipmentCount,
        string.Empty);

    private static string CreateRecoveryBackup(AppData data, DataStore store, string? password)
    {
        var directory = Path.Combine(store.DataDirectory, "SharedSyncBackups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"Before-Pull-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.nasc");
        PortableDataService.Export(path, data, password);
        return path;
    }

    private static void RememberSync(
        AppData data,
        DataStore store,
        string path,
        string fingerprint,
        byte[] baselineContents)
    {
        data.Settings.SharedMasterPath = path;
        data.Settings.SharedMasterFingerprint = fingerprint;
        data.Settings.SharedLocalContentFingerprint = SyncContentFingerprint.Compute(data);
        data.Settings.SharedMasterLastSyncUtc = DateTime.UtcNow;
        SyncBaselineStore.Save(store, SyncTarget.SharedFile, baselineContents);
        store.Save(data);
    }

    internal static void ClearActiveCheckout(AppSettings settings)
    {
        settings.ActiveCheckoutClientId = null;
        settings.ActiveCheckoutToken = null;
        settings.ActiveCheckoutUsername = string.Empty;
        settings.ActiveCheckoutBaselineFingerprint = string.Empty;
        settings.ActiveCheckoutTarget = string.Empty;
    }

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Link a company file first.");
        var fullPath = Path.GetFullPath(path.Trim());
        if (!InNascFileTypes.IsCompanyPath(fullPath))
            throw new InvalidDataException(
                "The company file must use .nasc. Legacy .avmatrix files remain readable for migration.");
        return fullPath;
    }

    internal static string Fingerprint(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string SaveRemoteRecoveryBackup(
        DataStore store,
        byte[] contents,
        string prefix)
    {
        var directory = Path.Combine(store.DataDirectory, "SharedSyncBackups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"{prefix}-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.nasc");
        File.WriteAllBytes(path, contents);
        return path;
    }

    private static MasterFileLock AcquireLock(string masterPath)
    {
        var directory = Path.GetDirectoryName(masterPath)
            ?? throw new InvalidOperationException("The company file path has no parent folder.");
        Directory.CreateDirectory(directory);
        var lockPath = masterPath + ".sync.lock";
        try
        {
            return new MasterFileLock(lockPath);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Another person is currently pushing or pulling this master file. Try again in a moment.",
                exception);
        }
    }

    private sealed class MasterFileLock : IDisposable
    {
        private readonly string _path;
        private readonly FileStream _stream;

        public MasterFileLock(string path)
        {
            _path = path;
            _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _stream.SetLength(0);
            using var writer = new StreamWriter(_stream, new UTF8Encoding(false), 1024, true);
            writer.Write($"{Environment.UserName} on {Environment.MachineName} | {DateTime.UtcNow:O}");
            writer.Flush();
            _stream.Flush(true);
        }

        public void Dispose()
        {
            _stream.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch
            {
                // A stale unlocked marker is safe; the next sync can reopen it.
            }
        }
    }
}

internal sealed record SharedMasterSnapshot(
    string Path,
    string Fingerprint,
    PortableImport Contents,
    long SizeBytes,
    byte[] RawContents);

internal sealed record SharedSyncResult(
    string Action,
    string Path,
    string Fingerprint,
    DateTime ExportedUtc,
    string RevisionId,
    string SavedBy,
    int ClientCount,
    int EquipmentCount,
    string RecoveryBackupPath);

internal sealed record ClientCheckoutResult(
    Guid ClientId,
    string ClientName,
    ClientCheckoutRecord Checkout,
    bool BootedPreviousCheckout,
    string RecoveryBackupPath,
    string SubmatrixLocation);

internal sealed class SharedMasterConflictException : InvalidOperationException
{
    public SharedMasterConflictException(string message) : base(message)
    {
    }
}
