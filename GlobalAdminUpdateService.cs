using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InNasc;

internal static class GlobalAdminUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/IamN8Wright/AVMatrix/releases/latest";
    private const string ExecutableAsset = "InNasc.GlobalAdmin.exe";
    private const string ChecksumAsset = "InNasc.GlobalAdmin.exe.sha256";

    public static async Task CheckAndPromptAsync(Form owner, bool silentWhenCurrent)
    {
        try
        {
            using var http = CreateClient();
            using var response = await http.GetAsync(LatestReleaseApi);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                throw new InvalidDataException("The GitHub release version is unreadable.");

            var current = Version.TryParse(AppInfo.Revision, out var parsed)
                ? parsed
                : new Version(0, 0, 0);
            if (latest <= current)
            {
                if (!silentWhenCurrent)
                    MessageBox.Show(owner,
                        $"InNasc Global Admin {AppInfo.Revision} is current.",
                        "Check for updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(owner,
                    $"InNasc Global Admin {latest} is available.\r\n\r\n" +
                    "Download, verify, and install the update now?",
                    "Global Admin update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            var assets = root.GetProperty("assets").EnumerateArray().ToList();
            string AssetUrl(string name)
            {
                foreach (var asset in assets)
                {
                    if (string.Equals(asset.GetProperty("name").GetString(), name,
                            StringComparison.OrdinalIgnoreCase))
                        return asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                }
                throw new InvalidDataException($"The GitHub release does not contain {name}.");
            }

            var staging = Path.Combine(
                Path.GetTempPath(), "InNasc", "GlobalAdminUpdates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var executablePath = Path.Combine(staging, ExecutableAsset);
            var checksumText = await http.GetStringAsync(AssetUrl(ChecksumAsset));
            var expected = checksumText.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            var executableBytes = await http.GetByteArrayAsync(AssetUrl(ExecutableAsset));
            await File.WriteAllBytesAsync(executablePath, executableBytes);

            await using (var stream = File.OpenRead(executablePath))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "The Global Admin update failed SHA-256 verification.");
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.Equals(versionInfo.ProductName, "InNasc Global Admin",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The downloaded executable is not InNasc Global Admin.");
            if (!Version.TryParse(versionInfo.ProductVersion?.Split('+')[0], out var executableVersion) ||
                executableVersion != latest)
                throw new InvalidDataException(
                    "The downloaded Global Admin version does not match the GitHub release.");

            InstallAndRestart(executablePath, staging);
        }
        catch (Exception exception)
        {
            if (!silentWhenCurrent)
                MessageBox.Show(owner,
                    $"The Global Admin update could not be completed.\r\n\r\n{exception.Message}",
                    "Global Admin update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"InNasc-GlobalAdmin/{AppInfo.Revision}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static void InstallAndRestart(string source, string staging)
    {
        var target = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(target) ||
            !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Automatic installation requires the packaged Global Admin executable.");

        var script =
            "$ErrorActionPreference='Stop'\r\n" +
            $"$pidToWait={Environment.ProcessId}\r\n" +
            $"$source='{PowerShellLiteral(source)}'\r\n" +
            $"$target='{PowerShellLiteral(target)}'\r\n" +
            $"$staging='{PowerShellLiteral(staging)}'\r\n" +
            "Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue\r\n" +
            "Start-Sleep -Milliseconds 400\r\n" +
            "Copy-Item -LiteralPath $source -Destination $target -Force\r\n" +
            "Start-Process -FilePath $target\r\n" +
            "Start-Sleep -Milliseconds 400\r\n" +
            "Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue\r\n";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Application.Exit();
    }

    private static string PowerShellLiteral(string value) => value.Replace("'", "''");
}
