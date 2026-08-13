using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace InNasc;

internal sealed record GitHubMasterStorageOptions(
    string Owner,
    string Repository,
    string Branch,
    string CompanyId)
{
    public static GitHubMasterStorageOptions ForCompany(string companyId) => new(
        "IamN8Wright",
        "avmatrix_MasterMatrixStorage",
        "main",
        companyId);
}

internal sealed record GitHubRepositoryHead(string CommitSha, string TreeSha);

internal sealed record GitHubStorageFile(
    string Path,
    byte[] Contents,
    string BlobSha,
    string CommitSha);

internal sealed record GitHubStorageCommitResult(
    string PreviousCommitSha,
    string CommitSha,
    string TreeSha);

internal sealed class GitHubStorageConflictException(string message) : InvalidOperationException(message);

internal static class GitHubMasterStorageService
{
    // GitHub's Git blob API supports blobs up to 100 MB. Stay below the hard edge so
    // JSON/base64 overhead and future API behavior do not turn a large check-in into
    // an ambiguous failure.
    public const long MaximumStoredFileBytes = 95L * 1024 * 1024;

    private const string ApiVersion = "2026-03-10";

    public static string CompanyRoot(GitHubMasterStorageOptions options) =>
        $"companies/{ValidateCompanyId(options.CompanyId)}";

    public static string CompanyMetadataPath(GitHubMasterStorageOptions options) =>
        $"{CompanyRoot(options)}/company.json";

    public static string MasterPath(GitHubMasterStorageOptions options) =>
        $"{CompanyRoot(options)}/master.nasc";

    public static string LegacyMasterPath(GitHubMasterStorageOptions options) =>
        $"{CompanyRoot(options)}/master.avmatrix";

    public static string ClientPath(GitHubMasterStorageOptions options, Guid clientId) =>
        $"{CompanyRoot(options)}/clients/{clientId:N}.nascclient";

    public static string LegacyClientPath(GitHubMasterStorageOptions options, Guid clientId) =>
        $"{CompanyRoot(options)}/clients/{clientId:N}.avclient";

    public static void SaveAccessToken(GitHubMasterStorageOptions options, string token)
    {
        var normalized = token.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("A GitHub access token is required.", nameof(token));
        WindowsCredentialStore.Write(CredentialTarget(options), normalized);
    }

    public static bool HasAccessToken(GitHubMasterStorageOptions options) =>
        !string.IsNullOrWhiteSpace(ReadAccessToken(options));

    public static void ClearAccessToken(GitHubMasterStorageOptions options)
    {
        WindowsCredentialStore.Delete(CredentialTarget(options));
        WindowsCredentialStore.Delete(LegacyCredentialTarget(options));
    }

    public static async Task TestConnectionAsync(
        GitHubMasterStorageOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        using var http = CreateHttpClient(options);
        using var response = await http.GetAsync(
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}",
            cancellationToken);
        var json = await EnsureJsonSuccessAsync(response, "checking the GitHub storage repository", cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("private", out var isPrivate) || isPrivate.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException(
                "InNasc company storage must use a private GitHub repository.");

        _ = await GetHeadAsync(http, options, cancellationToken);
    }

    public static async Task<GitHubRepositoryHead> GetHeadAsync(
        GitHubMasterStorageOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        using var http = CreateHttpClient(options);
        return await GetHeadAsync(http, options, cancellationToken);
    }

    public static async Task<GitHubStorageFile> ReadMasterAsync(
        GitHubMasterStorageOptions options,
        CancellationToken cancellationToken = default) =>
        await ReadFileWithLegacyFallbackAsync(
            options, MasterPath(options), LegacyMasterPath(options), cancellationToken);

