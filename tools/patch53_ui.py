from pathlib import Path
import re


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8")


def once(text, old, new, label):
    if old not in text:
        raise SystemExit(f"Missing patch anchor: {label}")
    return text.replace(old, new, 1)

# ---------- Backup privacy UI ----------
write("BackupPrivacyOptionsForm.cs", r'''namespace InNasc;

internal sealed class BackupPrivacyOptionsForm : Form
{
    private readonly CheckBox _credentials = new() { Checked = true };
    private readonly CheckBox _configs = new() { Checked = true };

    public PortableBackupOptions Options =>
        new(_credentials.Checked, _configs.Checked);

    private BackupPrivacyOptionsForm()
    {
        Text = "Backup privacy";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 315);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(MasterSignInForm.Heading("Choose backup contents", 26, 22));
        Controls.Add(MasterSignInForm.Description(
            "All data is included by default. Clear either option when sending a backup to someone who should not receive sensitive credentials or configuration files.",
            28, 60, 480, 60));

        _credentials.Text = "Include device usernames and passwords";
        _credentials.Location = new Point(30, 140);
        _credentials.Size = new Size(450, 28);
        _configs.Text = "Include device/client configuration files";
        _configs.Location = new Point(30, 180);
        _configs.Size = new Size(450, 28);
        Controls.AddRange([_credentials, _configs]);

        var note = new Label
        {
            Text = "Default: full backup. Clearing an option affects only the exported copy.",
            AutoSize = false,
            Location = new Point(30, 216),
            Size = new Size(475, 30),
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.5f)
        };
        Controls.Add(note);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(86, 36);
        cancel.Location = new Point(350, 260);
        cancel.DialogResult = DialogResult.Cancel;
        var next = UiTheme.PrimaryButton("Continue");
        next.AutoSize = false;
        next.Size = new Size(86, 36);
        next.Location = new Point(446, 260);
        next.DialogResult = DialogResult.OK;
        Controls.AddRange([cancel, next]);
        AcceptButton = next;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    public static PortableBackupOptions? Prompt(IWin32Window owner)
    {
        using var form = new BackupPrivacyOptionsForm();
        return form.ShowDialog(owner) == DialogResult.OK ? form.Options : null;
    }
}
''')

p = "SettingsForm.cs"
s = read(p)
s = once(s,
'''            Text = "Transfer files may contain stored usernames and passwords. Keep them secure.",''',
'''            Text = "Backups include credentials and configuration files by default; privacy options are offered before export.",''',
"backup warning text")
s = once(s,
'''        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var protection = PasswordDialog.PromptForNewFile(this);''',
'''        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var backupOptions = BackupPrivacyOptionsForm.Prompt(this);
        if (backupOptions is null) return;
        var protection = PasswordDialog.PromptForNewFile(this);''',
"backup privacy prompt")
s = once(s,
'''            PortableDataService.Export(dialog.FileName, _data, protection.Password);''',
'''            PortableDataService.Export(
                dialog.FileName, _data, protection.Password, backupOptions);''',
"backup privacy export")
write(p, s)

