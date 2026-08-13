using System.Text.Json;
using System.Text.Json.Serialization;

namespace InNasc;

internal static class InNascCompanyOpenService
{
    private static readonly JsonSerializerOptions ParkingJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Open(
        AppData data,
        DataStore store,
        InNascCompanySelection selection)
    {
        var company = selection.Company;
        var session = selection.CompanySession;
        if (!File.Exists(company.FilePath))
            throw new FileNotFoundException(
                $"The company file for {company.Name} could not be found.", company.FilePath);

        ParkCheckoutIfSwitchingCompanies(data, store, company.FilePath);

        _ = SharedSyncService.LinkPath(company.FilePath, data, store);
        var snapshot = SharedSyncService.Inspect(company.FilePath, company.CompanyKeyBase64);

        if (!TryRestoreParkedCheckout(data, store, snapshot, session))
        {
            var hasCheckout = data.Settings.ActiveCheckoutClientId.HasValue &&
                              data.Settings.ActiveCheckoutToken.HasValue;
            var canResume = hasCheckout &&
                            CanResumeCheckout(data, snapshot.Contents.Data.MasterAccess, session);
            if (!canResume)
                _ = SharedSyncService.Pull(data, store, company.CompanyKeyBase64, session);
            else
            {
                data.MasterAccess = MasterAccessService.Clone(snapshot.Contents.Data.MasterAccess);
                data.Settings.SharedMasterFingerprint = snapshot.Fingerprint;
                data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
                store.Save(data);
            }
        }

        data.ProjectName = company.Name;
        data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
        store.Save(data);
        MasterSessionContext.Set(SyncTarget.SharedFile, company.FilePath, session);
        InNascGlobalSessionContext.Set(
            selection.GlobalSession,
            InNascGlobalSessionContext.CatalogPath,
            company.Id);
    }

    private static void ParkCheckoutIfSwitchingCompanies(
        AppData data,
        DataStore store,
        string destinationCompanyPath)
    {
        var clientId = data.Settings.ActiveCheckoutClientId;
        var checkoutToken = data.Settings.ActiveCheckoutToken;
        if (!clientId.HasValue || !checkoutToken.HasValue)
            return;

        var currentCompanyPath = data.Settings.SharedMasterPath;
        if (string.IsNullOrWhiteSpace(currentCompanyPath) ||
            SamePath(currentCompanyPath, destinationCompanyPath))
            return;

        var localClient = data.Clients.FirstOrDefault(client => client.Id == clientId.Value);
        if (localClient is not null)
        {
            var parked = LoadParkedCheckouts(store);
            parked.RemoveAll(item => SamePath(item.CompanyPath, currentCompanyPath));
            parked.Add(new ParkedCompanyCheckout
            {
                CompanyPath = Path.GetFullPath(currentCompanyPath),
                CompanyName = data.ProjectName,
                ClientId = clientId.Value,
                CheckoutToken = checkoutToken.Value,
                Username = data.Settings.ActiveCheckoutUsername,
                BaselineFingerprint = data.Settings.ActiveCheckoutBaselineFingerprint,
                Client = ClientSubmatrixService.CloneClient(localClient),
                ParkedUtc = DateTime.UtcNow
            });
            SaveParkedCheckouts(store, parked);
        }

        SharedSyncService.ClearActiveCheckout(data.Settings);
        store.Save(data);
    }

