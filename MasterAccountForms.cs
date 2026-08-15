namespace InNasc;

internal sealed record MasterOwnerSetupResult(
    MasterAccessControl Access,
    MasterSession Session);

internal sealed class MasterSignInForm : Form
{
    private readonly MasterAccessControl _access;
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly Label _error = new();

    public MasterSession? Session { get; private set; }

    private MasterSignInForm(MasterAccessControl access)
    {
        _access = access;
        Text = "Sign in to company workspace";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 330);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(Heading("company workspace sign in", 26, 22));
        Controls.Add(Description(
            "Use the account created by the company Owner. Your role controls read, write, checkout, and account-management access.",
            28, 61, 438, 44));
        Controls.Add(FieldLabel("Username", 28, 116));
        ConfigureBox(_username, 28, 138);
        Controls.Add(_username);
        Controls.Add(FieldLabel("Password", 28, 180));
        ConfigureBox(_password, 28, 202);
        _password.UseSystemPasswordChar = true;
        Controls.Add(_password);
        _error.Location = new Point(28, 239);
        _error.Size = new Size(438, 24);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);

        var signIn = UiTheme.PrimaryButton("Sign in");
        signIn.Size = new Size(102, 36);
        signIn.AutoSize = false;
        signIn.Location = new Point(364, 276);
        signIn.Click += (_, _) => SignIn();
        Controls.Add(signIn);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.Size = new Size(88, 36);
        cancel.AutoSize = false;
        cancel.Location = new Point(266, 276);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        AcceptButton = signIn;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
        Shown += (_, _) => _username.Focus();
    }

    public static MasterSession? Prompt(IWin32Window owner, MasterAccessControl access)
    {
        using var dialog = new MasterSignInForm(access);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Session : null;
    }

    private void SignIn()
    {
        try
        {
            Session = MasterAccessService.SignIn(_access, _username.Text, _password.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
            _password.SelectAll();
            _password.Focus();
        }
    }

    internal static Label Heading(string text, int left, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(18, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        Location = new Point(left, top)
    };

    internal static Label Description(string text, int left, int top, int width, int height) => new()
    {
        Text = text,
        AutoSize = false,
        Font = UiTheme.Font(9.2f),
        ForeColor = UiTheme.Muted,
        Location = new Point(left, top),
        Size = new Size(width, height)
    };

    internal static Label FieldLabel(string text, int left, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTheme.Font(8.5f, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        Location = new Point(left, top)
    };

    internal static void ConfigureBox(TextBox box, int left, int top, int width = 438)
    {
        box.Location = new Point(left, top);
        box.Size = new Size(width, 29);
        box.Font = UiTheme.Font(10);
    }
}

internal sealed class MasterOwnerSetupForm : Form
{
    private readonly TextBox _username = new() { Text = "owner" };
    private readonly TextBox _displayName = new();
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly Label _error = new();

    public MasterOwnerSetupResult? Result { get; private set; }

    private MasterOwnerSetupForm()
    {
        Text = "Set up company Owner";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(530, 485);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        Controls.Add(MasterSignInForm.Heading("Create the first Owner", 26, 22));
        Controls.Add(MasterSignInForm.Description(
            "The Owner has full access and can create Tech and Read-only accounts. Each user's password securely unlocks the same encrypted company workspace; passwords are never stored as readable text.",
            28, 61, 468, 48));
        AddField("Username", _username, 116);
        AddField("Display name", _displayName, 180);
        AddField("Password", _password, 244, password: true);
        AddField("Confirm password", _confirm, 308, password: true);
        _error.Location = new Point(28, 370);
        _error.Size = new Size(468, 42);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);

        var create = UiTheme.PrimaryButton("Create Owner");
        create.Size = new Size(132, 36);
        create.AutoSize = false;
        create.Location = new Point(364, 427);
        create.Click += (_, _) => CreateOwner();
        Controls.Add(create);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.Size = new Size(88, 36);
        cancel.AutoSize = false;
        cancel.Location = new Point(266, 427);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        AcceptButton = create;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    public static MasterOwnerSetupResult? Prompt(IWin32Window owner)
    {
        using var dialog = new MasterOwnerSetupForm();
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    private void AddField(string label, TextBox box, int top, bool password = false)
    {
        Controls.Add(MasterSignInForm.FieldLabel(label, 28, top));
        MasterSignInForm.ConfigureBox(box, 28, top + 22, 468);
        box.UseSystemPasswordChar = password;
        Controls.Add(box);
    }

    private void CreateOwner()
    {
        try
        {
            if (_password.Text != _confirm.Text)
                throw new InvalidOperationException("The passwords do not match.");
            var access = new MasterAccessControl();
            var user = MasterAccessService.CreateInitialOwner(
                access, _username.Text, _displayName.Text, _password.Text);
            Result = new MasterOwnerSetupResult(
                access,
                MasterAccessService.SignIn(access, user.Username, _password.Text));
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }
}

internal sealed class MasterUserManagementForm : Form
{
    private readonly MasterSession _session;
    private readonly IReadOnlyList<ClientRecord> _clients;
    private readonly ListView _users = new();

    public MasterAccessControl ResultAccess { get; }

    public MasterUserManagementForm(
        MasterAccessControl access,
        MasterSession session,
        IReadOnlyCollection<ClientRecord> clients)
    {
        _session = session;
        _clients = clients.OrderBy(client => client.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        ResultAccess = MasterAccessService.Clone(access);
        Text = "Master accounts";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 530);
        Size = new Size(1080, 600);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(MasterSignInForm.Heading("Master accounts", 0, 0));
        heading.Controls.Add(MasterSignInForm.Description(
            session.IsOwner
                ? "Owners manage logins. Every signed-in user can see who has access and change their own password."
                : "Everyone with access is listed below. You can change your own password; only an Owner can add or edit accounts.",
            2, 38, 960, 34));
        shell.Controls.Add(heading, 0, 0);

        _users.Dock = DockStyle.Fill;
        _users.View = View.Details;
        _users.FullRowSelect = true;
        _users.MultiSelect = false;
        _users.HideSelection = false;
        _users.Columns.Add("Username", 175);
        _users.Columns.Add("Display name", 230);
        _users.Columns.Add("Role", 120);
        _users.Columns.Add("Client access", 185);
        _users.Columns.Add("Status", 110);
        shell.Controls.Add(_users, 0, 1);
        shell.Controls.Add(BuildActions(), 0, 2);
        Controls.Add(shell);
        RefreshUsers();
        UiTheme.ApplyTheme(this);
    }

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };
        var add = UiTheme.PrimaryButton("＋ Add account");
        add.Click += (_, _) => AddAccount();
        var edit = UiTheme.SecondaryButton("Edit");
        edit.Click += (_, _) => EditAccount();
        var clientAccess = UiTheme.SecondaryButton("Client access…");
        clientAccess.Click += (_, _) => EditClientAccess();
        var reset = UiTheme.SecondaryButton("Reset password");
        reset.Click += (_, _) => ResetPassword();
        var delete = UiTheme.DangerButton("Delete");
        delete.Click += (_, _) => DeleteAccount();
        var changeMine = UiTheme.SecondaryButton("Change my password");
        changeMine.Click += (_, _) => ChangeMyPassword();
        add.Visible = _session.IsOwner;
        edit.Visible = _session.IsOwner;
        clientAccess.Visible = _session.IsOwner;
        reset.Visible = _session.IsOwner;
        delete.Visible = _session.IsOwner;
        var spacer = new Panel { Width = _session.IsOwner ? 20 : 240, Height = 1 };
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var save = UiTheme.PrimaryButton("Save accounts");
        save.DialogResult = DialogResult.OK;
        panel.Controls.AddRange(
            [add, edit, clientAccess, reset, delete, changeMine, spacer, cancel, save]);
        return panel;
    }

    private void EditClientAccess()
    {
        if (SelectedUser() is not { } user) return;
        if (user.Role == MasterUserRole.Owner)
        {
            MessageBox.Show(this,
                "Owners always have access to every current and future client.",
                "Client access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var editor = new MasterClientAccessForm(user, _clients);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var selectedIds = editor.SelectedClientIds;
            var releasedCheckouts = editor.HasAllClientAccess
                ? []
                : ResultAccess.Checkouts
                    .Where(checkout => checkout.UserId == user.Id &&
                        !selectedIds.Contains(checkout.ClientId))
                    .ToList();
            if (releasedCheckouts.Count > 0 &&
                MessageBox.Show(this,
                    $"Removing this access will also release {releasedCheckouts.Count:N0} client checkout(s) held by {user.DisplayName}. " +
                    "Ask the technician whether their work has been pushed; unpushed work will remain only on their PC.\r\n\r\nContinue?",
                    "Release client checkout",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            MasterAccessService.UpdateClientAccess(
                ResultAccess,
                _session,
                user.Id,
                editor.HasAllClientAccess,
                selectedIds);
            RefreshUsers(user.Id);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void ChangeMyPassword()
    {
        var passwords = MasterOwnPasswordForm.Prompt(this, _session.Username);
        if (passwords is null) return;
        try
        {
            MasterAccessService.ChangeOwnPassword(
                ResultAccess,
                _session,
                passwords.Value.CurrentPassword,
                passwords.Value.NewPassword);
            MessageBox.Show(this,
                "Your password will be updated when you click Save accounts.",
                "Company access",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void AddAccount()
    {
        using var editor = new MasterUserEditorForm();
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var user = MasterAccessService.AddUser(
                ResultAccess,
                _session,
                editor.Username,
                editor.DisplayName,
                editor.Password!,
                editor.Role);
            RefreshUsers(user.Id);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void EditAccount()
    {
        if (SelectedUser() is not { } user) return;
        using var editor = new MasterUserEditorForm(user);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var checkoutCount = ResultAccess.Checkouts.Count(checkout => checkout.UserId == user.Id);
            if (checkoutCount > 0 &&
                (!editor.AccountEnabled || editor.Role == MasterUserRole.ReadOnly) &&
                MessageBox.Show(this,
                    $"This account holds {checkoutCount:N0} client checkout(s). Making it read-only or disabled will release those locks. " +
                    "Ask the technician whether their work has been pushed; unpushed work will remain only on their PC.\r\n\r\nContinue?",
                    "Release client checkout",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            MasterAccessService.UpdateUser(
                ResultAccess,
                _session,
                user.Id,
                editor.DisplayName,
                editor.Role,
                editor.AccountEnabled);
            RefreshUsers(user.Id);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void ResetPassword()
    {
        if (SelectedUser() is not { } user) return;
        var password = MasterPasswordResetForm.Prompt(this, user.Username);
        if (password is null) return;
        try
        {
            MasterAccessService.ResetPassword(ResultAccess, _session, user.Id, password);
            MessageBox.Show(this, "The account password was reset.", "Master accounts",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void DeleteAccount()
    {
        if (SelectedUser() is not { } user) return;
        if (MessageBox.Show(this,
                $"Delete the account '{user.Username}'? Any client checkout held by it will be released.",
                "Delete master account",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            MasterAccessService.DeleteUser(ResultAccess, _session, user.Id);
            RefreshUsers();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private MasterUserRecord? SelectedUser() =>
        _users.SelectedItems.Count == 1
            ? (MasterUserRecord)_users.SelectedItems[0].Tag!
            : null;

    private void RefreshUsers(Guid? selectId = null)
    {
        _users.Items.Clear();
        foreach (var user in ResultAccess.Users.Where(user => !user.IsRecoveryAccount).OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(user.Username) { Tag = user };
            item.SubItems.Add(user.DisplayName);
            item.SubItems.Add(user.Role == MasterUserRole.ReadOnly ? "Read-only" : user.Role.ToString());
            item.SubItems.Add(ClientAccessText(user));
            item.SubItems.Add(user.Enabled ? "Enabled" : "Disabled");
            _users.Items.Add(item);
            if (user.Id == selectId) item.Selected = true;
        }
    }

    private string ClientAccessText(MasterUserRecord user)
    {
        if (user.Role == MasterUserRole.Owner) return "All clients (Owner)";
        if (user.HasAllClientAccess) return "All current & future";
        var availableIds = _clients.Select(client => client.Id).ToHashSet();
        var count = user.ClientAccessIds.Count(availableIds.Contains);
        return $"{count:N0} of {_clients.Count:N0} clients";
    }

    private void ShowError(Exception exception) => MessageBox.Show(this, exception.Message,
        "Master accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

internal sealed class MasterClientAccessForm : Form
{
    private readonly CheckBox _allClients = new()
    {
        Text = "Access to all current and future clients",
        AutoSize = true
    };
    private readonly CheckedListBox _clients = new();

    public bool HasAllClientAccess => _allClients.Checked;
    public IReadOnlyList<Guid> SelectedClientIds => _clients.CheckedItems
        .Cast<ClientAccessChoice>()
        .Select(choice => choice.Id)
        .ToList();

    public MasterClientAccessForm(
        MasterUserRecord user,
        IReadOnlyCollection<ClientRecord> clients)
    {
        Text = $"Client access — {user.Username}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 520);
        Size = new Size(640, 650);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24, 20, 24, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(MasterSignInForm.Heading("Client access", 0, 0));
        heading.Controls.Add(MasterSignInForm.Description(
            $"Choose which clients {user.DisplayName} can view. Their {MasterSignInNotification.RoleText(user.Role)} role still controls whether that access is read/write or read-only.",
            2, 38, 560, 34));
        shell.Controls.Add(heading, 0, 0);

        _allClients.Checked = user.HasAllClientAccess;
        _allClients.Margin = new Padding(3, 12, 3, 6);
        _allClients.CheckedChanged += (_, _) => RefreshEnabledState();
        shell.Controls.Add(_allClients, 0, 1);

        _clients.Dock = DockStyle.Fill;
        _clients.CheckOnClick = true;
        _clients.IntegralHeight = false;
        _clients.Font = UiTheme.Font(10);
        var selectedIds = user.ClientAccessIds.ToHashSet();
        foreach (var client in clients.OrderBy(
                     client => client.Name,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            var choice = new ClientAccessChoice(client.Id, client.Name);
            _clients.Items.Add(choice, selectedIds.Contains(client.Id));
        }
        shell.Controls.Add(_clients, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var save = UiTheme.PrimaryButton("Save client access");
        save.DialogResult = DialogResult.OK;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        shell.Controls.Add(actions, 0, 3);

        Controls.Add(shell);
        AcceptButton = save;
        CancelButton = cancel;
        RefreshEnabledState();
        UiTheme.ApplyTheme(this);
    }

    private void RefreshEnabledState()
    {
        _clients.Enabled = !_allClients.Checked;
        if (_allClients.Checked)
            _clients.ClearSelected();
    }

    private sealed record ClientAccessChoice(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
}

internal sealed class MasterOwnPasswordForm : Form
{
    private readonly TextBox _current = new();
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly Label _error = new();
    private (string CurrentPassword, string NewPassword)? _result;

    private MasterOwnPasswordForm(string username)
    {
        Text = "Change my password";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 410);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(MasterSignInForm.Heading($"Change {username}'s password", 26, 20));
        AddField("Current password", _current, 76);
        AddField("New password", _password, 142);
        AddField("Confirm new password", _confirm, 208);
        _error.Location = new Point(28, 273);
        _error.Size = new Size(438, 42);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var save = UiTheme.PrimaryButton("Change password");
        save.AutoSize = false;
        save.Size = new Size(142, 36);
        save.Location = new Point(324, 356);
        save.Click += (_, _) => Save();
        Controls.Add(save);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(226, 356);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    public static (string CurrentPassword, string NewPassword)? Prompt(
        IWin32Window owner,
        string username)
    {
        using var dialog = new MasterOwnPasswordForm(username);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._result : null;
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(MasterSignInForm.FieldLabel(label, 28, top));
        MasterSignInForm.ConfigureBox(box, 28, top + 22);
        box.UseSystemPasswordChar = true;
        Controls.Add(box);
    }

    private void Save()
    {
        if (_password.Text.Length < 10)
        {
            _error.Text = "Use a password of at least 10 characters.";
            return;
        }
        if (_password.Text != _confirm.Text)
        {
            _error.Text = "The new passwords do not match.";
            return;
        }
        _result = (_current.Text, _password.Text);
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class MasterUserEditorForm : Form
{
    private readonly bool _creating;
    private readonly TextBox _username = new();
    private readonly TextBox _displayName = new();
    private readonly ComboBox _role = new();
    private readonly CheckBox _enabled = new() { Text = "Account enabled", Checked = true, AutoSize = true };
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly Label _error = new();

    public string Username => _username.Text;
    public string DisplayName => _displayName.Text;
    public MasterUserRole Role => _role.SelectedItem is MasterUserRole role ? role : MasterUserRole.Tech;
    public bool AccountEnabled => _enabled.Checked;
    public string? Password => _creating ? _password.Text : null;

    public MasterUserEditorForm(MasterUserRecord? user = null)
    {
        _creating = user is null;
        Text = _creating ? "Add master account" : "Edit master account";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, _creating ? 500 : 385);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(MasterSignInForm.Heading(Text, 26, 20));
        AddField("Username", _username, 72);
        AddField("Display name", _displayName, 136);
        Controls.Add(MasterSignInForm.FieldLabel("Role", 28, 200));
        _role.Location = new Point(28, 222);
        _role.Size = new Size(210, 29);
        _role.DropDownStyle = ComboBoxStyle.DropDownList;
        _role.Items.AddRange(Enum.GetValues<MasterUserRole>().Cast<object>().ToArray());
        _role.SelectedItem = user?.Role ?? MasterUserRole.Tech;
        UiTheme.ConfigureUniformComboBox(_role);
        Controls.Add(_role);
        _enabled.Location = new Point(270, 226);
        _enabled.Checked = user?.Enabled ?? true;
        Controls.Add(_enabled);
        var top = 270;
        if (_creating)
        {
            AddField("Password", _password, top, true);
            AddField("Confirm password", _confirm, top + 64, true);
            top += 132;
        }
        _username.Text = user?.Username ?? string.Empty;
        _username.ReadOnly = !_creating;
        _displayName.Text = user?.DisplayName ?? string.Empty;
        _error.Location = new Point(28, top);
        _error.Size = new Size(438, 34);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var save = UiTheme.PrimaryButton(_creating ? "Add account" : "Save changes");
        save.AutoSize = false;
        save.Size = new Size(125, 36);
        save.Location = new Point(341, ClientSize.Height - 54);
        save.Click += (_, _) => Save();
        Controls.Add(save);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(243, ClientSize.Height - 54);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    private void AddField(string label, TextBox box, int top, bool password = false)
    {
        Controls.Add(MasterSignInForm.FieldLabel(label, 28, top));
        MasterSignInForm.ConfigureBox(box, 28, top + 22);
        box.UseSystemPasswordChar = password;
        Controls.Add(box);
    }

    private void Save()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_displayName.Text))
                throw new InvalidOperationException("Enter a display name.");
            if (_creating)
            {
                if (_password.Text != _confirm.Text)
                    throw new InvalidOperationException("The passwords do not match.");
                var temporary = new MasterAccessControl();
                MasterAccessService.CreateInitialOwner(
                    temporary, _username.Text, _displayName.Text, _password.Text);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _error.Text = exception.Message;
        }
    }
}

internal sealed class MasterPasswordResetForm : Form
{
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    private readonly Label _error = new();
    private string? _result;

    private MasterPasswordResetForm(string username)
    {
        Text = "Reset master password";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 330);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        Controls.Add(MasterSignInForm.Heading($"Reset {username}", 26, 20));
        AddField("New password", _password, 78);
        AddField("Confirm password", _confirm, 144);
        _error.Location = new Point(28, 210);
        _error.Size = new Size(438, 30);
        _error.ForeColor = UiTheme.Red;
        Controls.Add(_error);
        var save = UiTheme.PrimaryButton("Reset password");
        save.AutoSize = false;
        save.Size = new Size(140, 36);
        save.Location = new Point(326, 276);
        save.Click += (_, _) => Save();
        Controls.Add(save);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(88, 36);
        cancel.Location = new Point(228, 276);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }

    public static string? Prompt(IWin32Window owner, string username)
    {
        using var dialog = new MasterPasswordResetForm(username);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._result : null;
    }

    private void AddField(string label, TextBox box, int top)
    {
        Controls.Add(MasterSignInForm.FieldLabel(label, 28, top));
        MasterSignInForm.ConfigureBox(box, 28, top + 22);
        box.UseSystemPasswordChar = true;
        Controls.Add(box);
    }

    private void Save()
    {
        if (_password.Text.Length < 10)
        {
            _error.Text = "Use a password of at least 10 characters.";
            return;
        }
        if (_password.Text != _confirm.Text)
        {
            _error.Text = "The passwords do not match.";
            return;
        }
        _result = _password.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}