# ---------- License admin dialogs ----------
write("InNascLicenseAdminForms.cs", r'''namespace InNasc;

internal sealed class InNascLicenseExpirationForm : Form
{
    private readonly ComboBox _term = new();
    public DateTime? ExpiresUtc { get; private set; }

    private sealed record Option(string Text, int Years)
    {
        public override string ToString() => Text;
    }

    public InNascLicenseExpirationForm(DateTime? current = null)
    {
        Text = "License expiration";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 250);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(MasterSignInForm.Heading("License term", 26, 22));
        Controls.Add(MasterSignInForm.Description(
            current is null
                ? "Choose how long this license remains active. The term begins when you save it."
                : $"Current expiration: {DeviceLimitPolicy.ExpirationText(current)}. Choose a new term from today.",
            28, 60, 444, 48));

        _term.DropDownStyle = ComboBoxStyle.DropDownList;
        _term.Location = new Point(28, 120);
        _term.Size = new Size(310, 32);
        _term.Items.AddRange([
            new Option("1 year", 1),
            new Option("2 years", 2),
            new Option("3 years", 3),
            new Option("Unlimited / no expiration", 0)
        ]);
        _term.SelectedIndex = current is null ? 3 : 0;
        Controls.Add(_term);
        UiTheme.ConfigureUniformComboBox(_term);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(300, 194);
        cancel.DialogResult = DialogResult.Cancel;
        var save = UiTheme.PrimaryButton("Set term");
        save.AutoSize = false;
        save.Size = new Size(88, 36);
        save.Location = new Point(398, 194);
        save.Click += (_, _) =>
        {
            var option = (Option)_term.SelectedItem!;
            ExpiresUtc = option.Years <= 0 ? null : DateTime.UtcNow.AddYears(option.Years);
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.AddRange([cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }
}

internal sealed class InNascRecoveryPasswordForm : Form
{
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly Label _error = new();

    public string Password => _password.Text;

    private InNascRecoveryPasswordForm(string licenseName)
    {
        Text = "Root recovery";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(525, 345);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(MasterSignInForm.Heading("Set break-glass root access", 26, 22));
        Controls.Add(MasterSignInForm.Description(
            $"{licenseName}: the recovery username is root. The password is not stored as readable text; it is hashed and used to encrypt a credential that unlocks this .nasc.",
            28, 60, 468, 58));

        Controls.Add(MasterSignInForm.FieldLabel("Root password", 28, 132));
        MasterSignInForm.ConfigureBox(_password, 28, 154, 468);
        _password.UseSystemPasswordChar = true;
        Controls.Add(_password);
        Controls.Add(MasterSignInForm.FieldLabel("Confirm password", 28, 196));
        MasterSignInForm.ConfigureBox(_confirm, 28, 218, 468);
        _confirm.UseSystemPasswordChar = true;
        Controls.Add(_confirm);

        _error.Location = new Point(28, 256);
        _error.Size = new Size(320, 40);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(326, 296);
        cancel.DialogResult = DialogResult.Cancel;
        var save = UiTheme.PrimaryButton("Set root");
        save.AutoSize = false;
        save.Size = new Size(82, 36);
        save.Location = new Point(424, 296);
        save.Click += (_, _) => Save();
        Controls.AddRange([cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private void Save()
    {
        if (_password.Text.Length < 8)
        {
            _error.Text = "Use at least 8 characters.";
            return;
        }
        if (_password.Text != _confirm.Text)
        {
            _error.Text = "Passwords do not match.";
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    public static string? Prompt(IWin32Window owner, string licenseName)
    {
        using var form = new InNascRecoveryPasswordForm(licenseName);
        return form.ShowDialog(owner) == DialogResult.OK ? form.Password : null;
    }
}
''')

