using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InNasc;

internal sealed record AppReleaseInfo(
    Version Version,
    string Tag,
    string ExecutableUrl,
    string ChecksumUrl,
    string ReleasePageUrl);

internal sealed record AppUpdateCandidate(
    string ExecutablePath,
    Version Version,
    string Sha256,
    long SizeBytes);

internal static class AppUpdateService
{
    private const string GitHubOwner = "IamN8Wright";
    private const string GitHubRepository = "AVMatrix";
    private const string ExecutableAssetName = "InNasc.exe";
    private const string ChecksumAssetName = "InNasc.exe.sha256";
    private const long MinimumExecutableBytes = 1_000_000;

    private static string LatestReleaseApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepository}/releases/latest";

    public static async Task<AppReleaseInfo> CheckLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        using var http = CreateHttpClient();
        return await ReadLatestReleaseAsync(http, cancellationToken);
    }

    public static async Task<AppUpdateCandidate> DownloadAndVerifyAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking GitHub for the latest InNasc release…");
        using var http = CreateHttpClient();
        var release = await ReadLatestReleaseAsync(http, cancellationToken);

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "InNasc",
            "Updates",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var executablePath = Path.Combine(stagingDirectory, ExecutableAssetName);

        try
        {
            progress?.Report($"Downloading InNasc {release.Version} from GitHub…");
            var expectedHash = await DownloadExpectedChecksumAsync(
                http,
                release.ChecksumUrl,
                cancellationToken);
            await DownloadFileAsync(
                http,
                release.ExecutableUrl,
                executablePath,
                cancellationToken);

            progress?.Report("Verifying the GitHub release…");
            var info = new FileInfo(executablePath);
            if (info.Length < MinimumExecutableBytes)
                throw new InvalidDataException(
                    "The downloaded executable is too small to be an InNasc release.");
            if (!HasPortableExecutableHeader(executablePath))
                throw new InvalidDataException(
                    "The downloaded file is not a valid Windows executable.");

            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.Equals(
                    versionInfo.ProductName,
                    "InNasc",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The downloaded executable is not identified as InNasc.");
            if (!Version.TryParse(
                    versionInfo.ProductVersion?.Split('+')[0],
                    out var executableVersion))
                throw new InvalidDataException(
                    "The downloaded release does not contain a readable product version.");
            if (executableVersion != release.Version)
                throw new InvalidDataException(
                    $"The GitHub release tag is {release.Version}, but the downloaded executable " +
                    $"identifies itself as {executableVersion}. The update was not installed.");

            await using var stream = File.OpenRead(executablePath);
            var computedHash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The downloaded executable did not match the SHA-256 checksum published " +
                    "with the GitHub Release. The update was rejected.");

            return new AppUpdateCandidate(
                executablePath,
                executableVersion,
                computedHash,
                info.Length);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    public static bool IsNewer(Version releaseVersion)
    {
        var current = Version.TryParse(AppInfo.Revision, out var parsed)
            ? parsed
            : new Version(0, 0, 0);
        return releaseVersion > current;
    }

    public static bool IsNewer(AppUpdateCandidate candidate) => IsNewer(candidate.Version);

    public static void InstallAndRestart(AppUpdateCandidate candidate)
    {
        var targetPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(targetPath) ||
            !targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !IsInNascExecutable(targetPath))
            throw new InvalidOperationException(
                "Automatic installation is available from a packaged InNasc executable. " +
                "This copy appears to be running from source.");

        var backupPath = targetPath + ".previous";
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InNasc",
            "update-error.log");
        var sourceDirectory = Path.GetDirectoryName(candidate.ExecutablePath) ?? Path.GetTempPath();
        var script =
            "$ErrorActionPreference='Stop'\r\n" +
            $"$processId={Environment.ProcessId}\r\n" +
            $"$source='{PowerShellLiteral(candidate.ExecutablePath)}'\r\n" +
            $"$target='{PowerShellLiteral(targetPath)}'\r\n" +
            $"$backup='{PowerShellLiteral(backupPath)}'\r\n" +
            $"$log='{PowerShellLiteral(logPath)}'\r\n" +
            $"$staging='{PowerShellLiteral(sourceDirectory)}'\r\n" +
            "try {\r\n" +
            "  Wait-Process -Id $processId -ErrorAction SilentlyContinue\r\n" +
            "  Start-Sleep -Milliseconds 400\r\n" +
            "  if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination $backup -Force }\r\n" +
            "  Copy-Item -LiteralPath $source -Destination $target -Force\r\n" +
            "  Start-Process -FilePath $target\r\n" +
            "  Start-Sleep -Milliseconds 500\r\n" +
            "  Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
            "} catch {\r\n" +
            "  New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null\r\n" +
            "  $_ | Out-String | Set-Content -LiteralPath $log\r\n" +
            "  if (Test-Path -LiteralPath $backup) { Copy-Item -LiteralPath $backup -Destination $target -Force }\r\n" +
            "  if (Test-Path -LiteralPath $target) { Start-Process -FilePath $target }\r\n" +
            "}\r\n";
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

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"InNasc/{AppInfo.Revision}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private static async Task<AppReleaseInfo> ReadLatestReleaseAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(LatestReleaseApiUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "No published InNasc GitHub Release is available yet.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase} while checking for updates.");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = RequiredString(root, "tag_name", "GitHub release tag");
        var releasePageUrl = OptionalString(root, "html_url");
        var versionText = tag.StartsWith('v') || tag.StartsWith('V')
            ? tag[1..]
            : tag;
        if (!Version.TryParse(versionText, out var version))
            throw new InvalidDataException(
                $"The latest GitHub Release tag '{tag}' is not a valid InNasc version.");

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The latest GitHub Release does not contain release assets.");

        string? executableUrl = null;
        string? checksumUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = OptionalString(asset, "name");
            var downloadUrl = OptionalString(asset, "browser_download_url");
            if (string.Equals(name, ExecutableAssetName, StringComparison.OrdinalIgnoreCase))
                executableUrl = downloadUrl;
            else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                checksumUrl = downloadUrl;
        }

        if (string.IsNullOrWhiteSpace(executableUrl))
            throw new InvalidDataException(
                $"The latest GitHub Release does not contain '{ExecutableAssetName}'.");
        if (string.IsNullOrWhiteSpace(checksumUrl))
            throw new InvalidDataException(
                $"The latest GitHub Release does not contain '{ChecksumAssetName}'.");

        return new AppReleaseInfo(
            version,
            tag,
            executableUrl,
            checksumUrl,
            releasePageUrl);
    }

    private static async Task<string> DownloadExpectedChecksumAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokens = text.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        var checksum = tokens.FirstOrDefault()?.Trim() ?? string.Empty;
        if (checksum.Length != 64 || checksum.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException(
                "The GitHub Release checksum file does not contain a valid SHA-256 value.");
        return checksum;
    }

    private static async Task DownloadFileAsync(
        HttpClient http,
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);
    }

    private static string RequiredString(JsonElement element, string property, string label)
    {
        var value = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"The {label} is missing.");
        return value;
    }

    private static string OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsInNascExecutable(string path)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            return string.Equals(
                versionInfo.ProductName,
                "InNasc",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPortableExecutableHeader(string path)
    {
        using var stream = File.OpenRead(path);
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }

    private static string PowerShellLiteral(string value) => value.Replace("'", "''");

    public static void Discard(AppUpdateCandidate candidate)
    {
        var directory = Path.GetDirectoryName(candidate.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(directory)) TryDeleteDirectory(directory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // Temporary update files can be cleared by Windows later.
        }
    }
}

internal sealed class AppUpdateProgressForm : Form
{
    private readonly Label _status = new();

    public AppUpdateProgressForm()
    {
        Text = "Update InNasc";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 132);
        BackColor = UiTheme.Surface;
        Font = UiTheme.Font();
        ControlBox = false;

        _status.Text = "Preparing update…";
        _status.AutoSize = false;
        _status.Location = new Point(24, 18);
        _status.Size = new Size(422, 42);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UiTheme.Text;
        Controls.Add(_status);

        Controls.Add(new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28,
            Location = new Point(24, 76),
            Size = new Size(422, 22)
        });
        UiTheme.ApplyTheme(this);
    }

    public void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(message));
            return;
        }
        _status.Text = message;
    }
}