    public static async Task<GitHubStorageFile> ReadClientAsync(
        GitHubMasterStorageOptions options,
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        await ReadFileWithLegacyFallbackAsync(
            options, ClientPath(options, clientId), LegacyClientPath(options, clientId), cancellationToken);

    private static async Task<GitHubStorageFile> ReadFileWithLegacyFallbackAsync(
        GitHubMasterStorageOptions options,
        string currentPath,
        string legacyPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadFileAsync(options, currentPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return await ReadFileAsync(options, legacyPath, cancellationToken);
        }
    }

    public static async Task<GitHubStorageFile> ReadFileAsync(
        GitHubMasterStorageOptions options,
        string path,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var normalizedPath = NormalizeRepositoryPath(path);
        using var http = CreateHttpClient(options);
        var head = await GetHeadAsync(http, options, cancellationToken);
        var blobSha = await FindBlobShaAsync(
            http,
            options,
            head.TreeSha,
            normalizedPath,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(blobSha))
            throw new FileNotFoundException(
                $"'{normalizedPath}' does not exist in the GitHub company storage repository.");
        var bytes = await ReadBlobAsync(http, options, blobSha, cancellationToken);
        return new GitHubStorageFile(normalizedPath, bytes, blobSha, head.CommitSha);
    }

    public static async Task<IReadOnlyList<string>> ListCompanyIdsAsync(
        GitHubMasterStorageOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options, requireCompanyId: false);
        using var http = CreateHttpClient(options);
        var head = await GetHeadAsync(http, options, cancellationToken);
        var tree = await ReadRecursiveTreeAsync(http, options, head.TreeSha, cancellationToken);
        var companyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in tree)
        {
            if (!entry.Path.StartsWith("companies/", StringComparison.OrdinalIgnoreCase) ||
                !entry.Path.EndsWith("/company.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && !string.Equals(parts[1], "_TEMPLATE", StringComparison.OrdinalIgnoreCase))
                companyIds.Add(parts[1]);
        }
        return companyIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<GitHubStorageCommitResult> CreateCompanyAsync(
        GitHubMasterStorageOptions options,
        string displayName,
        byte[] masterContents,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        ValidateFileSize(MasterPath(options), masterContents);
        using var http = CreateHttpClient(options);
        var head = await GetHeadAsync(http, options, cancellationToken);
        var existing = await FindBlobShaAsync(
            http,
            options,
            head.TreeSha,
            MasterPath(options),
            cancellationToken);
        existing ??= await FindBlobShaAsync(
            http,
            options,
            head.TreeSha,
            LegacyMasterPath(options),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing))
            throw new InvalidOperationException(
                $"The GitHub company folder '{options.CompanyId}' already contains a company workspace.");

        var metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            companyId = ValidateCompanyId(options.CompanyId),
            displayName = displayName.Trim(),
            masterFile = "master.nasc",
            clientsFolder = "clients"
        }, new JsonSerializerOptions { WriteIndented = true });

