using System.Text.Json;

namespace InNasc;

internal static class ClientSubmatrixService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AppData MasterMetadataOnly(AppData source)
    {
        var clone = Clone(source);
        foreach (var client in clone.Clients) StripFileContents(client);
        return clone;
    }

    public static ClientRecord MetadataOnly(ClientRecord source)
    {
        var clone = Clone(source);
        StripFileContents(clone);
        return clone;
    }

    public static ClientRecord CloneClient(ClientRecord source) => Clone(source);
    public static EquipmentRecord CloneEquipment(EquipmentRecord source) => Clone(source);

    public static ClientRecord CombineMetadataAndPayloads(
        ClientRecord metadata,
        ClientRecord? payloadSource)
    {
        var result = Clone(metadata);
        if (payloadSource is null) return result;
        var payloadEquipment = payloadSource.Locations
            .SelectMany(location => location.Rooms)
            .SelectMany(room => room.Equipment)
            .ToDictionary(equipment => equipment.Id);
        foreach (var equipment in result.Locations
                     .SelectMany(location => location.Rooms)
                     .SelectMany(room => room.Equipment))
        {
            if (!payloadEquipment.TryGetValue(equipment.Id, out var sourceEquipment)) continue;
            var sourceFiles = sourceEquipment.ConfigurationFiles.ToDictionary(file => file.Id);
            foreach (var file in equipment.ConfigurationFiles)
            {
                if (!sourceFiles.TryGetValue(file.Id, out var sourceFile) ||
                    !sourceFile.ContentIncluded) continue;
                file.ContentBase64 = sourceFile.ContentBase64;
                file.ContentIncluded = true;
            }
            foreach (var sourceFile in sourceEquipment.ConfigurationFiles.Where(sourceFile =>
                         sourceFile.ContentIncluded &&
                         equipment.ConfigurationFiles.All(file => file.Id != sourceFile.Id)))
                equipment.ConfigurationFiles.Add(Clone(sourceFile));
        }
        return result;
    }

    public static string SharedDirectory(string masterPath)
    {
        var fullPath = Path.GetFullPath(masterPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The company path has no parent folder.");
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(fullPath) + ".clients");
    }

    public static string SharedClientPath(string masterPath, Guid clientId)
    {
        var directory = SharedDirectory(masterPath);
        var current = Path.Combine(directory, $"{clientId:N}.nascclient");
        var legacy = Path.Combine(directory, $"{clientId:N}.avclient");
        return File.Exists(current) || !File.Exists(legacy) ? current : legacy;
    }

    public static string GoogleDriveClientFileName(Guid masterId, Guid clientId) =>
        $"InNasc-{masterId:N}-{clientId:N}.nascclient";

    public static string LegacyGoogleDriveClientFileName(Guid masterId, Guid clientId) =>
        $"AVMatrix-{masterId:N}-{clientId:N}.avclient";

    public static AppData ClientPackage(ClientRecord client) => new()
    {
        ProjectName = client.Name,
        Clients = [Clone(client)],
        MasterAccess = new MasterAccessControl(),
        Settings = new AppSettings()
    };

    public static ClientRecord ReadClientPackage(byte[] contents, Guid clientId, string? password)
    {
        var imported = PortableDataService.ImportBytes(contents, password);
        var client = imported.Data.Clients.SingleOrDefault(candidate => candidate.Id == clientId)
            ?? throw new InvalidDataException(
                "The client package does not contain the expected client.");
        return client;
    }

    private static void StripFileContents(ClientRecord client)
    {
        foreach (var file in client.Locations
                     .SelectMany(location => location.Rooms)
                     .SelectMany(room => room.Equipment)
                     .SelectMany(equipment => equipment.ConfigurationFiles))
        {
            file.ContentBase64 = string.Empty;
            file.ContentIncluded = false;
        }
    }

    private static T Clone<T>(T source) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, Options), Options)
        ?? throw new InvalidOperationException("The InNasc company data could not be copied.");
}
