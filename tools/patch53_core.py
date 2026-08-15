from pathlib import Path
import re


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8")


def once(text, old, new, label):
    if old not in text:
        raise SystemExit(f"Missing patch anchor: {label}")
    return text.replace(old, new, 1)

# ---------- Shared models ----------
p = "Models.cs"
s = read(p)
s = once(s,
'''    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ClientCheckoutRecord''',
'''    public bool Enabled { get; set; } = true;
    public bool IsRecoveryAccount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ClientCheckoutRecord''',
"recovery account marker")
s = once(s,
'''    public string LicenseName { get; set; } = string.Empty;
    public int DeviceLimit { get; set; }
    public List<MasterUserRecord> Users { get; set; } = [];''',
'''    public string LicenseName { get; set; } = string.Empty;
    public int DeviceLimit { get; set; }
    public DateTime? LicenseExpiresUtc { get; set; }
    public List<MasterUserRecord> Users { get; set; } = [];''',
"license expiration model")
write(p, s)

# ---------- Global catalog models ----------
p = "InNascGlobalModels.cs"
s = read(p)
s = once(s,
'''    // Zero means Unlimited. Positive values are hard caps on equipment records.
    public int DeviceLimit { get; set; } = 250;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}''',
'''    // Zero means Unlimited. Positive values are hard caps on equipment records.
    public int DeviceLimit { get; set; } = 250;
    public DateTime? ExpiresUtc { get; set; }
    public InNascCompanyUserRecord? RecoveryUser { get; set; }
    public DateTime? LastCatalogSyncUtc { get; set; }
    public int LastObservedDeviceCount { get; set; }
    public string LastObservedRevisionId { get; set; } = string.Empty;
    // Local reconciliation is active now. This state is intentionally transport-neutral
    // so a later release can send license snapshots through an authenticated service.
    public string RemoteSyncState { get; set; } = "LocalOnly";
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public bool RecoveryCredentialReady => RecoveryUser?.CredentialReady == true;
}''',
"global file lifecycle fields")
write(p, s)

# ---------- Device/license policy ----------
write("DeviceLimitPolicy.cs", r'''namespace InNasc;

internal static class DeviceLimitPolicy
{
    public static int CountDevices(AppData data) =>
        data.Clients.Sum(client => client.Locations.Sum(location =>
            location.Rooms.Sum(room => room.Equipment.Count)));

    public static string LimitText(int deviceLimit) =>
        deviceLimit <= 0 ? "Unlimited" : $"{deviceLimit:N0}";

    public static string ExpirationText(DateTime? expiresUtc) =>
        expiresUtc is null ? "Never" : expiresUtc.Value.ToLocalTime().ToString("yyyy-MM-dd");

    public static bool IsExpired(MasterAccessControl access, DateTime? nowUtc = null) =>
        access.LicenseExpiresUtc is DateTime expires && expires <= (nowUtc ?? DateTime.UtcNow);

    public static bool IsOverLimit(MasterAccessControl access, AppData data) =>
        access.DeviceLimit > 0 && CountDevices(data) > access.DeviceLimit;

    public static string UsageText(MasterAccessControl access, AppData data)
    {
        var count = CountDevices(data);
        return access.DeviceLimit <= 0
            ? $"{count:N0} devices / Unlimited"
            : $"{count:N0} / {access.DeviceLimit:N0} devices";
    }

    public static string WarningText(MasterAccessControl access, AppData data)
    {
        var count = CountDevices(data);
        if (IsExpired(access))
            return $"LICENSE EXPIRED {ExpirationText(access.LicenseExpiresUtc)} - new client cards and devices are locked.";
        if (access.DeviceLimit > 0 && count > access.DeviceLimit)
            return $"LICENSE OVER DEVICE LIMIT: {count:N0} / {access.DeviceLimit:N0} - new client cards and devices are locked.";
        return string.Empty;
    }

    public static void RequireNewClientAllowed(MasterAccessControl access, AppData data)
    {
        if (IsExpired(access))
            throw new LicenseExpiredException(
                access.LicenseExpiresUtc,
                $"This InNasc license expired on {ExpirationText(access.LicenseExpiresUtc)}. Existing records remain available, but new client cards and devices are locked. Ask your InNasc Global Admin to renew the license.");
        if (IsOverLimit(access, data))
            throw new DeviceLimitExceededException(
                access.DeviceLimit,
                CountDevices(data),
                0,
                $"This .nasc contains {CountDevices(data):N0} devices, above its {access.DeviceLimit:N0}-device tier. Existing records remain available, but new client cards and devices are locked until usage is reduced or the tier is increased.");
    }

    public static void RequireCapacity(
        MasterAccessControl access,
        AppData data,
        int additionalDevices)
    {
        RequireNewClientAllowed(access, data);
        if (additionalDevices <= 0 || access.DeviceLimit <= 0) return;
        var current = CountDevices(data);
        if (current + additionalDevices <= access.DeviceLimit) return;
        var remaining = Math.Max(0, access.DeviceLimit - current);
        throw new DeviceLimitExceededException(
            access.DeviceLimit,
            current,
            additionalDevices,
            $"This .nasc license allows {access.DeviceLimit:N0} devices. It currently contains {current:N0}, leaving {remaining:N0} available. Ask your InNasc Global Admin to unlock a higher device tier.");
    }

    public static void RequireWithinLimit(MasterAccessControl access, AppData data)
    {
        if (access.DeviceLimit <= 0) return;
        var count = CountDevices(data);
        if (count <= access.DeviceLimit) return;
        throw new DeviceLimitExceededException(
            access.DeviceLimit,
            count,
            0,
            $"This .nasc license allows {access.DeviceLimit:N0} devices, but the merged workspace would contain {count:N0}. Ask your InNasc Global Admin to unlock a higher tier.");
    }
}

internal sealed class DeviceLimitExceededException(
    int limit,
    int currentCount,
    int requestedAdditional,
    string message) : InvalidOperationException(message)
{
    public int Limit { get; } = limit;
    public int CurrentCount { get; } = currentCount;
    public int RequestedAdditional { get; } = requestedAdditional;
}

internal sealed class LicenseExpiredException(DateTime? expiresUtc, string message)
    : InvalidOperationException(message)
{
    public DateTime? ExpiresUtc { get; } = expiresUtc;
}
''')

