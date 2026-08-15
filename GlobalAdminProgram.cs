namespace InNasc;

internal static class GlobalAdminProgram
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatal(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatal(args.ExceptionObject as Exception ?? new Exception("Unknown application error."));

        var data = new DataStore().Load();
        UiTheme.SetDarkMode(data.Settings.DarkMode);
        using var startup = new InNascStartupForm();
        if (startup.ShowDialog() != DialogResult.OK || startup.Selection is null)
            return;

        var selection = startup.Selection;
        using var admin = new InNascGlobalAdminForm(
            selection.GlobalPath,
            selection.Catalog,
            selection.Session);
        var startupUpdateChecked = false;
        admin.Shown += async (_, _) =>
        {
            if (startupUpdateChecked) return;
            startupUpdateChecked = true;
            await GlobalAdminUpdateService.CheckAndPromptAsync(admin, true);
        };
        Application.Run(admin);
    }

    private static void ShowFatal(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InNasc",
                "GlobalAdmin");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Preserve the original error if logging is unavailable.
        }

        MessageBox.Show(
            $"InNasc Global Admin encountered an error.\r\n\r\n{exception.Message}",
            "InNasc Global Admin",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
