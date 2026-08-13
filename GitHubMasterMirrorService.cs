namespace AVMatrixStudio;

internal sealed record GitHubMasterMirrorResult(
    string CompanyId,
    int ClientPayloadCount,
    long TotalBytes,
    string CommitSha,
    bool CompanyCreated);

internal static class GitHubMasterMirrorService
{
    public static async Task<GitHubMasterMirrorResult> MirrorActiveMasterAsync(
        AppData data,
        GitHubMasterStorageConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var active = MasterSessionContext.Current
            ?? throw new MasterAuthorizationException(
                "Sign in to a Master Matrix before mirroring it to GitHub.");
        var options = configuration.ToOptions();
        var displayName = string.IsNullOrWhiteSpace(configuration.CompanyDisplayName)
            ? data.ProjectName
            : configuration.CompanyDisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) displayName = options.CompanyId;

        await GitHubMasterStorageService.TestConnectionAsync(options, cancellationToken);

        var source = await ReadActiveRemoteAsync(data, active, cancellationToken);
        var totalBytes = source.MasterBytes.LongLength +
                         source.ClientPayloads.Values.Sum(bytes => bytes.LongLength);

        var companyCreated = false;
        string commitSha;
        try
        {
            var existing = await GitHubMasterStorageService.ReadMasterAsync(options, cancellationToken);
            var files = new Dictionary<string, byte[]>
            {
                [GitHubMasterStorageService.MasterPath(options)] = source.MasterBytes
            };
            foreach (var payload in source.ClientPayloads)
                files[GitHubMasterStorageService.ClientPath(options, payload.Key)] = payload.Value;

            var committed = await GitHubMasterStorageService.CommitFilesAsync(
                options,
                files,
                existing.CommitSha,
                $"Mirror InNasc Master for {displayName}",
                cancellationToken);
            commitSha = committed.CommitSha;
        }
        catch (FileNotFoundException)
        {
            var created = await GitHubMasterStorageService.CreateCompanyAsync(
                options,
                displayName,
                source.MasterBytes,
                cancellationToken);
            companyCreated = true;
            commitSha = created.CommitSha;

            if (source.ClientPayloads.Count > 0)
            {
                var payloadFiles = source.ClientPayloads.ToDictionary(
                    item => GitHubMasterStorageService.ClientPath(options, item.Key),
                    item => item.Value);
                var committed = await GitHubMasterStorageService.CommitFilesAsync(
                    options,
                    payloadFiles,
                    created.CommitSha,
                    $"Mirror InNasc client payloads for {displayName}",
                    cancellationToken);
                commitSha = committed.CommitSha;
            }
        }

        return new GitHubMasterMirrorResult(
            options.CompanyId,
            source.ClientPayloads.Count,
            totalBytes,
            commitSha,
            companyCreated);
    }

    private static async Task<ActiveRemoteSnapshot> ReadActiveRemoteAsync(
        AppData data,
        ActiveMasterSession active,
        CancellationToken cancellationToken)
    {
        return active.Target switch
        {
            SyncTarget.SharedFile => ReadSharedRemote(data),
            SyncTarget.GoogleDrive => await ReadGoogleDriveRemoteAsync(data, cancellationToken),
            _ => throw new InvalidOperationException(
                "This Master Matrix provider cannot be mirrored to GitHub yet.")
        };
    }

    private static ActiveRemoteSnapshot ReadSharedRemote(AppData data)
    {
        var masterPath = data.Settings.SharedMasterPath;
        if (string.IsNullOrWhiteSpace(masterPath) || !File.Exists(masterPath))
            throw new FileNotFoundException(
                "The active shared Master Matrix file is not available.",
                masterPath);

        var payloads = new Dictionary<Guid, byte[]>();
        foreach (var client in data.Clients)
        {
            var path = ClientSubmatrixService.SharedClientPath(masterPath, client.Id);
            if (!File.Exists(path)) continue;
            var bytes = File.ReadAllBytes(path);
            ValidatePayloadSize(path, bytes);
            payloads[client.Id] = bytes;
        }

        var masterBytes = File.ReadAllBytes(masterPath);
        ValidatePayloadSize(masterPath, masterBytes);
        return new ActiveRemoteSnapshot(masterBytes, payloads);
    }

    private static async Task<ActiveRemoteSnapshot> ReadGoogleDriveRemoteAsync(
        AppData data,
        CancellationToken cancellationToken)
    {
        var master = await GoogleDriveService.DownloadAsync(data.Settings, cancellationToken);
        ValidatePayloadSize("master.avmatrix", master.Contents);

        var payloads = new Dictionary<Guid, byte[]>();
        foreach (var reference in data.MasterAccess.ClientSubmatrices)
        {
            if (reference.ClientId == Guid.Empty ||
                string.IsNullOrWhiteSpace(reference.GoogleDriveFileId))
                continue;
            var file = await GoogleDriveService.DownloadByIdAsync(
                data.Settings,
                reference.GoogleDriveFileId,
                cancellationToken);
            ValidatePayloadSize(reference.FileName, file.Contents);
            payloads[reference.ClientId] = file.Contents;
        }

        return new ActiveRemoteSnapshot(master.Contents, payloads);
    }

    private static void ValidatePayloadSize(string name, byte[] bytes)
    {
        if (bytes.LongLength <= GitHubMasterStorageService.MaximumStoredFileBytes) return;
        throw new InvalidOperationException(
            $"'{name}' is {bytes.LongLength / (1024d * 1024d):N1} MB and is too large for the " +
            "GitHub Master Matrix storage provider. Leave this Master on Google Drive or a local/network share.");
    }

    private sealed record ActiveRemoteSnapshot(
        byte[] MasterBytes,
        Dictionary<Guid, byte[]> ClientPayloads);
}