# ---------- Envelope metadata ----------
p = "InNascCompanyEnvelopeMetadataService.cs"
s = read(p)
s = once(s,
'''    public static void Apply(
        string path,
        string companyName,
        string licenseName,
        Guid licenseId,
        int deviceLimit,
        string? companyLogoBase64)
    {''',
'''    public static void Apply(
        string path,
        string companyName,
        string licenseName,
        Guid licenseId,
        int deviceLimit,
        string? companyLogoBase64) =>
        ApplyCore(path, companyName, licenseName, licenseId, deviceLimit,
            companyLogoBase64, null, preserveExistingExpiration: true);

    public static void Apply(
        string path,
        string companyName,
        string licenseName,
        Guid licenseId,
        int deviceLimit,
        string? companyLogoBase64,
        DateTime? licenseExpiresUtc) =>
        ApplyCore(path, companyName, licenseName, licenseId, deviceLimit,
            companyLogoBase64, licenseExpiresUtc, preserveExistingExpiration: false);

    private static void ApplyCore(
        string path,
        string companyName,
        string licenseName,
        Guid licenseId,
        int deviceLimit,
        string? companyLogoBase64,
        DateTime? licenseExpiresUtc,
        bool preserveExistingExpiration)
    {''',
"metadata apply overload")
s = once(s,
'''        root["DeviceLimit"] = JsonValue.Create(deviceLimit);
        root["CompanyLogoBase64"] = JsonValue.Create(companyLogoBase64?.Trim() ?? string.Empty);''',
'''        root["DeviceLimit"] = JsonValue.Create(deviceLimit);
        root["CompanyLogoBase64"] = JsonValue.Create(companyLogoBase64?.Trim() ?? string.Empty);
        if (!preserveExistingExpiration)
            root["LicenseExpiresUtc"] = licenseExpiresUtc is null
                ? null
                : JsonValue.Create(licenseExpiresUtc.Value.ToUniversalTime());''',
"metadata expiration")
write(p, s)

