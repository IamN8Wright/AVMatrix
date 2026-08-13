namespace InNasc;

internal sealed record ActiveMasterSession(
    SyncTarget Target,
    string MasterKey,
    MasterSession Session);

internal static class MasterSessionContext
{
    private static ActiveMasterSession? _current;

    public static event EventHandler? Changed;

    public static ActiveMasterSession? Current => _current;

    public static MasterSession? Get(SyncTarget target, string masterKey)
    {
        var normalizedKey = NormalizeKey(target, masterKey);
        if (_current is not null &&
            _current.Target == target &&
            string.Equals(_current.MasterKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
            return _current.Session;

        var global = InNascGlobalSessionContext.Current;
        if (_current is not null &&
            global is not null &&
            InNascGlobalSessionContext.CompanyId.HasValue &&
            _current.Session.UserId == global.UserId &&
            target == SyncTarget.GoogleDrive &&
            _current.Target == SyncTarget.SharedFile)
            return _current.Session;

        return null;
    }

    public static void Set(SyncTarget target, string masterKey, MasterSession session)
    {
        var global = InNascGlobalSessionContext.Current;
        if (_current is not null &&
            global is not null &&
            InNascGlobalSessionContext.CompanyId.HasValue &&
            _current.Target == SyncTarget.SharedFile &&
            target == SyncTarget.GoogleDrive &&
            _current.Session.UserId == global.UserId &&
            session.UserId == global.UserId)
            return;

        _current = new ActiveMasterSession(target, NormalizeKey(target, masterKey), session);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Clear(SyncTarget target, string masterKey)
    {
        if (_current is null) return;
        var normalizedKey = NormalizeKey(target, masterKey);
        if (_current.Target != target ||
            !string.Equals(_current.MasterKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
            return;
        _current = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Clear()
    {
        if (_current is null) return;
        _current = null;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static string NormalizeKey(SyncTarget target, string masterKey)
    {
        var value = masterKey.Trim();
        if (target != SyncTarget.SharedFile || value.Length == 0) return value;
        try
        {
            return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return value;
        }
    }
}

internal sealed class MasterSignInNotification : Form
{
    private readonly System.Windows.Forms.Timer _closeTimer = new() { Interval = 4500 };

    private MasterSignInNotification(MasterSession session)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(330, 88);
        BackColor = UiTheme.NavySoft;
        Padding = new Padding(18, 12, 18, 12);
        Font = UiTheme.Font();

        var accent = new Panel
        {
            BackColor = UiTheme.Green,
            Location = new Point(0, 0),
            Size = new Size(5, ClientSize.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        Controls.Add(accent);
        Controls.Add(new Label
        {
            Text = "SIGNED IN TO COMPANY",
            AutoSize = true,
            ForeColor = UiTheme.SidebarMuted,
            Font = UiTheme.Font(8, FontStyle.Bold),
            Location = new Point(20, 13)
        });
        Controls.Add(new Label
        {
            Text = $"{session.DisplayName}  •  {RoleText(session.Role)}",
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = Color.White,
            Font = UiTheme.Font(12, FontStyle.Bold),
            Location = new Point(18, 38),
            Size = new Size(292, 30),
            TextAlign = ContentAlignment.MiddleLeft
        });

        Click += (_, _) => Close();
        foreach (Control child in Controls) child.Click += (_, _) => Close();
        _closeTimer.Tick += (_, _) => Close();
        FormClosed += (_, _) => _closeTimer.Dispose();
    }

    protected override bool ShowWithoutActivation => true;

    public static void ShowFor(IWin32Window owner, MasterSession session)
    {
        var notification = new MasterSignInNotification(session);
        var main = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
        var bounds = main is { IsDisposed: false }
            ? main.RectangleToScreen(main.ClientRectangle)
            : Screen.FromHandle(owner.Handle).WorkingArea;
        notification.Location = new Point(
            Math.Max(bounds.Left + 16, bounds.Right - notification.Width - 24),
            Math.Max(bounds.Top + 16, bounds.Bottom - notification.Height - 42));
        notification.Show(owner);
        _ = notification.Handle;
        notification._closeTimer.Start();
    }

    public static string RoleText(MasterUserRole role) => role == MasterUserRole.ReadOnly
        ? "Read-only"
        : role.ToString();
}