# ---------- Global Admin updater ----------
write("GlobalAdminUpdateService.cs", r'''using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InNasc;

internal static class GlobalAdminUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/IamN8Wright/AVMatrix/releases/latest";
    private const string ExecutableAsset = "InNasc.GlobalAdmin.exe";
    private const string ChecksumAsset = "InNasc.GlobalAdmin.exe.sha256";

    public static async Task CheckAndPromptAsync(Form owner, bool silentWhenCurrent)
    {
        try
        {
            using var http = CreateClient();
            using var response = await http.GetAsync(LatestReleaseApi);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                throw new InvalidDataException("The GitHub release version is unreadable.");

            var current = Version.TryParse(AppInfo.Revision, out var parsed)
                ? parsed
                : new Version(0, 0, 0);
            if (latest <= current)
            {
                if (!silentWhenCurrent)
                    MessageBox.Show(owner,
                        $"InNasc Global Admin {AppInfo.Revision} is current.",
                        "Check for updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(owner,
                    $"InNasc Global Admin {latest} is available.\r\n\r\n" +
                    "Download, verify, and install the update now?",
                    "Global Admin update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            var assets = root.GetProperty("assets").EnumerateArray().ToList();
            string AssetUrl(string name)
            {
                foreach (var asset in assets)
                {
                    if (string.Equals(asset.GetProperty("name").GetString(), name,
                            StringComparison.OrdinalIgnoreCase))
                        return asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                }
                throw new InvalidDataException($"The GitHub release does not contain {name}.");
            }

            var staging = Path.Combine(
                Path.GetTempPath(), "InNasc", "GlobalAdminUpdates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var executablePath = Path.Combine(staging, ExecutableAsset);
            var checksumText = await http.GetStringAsync(AssetUrl(ChecksumAsset));
            var expected = checksumText.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            var executableBytes = await http.GetByteArrayAsync(AssetUrl(ExecutableAsset));
            await File.WriteAllBytesAsync(executablePath, executableBytes);

            await using (var stream = File.OpenRead(executablePath))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "The Global Admin update failed SHA-256 verification.");
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.Equals(versionInfo.ProductName, "InNasc Global Admin",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The downloaded executable is not InNasc Global Admin.");
            if (!Version.TryParse(versionInfo.ProductVersion?.Split('+')[0], out var executableVersion) ||
                executableVersion != latest)
                throw new InvalidDataException(
                    "The downloaded Global Admin version does not match the GitHub release.");

            InstallAndRestart(executablePath, staging);
        }
        catch (Exception exception)
        {
            if (!silentWhenCurrent)
                MessageBox.Show(owner,
                    $"The Global Admin update could not be completed.\r\n\r\n{exception.Message}",
                    "Global Admin update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"InNasc-GlobalAdmin/{AppInfo.Revision}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static void InstallAndRestart(string source, string staging)
    {
        var target = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(target) ||
            !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Automatic installation requires the packaged Global Admin executable.");

        var script =
            "$ErrorActionPreference='Stop'\r\n" +
            $"$pidToWait={Environment.ProcessId}\r\n" +
            $"$source='{PowerShellLiteral(source)}'\r\n" +
            $"$target='{PowerShellLiteral(target)}'\r\n" +
            $"$staging='{PowerShellLiteral(staging)}'\r\n" +
            "Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue\r\n" +
            "Start-Sleep -Milliseconds 400\r\n" +
            "Copy-Item -LiteralPath $source -Destination $target -Force\r\n" +
            "Start-Process -FilePath $target\r\n" +
            "Start-Sleep -Milliseconds 400\r\n" +
            "Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue\r\n";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Application.Exit();
    }

    private static string PowerShellLiteral(string value) => value.Replace("'", "''");
}
''')

p = "GlobalAdminProgram.cs"
s = read(p)
s = once(s,
'''        using var admin = new InNascGlobalAdminForm(
            selection.GlobalPath,
            selection.Catalog,
            selection.Session);
        Application.Run(admin);''',
'''        using var admin = new InNascGlobalAdminForm(
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
        Application.Run(admin);''',
"global admin startup updater")
write(p, s)

# ---------- Global Admin company UI ----------
p = "InNascGlobalAdminForm.cs"
s = read(p)
# Top-level Close should explicitly close a modeless Application.Run form.
s = once(s,
'''        close.DialogResult = DialogResult.OK;
        panel.Controls.Add(close);''',
'''        close.Click += (_, _) => Close();
        panel.Controls.Add(close);''',
"directory close")
# Manual update button in top-level header.
s = once(s,
'''        var admins = UiTheme.SecondaryButton("Global Admins");''',
'''        var update = UiTheme.SecondaryButton("Check updates");
        update.AutoSize = false;
        update.Size = new Size(120, 38);
        update.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        update.Click += async (_, _) =>
            await GlobalAdminUpdateService.CheckAndPromptAsync(this, false);
        panel.Controls.Add(update);

        var admins = UiTheme.SecondaryButton("Global Admins");''',
"check updates header")
s = once(s,
'''            admins.Left = migrate.Left - admins.Width - 10;
        };''',
'''            admins.Left = migrate.Left - admins.Width - 10;
            update.Left = admins.Left - update.Width - 10;
        };''',
"update header alignment")
# Nested company details Back button is modal but explicit close is clearer and fixes dead buttons.
s = once(s,
'''        close.DialogResult = DialogResult.OK;
        panel.Controls.Add(close);''',
'''        close.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        panel.Controls.Add(close);''',
"company back close")

