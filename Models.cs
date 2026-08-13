using System.Text.Json.Serialization;
using System.Net;

namespace InNasc;

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 6;
    public string ProjectName { get; set; } = "InNasc";
    public List<ClientRecord> Clients { get; set; } = [];
    public MasterAccessControl MasterAccess { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public sealed class AppSettings
{
    public string SelectedNicId { get; set; } = string.Empty;
    public string SelectedSourceIpv4 { get; set; } = string.Empty;
    public int PingIntervalSeconds { get; set; } = 60;
    public int PingTimeoutMilliseconds { get; set; } = 1800;
    public bool DarkMode { get; set; }
    public string SharedMasterPath { get; set; } = string.Empty;
    public string SharedMasterFingerprint { get; set; } = string.Empty;
    public string SharedLocalContentFingerprint { get; set; } = string.Empty;
    public DateTime? SharedMasterLastSyncUtc { get; set; }
    public string GoogleDriveShareLink { get; set; } = string.Empty;
    public string GoogleDriveFileId { get; set; } = string.Empty;
    public string GoogleDriveOAuthClientId { get; set; } = string.Empty;
    public string GoogleDriveFingerprint { get; set; } = string.Empty;
    public string GoogleDriveLocalContentFingerprint { get; set; } = string.Empty;
    public bool GoogleDriveRemoteChangesDetected { get; set; }
    public DateTime? GoogleDriveLastSyncUtc { get; set; }
    public Guid? ActiveCheckoutClientId { get; set; }
    public Guid? ActiveCheckoutToken { get; set; }
    public string ActiveCheckoutUsername { get; set; } = string.Empty;
    public string ActiveCheckoutBaselineFingerprint { get; set; } = string.Empty;
    public string ActiveCheckoutTarget { get; set; } = string.Empty;
    public ClientRecord? RecoveredCheckoutClient { get; set; }
    public string RecoveredCheckoutHolder { get; set; } = string.Empty;
    public DateTime? RecoveredCheckoutUtc { get; set; }
    public bool MasterWorkspaceReadOnly { get; set; }
    public string LastMasterTarget { get; set; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MasterUserRole
{
    Owner,
    Tech,
    ReadOnly
}

public sealed class MasterUserRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MasterUserRole Role { get; set; } = MasterUserRole.Tech;
    public string PasswordSaltBase64 { get; set; } = string.Empty;
    public string PasswordHashBase64 { get; set; } = string.Empty;
    public int PasswordIterations { get; set; } = 310000;
    public string MasterKeySaltBase64 { get; set; } = string.Empty;
    public string MasterKeyNonceBase64 { get; set; } = string.Empty;
    public string MasterKeyCiphertextBase64 { get; set; } = string.Empty;
    public string MasterKeyTagBase64 { get; set; } = string.Empty;
    public bool HasAllClientAccess { get; set; } = true;
    public List<Guid> ClientAccessIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ClientCheckoutRecord
{
    public Guid ClientId { get; set; }
    public Guid CheckoutToken { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime CheckedOutUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MasterAccessControl
{
    public Guid MasterId { get; set; } = Guid.NewGuid();
    public List<MasterUserRecord> Users { get; set; } = [];
    public List<ClientCheckoutRecord> Checkouts { get; set; } = [];
    public List<ClientSubmatrixReference> ClientSubmatrices { get; set; } = [];

    [JsonIgnore]
    public bool IsConfigured => Users.Count > 0;
}

public sealed class ClientSubmatrixReference
{
    public Guid ClientId { get; set; }
    public string GoogleDriveFileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ClientRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Client";
    public string Address { get; set; } = string.Empty;
    public string LogoBase64 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<LocationRecord> Locations { get; set; } = [];
}

public sealed class LocationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Main Location";
    public string Address { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<RoomRecord> Rooms { get; set; } = [];
}

public sealed class RoomRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Room 1";
    public string Notes { get; set; } = string.Empty;
    public List<EquipmentRecord> Equipment { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkState
{
    Unknown,
    Reachable,
    Unreachable,
    NoAddress,
    Partial,
    MacMismatch
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkInterfaceType
{
    Main,
    Control,
    Dante,
    AVB,
    CobraNet,
    AES67
}

public sealed class NetworkInterfaceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NetworkInterfaceType Type { get; set; } = NetworkInterfaceType.Main;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public NetworkState NetworkState { get; set; } = NetworkState.Unknown;
    public DateTime? LastCheckedUtc { get; set; }
    public long? LastLatencyMs { get; set; }
    public string LastNetworkError { get; set; } = string.Empty;
    public string ObservedMacAddress { get; set; } = string.Empty;
    public string MacVerificationMessage { get; set; } = string.Empty;
    public bool HttpPortOpen { get; set; }
    public bool HttpsPortOpen { get; set; }

    [JsonIgnore]
    public bool HasAddress => !string.IsNullOrWhiteSpace(IpAddress);

    [JsonIgnore]
    public string PortalUrl => HttpsPortOpen
        ? $"https://{Ipv4AddressText.NormalizeOrOriginal(IpAddress)}"
        : HttpPortOpen
            ? $"http://{Ipv4AddressText.NormalizeOrOriginal(IpAddress)}"
            : string.Empty;

    public NetworkInterfaceRecord Clone() => new()
    {
        Type = Type,
        IpAddress = IpAddress,
        MacAddress = MacAddress,
        NetworkState = string.IsNullOrWhiteSpace(IpAddress) ? NetworkState.NoAddress : NetworkState.Unknown
    };
}

public sealed class DeviceConfigurationFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public bool ContentIncluded { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public string AddedBy { get; set; } = string.Empty;
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    public byte[] GetContents()
    {
        if (!ContentIncluded)
            throw new InvalidOperationException(
                "Check out this client to download its configuration-file contents.");
        try
        {
            return Convert.FromBase64String(ContentBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"Configuration file '{FileName}' is damaged.", exception);
        }
    }
}

public sealed class EquipmentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string EquipmentId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Firmware { get; set; } = string.Empty;
    public string PrimaryIp { get; set; } = string.Empty;
    public string SecondaryIp { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public string DanteIp { get; set; } = string.Empty;
    public string Subnet { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string Mac1 { get; set; } = string.Empty;
    public string Mac2 { get; set; } = string.Empty;
    public string Mac3 { get; set; } = string.Empty;
    public List<NetworkInterfaceRecord> NetworkInterfaces { get; set; } = [];
    public List<DeviceConfigurationFile> ConfigurationFiles { get; set; } = [];
    public string SerialConnection { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public NetworkState NetworkState { get; set; } = NetworkState.Unknown;
    public DateTime? LastCheckedUtc { get; set; }
    public long? LastLatencyMs { get; set; }
    public string LastNetworkError { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public void EnsureNetworkInterfaces()
    {
        NetworkInterfaces ??= [];
        if (NetworkInterfaces.Count == 0)
        {
            AddLegacyInterface(NetworkInterfaceType.Main, PrimaryIp, Mac1);
            AddLegacyInterface(NetworkInterfaceType.Control, SecondaryIp, Mac2);
            AddLegacyInterface(NetworkInterfaceType.Control, TargetIp, Mac3);
            AddLegacyInterface(NetworkInterfaceType.Dante, DanteIp, string.Empty);
        }
        if (NetworkInterfaces.Count == 0)
            NetworkInterfaces.Add(new NetworkInterfaceRecord { NetworkState = NetworkState.NoAddress });

        foreach (var networkInterface in NetworkInterfaces)
        {
            networkInterface.IpAddress = Ipv4AddressText.NormalizeOrOriginal(networkInterface.IpAddress);
            networkInterface.MacAddress = MacAddressText.NormalizeOrOriginal(networkInterface.MacAddress);
            networkInterface.ObservedMacAddress =
                MacAddressText.NormalizeOrOriginal(networkInterface.ObservedMacAddress);
            networkInterface.LastNetworkError ??= string.Empty;
            networkInterface.MacVerificationMessage ??= string.Empty;
            if (string.IsNullOrWhiteSpace(networkInterface.IpAddress))
                networkInterface.NetworkState = NetworkState.NoAddress;
        }
        SyncLegacyNetworkFields();
        UpdateAggregateNetworkState();
    }

    public void SyncLegacyNetworkFields()
    {
        NetworkInterfaces ??= [];
        var populated = NetworkInterfaces.Where(item => !string.IsNullOrWhiteSpace(item.IpAddress)).ToList();
        var main = populated.FirstOrDefault(item => item.Type == NetworkInterfaceType.Main)
            ?? populated.FirstOrDefault();
        PrimaryIp = main?.IpAddress ?? string.Empty;
        var nonMain = populated.Where(item => !ReferenceEquals(item, main)).ToList();
        SecondaryIp = nonMain.ElementAtOrDefault(0)?.IpAddress ?? string.Empty;
        TargetIp = nonMain.ElementAtOrDefault(1)?.IpAddress ?? string.Empty;
        DanteIp = populated.FirstOrDefault(item => item.Type == NetworkInterfaceType.Dante)?.IpAddress
            ?? string.Empty;
        Mac1 = populated.ElementAtOrDefault(0)?.MacAddress ?? string.Empty;
        Mac2 = populated.ElementAtOrDefault(1)?.MacAddress ?? string.Empty;
        Mac3 = populated.ElementAtOrDefault(2)?.MacAddress ?? string.Empty;
    }

    public void ResetNetworkVerification()
    {
        EnsureNetworkInterfaces();
        foreach (var networkInterface in NetworkInterfaces)
        {
            networkInterface.NetworkState = networkInterface.HasAddress
                ? NetworkState.Unknown
                : NetworkState.NoAddress;
            networkInterface.LastCheckedUtc = null;
            networkInterface.LastLatencyMs = null;
            networkInterface.LastNetworkError = string.Empty;
            networkInterface.ObservedMacAddress = string.Empty;
            networkInterface.MacVerificationMessage = string.Empty;
            networkInterface.HttpPortOpen = false;
            networkInterface.HttpsPortOpen = false;
        }
        UpdateAggregateNetworkState();
    }

    public void UpdateAggregateNetworkState()
    {
        NetworkInterfaces ??= [];
        var addressed = NetworkInterfaces.Where(item => item.HasAddress).ToList();
        if (addressed.Count == 0)
            NetworkState = NetworkState.NoAddress;
        else if (addressed.Any(item => item.NetworkState == NetworkState.MacMismatch))
            NetworkState = NetworkState.MacMismatch;
        else if (addressed.All(item => item.NetworkState == NetworkState.Reachable))
            NetworkState = NetworkState.Reachable;
        else if (addressed.Any(item => item.NetworkState == NetworkState.Reachable))
            NetworkState = NetworkState.Partial;
        else if (addressed.All(item => item.NetworkState == NetworkState.Unreachable))
            NetworkState = NetworkState.Unreachable;
        else
            NetworkState = NetworkState.Unknown;

        LastCheckedUtc = addressed.Max(item => item.LastCheckedUtc);
        LastLatencyMs = addressed.Where(item => item.LastLatencyMs.HasValue)
            .Select(item => item.LastLatencyMs).FirstOrDefault();
        LastNetworkError = string.Join("; ", addressed
            .Select(item => item.LastNetworkError)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct());
    }

    private void AddLegacyInterface(NetworkInterfaceType type, string? ipAddress, string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) && string.IsNullOrWhiteSpace(macAddress)) return;
        NetworkInterfaces.Add(new NetworkInterfaceRecord
        {
            Type = type,
            IpAddress = Ipv4AddressText.NormalizeOrOriginal(ipAddress),
            MacAddress = MacAddressText.NormalizeOrOriginal(macAddress),
            NetworkState = string.IsNullOrWhiteSpace(ipAddress) ? NetworkState.NoAddress : NetworkState
        });
    }

    public EquipmentRecord CloneForDuplicate()
    {
        return new EquipmentRecord
        {
            Description = Description,
            Manufacturer = Manufacturer,
            PartNumber = PartNumber,
            Firmware = Firmware,
            SerialConnection = SerialConnection,
            NetworkState = NetworkState.NoAddress,
            Notes = string.IsNullOrWhiteSpace(Notes)
                ? "Duplicated equipment record."
                : $"Duplicated equipment record.\r\n{Notes}"
        };
    }
}

public sealed record EquipmentContext(
    ClientRecord Client,
    LocationRecord Location,
    RoomRecord Room,
    EquipmentRecord Equipment);

public sealed record NetworkInterfaceContext(
    EquipmentContext EquipmentContext,
    NetworkInterfaceRecord NetworkInterface);

public sealed record NetworkAdapterChoice(
    string NicId,
    string NicName,
    string Description,
    string Ipv4Address,
    string SubnetMask)
{
    public override string ToString() => $"{NicName}  •  {Ipv4Address}";
}

public static class MacAddressText
{
    public static bool TryParse(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Any(character => !Uri.IsHexDigit(character) &&
                                   !char.IsWhiteSpace(character) &&
                                   character is not ':' and not '-' and not '.'))
            return false;
        var compact = new string(value.Where(Uri.IsHexDigit).ToArray());
        if (compact.Length != 12 || !compact.All(Uri.IsHexDigit)) return false;
        compact = compact.ToUpperInvariant();
        normalized = string.Join(":", Enumerable.Range(0, 6)
            .Select(index => compact.Substring(index * 2, 2)));
        return true;
    }

    public static string NormalizeOrOriginal(string? value) =>
        TryParse(value, out var normalized) ? normalized : value?.Trim() ?? string.Empty;

    public static bool EqualsNormalized(string? left, string? right) =>
        TryParse(left, out var normalizedLeft) && TryParse(right, out var normalizedRight) &&
        string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
}

public sealed record ImportedEquipment(
    string RoomName,
    EquipmentRecord Equipment,
    string SourceFile = "",
    string Worksheet = "",
    int RowNumber = 0,
    List<string>? Warnings = null)
{
    public IReadOnlyList<string> ImportWarnings => Warnings ?? [];
}

public static class Ipv4AddressText
{
    public static bool TryParse(string? value, out IPAddress address, out string normalized)
    {
        address = IPAddress.None;
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split('.');
        if (parts.Length != 4) return false;
        var octets = new byte[4];
        for (var index = 0; index < 4; index++)
        {
            if (!int.TryParse(parts[index], out var octet) || octet is < 0 or > 255)
                return false;
            octets[index] = (byte)octet;
        }

        normalized = string.Join(".", octets);
        address = new IPAddress(octets);
        return true;
    }

    public static string NormalizeOrOriginal(string? value) =>
        TryParse(value, out _, out var normalized) ? normalized : value?.Trim() ?? string.Empty;
}
