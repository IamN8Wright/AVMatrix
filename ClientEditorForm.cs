namespace AVMatrixStudio;

internal sealed class ClientEditorForm : Form
{
    private readonly ClientRecord _client;
    private readonly TextBox _name = new();
    private readonly TextBox _address = new();
    private readonly TextBox _notes = new();
    private readonly PictureBox _logo = new();
    private readonly Label _logoPlaceholder = new();
    private string _logoBase64;

    public ClientEditorForm(ClientRecord client, bool isNew)
    {
        _client = client;
        _logoBase64 = client.LogoBase64;
        Text = isNew ? "Add client" : "Edit client";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 520);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = Text,
            AutoSize = true,
            Font = UiTheme.Font(19, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });
        heading.Controls.Add(new Label
        {
            Text = "Client identity shown on the welcome card",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Location = new Point(2, 38)
        });

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 8, 0, 0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.Controls.Add(BuildLogoPanel(), 0, 0);
        body.Controls.Add(BuildFieldsPanel(), 1, 0);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var save = UiTheme.PrimaryButton(isNew ? "Add client" : "Save client");
        save.DialogResult = DialogResult.OK;
        save.Click += Save_Click;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.AddRange([save, cancel]);

        shell.Controls.Add(heading, 0, 0);
        shell.Controls.Add(body, 0, 1);
        shell.Controls.Add(footer, 0, 2);
        Controls.Add(shell);
        AcceptButton = save;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
        Shown += (_, _) => _name.Focus();
        FormClosed += (_, _) => _logo.Image?.Dispose();
    }

    private Control BuildLogoPanel()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 22, 0),
            Padding = new Padding(16)
        };
        panel.Controls.Add(new Label
        {
            Text = "CLIENT LOGO",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8, FontStyle.Bold),
            Location = new Point(16, 15)
        });

        var preview = new Panel
        {
            BackColor = UiTheme.HeaderSurface,
            Location = new Point(16, 43),
            Size = new Size(176, 176)
        };
        _logo.Dock = DockStyle.Fill;
        _logo.SizeMode = PictureBoxSizeMode.Zoom;
        _logoPlaceholder.Dock = DockStyle.Fill;
        _logoPlaceholder.Text = Initials(_client.Name);
        _logoPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
        _logoPlaceholder.Font = UiTheme.Font(30, FontStyle.Bold);
        _logoPlaceholder.ForeColor = UiTheme.Blue;
        preview.Controls.Add(_logo);
        preview.Controls.Add(_logoPlaceholder);
        panel.Controls.Add(preview);

        var upload = UiTheme.SecondaryButton("Upload logo");
        upload.AutoSize = false;
        upload.Location = new Point(16, 237);
        upload.Size = new Size(176, 34);
        upload.Click += Upload_Click;
        panel.Controls.Add(upload);
        var remove = UiTheme.SecondaryButton("Remove logo");
        remove.AutoSize = false;
        remove.Location = new Point(16, 279);
        remove.Size = new Size(176, 34);
        remove.Click += (_, _) =>
        {
            _logoBase64 = string.Empty;
            SetLogoPreview(null);
        };
        panel.Controls.Add(remove);
        SetLogoPreview(ClientLogoImage.Decode(_logoBase64));
        return panel;
    }

    private Control BuildFieldsPanel()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _name.Text = _client.Name;
        _address.Text = _client.Address;
        _address.Multiline = true;
        _notes.Text = _client.Notes;
        _notes.Multiline = true;
        _notes.ScrollBars = ScrollBars.Vertical;
        table.Controls.Add(Field("Client name", _name), 0, 0);
        table.Controls.Add(Field("Address", _address), 0, 1);
        table.Controls.Add(Field("Notes", _notes), 0, 2);
        return table;
    }

    private static Control Field(string label, TextBox textBox)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.5f, FontStyle.Bold),
            Location = new Point(0, 0)
        });
        textBox.Dock = DockStyle.Bottom;
        textBox.Font = UiTheme.Font(10);
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Height = textBox.Multiline ? Math.Max(54, panel.Height - 25) : 31;
        panel.Controls.Add(textBox);
        return panel;
    }

    private void Upload_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose client logo",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _logoBase64 = ClientLogoImage.LoadAndEncode(dialog.FileName);
            SetLogoPreview(ClientLogoImage.Decode(_logoBase64));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The logo could not be loaded.\r\n\r\n{exception.Message}",
                "Client logo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetLogoPreview(Image? image)
    {
        var old = _logo.Image;
        _logo.Image = image;
        _logo.Visible = image is not null;
        _logoPlaceholder.Visible = image is null;
        old?.Dispose();
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show(this, "Enter a client name.", "Client name required",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
            _name.Focus();
            return;
        }
        _client.Name = _name.Text.Trim();
        _client.Address = _address.Text.Trim();
        _client.Notes = _notes.Text.Trim();
        _client.LogoBase64 = _logoBase64;
    }

    private static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "C",
            1 => words[0][..1].ToUpperInvariant(),
            _ => string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])))
        };
    }
}