old = '''        var grant = Button("+ Grant .nasc", 18, GrantFile, true);
        var import = Button("Import .nasc", 144, ImportFile);
        var limit = Button("Change tier", 282, ChangeLimit);
        var sync = Button("Sync selected", 420, SyncSelectedFile);
        var remove = Button("Remove grant", 558, RemoveFile);
        card.Controls.AddRange([grant, import, limit, sync, remove]);
        ConfigureList(_licenses);
        _licenses.Location = new Point(18, 92);
        _licenses.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _licenses.Size = new Size(1040, 130);
        _licenses.Columns.Add("License", 190);
        _licenses.Columns.Add("Devices", 90);
        _licenses.Columns.Add("Tier", 130);
        _licenses.Columns.Add("File", 560);
        card.Controls.Add(_licenses);
        card.Resize += (_, _) => _licenses.Size = new Size(card.ClientSize.Width - 36, card.ClientSize.Height - 108);'''
new = '''        var tools = new FlowLayoutPanel
        {
            Location = new Point(18, 48),
            Height = 76,
            Width = 1080,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            WrapContents = true,
            Margin = Padding.Empty
        };
        tools.Controls.AddRange([
            ToolButton("+ Grant .nasc", GrantFile, true),
            ToolButton("Import / reconcile", ImportFile),
            ToolButton("Rename license", RenameSelectedLicense),
            ToolButton("Device tier", ChangeLimit),
            ToolButton("Expiration", ChangeExpiration),
            ToolButton("Root recovery", ChangeRecoveryPassword),
            ToolButton("Sync selected", SyncSelectedFile),
            ToolButton("Remove grant", RemoveFile)]);
        card.Controls.Add(tools);

        ConfigureList(_licenses);
        _licenses.Location = new Point(18, 128);
        _licenses.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _licenses.Size = new Size(1040, 110);
        _licenses.Columns.Add("License", 160);
        _licenses.Columns.Add("Devices", 72);
        _licenses.Columns.Add("Tier", 100);
        _licenses.Columns.Add("Expires", 110);
        _licenses.Columns.Add("Recovery", 92);
        _licenses.Columns.Add("File", 500);
        card.Controls.Add(_licenses);
        card.Resize += (_, _) =>
        {
            tools.Width = card.ClientSize.Width - 36;
            _licenses.Size = new Size(
                card.ClientSize.Width - 36,
                Math.Max(60, card.ClientSize.Height - 144));
        };'''
s = once(s, old, new, "license toolbar")

old = '''        var add = Button("+ Add user", 18, AddUser, true);
        var edit = Button("Edit access", 128, EditUser);
        var reset = Button("Reset password", 242, ResetUserPassword);
        var delete = Button("Delete user", 390, DeleteUser);
        add.Top = edit.Top = reset.Top = delete.Top = 72;
        card.Controls.AddRange([add, edit, reset, delete]);'''
new = '''        var userTools = new FlowLayoutPanel
        {
            Location = new Point(18, 72),
            Height = 40,
            Width = 760,
            WrapContents = false,
            Margin = Padding.Empty
        };
        userTools.Controls.AddRange([
            ToolButton("+ Add user", AddUser, true),
            ToolButton("Edit access", EditUser),
            ToolButton("Reset password", ResetUserPassword),
            ToolButton("Delete user", DeleteUser)]);
        card.Controls.Add(userTools);'''
s = once(s, old, new, "user toolbar")

# Uniform toolbar helper replaces positional helper.
start = s.index('    private Button Button(string text, int left, Action action, bool primary = false)')
end = s.index('    private static void ConfigureList', start)
s = s[:start] + '''    private Button ToolButton(string text, Action action, bool primary = false)
    {
        var button = primary ? UiTheme.PrimaryButton(text) : UiTheme.SecondaryButton(text);
        button.AutoSize = false;
        button.Size = new Size(124, 34);
        button.Margin = new Padding(0, 0, 10, 6);
        button.Click += (_, _) => action();
        return button;
    }

''' + s[end:]

s = once(s,
'''            item.SubItems.Add(DeviceLimitPolicy.LimitText(file.DeviceLimit));
            item.SubItems.Add(file.FilePath);''',
'''            item.SubItems.Add(DeviceLimitPolicy.LimitText(file.DeviceLimit));
            item.SubItems.Add(DeviceLimitPolicy.ExpirationText(file.ExpiresUtc));
            item.SubItems.Add(file.RecoveryCredentialReady ? "Ready" : "Set root");
            item.SubItems.Add(file.FilePath);''',
"license list lifecycle columns")
s = once(s,
'''            item.SubItems.Add(user.CredentialReady ? "Ready" : "Password reset needed");''',
'''            item.SubItems.Add(user.CredentialReady
                ? "Ready"
                : "Synced from .nasc - reset password to manage");''',
"pulled user status")