# ---------- Portable data / logo preservation / privacy backup ----------
p = "PortableDataService.cs"
s = read(p)
s = once(s,
'''    public static PortableExportInfo Export(string path, AppData data, string? password = null)
    {
        var bytes = ExportBytes(data, password, out var info);''',
'''    public static PortableExportInfo Export(
        string path,
        AppData data,
        string? password = null,
        PortableBackupOptions? backupOptions = null)
    {
        var exportData = backupOptions is null ? data : CreateBackupSnapshot(data, backupOptions);
        var bytes = ExportBytes(exportData, password, out var info);''',
"backup options export")
s = once(s,
'''        var bytes = ExportMasterBytes(data, session, out var info);
        WriteAtomically(path, bytes);
        return info;''',
'''        AccountEnvelope? existingEnvelope = null;
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
        return info;''',
"preserve admin envelope")
# Do not make an already-over-limit workspace impossible to save. Creation remains blocked.
s = s.replace("        DeviceLimitPolicy.RequireWithinLimit(data.MasterAccess, data);\n", "", 1)
s = once(s,
'''            LicenseName = data.MasterAccess.LicenseName,
            DeviceLimit = data.MasterAccess.DeviceLimit,
            Users = access.Users,''',
'''            LicenseName = data.MasterAccess.LicenseName,
            DeviceLimit = data.MasterAccess.DeviceLimit,
            LicenseExpiresUtc = data.MasterAccess.LicenseExpiresUtc,
            Users = access.Users,''',
"export expiration")
s = once(s,
'''            data.MasterAccess.DeviceLimit = accountEnvelope.DeviceLimit;
            data.MasterAccess.Users = accountEnvelope.Users ?? data.MasterAccess.Users;''',
'''            data.MasterAccess.DeviceLimit = accountEnvelope.DeviceLimit;
            data.MasterAccess.LicenseExpiresUtc = accountEnvelope.LicenseExpiresUtc;
            data.MasterAccess.Users = accountEnvelope.Users ?? data.MasterAccess.Users;''',
"import expiration")
s = once(s,
'''                LicenseName = envelope.LicenseName,
                DeviceLimit = envelope.DeviceLimit,
                Users = envelope.Users ?? []''',
'''                LicenseName = envelope.LicenseName,
                DeviceLimit = envelope.DeviceLimit,
                LicenseExpiresUtc = envelope.LicenseExpiresUtc,
                Users = envelope.Users ?? []''',
"read expiration")
s = once(s,
'''                envelope.DeviceLimit,
                true,
                envelope.CompanyLogoBase64);''',
'''                envelope.DeviceLimit,
                true,
                envelope.CompanyLogoBase64,
                envelope.LicenseExpiresUtc);''',
"summary expiration")
s = once(s,
'''        public string LicenseName { get; set; } = string.Empty;
        public int DeviceLimit { get; set; }
        public string CompanyLogoBase64 { get; set; } = string.Empty;''',
'''        public string LicenseName { get; set; } = string.Empty;
        public int DeviceLimit { get; set; }
        public DateTime? LicenseExpiresUtc { get; set; }
        public string CompanyLogoBase64 { get; set; } = string.Empty;''',
"account envelope expiration")
s = once(s,
'''internal sealed record CompanyFileSummary(
    string CompanyName,
    string LicenseName,
    int DeviceLimit,
    bool IsLicensed,
    string CompanyLogoBase64 = "");''',
'''internal sealed record CompanyFileSummary(
    string CompanyName,
    string LicenseName,
    int DeviceLimit,
    bool IsLicensed,
    string CompanyLogoBase64 = "",
    DateTime? LicenseExpiresUtc = null);''',
"summary model")
helper_anchor = '''    private static string FirstNonBlank(params string?[] values) =>'''
helper = '''    private static AppData CreateBackupSnapshot(AppData source, PortableBackupOptions options)
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

'''
s = once(s, helper_anchor, helper + helper_anchor, "backup snapshot helper")
s += '''\ninternal sealed record PortableBackupOptions(\n    bool IncludeDeviceCredentials = true,\n    bool IncludeConfigurationFiles = true);\n'''
write(p, s)