    private static bool TryRestoreParkedCheckout(
        AppData data,
        DataStore store,
        SharedMasterSnapshot snapshot,
        MasterSession session)
    {
        var parked = LoadParkedCheckouts(store);
        var pending = parked
            .Where(item => SamePath(item.CompanyPath, snapshot.Path))
            .OrderByDescending(item => item.ParkedUtc)
            .FirstOrDefault();
        if (pending is null)
            return false;

        var checkout = snapshot.Contents.Data.MasterAccess.Checkouts.FirstOrDefault(item =>
            item.ClientId == pending.ClientId &&
            item.CheckoutToken == pending.CheckoutToken &&
            item.UserId == session.UserId);
        if (checkout is null)
            return false;

        var remoteData = snapshot.Contents.Data;
        data.ProjectName = remoteData.ProjectName;
        data.Clients = remoteData.Clients
            .Select(client => client.Id == pending.ClientId
                ? ClientSubmatrixService.CloneClient(pending.Client)
                : ClientSubmatrixService.MetadataOnly(client))
            .ToList();
        data.MasterAccess = MasterAccessService.Clone(remoteData.MasterAccess);
        data.Settings.SharedMasterPath = snapshot.Path;
        data.Settings.SharedMasterFingerprint = snapshot.Fingerprint;
        data.Settings.SharedLocalContentFingerprint = string.Empty;
        data.Settings.SharedMasterLastSyncUtc = DateTime.UtcNow;
        data.Settings.ActiveCheckoutClientId = pending.ClientId;
        data.Settings.ActiveCheckoutToken = pending.CheckoutToken;
        data.Settings.ActiveCheckoutUsername = session.Username;
        data.Settings.ActiveCheckoutBaselineFingerprint = pending.BaselineFingerprint;
        data.Settings.ActiveCheckoutTarget = nameof(SyncTarget.SharedFile);
        data.Settings.MasterWorkspaceReadOnly = false;
        data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
        SyncBaselineStore.Save(store, SyncTarget.SharedFile, snapshot.RawContents);

        parked.RemoveAll(item => SamePath(item.CompanyPath, snapshot.Path));
        SaveParkedCheckouts(store, parked);
        store.Save(data);
        return true;
    }

    internal static bool HasParkedCheckout(DataStore store, string companyPath) =>
        LoadParkedCheckouts(store).Any(item => SamePath(item.CompanyPath, companyPath));

    private static List<ParkedCompanyCheckout> LoadParkedCheckouts(DataStore store)
    {
        var path = ParkingPath(store);
        if (!File.Exists(path)) return [];
        try
        {
            var parked = JsonSerializer.Deserialize<List<ParkedCompanyCheckout>>(
                File.ReadAllText(path), ParkingJson) ?? [];
            foreach (var item in parked)
            {
                item.CompanyPath ??= string.Empty;
                item.CompanyName ??= string.Empty;
                item.Username ??= string.Empty;
                item.BaselineFingerprint ??= string.Empty;
                item.Client ??= new ClientRecord();
            }
            return parked;
        }
        catch
        {
            return [];
        }
    }

    private static void SaveParkedCheckouts(DataStore store, List<ParkedCompanyCheckout> parked)
    {
        Directory.CreateDirectory(store.DataDirectory);
        var path = ParkingPath(store);
        if (parked.Count == 0)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            return;
        }

        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(parked, ParkingJson));
        File.Move(temporary, path, true);
    }

    private static string ParkingPath(DataStore store) =>
        Path.Combine(store.DataDirectory, "company-checkouts.json");

    private static bool CanResumeCheckout(
        AppData data,
        MasterAccessControl access,
        MasterSession session)
    {
        if (!data.Settings.ActiveCheckoutClientId.HasValue ||
            !data.Settings.ActiveCheckoutToken.HasValue)
            return false;
        var checkout = access.Checkouts.FirstOrDefault(item =>
            item.ClientId == data.Settings.ActiveCheckoutClientId &&
            item.CheckoutToken == data.Settings.ActiveCheckoutToken);
        return checkout is not null && checkout.UserId == session.UserId;
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ParkedCompanyCheckout
    {
        public string CompanyPath { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public Guid ClientId { get; set; }
        public Guid CheckoutToken { get; set; }
        public string Username { get; set; } = string.Empty;
        public string BaselineFingerprint { get; set; } = string.Empty;
        public ClientRecord Client { get; set; } = new();
        public DateTime ParkedUtc { get; set; } = DateTime.UtcNow;
    }
}
