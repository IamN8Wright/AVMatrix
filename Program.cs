namespace AVMatrixStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatal(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatal(args.ExceptionObject as Exception ?? new Exception("Unknown application error."));

        var store = new DataStore();
        var data = store.Load();
        Application.Run(new MainForm(data, store));
    }

    private static void ShowFatal(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AVMatrixStudio");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Do not obscure the original error if logging is unavailable.
        }

        MessageBox.Show(
            $"AV Matrix Studio encountered an error.\r\n\r\n{exception.Message}",
            "AV Matrix Studio",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
