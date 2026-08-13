using System.Security.Cryptography;
using System.Text.Json;

namespace InNasc;

internal enum SyncTarget
{
    SharedFile,
    GoogleDrive
}

internal static class SyncBaselineStore
{
    public static string SharedPath(DataStore store) =>
        Path.Combine(store.DataDirectory, "SharedCompanyBaseline.nasc");

    public static string GoogleDrivePath(DataStore store) =>
        Path.Combine(store.DataDirectory, "GoogleDriveCompanyBaseline.nasc");

    public static void Save(
        DataStore store,
        SyncTarget target,
        byte[] contents)
    {
        Directory.CreateDirectory(store.DataDirectory);
        var path = PathFor(store, target);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, contents);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static AppData Load(
        DataStore store,
        SyncTarget target,
        string expectedFingerprint,
        string? password)
    {
        var path = PathFor(store, target);
        if (!File.Exists(path))
            throw new SharedMasterConflictException(
                "Pull the master once with this revision before merging. " +
                "That pull creates the common baseline used to combine changes safely.");
        var contents = File.ReadAllBytes(path);
        var fingerprint = Fingerprint(contents);
        if (!string.Equals(fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new SharedMasterConflictException(
                "The saved merge baseline does not match the last synced revision. " +
                "Pull the master again before merging.");
        return PortableDataService.ImportBytes(contents, password).Data;
    }

    public static void Delete(DataStore store, SyncTarget target)
    {
        var path = ReadPathFor(store, target);
        if (File.Exists(path)) File.Delete(path);
        var legacyPath = LegacyPathFor(store, target);
        if (File.Exists(legacyPath)) File.Delete(legacyPath);
    }

    public static string Fingerprint(byte[] contents) =>
        Convert.ToHexString(SHA256.HashData(contents));

    private static string PathFor(DataStore store, SyncTarget target) => target switch
    {
        SyncTarget.SharedFile => SharedPath(store),
        SyncTarget.GoogleDrive => GoogleDrivePath(store),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    private static string ReadPathFor(DataStore store, SyncTarget target)
    {
        var current = PathFor(store, target);
        if (File.Exists(current)) return current;
        var legacy = LegacyPathFor(store, target);
        return File.Exists(legacy) ? legacy : current;
    }

    private static string LegacyPathFor(DataStore store, SyncTarget target) => target switch
    {
        SyncTarget.SharedFile => Path.Combine(store.DataDirectory, "SharedMasterBaseline.avmatrix"),
        SyncTarget.GoogleDrive => Path.Combine(store.DataDirectory, "GoogleDriveMasterBaseline.avmatrix"),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };
}

internal static class SyncContentFingerprint
{
    public static string ComputeClient(ClientRecord client) => Compute(new AppData
    {
        ProjectName = string.Empty,
        Clients = [client]
    });

    public static string Compute(AppData data)
    {
        var collaborativeData = new
        {
            data.ProjectName,
            Clients = data.Clients.Select(client => new
            {
                client.Id,
                client.Name,
                client.Address,
                client.LogoBase64,
                client.Notes,
                Locations = client.Locations.Select(location => new
                {
                    location.Id,
                    location.Name,
                    location.Address,
                    location.Notes,
                    Rooms = location.Rooms.Select(room => new
                    {
                        room.Id,
                        room.Name,
                        room.Notes,
                        Equipment = room.Equipment.Select(equipment => new
                        {
                            equipment.Id,
                            equipment.Description,
                            equipment.Manufacturer,
                            equipment.PartNumber,
                            equipment.EquipmentId,
                            equipment.Hostname,
                            equipment.SerialNumber,
                            equipment.Firmware,
                            equipment.PrimaryIp,
                            equipment.SecondaryIp,
                            equipment.TargetIp,
                            equipment.DanteIp,
                            equipment.Subnet,
                            equipment.Gateway,
                            equipment.Mac1,
                            equipment.Mac2,
                            equipment.Mac3,
                            equipment.SerialConnection,
                            equipment.Username,
                            equipment.Password,
                            equipment.Notes,
                            equipment.SourceFile,
                            equipment.CreatedUtc,
                            equipment.UpdatedUtc,
                            Interfaces = equipment.NetworkInterfaces.Select(networkInterface => new
                            {
                                networkInterface.Id,
                                networkInterface.Type,
                                networkInterface.IpAddress,
                                networkInterface.MacAddress
                            }),
                            ConfigurationFiles = equipment.ConfigurationFiles.Select(file => new
                            {
                                file.Id,
                                file.FileName,
                                file.ContentType,
                                file.SizeBytes,
                                file.Sha256,
                                file.Notes,
                                file.AddedBy,
                                file.AddedUtc
                            })
                        })
                    })
                })
            })
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(collaborativeData);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
