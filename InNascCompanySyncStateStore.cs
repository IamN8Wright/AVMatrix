using System.Text.Json;

namespace InNasc;

internal sealed class InNascCompanySyncState
{
    public string CompanyPath { get; set; } = string.Empty;
    public string GoogleDriveShareLink { get; set; } = string.Empty;
    public string GoogleDriveFileId { get; set; } = string.Empty;
    public string GoogleDriveFingerprint { get; set; } = string.Empty;
    public string GoogleDriveLocalContentFingerprint { get; set; } = string.Empty;
    public bool GoogleDriveRemoteChangesDetected { get; set; }
    public DateTime? GoogleDriveLastSyncUtc { get; set; }
}

internal static class InNascCompanySyncStateStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void CaptureCurrent(AppData data, DataStore store)
    {
        if (string.IsNullOrWhiteSpace(data.Settings.SharedMasterPath)) return;
        var companyPath = Normalize(data.Settings.SharedMasterPath);
        var states = Load(store);
        states.RemoveAll(x => SamePath(x.CompanyPath, companyPath));
        states.Add(new InNascCompanySyncState
        {
            CompanyPath = companyPath,
            GoogleDriveShareLink = data.Settings.GoogleDriveShareLink,
            GoogleDriveFileId = data.Settings.GoogleDriveFileId,
            GoogleDriveFingerprint = data.Settings.GoogleDriveFingerprint,
            GoogleDriveLocalContentFingerprint = data.Settings.GoogleDriveLocalContentFingerprint,
            GoogleDriveRemoteChangesDetected = data.Settings.GoogleDriveRemoteChangesDetected,
            GoogleDriveLastSyncUtc = data.Settings.GoogleDriveLastSyncUtc
        });
        Save(store, states);
    }

    public static void Apply(AppData data, DataStore store, string companyPath)
    {
        var normalized = Normalize(companyPath);
        var state = Load(store).FirstOrDefault(x => SamePath(x.CompanyPath, normalized));
        data.Settings.GoogleDriveShareLink = state?.GoogleDriveShareLink ?? string.Empty;
        data.Settings.GoogleDriveFileId = state?.GoogleDriveFileId ?? string.Empty;
        data.Settings.GoogleDriveFingerprint = state?.GoogleDriveFingerprint ?? string.Empty;
        data.Settings.GoogleDriveLocalContentFingerprint = state?.GoogleDriveLocalContentFingerprint ?? string.Empty;
        data.Settings.GoogleDriveRemoteChangesDetected = state?.GoogleDriveRemoteChangesDetected ?? false;
        data.Settings.GoogleDriveLastSyncUtc = state?.GoogleDriveLastSyncUtc;
        store.Save(data);
    }

    private static List<InNascCompanySyncState> Load(DataStore store)
    {
        var path = StorePath(store);
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<InNascCompanySyncState>>(
                File.ReadAllText(path), Json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void Save(DataStore store, List<InNascCompanySyncState> states)
    {
        Directory.CreateDirectory(store.DataDirectory);
        var path = StorePath(store);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(states, Json));
        File.Move(temp, path, true);
    }

    private static string StorePath(DataStore store) =>
        Path.Combine(store.DataDirectory, "company-sync-state.json");

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
