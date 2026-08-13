using System.Text.Json;

namespace AVMatrixStudio;

internal sealed class GitHubMasterStorageConfiguration
{
    public string Owner { get; set; } = "IamN8Wright";
    public string Repository { get; set; } = "avmatrix_MasterMatrixStorage";
    public string Branch { get; set; } = "main";
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyDisplayName { get; set; } = string.Empty;

    public GitHubMasterStorageOptions ToOptions() => new(
        Owner.Trim(),
        Repository.Trim(),
        string.IsNullOrWhiteSpace(Branch) ? "main" : Branch.Trim(),
        CompanyId.Trim());
}

internal static class GitHubMasterStorageConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string PathFor(DataStore store) =>
        Path.Combine(store.DataDirectory, "GitHubMasterStorage.json");

    public static GitHubMasterStorageConfiguration Load(DataStore store)
    {
        var path = PathFor(store);
        if (!File.Exists(path)) return new GitHubMasterStorageConfiguration();
        try
        {
            var configuration = JsonSerializer.Deserialize<GitHubMasterStorageConfiguration>(
                File.ReadAllText(path),
                JsonOptions) ?? new GitHubMasterStorageConfiguration();
            Normalize(configuration);
            return configuration;
        }
        catch
        {
            return new GitHubMasterStorageConfiguration();
        }
    }

    public static void Save(DataStore store, GitHubMasterStorageConfiguration configuration)
    {
        Normalize(configuration);
        Directory.CreateDirectory(store.DataDirectory);
        var path = PathFor(store);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static void Normalize(GitHubMasterStorageConfiguration configuration)
    {
        configuration.Owner = string.IsNullOrWhiteSpace(configuration.Owner)
            ? "IamN8Wright"
            : configuration.Owner.Trim();
        configuration.Repository = string.IsNullOrWhiteSpace(configuration.Repository)
            ? "avmatrix_MasterMatrixStorage"
            : configuration.Repository.Trim();
        configuration.Branch = string.IsNullOrWhiteSpace(configuration.Branch)
            ? "main"
            : configuration.Branch.Trim();
        configuration.CompanyId ??= string.Empty;
        configuration.CompanyDisplayName ??= string.Empty;
    }
}
