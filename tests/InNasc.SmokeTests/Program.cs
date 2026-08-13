using System.Security.Cryptography;
using System.Text;
using System.Drawing.Imaging;

namespace InNasc.SmokeTests;

internal static class Program
{
    private const string AdminPassword = "Admin-pass-500";
    private const string TechPassword = "Tech-pass-500";

    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "InNasc-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunGlobalLoginAndCompanyFlow(root);
            RunLegacyCompatibilityFlow(root);
            RunBrandingFlow();
            Console.WriteLine("InNasc smoke tests passed.");
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

    private static void RunGlobalLoginAndCompanyFlow(string root)
    {
        var globalPath = Path.Combine(root, "Test.nascglobal");
        var login = InNascGlobalCoreService.Create(
            globalPath, "admin", "Global Admin", AdminPassword);
        Require(File.Exists(globalPath), "Global catalog was not created.");
        Require(login.Session.IsGlobalAdmin, "Initial account is not a Global Admin.");
        RequireThrows<MasterAuthorizationException>(() =>
            InNascGlobalCoreService.SignIn(globalPath, "admin", "wrong-password"));

        var companyPath = Path.Combine(root, "Acme.nasc");
        var company = InNascGlobalCoreService.CreateCompany(
            globalPath, login.Catalog, login.Session, "Acme", companyPath);
        Require(File.Exists(companyPath), "Company file was not created.");

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
        InNascCompanyAccessSyncService.SyncAll(login.Catalog, login.Session);

        var techLogin = InNascGlobalCoreService.SignIn(globalPath, "TECH", TechPassword);
        var allowed = InNascGlobalCoreService.CompaniesFor(techLogin.Catalog, techLogin.Session);
        Require(allowed.Count == 1 && allowed[0].Id == company.Id,
            "Company selection did not honor the assigned membership.");
        var companySession = InNascGlobalCoreService.CreateCompanySession(
            techLogin.Session, techLogin.Catalog, allowed[0]);
        Require(companySession.Role == MasterUserRole.Tech,
            "The assigned company role was not applied.");

        var companyData = PortableDataService.Import(companyPath, company.CompanyKeyBase64).Data;
        Require(companyData.MasterAccess.Users.Any(x => x.Id == user.Id && x.Role == MasterUserRole.Tech),
            "Company access was not synchronized into the .nasc file.");

        const string resetPassword = "Tech-reset-500";
        InNascGlobalCoreService.ResetPassword(
            globalPath, login.Catalog, login.Session, user.Id, resetPassword);
        RequireThrows<MasterAuthorizationException>(() =>
            InNascGlobalCoreService.SignIn(globalPath, "tech", TechPassword));
        _ = InNascGlobalCoreService.SignIn(globalPath, "tech", resetPassword);
    }

    private static void RunLegacyCompatibilityFlow(string root)
    {
        var legacyPath = Path.Combine(root, "Legacy.avmatrix");
        var client = new ClientRecord { Name = "Legacy Client" };
        var access = new MasterAccessControl();
        _ = MasterAccessService.CreateInitialOwner(
            access, "legacy-owner", "Legacy Owner", "Legacy-pass-500");
        var legacySession = MasterAccessService.SignIn(access, "legacy-owner", "Legacy-pass-500");
        var legacyData = new AppData
        {
            ProjectName = "Legacy Company",
            Clients = [client],
            MasterAccess = access
        };
        PortableDataService.ExportMaster(legacyPath, legacyData, legacySession);

        var legacyClientDirectory = ClientSubmatrixService.SharedDirectory(legacyPath);
        Directory.CreateDirectory(legacyClientDirectory);
        var legacyClientPath = Path.Combine(legacyClientDirectory, $"{client.Id:N}.avclient");
        PortableDataService.Export(
            legacyClientPath,
            ClientSubmatrixService.ClientPackage(client),
            legacySession.MasterKey);

        var legacyBytes = File.ReadAllBytes(legacyPath);
        Require(PortableDataService.IsAccountProtected(legacyBytes),
            "Legacy account envelope was not detected.");
        var recoveredAccess = PortableDataService.ReadMasterAccess(legacyBytes);
        var recoveredSession = MasterAccessService.SignIn(
            recoveredAccess, "legacy-owner", "Legacy-pass-500");
        var sourceHash = Convert.ToHexString(SHA256.HashData(legacyBytes));

        var globalPath = Path.Combine(root, "Migration.nascglobal");
        var global = InNascGlobalCoreService.Create(
            globalPath, "migration-admin", "Migration Admin", AdminPassword);
        var destination = Path.Combine(root, "Migrated.nasc");
        var company = InNascGlobalCoreService.MigrateLegacyCompany(
            globalPath,
            global.Catalog,
            global.Session,
            "Migrated Company",
            legacyPath,
            destination,
            recoveredSession.MasterKey);

        Require(File.Exists(destination), "Migrated .nasc file was not created.");
        Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacyPath))) == sourceHash,
            "Legacy .avmatrix source was modified.");
        var destinationClient = ClientSubmatrixService.SharedClientPath(destination, client.Id);
        Require(File.Exists(destinationClient) && destinationClient.EndsWith(".nascclient"),
            "Legacy .avclient payload was not migrated to .nascclient.");
        var migrated = PortableDataService.Import(destination, company.CompanyKeyBase64).Data;
        Require(migrated.ProjectName == "Migrated Company" && migrated.Clients.Count == 1,
            "Migrated company data did not round-trip.");

        var unprotectedPath = Path.Combine(root, "Legacy-Unprotected.avmatrix");
        var unprotected = PortableDataService.ExportBytes(legacyData, null, out _);
        var legacyFormat = Encoding.UTF8.GetBytes("AV Matrix Studio Transfer");
        var currentFormat = Encoding.UTF8.GetBytes("InNasc Company");
        Require(currentFormat.Length <= legacyFormat.Length,
            "Legacy format fixture cannot be produced safely.");
        var json = Encoding.UTF8.GetString(unprotected)
            .Replace("InNasc Company", "AV Matrix Studio Transfer", StringComparison.Ordinal);
        File.WriteAllText(unprotectedPath, json);
        Require(PortableDataService.Import(unprotectedPath).Data.Clients.Count == 1,
            "Legacy AV Matrix transfer format is no longer readable.");
    }

    private static void RunBrandingFlow()
    {
        using var icon = AppBrand.CreateIcon();
        Require(icon.Width > 0 && icon.Height > 0, "The embedded InNasc app icon is unreadable.");

        UiTheme.SetDarkMode(false);
        using var light = AppBrand.CreateLogo();
        var lightHash = ImageHash(light);
        UiTheme.SetDarkMode(true);
        using var dark = AppBrand.CreateLogo();
        var darkHash = ImageHash(dark);
        Require(lightHash != darkHash, "Light and Dark InNasc marks are not distinct.");

        using var about = new AboutControl();
        var primary = Descendants(about)
            .OfType<PictureBox>()
            .FirstOrDefault(x => x.Image?.Width == 3000 && x.Image.Height == 945);
        Require(primary is not null, "Primary Horizontal branding is missing from About.");
        UiTheme.SetDarkMode(false);
    }

    private static string ImageHash(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
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
