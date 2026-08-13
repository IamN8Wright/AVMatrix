using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AVMatrixStudio;

internal static class GoogleDriveService
{
    private const string CredentialTarget = "AVMatrixStudio/GoogleDrive";
    private const string DriveScope = "https://www.googleapis.com/auth/drive";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static GoogleOAuthClientConfiguration ReadOAuthClientFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.TryGetProperty("installed", out var installed)) root = installed;
        else if (root.TryGetProperty("web", out var web)) root = web;
        var clientId = root.TryGetProperty("client_id", out var id) ? id.GetString() : null;
        var clientSecret = root.TryGetProperty("client_secret", out var secret) ? secret.GetString() : null;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidDataException(
                "Choose an OAuth client JSON file downloaded for a Google Cloud Desktop app.");
        return new GoogleOAuthClientConfiguration(clientId, clientSecret);
    }

    public static void ConfigureClient(GoogleOAuthClientConfiguration configuration)
    {
        var existing = ReadCredential() ?? new GoogleCredentialPayload();
        existing.ClientSecret = configuration.ClientSecret;
        WriteCredential(existing);
    }

    public static bool HasConfiguredClient(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.GoogleDriveOAuthClientId) &&
        !string.IsNullOrWhiteSpace(ReadCredential()?.ClientSecret);

    public static bool HasGoogleSignIn => !string.IsNullOrWhiteSpace(ReadCredential()?.RefreshToken);

    public static async Task ConnectAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var credential = ReadCredential()
            ?? throw new InvalidOperationException("Import a Google OAuth Desktop client JSON file first.");
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveOAuthClientId) ||
            string.IsNullOrWhiteSpace(credential.ClientSecret))
            throw new InvalidOperationException("Import a Google OAuth Desktop client JSON file first.");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/";
            var state = Base64Url(RandomNumberGenerator.GetBytes(24));
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var authorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
                $"?client_id={Escape(settings.GoogleDriveOAuthClientId)}" +
                $"&redirect_uri={Escape(redirectUri)}" +
                "&response_type=code" +
                $"&scope={Escape(DriveScope)}" +
                "&access_type=offline&prompt=consent" +
                $"&state={Escape(state)}" +
                "&code_challenge_method=S256" +
                $"&code_challenge={Escape(challenge)}";
            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

            using var client = await listener.AcceptTcpClientAsync(cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken)
                ?? throw new InvalidOperationException("Google did not return an authorization response.");
            var requestParts = requestLine.Split(' ');
            if (requestParts.Length < 2)
                throw new InvalidOperationException("Google returned an invalid authorization response.");
            var responseUri = new Uri("http://127.0.0.1" + requestParts[1]);
            var query = ParseQuery(responseUri.Query);
            var code = query.TryGetValue("code", out var returnedCode) ? returnedCode : string.Empty;
            var success = query.TryGetValue("state", out var returnedState) && returnedState == state &&
                          !string.IsNullOrWhiteSpace(code);
            var responseHtml = success
                ? "<h2>AV Matrix Studio is connected.</h2><p>You may close this browser window.</p>"
                : "<h2>Google Drive connection was not completed.</h2><p>Return to AV Matrix Studio for details.</p>";
            var responseBytes = Encoding.UTF8.GetBytes(responseHtml);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(success ? "200 OK" : "400 Bad Request")}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {responseBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(responseBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            if (!success)
            {
                var error = query.TryGetValue("error", out var errorValue)
                    ? errorValue
                    : "authorization was cancelled";
                throw new InvalidOperationException($"Google Drive sign-in failed: {error}.");
            }

            using var tokenResponse = await Http.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = settings.GoogleDriveOAuthClientId,
                    ["client_secret"] = credential.ClientSecret,
                    ["code"] = code,
                    ["code_verifier"] = verifier,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = redirectUri
                }), cancellationToken);
            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(tokenResponse, tokenJson, "Google sign-in");
            ApplyTokenResponse(credential, tokenJson, preserveRefreshToken: true);
            WriteCredential(credential);
        }
        finally
        {
            listener.Stop();
        }
    }

    public static void Disconnect() => WindowsCredentialStore.Delete(CredentialTarget);

    public static string ParseFileId(string shareLink)
    {
        var value = shareLink.Trim();
        if (Regex.IsMatch(value, "^[A-Za-z0-9_-]{20,}$")) return value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Paste a valid Google Drive file share link.");
        var pathMatch = Regex.Match(uri.AbsolutePath, @"/(?:d|folders)/([A-Za-z0-9_-]+)",
            RegexOptions.IgnoreCase);
        if (pathMatch.Success) return pathMatch.Groups[1].Value;
        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id)) return id;
        throw new InvalidDataException("The Google Drive file ID could not be found in this share link.");
    }

    public static async Task<GoogleDriveFile> DownloadAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var fileId = RequiredFileId(settings);
        return await DownloadByIdAsync(settings, fileId, cancellationToken);
    }

    public static async Task<GoogleDriveFile> DownloadByIdAsync(
        AppSettings settings,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(settings, cancellationToken);
        var metadata = await GetMetadataAsync(fileId, accessToken, cancellationToken);
        using var request = AuthorizedRequest(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{EscapePath(fileId)}?alt=media&supportsAllDrives=true",
            accessToken);
        using var response = await Http.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        EnsureSuccess(response, Encoding.UTF8.GetString(bytes), "Google Drive download");
        return new GoogleDriveFile(metadata, bytes);
    }

    public static async Task<GoogleDriveFileMetadata> UploadAsync(
        AppSettings settings,
        byte[] contents,
        CancellationToken cancellationToken = default)
    {
        var fileId = RequiredFileId(settings);
        return await UploadByIdAsync(settings, fileId, contents, cancellationToken);
    }

    public static async Task<GoogleDriveFileMetadata> UploadByIdAsync(
        AppSettings settings,
        string fileId,
        byte[] contents,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(settings, cancellationToken);
        using var request = AuthorizedRequest(new HttpMethod("PATCH"),
            $"https://www.googleapis.com/upload/drive/v3/files/{EscapePath(fileId)}" +
            "?uploadType=media&supportsAllDrives=true&fields=id,name,mimeType,modifiedTime,version,size,parents,capabilities(canEdit)",
            accessToken);
        request.Content = new ByteArrayContent(contents);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await Http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json, "Google Drive upload");
        return ParseMetadata(json);
    }

    public static async Task<GoogleDriveFileMetadata> GetMetadataAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(settings, cancellationToken);
        return await GetMetadataAsync(RequiredFileId(settings), accessToken, cancellationToken);
    }

    public static async Task<GoogleDriveFileMetadata?> FindSiblingAsync(
        AppSettings settings,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(settings, cancellationToken);
        var master = await GetMetadataAsync(RequiredFileId(settings), accessToken, cancellationToken);
        var parent = master.ParentIds.FirstOrDefault() ?? "root";
        var escapedName = fileName.Replace("\\", "\\\\").Replace("'", "\\'");
        var query = $"name = '{escapedName}' and '{parent}' in parents and trashed = false";
        using var request = AuthorizedRequest(HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/files" +
            $"?supportsAllDrives=true&includeItemsFromAllDrives=true&pageSize=10&q={Escape(query)}" +
            "&fields=files(id,name,mimeType,modifiedTime,version,size,parents,capabilities(canEdit))",
            accessToken);
        using var response = await Http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json, "Google Drive client sub-matrix lookup");
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
            return null;
        return ParseMetadata(files[0].GetRawText());
    }

    public static async Task<GoogleDriveFileMetadata> CreateSiblingAsync(
        AppSettings settings,
        string fileName,
        byte[] contents,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(settings, cancellationToken);
        var master = await GetMetadataAsync(RequiredFileId(settings), accessToken, cancellationToken);
        var parent = master.ParentIds.FirstOrDefault() ?? "root";
        var metadataJson = JsonSerializer.Serialize(new
        {
            name = fileName,
            parents = new[] { parent },
            mimeType = "application/octet-stream"
        });
        using var multipart = new MultipartContent("related", "avmatrix_" + Guid.NewGuid().ToString("N"));
        var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
        var fileContent = new ByteArrayContent(contents);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(metadataContent);
        multipart.Add(fileContent);
        using var request = AuthorizedRequest(HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files" +
            "?uploadType=multipart&supportsAllDrives=true&fields=id,name,mimeType,modifiedTime,version,size,parents,capabilities(canEdit)",
            accessToken);
        request.Content = multipart;
        using var response = await Http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json, "Google Drive client sub-matrix creation");
        var created = ParseMetadata(json);
        await CopySharePermissionsAsync(
            accessToken,
            RequiredFileId(settings),
            created.Id,
            cancellationToken);
        return created;
    }

    private static async Task<GoogleDriveFileMetadata> GetMetadataAsync(
        string fileId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{EscapePath(fileId)}" +
            "?supportsAllDrives=true&fields=id,name,mimeType,modifiedTime,version,size,parents,capabilities(canEdit)",
            accessToken);
        using var response = await Http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json, "Google Drive file lookup");
        return ParseMetadata(json);
    }

    private static GoogleDriveFileMetadata ParseMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var canEdit = root.TryGetProperty("capabilities", out var capabilities) &&
                      capabilities.TryGetProperty("canEdit", out var canEditProperty) &&
                      canEditProperty.GetBoolean();
        return new GoogleDriveFileMetadata(
            root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("mimeType", out var mime) ? mime.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("modifiedTime", out var modified) && modified.TryGetDateTime(out var modifiedUtc)
                ? modifiedUtc
                : null,
            root.TryGetProperty("version", out var version) ? version.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out var parsedSize)
                ? parsedSize
                : 0,
            canEdit,
            root.TryGetProperty("parents", out var parents) && parents.ValueKind == JsonValueKind.Array
                ? parents.EnumerateArray().Select(parent => parent.GetString() ?? string.Empty)
                    .Where(parent => parent.Length > 0).ToArray()
                : []);
    }

    private static async Task CopySharePermissionsAsync(
        string accessToken,
        string sourceFileId,
        string targetFileId,
        CancellationToken cancellationToken)
    {
        using var listRequest = AuthorizedRequest(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{EscapePath(sourceFileId)}/permissions" +
            "?supportsAllDrives=true&fields=permissions(type,role,emailAddress,domain,allowFileDiscovery,permissionDetails(inherited))",
            accessToken);
        using var listResponse = await Http.SendAsync(listRequest, cancellationToken);
        var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(listResponse, listJson, "Google Drive master sharing lookup");
        using var document = JsonDocument.Parse(listJson);
        if (!document.RootElement.TryGetProperty("permissions", out var permissions) ||
            permissions.ValueKind != JsonValueKind.Array)
            return;
        foreach (var permission in permissions.EnumerateArray())
        {
            var inherited = permission.TryGetProperty("permissionDetails", out var details) &&
                details.ValueKind == JsonValueKind.Array &&
                details.EnumerateArray().Any(detail =>
                    detail.TryGetProperty("inherited", out var inheritedValue) &&
                    inheritedValue.ValueKind == JsonValueKind.True);
            if (inherited) continue;
            var type = permission.TryGetProperty("type", out var typeValue)
                ? typeValue.GetString() ?? string.Empty
                : string.Empty;
            var role = permission.TryGetProperty("role", out var roleValue)
                ? roleValue.GetString() ?? string.Empty
                : string.Empty;
            if (type is not ("anyone" or "user" or "group" or "domain") ||
                role is not ("reader" or "commenter" or "writer"))
                continue;
            var email = permission.TryGetProperty("emailAddress", out var emailValue)
                ? emailValue.GetString()
                : null;
            var domain = permission.TryGetProperty("domain", out var domainValue)
                ? domainValue.GetString()
                : null;
            if (type is "user" or "group" && string.IsNullOrWhiteSpace(email)) continue;
            if (type == "domain" && string.IsNullOrWhiteSpace(domain)) continue;
            var allowDiscovery = permission.TryGetProperty("allowFileDiscovery", out var discovery) &&
                                 discovery.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? discovery.GetBoolean()
                : false;
            var payload = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["role"] = role
            };
            if (!string.IsNullOrWhiteSpace(email)) payload["emailAddress"] = email;
            if (!string.IsNullOrWhiteSpace(domain)) payload["domain"] = domain;
            if (type is "anyone" or "domain") payload["allowFileDiscovery"] = allowDiscovery;
            using var request = AuthorizedRequest(HttpMethod.Post,
                $"https://www.googleapis.com/drive/v3/files/{EscapePath(targetFileId)}/permissions" +
                "?supportsAllDrives=true&sendNotificationEmail=false&fields=id",
                accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, responseJson, "Google Drive client sub-matrix sharing");
        }
    }

    private static async Task<string> GetAccessTokenAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var credential = ReadCredential()
            ?? throw new InvalidOperationException("Connect a Google account first.");
        if (!string.IsNullOrWhiteSpace(credential.AccessToken) &&
            credential.AccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(2))
            return credential.AccessToken;
        if (string.IsNullOrWhiteSpace(credential.RefreshToken))
            throw new InvalidOperationException("Connect a Google account first.");

        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.GoogleDriveOAuthClientId,
                ["client_secret"] = credential.ClientSecret,
                ["refresh_token"] = credential.RefreshToken,
                ["grant_type"] = "refresh_token"
            }), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json, "Google token refresh");
        ApplyTokenResponse(credential, json, preserveRefreshToken: true);
        WriteCredential(credential);
        return credential.AccessToken;
    }

    private static void ApplyTokenResponse(
        GoogleCredentialPayload credential,
        string json,
        bool preserveRefreshToken)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        credential.AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
        credential.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
        if (root.TryGetProperty("refresh_token", out var refresh))
            credential.RefreshToken = refresh.GetString() ?? string.Empty;
        else if (!preserveRefreshToken)
            credential.RefreshToken = string.Empty;
    }

    private static GoogleCredentialPayload? ReadCredential()
    {
        var json = WindowsCredentialStore.Read(CredentialTarget);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<GoogleCredentialPayload>(json, JsonOptions);
    }

    private static void WriteCredential(GoogleCredentialPayload credential) =>
        WindowsCredentialStore.Write(CredentialTarget, JsonSerializer.Serialize(credential, JsonOptions));

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string operation)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = body.Length > 600 ? body[..600] : body;
        throw new InvalidOperationException(
            $"{operation} failed ({(int)response.StatusCode} {response.ReasonPhrase}).\r\n{detail}");
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(
                item => Uri.UnescapeDataString(item[0].Replace('+', ' ')),
                item => item.Length > 1 ? Uri.UnescapeDataString(item[1].Replace('+', ' ')) : string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string RequiredFileId(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFileId))
            throw new InvalidOperationException("Connect a Google Drive share link first.");
        return settings.GoogleDriveFileId;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string EscapePath(string value) => Uri.EscapeDataString(value);
    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class GoogleCredentialPayload
    {
        public string ClientSecret { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiresUtc { get; set; }
    }
}

internal sealed record GoogleOAuthClientConfiguration(string ClientId, string ClientSecret);

internal sealed record GoogleDriveFileMetadata(
    string Id,
    string Name,
    string MimeType,
    DateTime? ModifiedUtc,
    string Version,
    long SizeBytes,
    bool CanEdit,
    IReadOnlyList<string> ParentIds);

internal sealed record GoogleDriveFile(GoogleDriveFileMetadata Metadata, byte[] Contents);