# Create company: term + required recovery password.
s = once(s,
'''        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var company = InNascGlobalCoreService.CreateCompany(
                _globalPath, _catalog, _session, form.EnteredCompanyName, form.CompanyPath, form.DeviceLimit);''',
'''        if (form.ShowDialog(this) != DialogResult.OK) return;
        using var expiration = new InNascLicenseExpirationForm();
        if (expiration.ShowDialog(this) != DialogResult.OK) return;
        var recoveryPassword = InNascRecoveryPasswordForm.Prompt(this, form.EnteredCompanyName);
        if (recoveryPassword is null) return;
        try
        {
            var company = InNascGlobalCoreService.CreateCompany(
                _globalPath, _catalog, _session,
                form.EnteredCompanyName,
                form.CompanyPath,
                form.DeviceLimit,
                expiration.ExpiresUtc,
                recoveryPassword);''',
"create company lifecycle prompts")

# Grant license: term + required recovery.
s = once(s,
'''        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("Grant .nasc", () =>
        {
            var file = InNascGlobalCoreService.AddCompanyFile(
                _globalPath, _catalog, _session, _company.Id,
                form.FileName, form.CompanyPath, form.DeviceLimit);''',
'''        if (form.ShowDialog(this) != DialogResult.OK) return;
        using var expiration = new InNascLicenseExpirationForm();
        if (expiration.ShowDialog(this) != DialogResult.OK) return;
        var recoveryPassword = InNascRecoveryPasswordForm.Prompt(this, form.FileName);
        if (recoveryPassword is null) return;
        Try("Grant .nasc", () =>
        {
            var file = InNascGlobalCoreService.AddCompanyFile(
                _globalPath, _catalog, _session, _company.Id,
                form.FileName,
                form.CompanyPath,
                form.DeviceLimit,
                expiration.ExpiresUtc,
                recoveryPassword);''',
"grant lifecycle prompts")

# Import same path = reconcile rather than reject. A new import must get a recovery password.
s = once(s,
'''        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);''',
'''        try
        {
            var fullImportPath = Path.GetFullPath(dialog.FileName);
            var linkedHere = _company.Files.FirstOrDefault(file =>
                file.Enabled && string.Equals(file.FilePath, fullImportPath,
                    StringComparison.OrdinalIgnoreCase));
            var linkedElsewhere = _catalog.Companies
                .Where(candidate => candidate.Id != _company.Id)
                .SelectMany(candidate => candidate.Files)
                .FirstOrDefault(file => file.Enabled &&
                    string.Equals(file.FilePath, fullImportPath,
                        StringComparison.OrdinalIgnoreCase));
            if (linkedElsewhere is not null)
                throw new InvalidOperationException(
                    "That .nasc file is already assigned to another company in this Global Admin catalog.");
            if (linkedHere is not null)
            {
                if (!linkedHere.RecoveryCredentialReady)
                {
                    var rootPassword = InNascRecoveryPasswordForm.Prompt(this, linkedHere.Name);
                    if (rootPassword is null) return;
                    InNascGlobalCoreService.SetRecoveryPassword(
                        _globalPath, _catalog, _session, _company.Id,
                        linkedHere.Id, rootPassword);
                }
                InNascCompanyAccessSyncService.SyncFile(
                    _globalPath, _catalog, _session, _company, linkedHere);
                RefreshAll(linkedHere.Id);
                _status.Text = $"Reconciled {linkedHere.Name}: pulled current .nasc users/access and republished license metadata, logo, tier, expiration, and recovery access.";
                return;
            }

            var bytes = File.ReadAllBytes(dialog.FileName);''',
"same-file reconcile")
s = once(s,
'''            var file = InNascCompanyGlobalAdminService.ImportExisting(
                _globalPath, _catalog, _session, _company, dialog.FileName, companyKey!);
            RefreshAll(file.Id);''',
'''            var recoveryPassword = InNascRecoveryPasswordForm.Prompt(
                this, Path.GetFileNameWithoutExtension(dialog.FileName));
            if (recoveryPassword is null) return;
            var file = InNascCompanyGlobalAdminService.ImportExisting(
                _globalPath, _catalog, _session, _company, dialog.FileName, companyKey!);
            InNascGlobalCoreService.SetRecoveryPassword(
                _globalPath, _catalog, _session, _company.Id, file.Id, recoveryPassword);
            InNascCompanyAccessSyncService.SyncFile(
                _globalPath, _catalog, _session, _company, file);
            RefreshAll(file.Id);''',
"new import recovery")

