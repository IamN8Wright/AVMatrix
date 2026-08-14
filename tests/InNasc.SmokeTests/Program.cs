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
