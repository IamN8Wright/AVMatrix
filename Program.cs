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
        var mainForm = new MainForm(data, store);
        var startupUpdateChecked = false;
        mainForm.Shown += async (_, _) =>
        {
            if (startupUpdateChecked) return;
            startupUpdateChecked = true;
            await CheckForStartupUpdateAsync(mainForm);
        };
        Application.Run(mainForm);
    }

    private static async Task CheckForStartupUpdateAsync(Form owner)
    {
        AppReleaseInfo release;
        try
        {
            release = await AppUpdateService.CheckLatestReleaseAsync();
        }
        catch
        {
            // Update availability must never block or interrupt normal startup.
            return;
        }

        if (!AppUpdateService.IsNewer(release.Version)) return;

        var install = MessageBox.Show(
            owner,
            $"AV Matrix Studio {release.Version} is available from GitHub.\r\n\r\n" +
            $"This PC is running {AppInfo.Revision}.\r\n\r\n" +
            "Download, verify, and install the update now?",
            "AV Matrix Studio update available",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);
        if (install != DialogResult.Yes) return;

        using var progressForm = new AppUpdateProgressForm();
        progressForm.Show(owner);
        progressForm.BringToFront();
        owner.UseWaitCursor = true;
        AppUpdateCandidate? candidate = null;
        try
        {
            var progress = new Progress<string>(progressForm.SetStatus);
            candidate = await AppUpdateService.DownloadAndVerifyAsync(progress);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                owner,
                $"The GitHub update could not be downloaded or verified.\r\n\r\n{exception.Message}",
                "Update AV Matrix Studio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        finally
        {
            owner.UseWaitCursor = false;
            if (!progressForm.IsDisposed) progressForm.Close();
        }

        if (candidate is null) return;
        if (!AppUpdateService.IsNewer(candidate))
        {
            AppUpdateService.Discard(candidate);
            return;
        }

        try
        {
            AppUpdateService.InstallAndRestart(candidate);
        }
        catch (Exception exception)
        {
            AppUpdateService.Discard(candidate);
            MessageBox.Show(
                owner,
                $"The verified update could not be started.\r\n\r\n{exception.Message}",
                "Update AV Matrix Studio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