# ---------- Global core licensing/recovery ----------
p = "InNascGlobalCoreService.cs"
s = read(p)
s = once(s,
'''        string companyPath,
        int deviceLimit = 250)
    {''',
'''        string companyPath,
        int deviceLimit = 250,
        DateTime? expiresUtc = null,
        string? recoveryPassword = null)
    {''',
"create company signature")
s = once(s,
'''            _ = AddCompanyFileCore(company, name, companyPath, deviceLimit);
            WriteCompanyFile(globalPath, catalog, session, company, company.Files[0]);''',
'''            _ = AddCompanyFileCore(company, name, companyPath, deviceLimit);
            company.Files[0].ExpiresUtc = expiresUtc?.ToUniversalTime();
            if (!string.IsNullOrWhiteSpace(recoveryPassword))
                SetRecoveryPasswordCore(company.Files[0], recoveryPassword, session.GlobalKey);
            WriteCompanyFile(globalPath, catalog, session, company, company.Files[0]);''',
"create company lifecycle")
s = once(s,
'''        string companyPath,
        int deviceLimit)
    {
        RequireAdmin(catalog, session);''',
'''        string companyPath,
        int deviceLimit,
        DateTime? expiresUtc = null,
        string? recoveryPassword = null)
    {
        RequireAdmin(catalog, session);''',
"add company file signature")
s = once(s,
'''        var file = AddCompanyFileCore(company, fileName, companyPath, deviceLimit);
        try
        {
            WriteCompanyFile(globalPath, catalog, session, company, file);''',
'''        var file = AddCompanyFileCore(company, fileName, companyPath, deviceLimit);
        file.ExpiresUtc = expiresUtc?.ToUniversalTime();
        if (!string.IsNullOrWhiteSpace(recoveryPassword))
            SetRecoveryPasswordCore(file, recoveryPassword, session.GlobalKey);
        try
        {
            WriteCompanyFile(globalPath, catalog, session, company, file);''',
"add file lifecycle")
# Add lifecycle methods before GetDeviceCount.
anchor = '''    public static int GetDeviceCount(InNascCompanyFileRecord file)
    {'''
