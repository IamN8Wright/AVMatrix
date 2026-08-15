using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace InNasc.SmokeTests;

internal static class Program
{
    private const string OwnerPassword = "Owner-pass-510";

    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "InNasc-User-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunDirectCompanyLogin(root);
            RunDeviceLimitEnforcement(root);
            RunLegacyReadCompatibility(root);
            RunBrandingFlow();
            Console.WriteLine("InNasc user application smoke tests passed.");
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

    private static void RunDirectCompanyLogin(string root)
    {
        var companyPath = Path.Combine(root, "Direct-Login.nasc");
        var access = new MasterAccessControl();
        _ = MasterAccessService.CreateInitialOwner(
            access, "owner", "Company Owner", OwnerPassword);
        var ownerSession = MasterAccessService.SignIn(access, "owner", OwnerPassword);
        var data = new AppData
        {
            ProjectName = "Direct Login Company",
            Clients = [new ClientRecord { Name = "First Client" }],
            MasterAccess = access
        };
        PortableDataService.ExportMaster(companyPath, data, ownerSession);

        var publishedAccess = PortableDataService.ReadMasterAccess(companyPath);
        RequireThrows<MasterAuthorizationException>(() =>
            MasterAccessService.SignIn(publishedAccess, "owner", "wrong-password"));
        var signedIn = MasterAccessService.SignIn(publishedAccess, "OWNER", OwnerPassword);
        var opened = PortableDataService.Import(companyPath, signedIn.MasterKey).Data;
        Require(opened.ProjectName == "Direct Login Company", "The company name did not round-trip.");
        Require(opened.Clients.Count == 1, "The company inventory did not open after direct login.");
        Require(signedIn.Role == MasterUserRole.Owner, "The company role was not loaded.");
    }

    private static void RunLegacyReadCompatibility(string root)
    {
        var legacyPath = Path.Combine(root, "Legacy-Unprotected.avmatrix");
        var legacyData = new AppData
        {
            ProjectName = "Legacy Company",
            Clients = [new ClientRecord { Name = "Legacy Client" }]
        };
        var bytes = PortableDataService.ExportBytes(legacyData, null, out _);
        var json = Encoding.UTF8.GetString(bytes)
            .Replace("InNasc Company", "AV Matrix Studio Transfer", StringComparison.Ordinal);
        File.WriteAllText(legacyPath, json);
        Require(PortableDataService.Import(legacyPath).Data.Clients.Count == 1,
            "Legacy AV Matrix transfer data is no longer readable for Admin migration.");
        var summary = PortableDataService.ReadCompanySummary(legacyPath);
        Require(summary.CompanyName == "Legacy Company" && !summary.IsLicensed,
            "Legacy company identity was not preserved for the welcome page.");
    }

    private static void RunDeviceLimitEnforcement(string root)
    {
        var companyPath = Path.Combine(root, "Tiered.nasc");
        var access = new MasterAccessControl
        {
            LicenseId = Guid.NewGuid(),
            LicenseName = "Tiered Company",
            DeviceLimit = 1
        };
        _ = MasterAccessService.CreateInitialOwner(
            access, "tier-owner", "Tier Owner", OwnerPassword);
        var session = MasterAccessService.SignIn(access, "tier-owner", OwnerPassword);
        var room = new RoomRecord { Name = "Room", Equipment = [new EquipmentRecord { Description = "One" }] };
        var location = new LocationRecord { Name = "Location", Rooms = [room] };
        var data = new AppData
        {
            ProjectName = "Tiered Company",
            Clients = [new ClientRecord { Name = "Client", Locations = [location] }],
            MasterAccess = access
        };
        PortableDataService.ExportMaster(companyPath, data, session);
        var published = PortableDataService.ReadMasterAccess(companyPath);
        Require(published.DeviceLimit == 1 && published.LicenseId == access.LicenseId,
            "The .nasc license metadata did not round-trip.");
        var summary = PortableDataService.ReadCompanySummary(companyPath);
        Require(summary.CompanyName == "Tiered Company" &&
                summary.LicenseName == "Tiered Company" &&
                summary.DeviceLimit == 1 && summary.IsLicensed,
            "The company name and device tier were not exposed in the safe login metadata.");

        var welcomeData = new AppData();
        welcomeData.Settings.SharedMasterPath = companyPath;
        using var welcome = new MasterWelcomeControl(welcomeData);
        welcome.RefreshState();
        var title = Descendants(welcome).OfType<Label>()
            .Single(label => label.Name == "CompanyWelcomeTitle");
        var tier = Descendants(welcome).OfType<Label>()
            .Single(label => label.Name == "CompanyTierLabel");
        Require(title.Text == "Welcome to Tiered Company" && tier.Text == "1 DEVICE TIER",
            "The login welcome page did not display the embedded company name and tier.");
        RequireThrows<DeviceLimitExceededException>(() =>
            DeviceLimitPolicy.RequireCapacity(published, data, 1));
        room.Equipment.Add(new EquipmentRecord { Description = "Two" });
        RequireThrows<DeviceLimitExceededException>(() =>
            DeviceLimitPolicy.RequireNewClientAllowed(data.MasterAccess, data));
        // Existing data remains editable/saveable when a license is over its limit;
        // only creation of new client cards/devices is locked.
        PortableDataService.ExportMaster(companyPath, data, session);
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
