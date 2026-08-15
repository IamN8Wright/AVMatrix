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

# Keep both executables on one product release version.
for p in ["InNasc.csproj", "InNasc.GlobalAdmin.csproj"]:
    s = read(p)
    s = re.sub(r"<Version>[^<]+</Version>", "<Version>5.3.0</Version>", s, count=1)
    write(p, s)

# Extend Global Admin smoke tests around existing licensing and branding flows.
p = "tests/InNasc.GlobalAdmin.SmokeTests/Program.cs"
s = read(p)

s = once(s,
'''        RequireThrows<DeviceLimitExceededException>(() =>
            DeviceLimitPolicy.RequireCapacity(data.MasterAccess, data, 1));''',
'''        RequireThrows<DeviceLimitExceededException>(() =>
            DeviceLimitPolicy.RequireCapacity(data.MasterAccess, data, 1));
        data.MasterAccess.LicenseExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        RequireThrows<LicenseExpiredException>(() =>
            DeviceLimitPolicy.RequireNewClientAllowed(data.MasterAccess, data));
        data.MasterAccess.LicenseExpiresUtc = null;''',
"expiration enforcement smoke")

s = once(s,
'''        var reopened = PortableDataService.Import(sourcePath, sourceSession.MasterKey).Data;
        Require(reopened.ProjectName == "Branded Company" && reopened.MasterAccess.DeviceLimit == 500,
            "Editable envelope metadata was not applied when opening the imported company.");
        _ = MasterAccessService.SignIn(PortableDataService.ReadMasterAccess(sourcePath),
            "existing-owner", OwnerPassword);''',
'''        var reopened = PortableDataService.Import(sourcePath, sourceSession.MasterKey).Data;
        Require(reopened.ProjectName == "Branded Company" && reopened.MasterAccess.DeviceLimit == 500,
            "Editable envelope metadata was not applied when opening the imported company.");

        // Simulate a normal main-app save. Branding/license fields must survive.
        PortableDataService.ExportMaster(sourcePath, reopened, sourceSession);
        var afterAppSave = PortableDataService.ReadCompanySummary(sourcePath);
        Require(afterAppSave.CompanyLogoBase64 == logoBase64 &&
                afterAppSave.CompanyName == "Branded Company",
            "A normal InNasc save erased Global Admin company branding.");

        InNascCompanyGlobalAdminService.RenameLicense(
            globalPath, global.Catalog, global.Session, company, imported, "Branded License");
        InNascGlobalCoreService.SetLicenseExpiration(
            globalPath, global.Catalog, global.Session, company.Id, imported.Id,
            DateTime.UtcNow.AddYears(1));
        InNascGlobalCoreService.SetRecoveryPassword(
            globalPath, global.Catalog, global.Session, company.Id, imported.Id,
            "Root-pass-530");
        InNascCompanyAccessSyncService.SyncFile(
            globalPath, global.Catalog, global.Session, company, imported);

        var licensed = PortableDataService.ReadMasterAccess(sourcePath);
        Require(licensed.LicenseName == "Branded License" &&
                licensed.LicenseExpiresUtc is not null,
            "License rename/expiration did not publish.");
        Require(MasterAccessService.SignIn(licensed, "root", "Root-pass-530").IsOwner,
            "The hidden recovery account could not unlock the company.");
        _ = MasterAccessService.SignIn(licensed, "existing-owner", OwnerPassword);''',
"branding retention lifecycle smoke")

# Add a locally-created .nasc user and verify reconciliation imports identity/access without breaking login.
insert = '''        _ = MasterAccessService.SignIn(licensed, "existing-owner", OwnerPassword);'''
replacement = '''        _ = MasterAccessService.SignIn(licensed, "existing-owner", OwnerPassword);

        var localData = PortableDataService.Import(sourcePath, sourceSession.MasterKey).Data;
        var localSession = MasterAccessService.SignIn(
            localData.MasterAccess, "existing-owner", OwnerPassword);
        _ = MasterAccessService.AddUser(
            localData.MasterAccess, localSession,
            "field-tech", "Field Tech", "Field-pass-530", MasterUserRole.Tech);
        PortableDataService.ExportMaster(sourcePath, localData, localSession);
        InNascCompanyAccessSyncService.SyncFile(
            globalPath, global.Catalog, global.Session, company, imported);
        var pulled = company.Users.Single(user => user.Username == "field-tech");
        Require(pulled.Role == MasterUserRole.Tech && !pulled.CredentialReady,
            "Current .nasc users/access levels were not pulled into Global Admin as expected.");
        _ = MasterAccessService.SignIn(
            PortableDataService.ReadMasterAccess(sourcePath),
            "field-tech", "Field-pass-530");'''
s = once(s, insert, replacement, "user reconcile smoke")

# Backup redaction test just before branding-flow method closes.
anchor = '''        _ = MasterAccessService.SignIn(
            PortableDataService.ReadMasterAccess(sourcePath),
            "field-tech", "Field-pass-530");
    }'''
replacement = '''        _ = MasterAccessService.SignIn(
            PortableDataService.ReadMasterAccess(sourcePath),
            "field-tech", "Field-pass-530");

        var sensitive = new AppData
        {
            Clients = [CompanyWithDevice("Sensitive")],
            MasterAccess = new MasterAccessControl()
        };
        var equipment = sensitive.Clients[0].Locations[0].Rooms[0].Equipment[0];
        equipment.Username = "admin";
        equipment.Password = "secret";
        equipment.ConfigurationFiles.Add(new DeviceConfigurationFile
        {
            FileName = "config.bin",
            ContentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            ContentIncluded = true
        });
        var redactedPath = Path.Combine(root, "Redacted.nasc");
        PortableDataService.Export(
            redactedPath,
            sensitive,
            null,
            new PortableBackupOptions(false, false));
        var redacted = PortableDataService.Import(redactedPath).Data
            .Clients[0].Locations[0].Rooms[0].Equipment[0];
        Require(redacted.Username.Length == 0 && redacted.Password.Length == 0 &&
                redacted.ConfigurationFiles.Count == 0,
            "Backup privacy options did not redact credentials/config files.");
    }'''
s = once(s, anchor, replacement, "backup privacy smoke")

write(p, s)
