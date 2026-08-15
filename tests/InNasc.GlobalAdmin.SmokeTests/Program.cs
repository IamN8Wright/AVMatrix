using System.Security.Cryptography;
using System.Text.Json;

namespace InNasc.GlobalAdmin.SmokeTests;

internal static class Program
{
    private const string AdminPassword = "Admin-pass-520";
    private const string OwnerPassword = "Owner-pass-520";
    private const string TechPassword = "Tech-pass-520";

    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "InNasc-Admin-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunCompanyLicensingAndUserFlow(root);
            RunCompanyImportAndBrandingFlow(root);
            RunLegacyMigrationFlow(root);
            RunV51CatalogUpgradeFlow(root);
            RunUiSeparationFlow(root);
            Console.WriteLine("InNasc Global Admin smoke tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void RunCompanyLicensingAndUserFlow(string root)
    {
        var globalPath = Path.Combine(root, "Admin.nascglobal");
        var login = InNascGlobalCoreService.Create(
            globalPath, "admin", "Global Admin", AdminPassword);
        Require(File.Exists(globalPath), "Global Admin catalog was not created.");
        Require(login.Catalog.GlobalAdmins.Count == 1 && login.Catalog.Users.Count == 0,
            "Global Admin identities were not separated from legacy/company users.");

        var companyPath = Path.Combine(root, "Acme.nasc");
        var company = InNascGlobalCoreService.CreateCompany(
            globalPath, login.Catalog, login.Session, "Acme", companyPath, 250);
        Require(File.Exists(companyPath), "Company .nasc file was not generated.");
        var initialAccess = PortableDataService.ReadMasterAccess(companyPath);
        Require(initialAccess.DeviceLimit == 250, "The initial 250-device tier was not published.");
        Require(initialAccess.Users.Count == 0,
            "A Global Admin was incorrectly published as a company user.");
        RequireThrows<MasterAuthorizationException>(() =>
            MasterAccessService.SignIn(initialAccess, "admin", AdminPassword));

        var owner = InNascGlobalCoreService.AddCompanyUser(
            globalPath, login.Catalog, login.Session, company.Id,
            "owner", "Company Owner", OwnerPassword, MasterUserRole.Owner);
        var tech = InNascGlobalCoreService.AddCompanyUser(
            globalPath, login.Catalog, login.Session, company.Id,
            "tech", "Field Tech", TechPassword, MasterUserRole.Tech);
        InNascCompanyAccessSyncService.SyncCompany(globalPath, login.Catalog, login.Session, company);
        var published = PortableDataService.ReadMasterAccess(companyPath);
        var ownerSession = MasterAccessService.SignIn(published, "OWNER", OwnerPassword);
        var techSession = MasterAccessService.SignIn(published, "tech", TechPassword);
        Require(ownerSession.IsOwner && techSession.Role == MasterUserRole.Tech,
            "Company-scoped access levels were not published.");
        RequireThrows<MasterAuthorizationException>(() =>
            InNascGlobalCoreService.SignIn(globalPath, "tech", TechPassword));

        var secondPath = Path.Combine(root, "Acme-Expansion.nasc");
        var expansion = InNascGlobalCoreService.AddCompanyFile(
            globalPath, login.Catalog, login.Session, company.Id,
            "Acme Expansion", secondPath, 500);
        InNascCompanyAccessSyncService.SyncFile(
            globalPath, login.Catalog, login.Session, company, expansion);
        var expansionAccess = PortableDataService.ReadMasterAccess(secondPath);
        Require(expansionAccess.DeviceLimit == 500 && expansionAccess.LicenseId == expansion.Id,
            "The additional .nasc license or its tier was not published.");
        _ = MasterAccessService.SignIn(expansionAccess, "owner", OwnerPassword);

        InNascGlobalCoreService.UpdateCompanyUser(
            globalPath, login.Catalog, login.Session, company.Id,
            tech.Id, "Read Only User", MasterUserRole.ReadOnly);
        const string resetPassword = "Tech-reset-520";
        InNascGlobalCoreService.ResetCompanyUserPassword(
            globalPath, login.Catalog, login.Session, company.Id, tech.Id, resetPassword);
        InNascCompanyAccessSyncService.SyncCompany(globalPath, login.Catalog, login.Session, company);
        var resetAccess = PortableDataService.ReadMasterAccess(companyPath);
        RequireThrows<MasterAuthorizationException>(() =>
            MasterAccessService.SignIn(resetAccess, "tech", TechPassword));
        Require(MasterAccessService.SignIn(resetAccess, "tech", resetPassword).Role == MasterUserRole.ReadOnly,
            "Company password reset/access edit did not publish.");

        InNascGlobalCoreService.SetDeviceLimit(
            globalPath, login.Catalog, login.Session, company.Id, company.Files[0].Id, 1);
        InNascCompanyAccessSyncService.SyncFile(
            globalPath, login.Catalog, login.Session, company, company.Files[0]);
        var data = PortableDataService.Import(companyPath, ownerSession.MasterKey).Data;
        data.Clients = [CompanyWithDevice("One")];
        PortableDataService.ExportMaster(companyPath, data, ownerSession);
        Require(InNascGlobalCoreService.GetDeviceCount(company.Files[0]) == 1,
            "Global Admin did not read current device usage.");
        RequireThrows<DeviceLimitExceededException>(() =>
            DeviceLimitPolicy.RequireCapacity(data.MasterAccess, data, 1));
        data.MasterAccess.LicenseExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        RequireThrows<LicenseExpiredException>(() =>
            DeviceLimitPolicy.RequireNewClientAllowed(data.MasterAccess, data));
        data.MasterAccess.LicenseExpiresUtc = null;

        InNascGlobalCoreService.SetDeviceLimit(
            globalPath, login.Catalog, login.Session, company.Id, company.Files[0].Id, 0);
        InNascCompanyAccessSyncService.SyncFile(
            globalPath, login.Catalog, login.Session, company, company.Files[0]);
        data = PortableDataService.Import(companyPath, ownerSession.MasterKey).Data;
        data.Clients.Add(CompanyWithDevice("Two"));
        PortableDataService.ExportMaster(companyPath, data, ownerSession);
        RequireThrows<InvalidOperationException>(() =>
            InNascGlobalCoreService.SetDeviceLimit(
                globalPath, login.Catalog, login.Session, company.Id, company.Files[0].Id, 1));

        InNascGlobalCoreService.DeleteCompanyUser(
            globalPath, login.Catalog, login.Session, company.Id, tech.Id);
        InNascCompanyAccessSyncService.SyncCompany(globalPath, login.Catalog, login.Session, company);
        RequireThrows<MasterAuthorizationException>(() =>
            MasterAccessService.SignIn(PortableDataService.ReadMasterAccess(companyPath), "tech", resetPassword));
        RequireThrows<InvalidOperationException>(() =>
            InNascGlobalCoreService.DeleteCompanyUser(
                globalPath, login.Catalog, login.Session, company.Id, owner.Id));

        var secondAdmin = InNascGlobalCoreService.AddGlobalAdmin(
            globalPath, login.Catalog, login.Session, "admin2", "Second Admin", "Admin2-pass-520");
        _ = InNascGlobalCoreService.SignIn(globalPath, "admin2", "Admin2-pass-520");
        InNascGlobalCoreService.ResetGlobalAdminPassword(
            globalPath, login.Catalog, login.Session, secondAdmin.Id, "Admin2-reset-520");
        RequireThrows<MasterAuthorizationException>(() =>
            InNascGlobalCoreService.SignIn(globalPath, "admin2", "Admin2-pass-520"));
        _ = InNascGlobalCoreService.SignIn(globalPath, "admin2", "Admin2-reset-520");
        InNascGlobalCoreService.DeleteGlobalAdmin(
            globalPath, login.Catalog, login.Session, secondAdmin.Id);

        var retained = InNascGlobalCoreService.DeleteCompany(
            globalPath, login.Catalog, login.Session, company.Id);
        Require(retained.Count == 2 && File.Exists(companyPath) && File.Exists(secondPath),
            "Deleting a company did not retain the physical .nasc files.");
    }

    private static void RunCompanyImportAndBrandingFlow(string root)
    {
        var globalPath = Path.Combine(root, "Branding.nascglobal");
        var global = InNascGlobalCoreService.Create(
            globalPath, "branding-admin", "Branding Admin", AdminPassword);
        var targetPath = Path.Combine(root, "Branding-Target.nasc");
        var company = InNascGlobalCoreService.CreateCompany(
            globalPath, global.Catalog, global.Session, "Target Company", targetPath, 250);

        var sourcePath = Path.Combine(root, "Imported-Existing.nasc");
        var access = new MasterAccessControl();
        _ = MasterAccessService.CreateInitialOwner(
            access, "existing-owner", "Existing Owner", OwnerPassword);
        var sourceSession = MasterAccessService.SignIn(access, "existing-owner", OwnerPassword);
        var sourceData = new AppData
        {
            ProjectName = "Old Embedded Name",
            Clients = [CompanyWithDevice("Imported")],
            MasterAccess = access
        };
        PortableDataService.ExportMaster(sourcePath, sourceData, sourceSession);

        var imported = InNascCompanyGlobalAdminService.ImportExisting(
            globalPath, global.Catalog, global.Session, company, sourcePath, sourceSession.MasterKey);
        Require(company.Files.Count == 2 && imported.FilePath == sourcePath,
            "Existing .nasc file was not imported into the company card.");
        _ = MasterAccessService.SignIn(PortableDataService.ReadMasterAccess(sourcePath),
            "existing-owner", OwnerPassword);

        using var logoBitmap = new Bitmap(8, 8);
        using var logoStream = new MemoryStream();
        logoBitmap.Save(logoStream, System.Drawing.Imaging.ImageFormat.Png);
        var logoBase64 = Convert.ToBase64String(logoStream.ToArray());
        InNascCompanyGlobalAdminService.SetCompanyLogo(
            globalPath, global.Catalog, global.Session, company, logoBase64);
        InNascCompanyGlobalAdminService.RenameCompany(
            globalPath, global.Catalog, global.Session, company, "Branded Company");
        InNascGlobalCoreService.SetDeviceLimit(
            globalPath, global.Catalog, global.Session, company.Id, imported.Id, 500);
        InNascCompanyGlobalAdminService.ApplyToFile(company, imported);

        var summary = PortableDataService.ReadCompanySummary(sourcePath);
        Require(summary.CompanyName == "Branded Company" && summary.DeviceLimit == 500 &&
                summary.CompanyLogoBase64 == logoBase64,
            "Embedded company name, tier, or logo did not update.");
        Require(InNascCompanyEnvelopeMetadataService.DecodeLogo(summary.CompanyLogoBase64) is not null,
            "Embedded company logo could not be decoded for the welcome page.");
        var reopened = PortableDataService.Import(sourcePath, sourceSession.MasterKey).Data;
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
        _ = MasterAccessService.SignIn(licensed, "existing-owner", OwnerPassword);

        var localData = PortableDataService.Import(sourcePath, sourceSession.MasterKey).Data;
        var localSession = MasterAccessService.SignIn(
            localData.MasterAccess, "existing-owner", OwnerPassword);
        _ = MasterAccessService.AddUser(
            localData.MasterAccess, localSession,
            "field-tech", "Field Tech", "Field-pass-530", MasterUserRole.Tech);
        PortableDataService.ExportMaster(sourcePath, localData, localSession);
        InNascCompanyAccessSyncService.ReconcileFile(
            globalPath, global.Catalog, global.Session, company, imported);
        var pulled = company.Users.Single(user => user.Username == "field-tech");
        Require(pulled.Role == MasterUserRole.Tech && !pulled.CredentialReady,
            "Current .nasc users/access levels were not pulled into Global Admin as expected.");
        _ = MasterAccessService.SignIn(
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
    }

    private static ClientRecord CompanyWithDevice(string name)
    {
        var room = new RoomRecord { Name = "Room" };
        room.Equipment.Add(new EquipmentRecord { Description = name });
        var location = new LocationRecord { Name = "Location", Rooms = [room] };
        return new ClientRecord { Name = $"Client {name}", Locations = [location] };
    }

    private static void RunLegacyMigrationFlow(string root)
    {
        var legacyPath = Path.Combine(root, "Legacy.avmatrix");
        var client = CompanyWithDevice("Legacy");
        var legacyAccess = new MasterAccessControl();
        _ = MasterAccessService.CreateInitialOwner(
            legacyAccess, "legacy-owner", "Legacy Owner", "Legacy-pass-520");
        var legacySession = MasterAccessService.SignIn(
            legacyAccess, "legacy-owner", "Legacy-pass-520");
        var legacyData = new AppData
        {
            ProjectName = "Legacy Company",
            Clients = [client],
            MasterAccess = legacyAccess
        };
        PortableDataService.ExportMaster(legacyPath, legacyData, legacySession);
        var clientDirectory = ClientSubmatrixService.SharedDirectory(legacyPath);
        Directory.CreateDirectory(clientDirectory);
        var legacyClientPath = Path.Combine(clientDirectory, $"{client.Id:N}.avclient");
        PortableDataService.Export(
            legacyClientPath, ClientSubmatrixService.ClientPackage(client), legacySession.MasterKey);
        var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacyPath)));