methods = r'''    public static void RenameLicense(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid fileId,
        string newName)
    {
        RequireAdmin(catalog, session);
        var company = RequiredCompany(catalog, companyId);
        var file = RequiredCompanyFile(company, fileId);
        var name = newName.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Enter a license name.");
        file.Name = name;
        Save(path, catalog, session);
    }

    public static void SetLicenseExpiration(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid fileId,
        DateTime? expiresUtc)
    {
        RequireAdmin(catalog, session);
        var company = RequiredCompany(catalog, companyId);
        var file = RequiredCompanyFile(company, fileId);
        file.ExpiresUtc = expiresUtc?.ToUniversalTime();
        Save(path, catalog, session);
    }

    public static void SetRecoveryPassword(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid fileId,
        string password)
    {
        RequireAdmin(catalog, session);
        ValidatePassword(password);
        var company = RequiredCompany(catalog, companyId);
        var file = RequiredCompanyFile(company, fileId);
        SetRecoveryPasswordCore(file, password, session.GlobalKey);
        Save(path, catalog, session);
    }

'''
s = once(s, anchor, methods + anchor, "lifecycle methods")
s = once(s,
'''            LicenseId = file.Id,
            LicenseName = string.IsNullOrWhiteSpace(file.Name) ? company.Name : file.Name,
            DeviceLimit = file.DeviceLimit
        };
        foreach (var user in company.Users.Where(candidate => candidate.Enabled && candidate.CredentialReady))
            access.Users.Add(CreatePublishedCompanyUser(
                user, file.CompanyKeyBase64, globalKey));
        return access;''',
'''            LicenseId = file.Id,
            LicenseName = string.IsNullOrWhiteSpace(file.Name) ? company.Name : file.Name,
            DeviceLimit = file.DeviceLimit,
            LicenseExpiresUtc = file.ExpiresUtc
        };
        foreach (var user in company.Users.Where(candidate => candidate.Enabled))
        {
            if (user.CredentialReady)
            {
                access.Users.Add(CreatePublishedCompanyUser(user, file.CompanyKeyBase64, globalKey));
                continue;
            }
            var preserved = existing?.Users.FirstOrDefault(candidate => !candidate.IsRecoveryAccount &&
                (candidate.Id == user.Id || string.Equals(candidate.Username, user.Username,
                    StringComparison.OrdinalIgnoreCase)));
            if (preserved is not null)
                access.Users.Add(MergePublishedProfile(user, preserved));
        }
        if (file.RecoveryUser?.CredentialReady == true)
        {
            var recovery = CreatePublishedCompanyUser(file.RecoveryUser, file.CompanyKeyBase64, globalKey);
            recovery.IsRecoveryAccount = true;
            recovery.Role = MasterUserRole.Owner;
            recovery.HasAllClientAccess = true;
            recovery.ClientAccessIds.Clear();
            access.Users.Add(recovery);
        }
        else
        {
            var preservedRecovery = existing?.Users.FirstOrDefault(candidate => candidate.IsRecoveryAccount);
            if (preservedRecovery is not null) access.Users.Add(ClonePublishedUser(preservedRecovery));
        }
        return access;''',
"build company access")
s = once(s,
'''        InNascCompanyEnvelopeMetadataService.Apply(
            file.FilePath, company.Name, file.Name, file.Id, file.DeviceLimit, company.LogoBase64);''',
'''        InNascCompanyEnvelopeMetadataService.Apply(
            file.FilePath, company.Name, file.Name, file.Id, file.DeviceLimit,
            company.LogoBase64, file.ExpiresUtc);''',
"write metadata lifecycle")
helper_anchor = '''    private static MasterUserRecord CreatePublishedCompanyUser(
'''
helpers = r'''    private static void SetRecoveryPasswordCore(
        InNascCompanyFileRecord file,
        string password,
        string globalKey)
    {
        ValidatePassword(password);
        var recovery = file.RecoveryUser ?? new InNascCompanyUserRecord();
        recovery.Username = "root";
        recovery.DisplayName = "InNasc Recovery";
        recovery.Role = MasterUserRole.Owner;
        recovery.HasAllClientAccess = true;
        recovery.ClientAccessIds.Clear();
        recovery.Enabled = true;
        SetCompanyUserPassword(recovery, password, globalKey);
        file.RecoveryUser = recovery;
    }

    private static MasterUserRecord MergePublishedProfile(
        InNascCompanyUserRecord profile,
        MasterUserRecord credential) => new()
    {
        Id = profile.Id,
        Username = profile.Username,
        DisplayName = profile.DisplayName,
        Role = profile.Role,
        PasswordSaltBase64 = credential.PasswordSaltBase64,
        PasswordHashBase64 = credential.PasswordHashBase64,
        PasswordIterations = credential.PasswordIterations,
        MasterKeySaltBase64 = credential.MasterKeySaltBase64,
        MasterKeyNonceBase64 = credential.MasterKeyNonceBase64,
        MasterKeyCiphertextBase64 = credential.MasterKeyCiphertextBase64,
        MasterKeyTagBase64 = credential.MasterKeyTagBase64,
        HasAllClientAccess = profile.Role == MasterUserRole.Owner || profile.HasAllClientAccess,
        ClientAccessIds = profile.Role == MasterUserRole.Owner || profile.HasAllClientAccess
            ? []
            : profile.ClientAccessIds.Distinct().ToList(),
        Enabled = profile.Enabled,
        IsRecoveryAccount = false,
        CreatedUtc = profile.CreatedUtc
    };

    private static MasterUserRecord ClonePublishedUser(MasterUserRecord user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        Role = user.Role,
        PasswordSaltBase64 = user.PasswordSaltBase64,
        PasswordHashBase64 = user.PasswordHashBase64,
        PasswordIterations = user.PasswordIterations,
        MasterKeySaltBase64 = user.MasterKeySaltBase64,
        MasterKeyNonceBase64 = user.MasterKeyNonceBase64,
        MasterKeyCiphertextBase64 = user.MasterKeyCiphertextBase64,
        MasterKeyTagBase64 = user.MasterKeyTagBase64,
        HasAllClientAccess = user.HasAllClientAccess,
        ClientAccessIds = user.ClientAccessIds.ToList(),
        Enabled = user.Enabled,
        IsRecoveryAccount = user.IsRecoveryAccount,
        CreatedUtc = user.CreatedUtc
    };

'''
s = once(s, helper_anchor, helpers + helper_anchor, "recovery helpers")
write(p, s)

