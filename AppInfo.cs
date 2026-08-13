namespace AVMatrixStudio;

internal static class AppInfo
{
    public static string Revision
    {
        get
        {
            var version = typeof(AppInfo).Assembly.GetName().Version;
            return version is null
                ? "3.2.6"
                : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }

    public static int CurrentYear => DateTime.Now.Year;
}