        return await CommitFilesAsync(
            http,
            options,
            new Dictionary<string, byte[]>
            {
                [CompanyMetadataPath(options)] = metadata,
                [MasterPath(options)] = masterContents
            },
            head.CommitSha,
            $"Create InNasc Master for {displayName.Trim()}",
            cancellationToken);
    }

    public static async Task<GitHubStorageCommitResult> CommitFilesAsync(
        GitHubMasterStorageOptions options,
        IReadOnlyDictionary<string, byte[]> files,
        string expectedCommitSha,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        using var http = CreateHttpClient(options);
        return await CommitFilesAsync(
            http,
            options,
            files,
            expectedCommitSha,
            commitMessage,
            cancellationToken);
    }

    public static async Task<GitHubStorageCommitResult> CommitMasterAndClientAsync(
        GitHubMasterStorageOptions options,
        byte[] masterContents,
        Guid clientId,
        byte[] clientContents,
        string expectedCommitSha,
        string commitMessage,
        CancellationToken cancellationToken = default) =>
        await CommitFilesAsync(
            options,
            new Dictionary<string, byte[]>
            {
                [MasterPath(options)] = masterContents,
                [ClientPath(options, clientId)] = clientContents
            },
            expectedCommitSha,
            commitMessage,
            cancellationToken);

    private static async Task<GitHubStorageCommitResult> CommitFilesAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        IReadOnlyDictionary<string, byte[]> files,
        string expectedCommitSha,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
            throw new ArgumentException("At least one GitHub storage file is required.", nameof(files));
        if (string.IsNullOrWhiteSpace(commitMessage))
            throw new ArgumentException("A GitHub commit message is required.", nameof(commitMessage));

        foreach (var file in files)
            ValidateFileSize(NormalizeRepositoryPath(file.Key), file.Value);

        var head = await GetHeadAsync(http, options, cancellationToken);
        if (!string.IsNullOrWhiteSpace(expectedCommitSha) &&
            !string.Equals(head.CommitSha, expectedCommitSha.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new GitHubStorageConflictException(
                "The GitHub company workspace changed after this PC downloaded it. " +
                "Refresh and run the normal InNasc merge before trying to push again.");

        var entries = new List<object>();
        foreach (var file in files)
        {
            var path = NormalizeRepositoryPath(file.Key);
            var blobSha = await CreateBlobAsync(http, options, file.Value, cancellationToken);
            entries.Add(new
            {
                path,
                mode = "100644",
                type = "blob",
                sha = blobSha
            });
        }

        var treeSha = await CreateTreeAsync(
            http,
            options,
            head.TreeSha,
            entries,
            cancellationToken);
        var commitSha = await CreateCommitAsync(
            http,
            options,
            treeSha,
            head.CommitSha,
            commitMessage.Trim(),
            cancellationToken);

        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/refs/heads/{EscapeBranch(options.Branch)}")
        {
            Content = JsonBody(new { sha = commitSha, force = false })
        };
        using var updateResponse = await http.SendAsync(updateRequest, cancellationToken);
        if (updateResponse.StatusCode == HttpStatusCode.UnprocessableEntity ||
            updateResponse.StatusCode == HttpStatusCode.Conflict)
            throw new GitHubStorageConflictException(
                "Another InNasc user updated the GitHub company workspace at the same time. " +
                "No overwrite was forced. Refresh, merge, and try again.");
        _ = await EnsureJsonSuccessAsync(updateResponse, "publishing the GitHub company workspace commit", cancellationToken);

        return new GitHubStorageCommitResult(head.CommitSha, commitSha, treeSha);
    }

    private static async Task<GitHubRepositoryHead> GetHeadAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        CancellationToken cancellationToken)
    {
        var referenceUrl =
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/ref/heads/{EscapeBranch(options.Branch)}";
        using var referenceResponse = await http.GetAsync(referenceUrl, cancellationToken);
        var referenceJson = await EnsureJsonSuccessAsync(
            referenceResponse,
            "reading the GitHub storage branch",
            cancellationToken);
        using var referenceDocument = JsonDocument.Parse(referenceJson);
        var commitSha = referenceDocument.RootElement
            .GetProperty("object")
            .GetProperty("sha")
            .GetString() ?? string.Empty;
        if (commitSha.Length == 0)
            throw new InvalidDataException("GitHub did not return a branch commit SHA.");

        var commitUrl =
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/commits/{Escape(commitSha)}";
        using var commitResponse = await http.GetAsync(commitUrl, cancellationToken);
        var commitJson = await EnsureJsonSuccessAsync(
            commitResponse,
            "reading the GitHub storage commit",
            cancellationToken);
        using var commitDocument = JsonDocument.Parse(commitJson);
        var treeSha = commitDocument.RootElement
            .GetProperty("tree")
            .GetProperty("sha")
            .GetString() ?? string.Empty;
        if (treeSha.Length == 0)
            throw new InvalidDataException("GitHub did not return a storage tree SHA.");
        return new GitHubRepositoryHead(commitSha, treeSha);
    }

    private static async Task<string?> FindBlobShaAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        string treeSha,
        string path,
        CancellationToken cancellationToken)
    {
        var entries = await ReadRecursiveTreeAsync(http, options, treeSha, cancellationToken);
        return entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Type, "blob", StringComparison.OrdinalIgnoreCase))?.Sha;
    }

    private static async Task<List<GitHubTreeEntry>> ReadRecursiveTreeAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        string treeSha,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/trees/{Escape(treeSha)}?recursive=1";
        using var response = await http.GetAsync(url, cancellationToken);
        var json = await EnsureJsonSuccessAsync(response, "reading the GitHub storage tree", cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException(
                "The GitHub company storage tree is too large to enumerate safely.");
        if (!document.RootElement.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub did not return a storage tree.");

        var entries = new List<GitHubTreeEntry>();
        foreach (var item in tree.EnumerateArray())
        {
            var path = item.TryGetProperty("path", out var pathValue) ? pathValue.GetString() : null;
            var sha = item.TryGetProperty("sha", out var shaValue) ? shaValue.GetString() : null;
            var type = item.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(sha))
                entries.Add(new GitHubTreeEntry(path!, sha!, type ?? string.Empty));
        }
        return entries;
    }

    private static async Task<byte[]> ReadBlobAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        string blobSha,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/blobs/{Escape(blobSha)}";
        using var response = await http.GetAsync(url, cancellationToken);
        var json = await EnsureJsonSuccessAsync(response, "downloading a GitHub company workspace blob", cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var encoding = root.TryGetProperty("encoding", out var encodingValue)
            ? encodingValue.GetString()
            : null;
        if (!string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned a storage blob using an unsupported encoding.");
        var content = root.TryGetProperty("content", out var contentValue)
            ? contentValue.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(content)) return [];
        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("GitHub returned a damaged Base64 storage blob.", exception);
        }
    }

    private static async Task<string> CreateBlobAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        byte[] contents,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/blobs",
            JsonBody(new
            {
                content = Convert.ToBase64String(contents),
                encoding = "base64"
            }),
            cancellationToken);
        var json = await EnsureJsonSuccessAsync(response, "uploading a GitHub storage blob", cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("sha").GetString()
               ?? throw new InvalidDataException("GitHub did not return an uploaded blob SHA.");
    }

    private static async Task<string> CreateTreeAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        string baseTreeSha,
        IReadOnlyList<object> entries,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/trees",
            JsonBody(new
            {
                base_tree = baseTreeSha,
                tree = entries
            }),
            cancellationToken);
        var json = await EnsureJsonSuccessAsync(response, "creating the GitHub storage tree", cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("sha").GetString()
               ?? throw new InvalidDataException("GitHub did not return a storage tree SHA.");
    }

    private static async Task<string> CreateCommitAsync(
        HttpClient http,
        GitHubMasterStorageOptions options,
        string treeSha,
        string parentCommitSha,
        string message,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(
            $"https://api.github.com/repos/{Escape(options.Owner)}/{Escape(options.Repository)}/git/commits",
            JsonBody(new
            {
                message,
                tree = treeSha,
                parents = new[] { parentCommitSha }
            }),
            cancellationToken);
        var json = await EnsureJsonSuccessAsync(response, "creating the GitHub storage commit", cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("sha").GetString()
               ?? throw new InvalidDataException("GitHub did not return a storage commit SHA.");
    }

    private static HttpClient CreateHttpClient(GitHubMasterStorageOptions options)
    {
        var token = ReadAccessToken(options);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "GitHub company storage is not signed in on this PC. " +
                "Add a fine-grained GitHub access token for the private storage repository.");

        var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"InNasc/{AppInfo.Revision}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return http;
    }

    private static async Task<string> EnsureJsonSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return body;

        string detail = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message))
                detail = message.GetString() ?? string.Empty;
        }
        catch
        {
            // Fall through to the HTTP status when GitHub did not return JSON.
        }

        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"\r\n\r\nGitHub: {detail}";
        throw new HttpRequestException(
            $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase} while {operation}.{suffix}");
    }

    private static StringContent JsonBody(object value) => new(
        JsonSerializer.Serialize(value),
        Encoding.UTF8,
        "application/json");

    private static string CredentialTarget(GitHubMasterStorageOptions options) =>
        $"InNasc/GitHubStorage/{options.Owner.Trim()}/{options.Repository.Trim()}";

    private static string LegacyCredentialTarget(GitHubMasterStorageOptions options) =>
        $"AVMatrixStudio/GitHubMasterStorage/{options.Owner.Trim()}/{options.Repository.Trim()}";

    private static string? ReadAccessToken(GitHubMasterStorageOptions options)
    {
        var token = WindowsCredentialStore.Read(CredentialTarget(options));
        if (!string.IsNullOrWhiteSpace(token)) return token;
        token = WindowsCredentialStore.Read(LegacyCredentialTarget(options));
        if (!string.IsNullOrWhiteSpace(token))
            WindowsCredentialStore.Write(CredentialTarget(options), token);
        return token;
    }

    private static void ValidateOptions(
        GitHubMasterStorageOptions options,
        bool requireCompanyId = true)
    {
        if (string.IsNullOrWhiteSpace(options.Owner))
            throw new ArgumentException("A GitHub repository owner is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Repository))
            throw new ArgumentException("A GitHub repository name is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Branch))
            throw new ArgumentException("A GitHub branch is required.", nameof(options));
        if (requireCompanyId) _ = ValidateCompanyId(options.CompanyId);
    }

    private static string ValidateCompanyId(string companyId)
    {
        var value = companyId.Trim();
        if (value.Length == 0)
            throw new ArgumentException("A GitHub company folder ID is required.", nameof(companyId));
        if (value is "." or ".." || value.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException(
                "The GitHub company folder ID may contain only letters, numbers, periods, hyphens, and underscores.",
                nameof(companyId));
        return value;
    }

    private static string NormalizeRepositoryPath(string path)
    {
        var value = path.Replace('\\', '/').Trim('/');
        if (value.Length == 0 || value.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new ArgumentException("The GitHub repository path is invalid.", nameof(path));
        return value;
    }

    private static void ValidateFileSize(string path, byte[] contents)
    {
        if (contents.LongLength <= MaximumStoredFileBytes) return;
        throw new InvalidOperationException(
            $"'{path}' is {contents.LongLength / (1024d * 1024d):N1} MB. " +
            $"GitHub company storage is limited to {MaximumStoredFileBytes / (1024 * 1024)} MB per file. " +
            "Use Google Drive or Local / Network Share for a client payload this large.");
    }

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());

    private static string EscapeBranch(string value) => Uri.EscapeDataString(value.Trim());

    private sealed record GitHubTreeEntry(string Path, string Sha, string Type);
}
