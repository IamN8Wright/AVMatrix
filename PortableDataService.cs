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

    public static PortableExportInfo Export(
        string path,
        AppData data,
        string? password = null,
        PortableBackupOptions? backupOptions = null)
    {
        var exportData = backupOptions is null ? data : CreateBackupSnapshot(data, backupOptions);
        var bytes = ExportBytes(exportData, password, out var info);
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
        AccountEnvelope? existingEnvelope = null;
        if (File.Exists(path))
        {
            try
            {
                if (TryReadAccountEnvelope(File.ReadAllBytes(path), out var existing))
                    existingEnvelope = existing;
            }
            catch
            {
                existingEnvelope = null;
            }
        }

        var bytes = ExportMasterBytes(data, session, out var info);
        if (existingEnvelope is not null && TryReadAccountEnvelope(bytes, out var nextEnvelope))
        {
            // These fields are controlled by Global Admin. A normal InNasc save may
            // update the encrypted payload but must not erase company branding or licensing.
            nextEnvelope.CompanyName = existingEnvelope.CompanyName;
            nextEnvelope.LicenseId = existingEnvelope.LicenseId;
            nextEnvelope.LicenseName = existingEnvelope.LicenseName;
            nextEnvelope.DeviceLimit = existingEnvelope.DeviceLimit;
            nextEnvelope.LicenseExpiresUtc = existingEnvelope.LicenseExpiresUtc;
            nextEnvelope.CompanyLogoBase64 = existingEnvelope.CompanyLogoBase64;
            bytes = JsonSerializer.SerializeToUtf8Bytes(nextEnvelope, Options);

            data.ProjectName = FirstNonBlank(existingEnvelope.CompanyName, data.ProjectName);
            data.MasterAccess.LicenseId = existingEnvelope.LicenseId;
            data.MasterAccess.LicenseName = existingEnvelope.LicenseName;
            data.MasterAccess.DeviceLimit = existingEnvelope.DeviceLimit;
            data.MasterAccess.LicenseExpiresUtc = existingEnvelope.LicenseExpiresUtc;
        }

        WriteAtomically(path, bytes);
        return info;
    }

    public static byte[] ExportMasterBytes(
        AppData data,
        MasterSession session,
        out PortableExportInfo info)
    {
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
            CompanyName = string.IsNullOrWhiteSpace(data.ProjectName)
                ? data.MasterAccess.LicenseName
                : data.ProjectName.Trim(),
            LicenseId = data.MasterAccess.LicenseId,
            LicenseName = data.MasterAccess.LicenseName,
            DeviceLimit = data.MasterAccess.DeviceLimit,
            LicenseExpiresUtc = data.MasterAccess.LicenseExpiresUtc,
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
        AccountEnvelope? accountEnvelope = null;
        if (TryReadAccountEnvelope(contents, out var envelope))
        {
            accountEnvelope = envelope;
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
        if (accountEnvelope is not null)
        {
            data.ProjectName = FirstNonBlank(
                accountEnvelope.CompanyName,
                accountEnvelope.LicenseName,
                data.ProjectName);
            data.MasterAccess.LicenseId = accountEnvelope.LicenseId;
            data.MasterAccess.LicenseName = FirstNonBlank(
                accountEnvelope.LicenseName,
                accountEnvelope.CompanyName,
                data.MasterAccess.LicenseName);
            data.MasterAccess.DeviceLimit = accountEnvelope.DeviceLimit;
            data.MasterAccess.LicenseExpiresUtc = accountEnvelope.LicenseExpiresUtc;
            data.MasterAccess.Users = accountEnvelope.Users ?? data.MasterAccess.Users;
        }
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
                LicenseExpiresUtc = envelope.LicenseExpiresUtc,
                Users = envelope.Users ?? []
            };
            DataStore.Normalize(new AppData { MasterAccess = access });
            return access;
        }
        return ImportBytes(contents, legacyPassword).Data.MasterAccess;
    }

    public static MasterAccessControl ReadMasterAccess(string path, string? legacyPassword = null) =>
        ReadMasterAccess(File.ReadAllBytes(path), legacyPassword);

    public static CompanyFileSummary ReadCompanySummary(
        byte[] contents,
        string? legacyPassword = null)
    {
        if (TryReadAccountEnvelope(contents, out var envelope))
        {
            var companyName = FirstNonBlank(
                envelope.CompanyName,
                envelope.LicenseName,
                "InNasc Company");
            return new CompanyFileSummary(
                companyName,
                FirstNonBlank(envelope.LicenseName, companyName),
                envelope.DeviceLimit,
                true,
                envelope.CompanyLogoBase64,
                envelope.LicenseExpiresUtc);
        }

        var data = ImportBytes(contents, legacyPassword).Data;
        return new CompanyFileSummary(
            FirstNonBlank(data.ProjectName, data.MasterAccess.LicenseName, "InNasc Company"),
            FirstNonBlank(data.MasterAccess.LicenseName, data.ProjectName),
            data.MasterAccess.DeviceLimit,
            data.MasterAccess.LicenseId != Guid.Empty);
    }

    public static CompanyFileSummary ReadCompanySummary(
        string path,
        string? legacyPassword = null) =>
        ReadCompanySummary(File.ReadAllBytes(path), legacyPassword);

    private static AppData CreateBackupSnapshot(AppData source, PortableBackupOptions options)
    {
        var clone = JsonSerializer.Deserialize<AppData>(
            JsonSerializer.SerializeToUtf8Bytes(source, Options), Options)
            ?? throw new InvalidOperationException("The backup snapshot could not be created.");
        foreach (var equipment in clone.Clients.SelectMany(client => client.Locations)
                     .SelectMany(location => location.Rooms)
                     .SelectMany(room => room.Equipment))
        {
            if (!options.IncludeDeviceCredentials)
            {
                equipment.Username = string.Empty;
                equipment.Password = string.Empty;
            }
            if (!options.IncludeConfigurationFiles)
                equipment.ConfigurationFiles.Clear();
        }
        return clone;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

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
        public string CompanyName { get; set; } = string.Empty;
        public Guid LicenseId { get; set; }
        public string LicenseName { get; set; } = string.Empty;
        public int DeviceLimit { get; set; }
        public DateTime? LicenseExpiresUtc { get; set; }
        public string CompanyLogoBase64 { get; set; } = string.Empty;
        public List<MasterUserRecord>? Users { get; set; }
        public string PayloadBase64 { get; set; } = string.Empty;
    }
}

internal sealed record CompanyFileSummary(
    string CompanyName,
    string LicenseName,
    int DeviceLimit,
    bool IsLicensed,
    string CompanyLogoBase64 = "",
    DateTime? LicenseExpiresUtc = null);

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

internal sealed record PortableBackupOptions(
    bool IncludeDeviceCredentials = true,
    bool IncludeConfigurationFiles = true);
