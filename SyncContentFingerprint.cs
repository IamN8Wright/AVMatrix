using System.Security.Cryptography;
using System.Text.Json;

namespace InNasc;

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
