namespace AVMatrixStudio;

internal sealed class GitHubMasterStorageForm : Form
{
    private readonly AppData _data;
    private readonly DataStore _store;
    private readonly GitHubMasterStorageConfiguration _configuration;
    private readonly TextBox _owner = new();
    private readonly TextBox _repository = new();
    private readonly TextBox _branch = new();
    private readonly TextBox _companyId = new();
    private readonly TextBox _displayName = new();
    private readonly TextBox _token = new();
    private readonly Label _credentialState = new();
    private readonly Label _status = new();
    private readonly Button _test = UiTheme.SecondaryButton("Test private repository");
    private readonly Button _mirror = UiTheme.PrimaryButton("Mirror active Master now");
    private bool _busy;

    public GitHubMasterStorageForm(AppData data, DataStore store)
    {
        _data = data;
        _store = store;
        _configuration = GitHubMasterStorageConfigStore.Load(store);
        if (string.IsNullOrWhiteSpace(_configuration.CompanyId))
            _configuration.CompanyId = SafeCompanyId(data.ProjectName);
        if (string.IsNullOrWhiteSpace(_configuration.CompanyDisplayName))
            _configuration.CompanyDisplayName = data.ProjectName;

        Text = "GitHub Master Matrix storage";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 610);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(26, 20, 26, 18)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 245));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        shell.Controls.Add(BuildHeading(), 0, 0);
        shell.Controls.Add(BuildConnectionPanel(), 0, 1);
        shell.Controls.Add(BuildMirrorPanel(), 0, 2);
        shell.Controls.Add(BuildStatusPanel(), 0, 3);
        shell.Controls.Add(BuildFooter(), 0, 4);
        Controls.Add(shell);

        PopulateFields();
        RefreshCredentialState();
        UiTheme.ApplyTheme(this);
    }

    private Control BuildHeading()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "GitHub Master Matrix storage",
            AutoSize = true,
            Font = UiTheme.Font(19, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = "Keep an encrypted, company-separated mirror in a private GitHub repository.",
            AutoSize = true,
            Font = UiTheme.Font(9.5f),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 39)
        });
        return panel;
    }

    private Control BuildConnectionPanel()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(18)
        };
        card.Controls.Add(new Label
        {
            Text = "PRIVATE GITHUB REPOSITORY",
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(18, 14)
        });

        AddField(card, "Owner", _owner, 18, 42, 185);
        AddField(card, "Repository", _repository, 218, 42, 300);
        AddField(card, "Branch", _branch, 533, 42, 165);
        AddField(card, "Company folder ID", _companyId, 18, 104, 250);
        AddField(card, "Company display name", _displayName, 283, 104, 415);

        card.Controls.Add(new Label
        {
            Text = "Fine-grained access token",
            AutoSize = true,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(18, 166)
        });
        _token.UseSystemPasswordChar = true;
        _token.PlaceholderText = "Leave blank to keep the token already saved on this PC";
        _token.Location = new Point(18, 187);
        _token.Size = new Size(430, 29);
        card.Controls.Add(_token);

        _credentialState.AutoSize = true;
        _credentialState.Font = UiTheme.Font(8.5f, FontStyle.Bold);
        _credentialState.Location = new Point(462, 193);
        card.Controls.Add(_credentialState);
        return card;
    }

    private Control BuildMirrorPanel()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(18)
        };
        card.Controls.Add(new Label
        {
            Text = "MIRROR CURRENT MASTER",
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(18, 12)
        });
        card.Controls.Add(new Label
        {
            Text = "The currently signed-in Google Drive or Local / Network Master stays authoritative. " +
                   "This copies its exact encrypted master and available client payload files to GitHub.",
            AutoSize = false,
            Size = new Size(675, 36),
            Font = UiTheme.Font(8.7f),
            ForeColor = UiTheme.Text,
            Location = new Point(18, 33)
        });

        _test.AutoSize = false;
        _test.Size = new Size(178, 34);
        _test.Location = new Point(18, 70);
        _test.Click += async (_, _) => await TestConnectionAsync();
        card.Controls.Add(_test);

        _mirror.AutoSize = false;
        _mirror.Size = new Size(190, 34);
        _mirror.Location = new Point(208, 70);
        _mirror.Click += async (_, _) => await MirrorAsync();
        card.Controls.Add(_mirror);
        return card;
    }

    private Control BuildStatusPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "STATUS",
            AutoSize = true,
            Font = UiTheme.Font(8, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 4)
        });
        _status.AutoSize = false;
        _status.Location = new Point(2, 27);
        _status.Size = new Size(700, 82);
        _status.Font = UiTheme.Font(9);
        _status.ForeColor = UiTheme.Text;
        _status.Text = MasterSessionContext.Current is null
            ? "Repository settings can be configured now. Sign in to a Master Matrix before mirroring data."
            : "Ready to test the repository or mirror the active Master Matrix.";
        panel.Controls.Add(_status);
        return panel;
    }

    private Control BuildFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        var close = UiTheme.SecondaryButton("Close");
        close.AutoSize = false;
        close.Size = new Size(82, 34);
        close.Click += (_, _) => Close();
        var removeToken = UiTheme.DangerButton("Forget token");
        removeToken.AutoSize = false;
        removeToken.Size = new Size(104, 34);
        removeToken.Click += (_, _) => ForgetToken();
        var save = UiTheme.SecondaryButton("Save settings");
        save.AutoSize = false;
        save.Size = new Size(112, 34);
        save.Click += (_, _) => SaveConfiguration(showStatus: true);
        footer.Controls.Add(close);
        footer.Controls.Add(removeToken);
        footer.Controls.Add(save);
        return footer;
    }

    private static void AddField(
        Control parent,
        string label,
        TextBox textBox,
        int left,
        int top,
        int width)
    {
        parent.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(left, top)
        });
        textBox.Location = new Point(left, top + 21);
        textBox.Size = new Size(width, 29);
        parent.Controls.Add(textBox);
    }

    private void PopulateFields()
    {
        _owner.Text = _configuration.Owner;
        _repository.Text = _configuration.Repository;
        _branch.Text = _configuration.Branch;
        _companyId.Text = _configuration.CompanyId;
        _displayName.Text = _configuration.CompanyDisplayName;
    }

    private bool SaveConfiguration(bool showStatus)
    {
        if (_busy) return false;
        try
        {
            _configuration.Owner = Required(_owner, "GitHub owner");
            _configuration.Repository = Required(_repository, "GitHub repository");
            _configuration.Branch = Required(_branch, "GitHub branch");
            _configuration.CompanyId = Required(_companyId, "Company folder ID");
            _configuration.CompanyDisplayName = string.IsNullOrWhiteSpace(_displayName.Text)
                ? _configuration.CompanyId
                : _displayName.Text.Trim();
            var options = _configuration.ToOptions();

            if (!string.IsNullOrWhiteSpace(_token.Text))
            {
                GitHubMasterStorageService.SaveAccessToken(options, _token.Text);
                _token.Clear();
            }
            GitHubMasterStorageConfigStore.Save(_store, _configuration);
            RefreshCredentialState();
            if (showStatus)
                _status.Text = "GitHub Master Matrix storage settings saved on this PC. The access token is stored in Windows Credential Manager, not in AV Matrix data files.";
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                exception.Message,
                "GitHub Master Matrix storage",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (_busy || !SaveConfiguration(showStatus: false)) return;
        await RunBusyAsync(async () =>
        {
            var options = _configuration.ToOptions();
            await GitHubMasterStorageService.TestConnectionAsync(options);
            var companies = await GitHubMasterStorageService.ListCompanyIdsAsync(options);
            _status.Text = companies.Count == 0
                ? "Connected successfully. The repository is private and writable. No company Master folders exist yet."
                : $"Connected successfully. The repository is private. Existing company folders: {string.Join(", ", companies)}";
        }, "The GitHub storage connection could not be verified.");
    }

    private async Task MirrorAsync()
    {
        if (_busy || !SaveConfiguration(showStatus: false)) return;
        if (MasterSessionContext.Current is null)
        {
            MessageBox.Show(this,
                "Sign in to the Google Drive or Local / Network Master you want to mirror, then return here.",
                "No active Master Matrix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Mirror the currently active encrypted Master Matrix to the GitHub company folder\r\n\r\n" +
            $"companies/{_configuration.CompanyId}/\r\n\r\n" +
            "Existing files in that company folder will be replaced only through a revision-checked Git commit. Continue?",
            "Mirror Master Matrix to GitHub",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync(async () =>
        {
            _status.Text = "Reading the active Master Matrix and client payloads…";
            var result = await GitHubMasterMirrorService.MirrorActiveMasterAsync(
                _data,
                _configuration);
            _status.Text =
                $"GitHub mirror complete. Company: {result.CompanyId}. " +
                $"Client payloads copied: {result.ClientPayloadCount:N0}. " +
                $"Stored: {result.TotalBytes / (1024d * 1024d):N1} MB. " +
                $"Commit: {result.CommitSha[..Math.Min(12, result.CommitSha.Length)]}." +
                (result.CompanyCreated ? " A new company folder was created." : string.Empty);
        }, "The active Master Matrix could not be mirrored to GitHub.");
    }

    private void ForgetToken()
    {
        try
        {
            var options = _configuration.ToOptions();
            GitHubMasterStorageService.ClearAccessToken(options);
            _token.Clear();
            RefreshCredentialState();
            _status.Text = "The GitHub access token was removed from Windows Credential Manager.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                exception.Message,
                "Forget GitHub token",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void RefreshCredentialState()
    {
        try
        {
            var hasToken = GitHubMasterStorageService.HasAccessToken(_configuration.ToOptions());
            _credentialState.Text = hasToken ? "TOKEN SAVED ON THIS PC" : "TOKEN REQUIRED";
            _credentialState.ForeColor = hasToken ? UiTheme.Green : UiTheme.Amber;
        }
        catch
        {
            _credentialState.Text = "TOKEN REQUIRED";
            _credentialState.ForeColor = UiTheme.Amber;
        }
    }

    private async Task RunBusyAsync(Func<Task> action, string errorMessage)
    {
        if (_busy) return;
        _busy = true;
        _test.Enabled = false;
        _mirror.Enabled = false;
        UseWaitCursor = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"{errorMessage}\r\n\r\n{exception.Message}",
                "GitHub Master Matrix storage",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _busy = false;
            _test.Enabled = true;
            _mirror.Enabled = true;
            RefreshCredentialState();
        }
    }

    private static string Required(TextBox textBox, string label)
    {
        var value = textBox.Text.Trim();
        if (value.Length > 0) return value;
        textBox.Focus();
        throw new InvalidOperationException($"Enter the {label}.");
    }

    private static string SafeCompanyId(string value)
    {
        var text = new string((value ?? string.Empty)
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (text.Contains("--", StringComparison.Ordinal)) text = text.Replace("--", "-");
        text = text.Trim('-');
        return text.Length == 0 ? "company" : text;
    }
}