        var globalPath = Path.Combine(root, "Migration.nascglobal");
        var global = InNascGlobalCoreService.Create(
            globalPath, "migration-admin", "Migration Admin", AdminPassword);
        var destination = Path.Combine(root, "Migrated.nasc");
        var company = InNascGlobalCoreService.MigrateLegacyCompany(
            globalPath, global.Catalog, global.Session, "Migrated Company",
            legacyPath, destination, legacySession.MasterKey);

        Require(File.Exists(destination), "Migrated .nasc file was not generated.");
        Require(company.Files.Single().DeviceLimit == 0,
            "Legacy migration was not preserved with an Unlimited tier.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacyPath))) == sourceHash,
            "Legacy .avmatrix source was modified.");
        var destinationClient = ClientSubmatrixService.SharedClientPath(destination, client.Id);
        Require(File.Exists(destinationClient) && destinationClient.EndsWith(".nascclient"),
            "Legacy .avclient payload was not migrated to .nascclient.");
        Require(PortableDataService.ReadMasterAccess(destination).Users.Count == 0,
            "Global Admin was incorrectly granted migrated company access.");
        _ = InNascGlobalCoreService.AddCompanyUser(
            globalPath, global.Catalog, global.Session, company.Id,
            "migration-owner", "Migration Owner", OwnerPassword, MasterUserRole.Owner);
        InNascCompanyAccessSyncService.SyncCompany(globalPath, global.Catalog, global.Session, company);
        var access = PortableDataService.ReadMasterAccess(destination);
        var session = MasterAccessService.SignIn(access, "migration-owner", OwnerPassword);
        var migrated = PortableDataService.Import(destination, session.MasterKey).Data;
        Require(migrated.ProjectName == "Migrated Company" && migrated.Clients.Count == 1,
            "Migrated company data did not round-trip through company login.");
    }

    private static void RunUiSeparationFlow(string root)
    {
        var globalPath = Path.Combine(root, "Ui.nascglobal");
        var login = InNascGlobalCoreService.Create(globalPath, "ui-admin", "UI Admin", AdminPassword);
        var company = InNascGlobalCoreService.CreateCompany(
            globalPath, login.Catalog, login.Session, "UI Company", Path.Combine(root, "Ui.nasc"), 500);
        using var directory = new InNascGlobalAdminForm(globalPath, login.Catalog, login.Session);
        using var details = new InNascCompanyAdminForm(globalPath, login.Catalog, login.Session, company);
        using var admins = new InNascGlobalAdminUsersForm(globalPath, login.Catalog, login.Session);
        var directoryText = string.Join(" ", Descendants(directory).Select(control => control.Text));
        var detailsText = string.Join(" ", Descendants(details).Select(control => control.Text));
        Require(directoryText.Contains("Company Directory") && directoryText.Contains("Global Admins"),
            "Company-card Global Admin directory UI is missing.");
        Require(detailsText.Contains(".nasc licenses and device tiers") && detailsText.Contains("Company users"),
            "Company license/user detail UI is missing.");
    }

    private static void RunV51CatalogUpgradeFlow(string root)
    {
        var globalPath = Path.Combine(root, "V51.nascglobal");
        var created = InNascGlobalCoreService.Create(
            globalPath, "legacy-admin", "Legacy Global Admin", AdminPassword);
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        var envelope = JsonSerializer.Deserialize<InNascGlobalEnvelope>(File.ReadAllBytes(globalPath), json)!;
        var legacyCompanyPath = Path.Combine(root, "V51-Company.nasc");
        var legacyKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var legacy = new InNascGlobalCatalog
        {
            FormatVersion = 1,
            CatalogId = created.Catalog.CatalogId,
            GlobalAdmins = [],
            Users =
            [
                new InNascGlobalUserRecord
                {
                    Id = created.Session.UserId,
                    Username = created.Session.Username,
                    DisplayName = created.Session.DisplayName,
                    IsGlobalAdmin = true
                }
            ],
            Companies =
            [
                new InNascCompanyRecord
                {
                    Name = "5.1 Company",
                    FilePath = legacyCompanyPath,
                    CompanyKeyBase64 = legacyKey,
                    Files = [],
                    Users = []
                }
            ]
        };
        envelope.PayloadBase64 = Convert.ToBase64String(JwePasswordProtection.Protect(
            JsonSerializer.SerializeToUtf8Bytes(legacy, json), created.Session.GlobalKey));
        File.WriteAllBytes(globalPath, JsonSerializer.SerializeToUtf8Bytes(envelope, json));

        var upgraded = InNascGlobalCoreService.SignIn(globalPath, "legacy-admin", AdminPassword);
        var company = upgraded.Catalog.Companies.Single();
        Require(upgraded.Catalog.FormatVersion == 2 && upgraded.Catalog.Users.Count == 0,
            "The 5.1 Global catalog was not upgraded to the separated account model.");
        Require(upgraded.Catalog.GlobalAdmins.Single().Id == upgraded.Session.UserId,
            "The 5.1 Global Admin identity was not retained.");
        Require(company.Files.Count == 1 && company.Files[0].FilePath == legacyCompanyPath &&
                company.Files[0].CompanyKeyBase64 == legacyKey && company.Files[0].DeviceLimit == 0,
            "The 5.1 company file/key was not preserved as an Unlimited license.");
        Require(company.Users.Count == 1 && company.Users[0].Role == MasterUserRole.Owner,
            "The legacy Global Admin company access was not migrated to a separate company-user record.");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }
}