# Add selected-license actions.
action_anchor = '''    private void ChangeLimit()
    {'''
actions = r'''    private void RenameSelectedLicense()
    {
        var file = SelectedFile();
        if (file is null)
        {
            SelectMessage("Select a .nasc license first.");
            return;
        }
        var name = InputDialog.Show(this, "Rename license", "License name", file.Name);
        if (name is null || string.Equals(name.Trim(), file.Name, StringComparison.Ordinal)) return;
        Try("Rename license", () =>
        {
            InNascCompanyGlobalAdminService.RenameLicense(
                _globalPath, _catalog, _session, _company, file, name);
            RefreshAll(file.Id);
            _status.Text = $"Renamed the selected license to {file.Name}.";
        });
    }

    private void ChangeExpiration()
    {
        var file = SelectedFile();
        if (file is null)
        {
            SelectMessage("Select a .nasc license first.");
            return;
        }
        using var form = new InNascLicenseExpirationForm(file.ExpiresUtc);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        Try("License expiration", () =>
        {
            InNascGlobalCoreService.SetLicenseExpiration(
                _globalPath, _catalog, _session, _company.Id, file.Id, form.ExpiresUtc);
            InNascCompanyGlobalAdminService.ApplyToFile(_company, file);
            RefreshAll(file.Id);
            _status.Text = $"Expiration for {file.Name}: {DeviceLimitPolicy.ExpirationText(file.ExpiresUtc)}.";
        });
    }

    private void ChangeRecoveryPassword()
    {
        var file = SelectedFile();
        if (file is null)
        {
            SelectMessage("Select a .nasc license first.");
            return;
        }
        var password = InNascRecoveryPasswordForm.Prompt(this, file.Name);
        if (password is null) return;
        Try("Root recovery", () =>
        {
            InNascGlobalCoreService.SetRecoveryPassword(
                _globalPath, _catalog, _session, _company.Id, file.Id, password);
            InNascCompanyAccessSyncService.SyncFile(
                _globalPath, _catalog, _session, _company, file);
            RefreshAll(file.Id);
            _status.Text = $"Reset the hidden root recovery credential for {file.Name}.";
        });
    }

'''
s = once(s, action_anchor, actions + action_anchor, "selected license actions")

# Sync must ensure every file gets recovery access.
s = once(s,
'''        Try("Sync .nasc", () =>
        {
            InNascCompanyAccessSyncService.SyncFile(_globalPath, _catalog, _session, _company, file);
            _status.Text = $"Published company users and the current device tier to {file.Name}.";''',
'''        if (!file.RecoveryCredentialReady)
        {
            var password = InNascRecoveryPasswordForm.Prompt(this, file.Name);
            if (password is null) return;
            InNascGlobalCoreService.SetRecoveryPassword(
                _globalPath, _catalog, _session, _company.Id, file.Id, password);
        }
        Try("Sync .nasc", () =>
        {
            InNascCompanyAccessSyncService.SyncFile(
                _globalPath, _catalog, _session, _company, file);
            _status.Text = $"Reconciled {file.Name}: pulled current .nasc users/access and republished license name, tier, expiration, logo, and recovery access.";''',
"sync reconcile status/root")
write(p, s)

