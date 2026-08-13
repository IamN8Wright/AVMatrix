namespace AVMatrixStudio;

internal static class AppInfo
{
    public const string ProductName = "InNasc";
    public const string Tagline = "Systems in context.";
    public const string Website = "https://InNasc.com";

    public static string Revision
    {
        get
        {
            var version = typeof(AppInfo).Assembly.GetName().Version;
            return version is null
                ? "5.0.0"
                : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }

    public static int CurrentYear => DateTime.Now.Year;
}
