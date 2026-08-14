using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InNasc;

internal static class PortableDataService
{
    private const string FormatName = "InNasc Company";
    private const string LegacyFormatName = "AV Matrix Studio Transfer";
    private const int CurrentFormatVersion = 4;
    private const string AccountEnvelopeFormat = "InNasc Account Envelope";
    private const string LegacyAccountEnvelopeFormat = "AV Matrix Studio Account Envelope";
    private const int AccountEnvelopeVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PortableExportInfo Export(string path, AppData data, string? password = null)
    {
        var bytes = ExportBytes(data, password, out var info);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return info;
    }

    public static PortableExportInfo ExportMaster(
        string path,
        AppData data,
        MasterSession session)
    {
        var bytes = ExportMasterBytes(data, session, out var info);
        WriteAtomically(path, bytes);
        return info;
    }

    public static byte[] ExportMasterBytes(
        AppData data,
        MasterSession session,
        out PortableExportInfo info)
    {
        DeviceLimitPolicy.RequireWithinLimit(data.MasterAccess, data);
        if (string.IsNullOrWhiteSpace(session.MasterKey))
        {
            // Legacy unencrypted company files remain usable during migration.
            return ExportBytes(data, null, out info);
        }

        var payload = ExportBytes(data, session.MasterKey, out info);
        var access = MasterAccessService.Clone(data.MasterAccess);
        access.Checkouts = [];
        access.ClientSubmatrices = [];
        var envelope = new AccountEnvelope
        {
            Format = AccountEnvelopeFormat,
            FormatVersion = AccountEnvelopeVersion,
            MasterId = data.MasterAccess.MasterId,
            LicenseId = data.MasterAccess.LicenseId,
            LicenseName = data.MasterAccess.LicenseName,
            DeviceLimit = data.MasterAccess.DeviceLimit,
            Users = access.Users,
            PayloadBase64 = Convert.ToBase64String(payload)
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
    }

    public static byte[] ExportBytes(
        AppData data,
        string? password,
        out PortableExportInfo info)
    {
        var exportedUtc = DateTime.UtcNow;
        var revisionId = Guid.NewGuid().ToString("N");
        var savedBy = $"{Environment.UserName} on {Environment.MachineName}";
        var package = new PortableDataPackage
        {
            Format = FormatName,
            FormatVersion = CurrentFormatVersion,
            ApplicationRevision = AppInfo.Revision,
            ExportedUtc = exportedUtc,
            RevisionId = revisionId,
            SavedBy = savedBy,
            ProjectName = data.ProjectName,
            Clients = data.Clients,
            MasterAccess = data.MasterAccess
        };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(package, Options);
        info = new PortableExportInfo(exportedUtc, revisionId, savedBy,
            !string.IsNullOrEmpty(password));
        return string.IsNullOrEmpty(password)
            ? jsonBytes
            : JwePasswordProtection.Protect(jsonBytes, password);
    }

    public static PortableImport Import(string path, string? password = null) =>
        ImportBytes(File.ReadAllBytes(path), password);

    public static PortableImport ImportBytes(byte[] contents, string? password = null)
    {
        if (TryReadAccountEnvelope(contents, out var envelope))
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new MasterLoginRequiredException();
            contents = Convert.FromBase64String(envelope.PayloadBase64);
        }
        var passwordProtected = IsPasswordProtected(contents);
        if (passwordProtected && string.IsNullOrEmpty(password))
            throw new PasswordRequiredException();
        var jsonBytes = passwordProtected
            ? JwePasswordProtection.Unprotect(contents, password!)
            : contents;
        var package = JsonSerializer.Deserialize<PortableDataPackage>(jsonBytes, Options)
            ?? throw new InvalidDataException("The transfer file is empty or unreadable.");
        if (!string.Equals(package.Format, FormatName, StringComparison.Ordinal) &&
            !string.Equals(package.Format, LegacyFormatName, StringComparison.Ordinal))
            throw new InvalidDataException("This is not a supported InNasc company or legacy AV Matrix file.");
        if (package.FormatVersion is < 1 or > CurrentFormatVersion)
            throw new InvalidDataException(
                $"Transfer format {package.FormatVersion} is not supported by this revision.");
        if (package.Clients is null)
            throw new InvalidDataException("The transfer file does not contain a client collection.");

        var data = new AppData
        {
            ProjectName = string.IsNullOrWhiteSpace(package.ProjectName)
                ? "InNasc"
                : package.ProjectName.Trim(),
            Clients = package.Clients,
            MasterAccess = package.MasterAccess ?? new MasterAccessControl(),
            Settings = new AppSettings()
        };
        DataStore.Normalize(data);
        return new PortableImport(
            data,
            package.ExportedUtc,
            package.ApplicationRevision ?? string.Empty,
            package.RevisionId ?? string.Empty,
            package.SavedBy ?? string.Empty,
            passwordProtected);
    }