# ---------- Welcome page warning before sign-in ----------
p = "MasterWelcomeControl.cs"
s = read(p)
s = once(s,
'''    private readonly Label _message = new();''',
'''    private readonly Label _message = new();
    private readonly Label _licenseWarning = new();''',
"master welcome warning field")
s = once(s,
'''        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));''',
'''        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 278));''',
"master welcome heading height")
s = once(s,
'''            RowCount = 4,''',
'''            RowCount = 5,''',
"master welcome heading rows")
s = once(s,
'''        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));''',
'''        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));''',
"master welcome warning row")
s = once(s,
'''        heading.Controls.Add(_welcomeSubtitle, 0, 3);
        return heading;''',
'''        heading.Controls.Add(_welcomeSubtitle, 0, 3);

        _licenseWarning.Dock = DockStyle.Fill;
        _licenseWarning.Font = UiTheme.Font(9, FontStyle.Bold);
        _licenseWarning.ForeColor = UiTheme.Red;
        _licenseWarning.TextAlign = ContentAlignment.MiddleCenter;
        _licenseWarning.Visible = false;
        heading.Controls.Add(_licenseWarning, 0, 4);
        return heading;''',
"master welcome warning label")
s = once(s,
'''        ShowCompanyLogo(summary?.CompanyLogoBase64);''',
'''        ShowCompanyLogo(summary?.CompanyLogoBase64);
        _licenseWarning.Text = summary?.LicenseExpiresUtc is DateTime expiry &&
                               expiry <= DateTime.UtcNow
            ? $"LICENSE EXPIRED {DeviceLimitPolicy.ExpirationText(expiry)} - new client cards and devices are locked after sign-in."
            : string.Empty;
        _licenseWarning.Visible = _licenseWarning.Text.Length > 0;''',
"prelogin expiration warning")
write(p, s)

# ---------- Main app warning / lock new clients ----------
p = "MainForm.cs"
s = read(p)
s = once(s,
'''    private readonly Label _workspaceCheckoutStatus = new();''',
'''    private readonly Label _workspaceCheckoutStatus = new();
    private readonly Label _licenseWarning = new();''',
"main warning field")
old = '''        top.Controls.Add(new Label
        {
            Text = "Choose a client to open locations, rooms, and equipment",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.7f),
            Location = new Point(26, 62)
        });
        return top;'''
new = '''        top.Controls.Add(new Label
        {
            Text = "Choose a client to open locations, rooms, and equipment",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.7f),
            Location = new Point(26, 56)
        });
        _licenseWarning.AutoSize = false;
        _licenseWarning.Location = new Point(26, 78);
        _licenseWarning.Size = new Size(980, 26);
        _licenseWarning.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _licenseWarning.ForeColor = UiTheme.Red;
        _licenseWarning.Font = UiTheme.Font(8.8f, FontStyle.Bold);
        _licenseWarning.Visible = false;
        top.Controls.Add(_licenseWarning);
        top.Resize += (_, _) =>
            _licenseWarning.Width = Math.Max(240, top.ClientSize.Width - 52);
        return top;'''
s = once(s, old, new, "main welcome warning")
s = once(s,
'''    private void AddClient()
    {
        if (!EnsureWorkspaceWritable()) return;''',
'''    private void AddClient()
    {
        if (!EnsureWorkspaceWritable()) return;
        if (!EnsureNewClientAllowed()) return;''',
"new client lock")
anchor = '''    private bool EnsureDeviceCapacity(int additionalDevices)
    {'''
helper = r'''    private bool EnsureNewClientAllowed()
    {
        try
        {
            DeviceLimitPolicy.RequireNewClientAllowed(_data.MasterAccess, _data);
            return true;
        }
        catch (Exception exception) when (
            exception is DeviceLimitExceededException or LicenseExpiredException)
        {
            MessageBox.Show(this,
                exception.Message,
                "License restriction",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            RefreshLicenseWarning();
            return false;
        }
    }

    private void RefreshLicenseWarning()
    {
        var warning = DeviceLimitPolicy.WarningText(_data.MasterAccess, _data);
        _licenseWarning.Text = warning;
        _licenseWarning.Visible =
            warning.Length > 0 && MasterSessionContext.Current is not null;
    }

'''
s = once(s, anchor, helper + anchor, "main license warning helpers")
s = once(s,
'''    private void ShowWelcomePage()
    {
        if (MasterSessionContext.Current is null)''',
'''    private void ShowWelcomePage()
    {
        RefreshLicenseWarning();
        if (MasterSessionContext.Current is null)''',
"refresh warning on welcome")
write(p, s)
