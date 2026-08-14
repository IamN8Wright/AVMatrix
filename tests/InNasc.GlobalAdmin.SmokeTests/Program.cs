using System.Security.Cryptography;

namespace InNasc.GlobalAdmin.SmokeTests;

internal static class Program
{
    private const string AdminPassword = "Admin-pass-510";
    private const string TechPassword = "Tech-pass-510";

    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "InNasc-Admin-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunCompanyPublishingFlow(root);
            RunLegacyMigrationFlow(root);
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

    private static void RunCompanyPublishingFlow(string root)
    {
        var globalPath = Path.Combine(root, "Admin.nascglobal");
        var login = InNascGlobalCoreService.Create(
            globalPath, "admin", "Global Admin", AdminPassword);
        Require(File.Exists(globalPath), "Global Admin catalog was not created.");
        Require(login.Session.IsGlobalAdmin, "Initial account is not a Global Admin.");

        var companyPath = Path.Combine(root, "Acme.nasc");
        var company = InNascGlobalCoreService.CreateCompany(
            globalPath, login.Catalog, login.Session, "Acme", companyPath);
        Require(File.Exists(companyPath), "Company .nasc file was not generated.");

        var initialAccess = PortableDataService.ReadMasterAccess(companyPath);
        var adminCompanySession = MasterAccessService.SignIn(
            initialAccess, "ADMIN", AdminPassword);
        Require(adminCompanySession.IsOwner, "Global Admin was not published as company Owner.");
        Require(adminCompanySession.MasterKey == company.CompanyKeyBase64,
            "The generated company key could not be unlocked by the company login.");

        var user = InNascGlobalCoreService.AddUser(
            globalPath,
            login.Catalog,
            login.Session,
            "tech",
            "Field Tech",
            TechPassword,
            globalAdmin: false);
        InNascGlobalCoreService.SetMembership(
            globalPath,
            login.Catalog,
            login.Session,
            user.Id,
            company.Id,
            assigned: true,
            MasterUserRole.Tech);
        InNascCompanyAccessSyncService.SyncAll(
            globalPath, login.Catalog, login.Session);

        var published = PortableDataService.ReadMasterAccess(companyPath);
        var techSession = MasterAccessService.SignIn(published, "TECH", TechPassword);
        Require(techSession.Role == MasterUserRole.Tech,
            "The assigned company role was not published.");
        Require(PortableDataService.Import(companyPath, techSession.MasterKey).Data.ProjectName == "Acme",
            "The user could not decrypt the assigned company directly.");

        const string resetPassword = "Tech-reset-510";
        InNascGlobalCoreService.ResetPassword(
            globalPath, login.Catalog, login.Session, user.Id, resetPassword);
        InNascCompanyAccessSyncService.SyncAll(
            globalPath, login.Catalog, login.Session);
        var resetAccess = PortableDataService.ReadMasterAccess(companyPath);
        RequireThrows<MasterAuthorizationException>(() =>
            MasterAccessService.SignIn(resetAccess, "tech", TechPassword));
        _ = MasterAccessService.SignIn(resetAccess, "tech", resetPassword);
    }

    private static void RunLegacyMigrationFlow(string root)
    {
        var legacyPath = Path.Combine(root, "Legacy.avmatrix");
        var client = new ClientRecord { Name = "Legacy Client" };
        var legacyAccess = new MasterAccessControl();
        _ = MasterAccessService.CreateInitialOwner(
            legacyAccess, "legacy-owner", "Legacy Owner", "Legacy-pass-510");
        var legacySession = MasterAccessService.SignIn(
            legacyAccess, "legacy-owner", "Legacy-pass-510");
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
            legacyClientPath,
            ClientSubmatrixService.ClientPackage(client),
            legacySession.MasterKey);
        var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacyPath)));

        var globalPath = Path.Combine(root, "Migration.nascglobal");
        var global = InNascGlobalCoreService.Create(
            globalPath, "migration-admin", "Migration Admin", AdminPassword);
        var destination = Path.Combine(root, "Migrated.nasc");
        _ = InNascGlobalCoreService.MigrateLegacyCompany(
            globalPath,
            global.Catalog,
            global.Session,
            "Migrated Company",
            legacyPath,
            destination,
            legacySession.MasterKey);

        Require(File.Exists(destination), "Migrated .nasc file was not generated.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacyPath))) == sourceHash,
            "Legacy .avmatrix source was modified.");
        var destinationClient = ClientSubmatrixService.SharedClientPath(destination, client.Id);
        Require(File.Exists(destinationClient) && destinationClient.EndsWith(".nascclient"),
            "Legacy .avclient payload was not migrated to .nascclient.");
        var access = PortableDataService.ReadMasterAccess(destination);
        var adminSession = MasterAccessService.SignIn(
            access, "migration-admin", AdminPassword);
        var migrated = PortableDataService.Import(destination, adminSession.MasterKey).Data;
        Require(migrated.ProjectName == "Migrated Company" && migrated.Clients.Count == 1,
            "Migrated company data did not round-trip through direct company login.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }
}
