using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AVMatrixStudio;

internal sealed record AppUpdateCandidate(
    string ExecutablePath,
    Version Version,
    string Sha256,
    long SizeBytes);

internal static class AppUpdateService
{
    private const string ReleaseFileId = "1cpoK8XXEzzfLtajq8pX9iRDVPINFiPUe";
    private const long MinimumExecutableBytes = 1_000_000;

    public static async Task<AppUpdateCandidate> DownloadAndVerifyAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Downloading the current release from Google Drive…");
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "AVMatrixStudio",
            "Updates",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var packagePath = Path.Combine(stagingDirectory, "release.download");

        try
        {
            await DownloadPublicDriveFileAsync(packagePath, cancellationToken);
            progress?.Report("Verifying the AV Matrix Studio release…");
            var executablePath = await ExtractExecutableAsync(
                packagePath,
                stagingDirectory,
                cancellationToken);
            var info = new FileInfo(executablePath);
            if (info.Length < MinimumExecutableBytes)
                throw new InvalidDataException(
                    "The downloaded executable is too small to be an AV Matrix Studio release.");
            if (!HasPortableExecutableHeader(executablePath))
                throw new InvalidDataException(
                    "The downloaded file is not a valid Windows executable.");

            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.Equals(
                    versionInfo.ProductName,
                    "AV Matrix Studio",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The downloaded executable is not identified as AV Matrix Studio.");
            if (!Version.TryParse(
                    versionInfo.ProductVersion?.Split('+')[0],
                    out var releaseVersion))
                throw new InvalidDataException(
                    "The downloaded release does not contain a readable product version.");

            await using var stream = File.OpenRead(executablePath);
            var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken));
            return new AppUpdateCandidate(
                executablePath,
                releaseVersion,
                hash,
                info.Length);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    public static bool IsNewer(AppUpdateCandidate candidate)
    {
        var current = Version.TryParse(AppInfo.Revision, out var parsed)
            ? parsed
            : new Version(0, 0, 0);
        return candidate.Version > current;
    }

    public static void InstallAndRestart(AppUpdateCandidate candidate)
    {
        var targetPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(targetPath) ||
            !targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !IsAvMatrixStudioExecutable(targetPath))
            throw new InvalidOperationException(
                "Automatic installation is available from a packaged AV Matrix Studio executable. " +
                "This copy appears to be running from source.");

        var backupPath = targetPath + ".previous";
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AVMatrixStudio",
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

    private static bool IsAvMatrixStudioExecutable(string path)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            return string.Equals(
                versionInfo.ProductName,
                "AV Matrix Studio",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task DownloadPublicDriveFileAsync(
        string destination,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"AV-Matrix-Studio/{AppInfo.Revision}");

        var url =
            $"https://drive.usercontent.google.com/download?id={ReleaseFileId}&export=download&confirm=t";
        using var response = await http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Google Drive returned a sharing page instead of the release. " +
                "Set the update file to “Anyone with the link — Viewer” and try again.");

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

    private static async Task<string> ExtractExecutableAsync(
        string packagePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await using (var stream = File.OpenRead(packagePath))
            _ = await stream.ReadAsync(header, cancellationToken);

        var executablePath = Path.Combine(stagingDirectory, "AVMatrixStudio.exe");
        if (header[0] == (byte)'M' && header[1] == (byte)'Z')
        {
            File.Move(packagePath, executablePath, true);
            return executablePath;
        }
        if (header[0] != (byte)'P' || header[1] != (byte)'K')
            throw new InvalidDataException(
                "The update file must be AVMatrixStudio.exe or a ZIP containing that executable.");

        using var archive = ZipFile.OpenRead(packagePath);
        var candidates = archive.Entries
            .Where(entry => string.Equals(
                Path.GetFileName(entry.FullName),
                "AVMatrixStudio.exe",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count != 1)
            throw new InvalidDataException(
                "The update ZIP must contain exactly one file named AVMatrixStudio.exe.");
        candidates[0].ExtractToFile(executablePath, true);
        return executablePath;
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
        Text = "Update AV Matrix Studio";
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
