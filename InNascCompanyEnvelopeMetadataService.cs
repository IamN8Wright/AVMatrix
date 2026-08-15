using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InNasc;

internal static class InNascCompanyEnvelopeMetadataService
{
    private const string AccountEnvelopeFormat = "InNasc Account Envelope";
    private const string LegacyAccountEnvelopeFormat = "AV Matrix Studio Account Envelope";
    private const int MaxLogoBytes = 2 * 1024 * 1024;

    public static void Apply(
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
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(Encoding.UTF8.GetString(bytes)) as JsonObject
                ?? throw new InvalidDataException("The .nasc file envelope is unreadable.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The .nasc file envelope is unreadable.", exception);
        }

        var format = root["Format"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(format, AccountEnvelopeFormat, StringComparison.Ordinal) &&
            !string.Equals(format, LegacyAccountEnvelopeFormat, StringComparison.Ordinal))
            throw new InvalidDataException(
                "This .nasc file is not an account-protected InNasc company file.");

        root["CompanyName"] = JsonValue.Create(companyName.Trim());
        root["LicenseName"] = JsonValue.Create(string.IsNullOrWhiteSpace(licenseName)
            ? companyName.Trim()
            : licenseName.Trim());
        root["LicenseId"] = JsonValue.Create(licenseId);
        root["DeviceLimit"] = JsonValue.Create(deviceLimit);
        root["CompanyLogoBase64"] = JsonValue.Create(companyLogoBase64?.Trim() ?? string.Empty);
        if (!preserveExistingExpiration)
            root["LicenseExpiresUtc"] = licenseExpiresUtc is null
                ? null
                : JsonValue.Create(licenseExpiresUtc.Value.ToUniversalTime());

        var updated = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        WriteAtomically(fullPath, updated);
    }

    public static string ReadLogoFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The logo file could not be found.", fullPath);
        if (info.Length <= 0) throw new InvalidDataException("The selected logo file is empty.");
        if (info.Length > MaxLogoBytes)
            throw new InvalidDataException("Company logos must be 2 MB or smaller.");
        var bytes = File.ReadAllBytes(fullPath);
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (image.Width <= 0 || image.Height <= 0)
            throw new InvalidDataException("The selected file is not a usable image.");
        return Convert.ToBase64String(bytes);
    }

    public static Image? DecodeLogo(string? logoBase64)
    {
        if (string.IsNullOrWhiteSpace(logoBase64)) return null;
        try
        {
            var bytes = Convert.FromBase64String(logoBase64);
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return new Bitmap(image);
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    public static Control CreateCompanyBadge(string companyName, string? logoBase64, int size)
    {
        var image = DecodeLogo(logoBase64);
        if (image is not null)
        {
            var picture = new PictureBox
            {
                AutoSize = false,
                Size = new Size(size, size),
                BackColor = Color.White,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = image
            };
            picture.Disposed += (_, _) => picture.Image?.Dispose();
            return picture;
        }

        var words = companyName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        return new Label
        {
            Text = initials,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = UiTheme.LogoTile,
            ForeColor = UiTheme.Blue,
            Font = UiTheme.Font(Math.Max(12, size / 4f), FontStyle.Bold),
            Size = new Size(size, size)
        };
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
