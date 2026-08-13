using System.Text.Json;
using System.Text.Json.Serialization;

namespace InNasc;

public sealed class DataStore
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InNasc");

    public string LegacyDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AVMatrixStudio");

    public string DataPath => Path.Combine(DataDirectory, "innasc-data.json");
    public string BackupPath => Path.Combine(DataDirectory, "innasc-data.backup.json");

    public AppData Load()
    {
        MigrateLegacyLocalData();
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(DataPath)) return CreateInitialData();

        try
        {
            var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(DataPath), _options);
            if (data is null) return CreateInitialData();
            Normalize(data);
            return data;
        }
        catch
        {
            if (File.Exists(BackupPath))
            {
                try
                {
                    var backup = JsonSerializer.Deserialize<AppData>(File.ReadAllText(BackupPath), _options);
                    if (backup is not null)
                    {
                        Normalize(backup);
                        return backup;
                    }
                }
                catch
                {
                    // Fall through to a safe new data set.
                }
            }
            return CreateInitialData();
        }
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(DataDirectory);
        Normalize(data);
        var json = JsonSerializer.Serialize(data, _options);
        var temporaryPath = Path.Combine(DataDirectory, $"innasc-data.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaryPath, json);
        if (File.Exists(DataPath)) File.Copy(DataPath, BackupPath, true);
        File.Move(temporaryPath, DataPath, true);
    }

    private static AppData CreateInitialData() => new()
    {
        ProjectName = "InNasc",
        Clients = []
    };

    private void MigrateLegacyLocalData()
    {
        if (File.Exists(DataPath) || !Directory.Exists(LegacyDataDirectory)) return;
        Directory.CreateDirectory(DataDirectory);
        foreach (var source in Directory.EnumerateFiles(LegacyDataDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(LegacyDataDirectory, source);
            var destination = Path.Combine(DataDirectory, relative);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            if (!File.Exists(destination)) File.Copy(source, destination);
        }
        var legacyData = Path.Combine(DataDirectory, "av-matrix-data.json");
        var legacyBackup = Path.Combine(DataDirectory, "av-matrix-data.backup.json");
        if (File.Exists(legacyData) && !File.Exists(DataPath)) File.Copy(legacyData, DataPath);
        if (File.Exists(legacyBackup) && !File.Exists(BackupPath)) File.Copy(legacyBackup, BackupPath);
    }

    internal static void Normalize(AppData data)
    {
        data.SchemaVersion = 6;
        data.ProjectName ??= "InNasc";
        data.Settings ??= new AppSettings();
        data.Settings.SharedMasterPath ??= string.Empty;
        data.Settings.SharedMasterFingerprint ??= string.Empty;
        data.Settings.SharedLocalContentFingerprint ??= string.Empty;
        data.Settings.GoogleDriveShareLink ??= string.Empty;
        data.Settings.GoogleDriveFileId ??= string.Empty;
        data.Settings.GoogleDriveOAuthClientId ??= string.Empty;
        data.Settings.GoogleDriveFingerprint ??= string.Empty;
        data.Settings.GoogleDriveLocalContentFingerprint ??= string.Empty;
        data.Settings.ActiveCheckoutUsername ??= string.Empty;
        data.Settings.ActiveCheckoutBaselineFingerprint ??= string.Empty;
        data.Settings.ActiveCheckoutTarget ??= string.Empty;
        data.Settings.RecoveredCheckoutHolder ??= string.Empty;
        data.Settings.LastMasterTarget ??= string.Empty;
        data.MasterAccess ??= new MasterAccessControl();
        data.MasterAccess.Users ??= [];
        data.MasterAccess.Checkouts ??= [];
        data.MasterAccess.ClientSubmatrices ??= [];
        foreach (var user in data.MasterAccess.Users)
        {
            user.Username ??= string.Empty;
            user.DisplayName ??= string.Empty;
            user.PasswordSaltBase64 ??= string.Empty;
            user.PasswordHashBase64 ??= string.Empty;
            user.MasterKeySaltBase64 ??= string.Empty;
            user.MasterKeyNonceBase64 ??= string.Empty;
            user.MasterKeyCiphertextBase64 ??= string.Empty;
            user.MasterKeyTagBase64 ??= string.Empty;
            user.ClientAccessIds ??= [];
            user.ClientAccessIds = user.ClientAccessIds.Distinct().ToList();
            if (user.Role == MasterUserRole.Owner)
            {
                user.HasAllClientAccess = true;
                user.ClientAccessIds.Clear();
            }
            if (user.PasswordIterations < 100000) user.PasswordIterations = 310000;
        }
        foreach (var checkout in data.MasterAccess.Checkouts)
        {
            checkout.Username ??= string.Empty;
            checkout.DisplayName ??= string.Empty;
            checkout.MachineName ??= string.Empty;
        }
        foreach (var submatrix in data.MasterAccess.ClientSubmatrices)
        {
            submatrix.GoogleDriveFileId ??= string.Empty;
            submatrix.FileName ??= string.Empty;
        }
        data.Clients ??= [];
        foreach (var client in data.Clients)
        {
            client.Name ??= "Client";
            client.Address ??= string.Empty;
            client.LogoBase64 ??= string.Empty;
            client.Notes ??= string.Empty;
            client.Locations ??= [];
            foreach (var location in client.Locations)
            {
                location.Name ??= "Location";
                location.Address ??= string.Empty;
                location.Notes ??= string.Empty;
                location.Rooms ??= [];
                foreach (var room in location.Rooms)
                {
                    room.Name ??= "Room";
                    room.Notes ??= string.Empty;
                    room.Equipment ??= [];
                    foreach (var equipment in room.Equipment)
                    {
                        equipment.Description ??= string.Empty;
                        equipment.Manufacturer ??= string.Empty;
                        equipment.PartNumber ??= string.Empty;
                        equipment.EquipmentId ??= string.Empty;
                        equipment.Hostname ??= string.Empty;
                        equipment.SerialNumber ??= string.Empty;
                        equipment.Firmware ??= string.Empty;
                        equipment.PrimaryIp ??= string.Empty;
                        equipment.SecondaryIp ??= string.Empty;
                        equipment.TargetIp ??= string.Empty;
                        equipment.DanteIp ??= string.Empty;
                        equipment.Subnet ??= string.Empty;
                        equipment.Gateway ??= string.Empty;
                        equipment.Mac1 ??= string.Empty;
                        equipment.Mac2 ??= string.Empty;
                        equipment.Mac3 ??= string.Empty;
                        equipment.ConfigurationFiles ??= [];
                        foreach (var configurationFile in equipment.ConfigurationFiles)
                        {
                            configurationFile.FileName ??= string.Empty;
                            configurationFile.ContentType ??= "application/octet-stream";
                            configurationFile.Sha256 ??= string.Empty;
                            configurationFile.ContentBase64 ??= string.Empty;
                            configurationFile.Notes ??= string.Empty;
                            configurationFile.AddedBy ??= string.Empty;
                            if (!string.IsNullOrEmpty(configurationFile.ContentBase64))
                                configurationFile.ContentIncluded = true;
                        }
                        equipment.SerialConnection ??= string.Empty;
                        equipment.Username ??= string.Empty;
                        equipment.Password ??= string.Empty;
                        equipment.Notes ??= string.Empty;
                        equipment.SourceFile ??= string.Empty;
                        equipment.LastNetworkError ??= string.Empty;
                        equipment.EnsureNetworkInterfaces();
                    }
                }
            }
        }
    }
}
