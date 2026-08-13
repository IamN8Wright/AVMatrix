using System.Text.Json;

namespace AVMatrixStudio;

internal sealed class InNascGlobalConfig
{
    public string GlobalPath { get; set; } = string.Empty;
    public Guid? LastCompanyId { get; set; }
}

internal static class InNascGlobalConfigStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InNasc");

    public static string FilePath => Path.Combine(DirectoryPath, "global-settings.json");

    public static InNascGlobalConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new InNascGlobalConfig();
            return JsonSerializer.Deserialize<InNascGlobalConfig>(File.ReadAllBytes(FilePath), Json)
                ?? new InNascGlobalConfig();
        }
        catch
        {
            return new InNascGlobalConfig();
        }
    }

    public static void Save(InNascGlobalConfig config)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(config, Json));
        File.Move(temp, FilePath, true);
    }
}