# ---------- Company metadata/admin service ----------
p = "InNascCompanyGlobalAdminService.cs"
s = read(p)
s = once(s,
'''            CompanyKeyBase64 = companyKeyBase64,
            DeviceLimit = summary.DeviceLimit
        };''',
'''            CompanyKeyBase64 = companyKeyBase64,
            DeviceLimit = summary.DeviceLimit,
            ExpiresUtc = summary.LicenseExpiresUtc
        };''',
"import expiration")
s = once(s,
'''    public static void SetCompanyLogo(''',
'''    public static void RenameLicense(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        InNascCompanyRecord company,
        InNascCompanyFileRecord file,
        string newName)
    {
        RequireAdmin(session);
        var previous = file.Name;
        InNascGlobalCoreService.RenameLicense(
            globalPath, catalog, session, company.Id, file.Id, newName);
        try
        {
            ApplyToFile(company, file);
        }
        catch
        {
            file.Name = previous;
            InNascGlobalCoreService.Save(globalPath, catalog, session);
            TryApplyAll(company);
            throw;
        }
    }

    public static void SetCompanyLogo(''',
"rename license wrapper")
s = once(s,
'''            file.DeviceLimit,
            company.LogoBase64);''',
'''            file.DeviceLimit,
            company.LogoBase64,
            file.ExpiresUtc);''',
"apply expiration")
write(p, s)

# ---------- Local two-way reconciliation ----------
write("InNascCompanyAccessSyncService.cs", r'''namespace InNasc;

internal static class InNascCompanyAccessSyncService
{
    public static void SyncAll(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession)
    {
        foreach (var company in catalog.Companies.Where(company => company.Enabled))
            SyncCompany(globalPath, catalog, globalSession, company);
    }

    public static void SyncCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company)
    {
        foreach (var file in company.Files.Where(file => file.Enabled && File.Exists(file.FilePath)))
            SyncFile(globalPath, catalog, globalSession, company, file);
    }

    public static void SyncFile(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company,
        InNascCompanyFileRecord file)
    {
        if (!globalSession.IsGlobalAdmin)
            throw new MasterAuthorizationException("Global Admin access is required.");
        if (!File.Exists(file.FilePath)) return;

        var imported = PortableDataService.ImportBytes(
            File.ReadAllBytes(file.FilePath), file.CompanyKeyBase64);
        var data = imported.Data;
        var old = data.MasterAccess ?? new MasterAccessControl();

        PullCurrentUsers(company, old);

        var next = InNascGlobalCoreService.BuildCompanyAccess(
            company, file, globalSession.GlobalKey, old);
        var allowedUsers = next.Users.Select(user => user.Id).ToHashSet();
        next.Checkouts.RemoveAll(checkout => !allowedUsers.Contains(checkout.UserId));
        data.MasterAccess = next;
        data.ProjectName = company.Name;

        var companySession = InNascGlobalCoreService.CreateCompanyFileSession(globalSession, file);
        PortableDataService.ExportMaster(file.FilePath, data, companySession);
        InNascCompanyEnvelopeMetadataService.Apply(
            file.FilePath, company.Name, file.Name, file.Id, file.DeviceLimit,
            company.LogoBase64, file.ExpiresUtc);

        file.LastCatalogSyncUtc = DateTime.UtcNow;
        file.LastObservedDeviceCount = DeviceLimitPolicy.CountDevices(data);
        file.LastObservedRevisionId = imported.RevisionId;
        file.RemoteSyncState = "LocalOnly";
        InNascGlobalCoreService.Save(globalPath, catalog, globalSession);
    }

    private static void PullCurrentUsers(
        InNascCompanyRecord company,
        MasterAccessControl access)
    {
        foreach (var source in access.Users.Where(user => user.Enabled && !user.IsRecoveryAccount))
        {
            var target = company.Users.FirstOrDefault(user => user.Id == source.Id)
                ?? company.Users.FirstOrDefault(user =>
                    string.Equals(user.Username, source.Username, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                // The password itself is not recoverable from the .nasc. Keep enough identity/hash
                // information to show the account in Global Admin; the encrypted .nasc credential
                // remains authoritative until Global Admin resets that user's password.
                target = new InNascCompanyUserRecord
                {
                    Id = source.Id,
                    Username = source.Username,
                    DisplayName = source.DisplayName,
                    Role = source.Role,
                    PasswordSaltBase64 = source.PasswordSaltBase64,
                    PasswordHashBase64 = source.PasswordHashBase64,
                    PasswordIterations = source.PasswordIterations,
                    CompanyKeySaltBase64 = source.MasterKeySaltBase64,
                    HasAllClientAccess = source.HasAllClientAccess,
                    ClientAccessIds = source.ClientAccessIds.ToList(),
                    Enabled = true,
                    CreatedUtc = source.CreatedUtc
                };
                company.Users.Add(target);
            }
            else
            {
                target.Username = source.Username;
                target.DisplayName = source.DisplayName;
                target.Role = source.Role;
                target.HasAllClientAccess = source.Role == MasterUserRole.Owner || source.HasAllClientAccess;
                target.ClientAccessIds = target.HasAllClientAccess
                    ? []
                    : source.ClientAccessIds.Distinct().ToList();
                target.Enabled = source.Enabled;
                if (!target.CredentialReady)
                {
                    target.PasswordSaltBase64 = source.PasswordSaltBase64;
                    target.PasswordHashBase64 = source.PasswordHashBase64;
                    target.PasswordIterations = source.PasswordIterations;
                    target.CompanyKeySaltBase64 = source.MasterKeySaltBase64;
                }
            }
        }
    }
}
''')