    public static bool IsPasswordProtected(string path) =>
        IsPasswordProtected(File.ReadAllBytes(path));

    public static bool IsPasswordProtected(byte[] contents) =>
        JwePasswordProtection.IsCompactJwe(contents);

    public static bool IsAccountProtected(byte[] contents) =>
        TryReadAccountEnvelope(contents, out _);

    public static MasterAccessControl ReadMasterAccess(byte[] contents, string? legacyPassword = null)
    {
        if (TryReadAccountEnvelope(contents, out var envelope))
        {
            var access = new MasterAccessControl
            {
                MasterId = envelope.MasterId,
                LicenseId = envelope.LicenseId,
                LicenseName = envelope.LicenseName,
                DeviceLimit = envelope.DeviceLimit,
                Users = envelope.Users ?? []
            };
            DataStore.Normalize(new AppData { MasterAccess = access });
            return access;
        }
        return ImportBytes(contents, legacyPassword).Data.MasterAccess;
    }

    public static MasterAccessControl ReadMasterAccess(string path, string? legacyPassword = null) =>
        ReadMasterAccess(File.ReadAllBytes(path), legacyPassword);

    private static bool TryReadAccountEnvelope(byte[] contents, out AccountEnvelope envelope)
    {
        envelope = null!;
        if (contents.Length == 0 || contents[0] != (byte)'{') return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<AccountEnvelope>(contents, Options);
            if (parsed is null ||
                (!string.Equals(parsed.Format, AccountEnvelopeFormat, StringComparison.Ordinal) &&
                 !string.Equals(parsed.Format, LegacyAccountEnvelopeFormat, StringComparison.Ordinal)) ||
                parsed.FormatVersion != AccountEnvelopeVersion ||
                string.IsNullOrWhiteSpace(parsed.PayloadBase64))
                return false;
            envelope = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed class PortableDataPackage
    {
        public string Format { get; set; } = string.Empty;
        public int FormatVersion { get; set; }
        public string? ApplicationRevision { get; set; }
        public DateTime ExportedUtc { get; set; }
        public string? RevisionId { get; set; }
        public string? SavedBy { get; set; }
        public string ProjectName { get; set; } = "InNasc";
        public List<ClientRecord>? Clients { get; set; }
        public MasterAccessControl? MasterAccess { get; set; }
    }

    private sealed class AccountEnvelope
    {
        public string Format { get; set; } = string.Empty;
        public int FormatVersion { get; set; }
        public Guid MasterId { get; set; }
        public Guid LicenseId { get; set; }
        public string LicenseName { get; set; } = string.Empty;
        public int DeviceLimit { get; set; }
        public List<MasterUserRecord>? Users { get; set; }
        public string PayloadBase64 { get; set; } = string.Empty;
    }
}

internal sealed record PortableImport(
    AppData Data,
    DateTime ExportedUtc,
    string ApplicationRevision,
    string RevisionId,
    string SavedBy,
    bool PasswordProtected)
{
    public int ClientCount => Data.Clients.Count;
    public int EquipmentCount => Data.Clients.Sum(client =>
        client.Locations.Sum(location => location.Rooms.Sum(room => room.Equipment.Count)));
}

internal sealed record PortableExportInfo(
    DateTime ExportedUtc,
    string RevisionId,
    string SavedBy,
    bool PasswordProtected);

internal sealed class PasswordRequiredException : InvalidOperationException
{
    public PasswordRequiredException()
        : base("This company file is password protected. Enter its password to continue.")
    {
    }
}

internal sealed class MasterLoginRequiredException : InvalidOperationException
{
    public MasterLoginRequiredException()
        : base("Sign in with an InNasc account to open this protected company file.")
    {
    }
}