# ---------- Recovery-account restrictions in ordinary user manager ----------
p = "MasterAccessService.cs"
s = read(p)
s = s.replace(
'''access.Users.Count(candidate => candidate.Enabled &&
                candidate.Role == MasterUserRole.Owner) <= 1''',
'''access.Users.Count(candidate => candidate.Enabled && !candidate.IsRecoveryAccount &&
                candidate.Role == MasterUserRole.Owner) <= 1''')
s = once(s,
'''        var user = ValidateSession(access, session);
        if (!VerifyPassword(user, currentPassword))''',
'''        var user = ValidateSession(access, session);
        if (user.IsRecoveryAccount)
            throw new MasterAuthorizationException(
                "The recovery password is managed only in InNasc Global Admin.");
        if (!VerifyPassword(user, currentPassword))''',
"root password restriction")
s = once(s,
'''        if (!access.Users.Any(user => user.Enabled && user.Role == MasterUserRole.Owner))''',
'''        if (!access.Users.Any(user => user.Enabled &&
                user.Role == MasterUserRole.Owner))''',
"keep owner validation compatible")
write(p, s)

# Hide recovery from normal master-user list. Keep it usable for sign-in only.
p = "MasterAccountForms.cs"
s = read(p)
s = s.replace(
"ResultAccess.Users.OrderBy(user => user.Username",
"ResultAccess.Users.Where(user => !user.IsRecoveryAccount).OrderBy(user => user.Username")
write(p, s)

# ---------- Future remote sync seam ----------
write("InNascLicenseSyncContract.cs", r'''namespace InNasc;

internal sealed record InNascLicenseSyncSnapshot(
    Guid CompanyId,
    Guid LicenseId,
    string LicenseName,
    int DeviceLimit,
    DateTime? ExpiresUtc,
    int DeviceCount,
    DateTime ObservedUtc,
    string RevisionId);

internal interface IInNascLicenseRemoteSyncTransport
{
    Task PushSnapshotAsync(
        InNascLicenseSyncSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

internal static class InNascLicenseSyncContract
{
    // 5.3 implements local .nasc <-> .nascglobal reconciliation. This transport-neutral
    // snapshot is the hook for a later authenticated server sync without another data-model rewrite.
    public static InNascLicenseSyncSnapshot Snapshot(
        InNascCompanyRecord company,
        InNascCompanyFileRecord file) =>
        new(
            company.Id,
            file.Id,
            file.Name,
            file.DeviceLimit,
            file.ExpiresUtc,
            file.LastObservedDeviceCount,
            file.LastCatalogSyncUtc ?? DateTime.UtcNow,
            file.LastObservedRevisionId);
}
''')
