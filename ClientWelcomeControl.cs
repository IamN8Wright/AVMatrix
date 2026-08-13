namespace InNasc;

internal sealed class ClientWelcomeControl : UserControl
{
    private readonly AppData _data;
    private readonly TextBox _search = new();
    private readonly FlowLayoutPanel _cards = new();

    public event Action<ClientRecord>? ClientSelected;
    public event Action<ClientRecord>? ClientEditRequested;
    public event Action<ClientRecord>? ClientExcelExportRequested;
    public event Action<ClientRecord>? ClientCheckoutRequested;
    public event Action? AddClientRequested;
    public event Action? DeleteClientRequested;

    public ClientWelcomeControl(AppData data)
    {
        _data = data;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Canvas;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(34, 28, 34, 26)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        var words = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        words.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        words.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        words.Controls.Add(new Label
        {
            Text = "Client Directory",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UiTheme.Font(23, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = Padding.Empty
        }, 0, 0);
        words.Controls.Add(new Label
        {
            Text = "Select a client card to open its equipment workspace.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = UiTheme.Font(10),
            ForeColor = UiTheme.Muted,
            Margin = Padding.Empty
        }, 0, 1);
        var headerActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 14, 0, 0)
        };
        var add = UiTheme.PrimaryButton("+ Add client");
        add.AutoSize = false;
        add.Size = new Size(110, 34);
        add.Margin = new Padding(8, 0, 0, 0);
        add.Click += (_, _) => AddClientRequested?.Invoke();
        var delete = UiTheme.DangerButton("Delete client");
        delete.AutoSize = false;
        delete.Size = new Size(112, 34);
        delete.Margin = Padding.Empty;
        delete.Click += (_, _) => DeleteClientRequested?.Invoke();
        headerActions.Controls.Add(add);
        headerActions.Controls.Add(delete);
        header.Controls.Add(new Panel(), 0, 0);
        header.Controls.Add(words, 1, 0);
        header.Controls.Add(headerActions, 2, 0);

        var searchPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 11, 14, 8),
            Margin = new Padding(0, 0, 0, 12)
        };
        _search.Dock = DockStyle.Fill;
        _search.BorderStyle = BorderStyle.None;
        _search.Font = UiTheme.Font(11);
        _search.PlaceholderText = "Search clients by name, address, or notes…";
        _search.TextChanged += (_, _) => RefreshClients();
        searchPanel.Controls.Add(_search);

        _cards.Dock = DockStyle.Fill;
        _cards.AutoScroll = true;
        _cards.WrapContents = true;
        _cards.BackColor = UiTheme.Canvas;
        _cards.Padding = new Padding(0, 8, 12, 18);

        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(searchPanel, 0, 1);
        shell.Controls.Add(_cards, 0, 2);
        Controls.Add(shell);
        RefreshClients();
    }

    public void RefreshClients()
    {
        DisposeImages(_cards);
        _cards.Controls.Clear();
        var term = _search.Text.Trim();
        var clients = _data.Clients
            .Where(CanViewClient)
            .Where(client => term.Length == 0 ||
                             string.Join('|', client.Name, client.Address, client.Notes)
                                 .Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(client => client.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var client in clients)
            _cards.Controls.Add(CreateCard(client));

        if (clients.Count == 0)
        {
            _cards.Controls.Add(new Label
            {
                Text = !_data.Clients.Any(CanViewClient)
                    ? _data.Clients.Count == 0
                        ? "No clients yet. Add your first client to begin."
                        : "No clients are assigned to this account."
                    : "No clients match that search.",
                AutoSize = true,
                Font = UiTheme.Font(12),
                ForeColor = UiTheme.Muted,
                Margin = new Padding(4, 34, 0, 0)
            });
        }
    }

    private Control CreateCard(ClientRecord client)
    {
        var card = new RoundedPanel
        {
            Size = new Size(318, 302),
            Margin = new Padding(0, 0, 18, 18),
            Padding = new Padding(18),
            Cursor = Cursors.Hand
        };
        var logoPanel = new Panel
        {
            BackColor = UiTheme.LogoTile,
            Location = new Point(18, 18),
            Size = new Size(62, 62),
            Cursor = Cursors.Hand
        };
        var image = ClientLogoImage.Decode(client.LogoBase64);
        if (image is not null)
        {
            logoPanel.Controls.Add(new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = image,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Hand
            });
        }
        else
        {
            logoPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = Initials(client.Name),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiTheme.Font(17, FontStyle.Bold),
                ForeColor = UiTheme.Blue,
                Cursor = Cursors.Hand
            });
        }
        card.Controls.Add(logoPanel);

        var name = new Label
        {
            Text = client.Name,
            AutoEllipsis = true,
            Font = UiTheme.Font(14, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(94, 20),
            Size = new Size(196, 28),
            Cursor = Cursors.Hand
        };
        var address = new Label
        {
            Text = string.IsNullOrWhiteSpace(client.Address) ? "No address entered" : client.Address,
            AutoEllipsis = true,
            Font = UiTheme.Font(9),
            ForeColor = string.IsNullOrWhiteSpace(client.Address) ? UiTheme.GrayLed : UiTheme.Muted,
            Location = new Point(94, 52),
            Size = new Size(196, 40),
            Cursor = Cursors.Hand
        };
        card.Controls.AddRange([name, address]);

        var locationCount = client.Locations.Count;
        var roomCount = client.Locations.Sum(location => location.Rooms.Count);
        var equipmentCount = client.Locations.Sum(location => location.Rooms.Sum(room => room.Equipment.Count));
        card.Controls.Add(new Label
        {
            Text = $"{locationCount} location{Plural(locationCount)}   •   {roomCount} room{Plural(roomCount)}",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.7f),
            Location = new Point(18, 113),
            Cursor = Cursors.Hand
        });
        card.Controls.Add(new Label
        {
            Text = $"{equipmentCount:N0}",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Font(22, FontStyle.Bold),
            Location = new Point(16, 137),
            Cursor = Cursors.Hand
        });
        card.Controls.Add(new Label
        {
            Text = "EQUIPMENT RECORDS",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8, FontStyle.Bold),
            Location = new Point(86, 153),
            Cursor = Cursors.Hand
        });

        var open = UiTheme.PrimaryButton("Open client");
        open.AutoSize = false;
        open.Size = new Size(108, 34);
        open.Location = new Point(192, 179);
        open.Click += (_, _) => ClientSelected?.Invoke(client);
        card.Controls.Add(open);
        var edit = UiTheme.SecondaryButton("Edit");
        edit.AutoSize = false;
        edit.Size = new Size(70, 34);
        edit.Location = new Point(114, 179);
        edit.Padding = new Padding(4, 0, 4, 0);
        edit.TextAlign = ContentAlignment.MiddleCenter;
        edit.Click += (_, _) => ClientEditRequested?.Invoke(client);
        card.Controls.Add(edit);

        var export = UiTheme.SecondaryButton("Excel");
        export.AutoSize = false;
        export.Size = new Size(88, 34);
        export.Location = new Point(18, 179);
        export.Click += (_, _) => ClientExcelExportRequested?.Invoke(client);
        card.Controls.Add(export);

        var checkoutRecord = _data.MasterAccess.Checkouts
            .FirstOrDefault(item => item.ClientId == client.Id);
        var activeHere = _data.Settings.ActiveCheckoutClientId == client.Id &&
                         checkoutRecord?.CheckoutToken == _data.Settings.ActiveCheckoutToken;
        var canCheckout = MasterSessionContext.Current?.Session.CanWrite == true;
        var checkoutStatus = new Label
        {
            AutoSize = false,
            Size = new Size(282, 20),
            Location = new Point(18, 218),
            Font = UiTheme.Font(8.3f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        };
        if (activeHere)
        {
            checkoutStatus.Text = "●  CHECKED OUT TO YOU";
            checkoutStatus.ForeColor = UiTheme.Blue;
        }
        else if (checkoutRecord is null)
        {
            checkoutStatus.Text = "●  CHECKED IN — AVAILABLE";
            checkoutStatus.ForeColor = UiTheme.Green;
        }
        else
        {
            var holder = string.IsNullOrWhiteSpace(checkoutRecord.DisplayName)
                ? checkoutRecord.Username
                : checkoutRecord.DisplayName;
            checkoutStatus.Text = $"●  CHECKED OUT BY {holder.ToUpperInvariant()}";
            checkoutStatus.ForeColor = UiTheme.Amber;
        }
        card.Controls.Add(checkoutStatus);

        var checkout = activeHere
            ? UiTheme.PrimaryButton("Check in & push")
            : checkoutRecord is null
                ? UiTheme.SecondaryButton("Check out client")
                : UiTheme.SecondaryButton("Checked out — take over");
        checkout.AutoSize = false;
        checkout.Size = new Size(282, 34);
        checkout.Location = new Point(18, 247);
        checkout.Enabled = canCheckout;
        checkout.Click += (_, _) => ClientCheckoutRequested?.Invoke(client);
        if (checkoutRecord is not null && !activeHere)
        {
            var holder = string.IsNullOrWhiteSpace(checkoutRecord.DisplayName)
                ? checkoutRecord.Username
                : checkoutRecord.DisplayName;
            checkout.Text = $"Checked out by {holder} — take over";
        }
        card.Controls.Add(checkout);

        AttachOpenHandler(card, client, [open, edit, export, checkout]);
        return card;
    }

    private bool CanViewClient(ClientRecord client)
    {
        var session = MasterSessionContext.Current?.Session;
        return session is not null &&
               MasterAccessService.CanAccessClient(_data.MasterAccess, session, client.Id);
    }

    private void AttachOpenHandler(Control control, ClientRecord client, IReadOnlyCollection<Control> exclusions)
    {
        if (!exclusions.Contains(control))
            control.Click += (_, _) => ClientSelected?.Invoke(client);
        foreach (Control child in control.Controls)
            AttachOpenHandler(child, client, exclusions);
    }

    private static void DisposeImages(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is PictureBox { Image: { } image })
            {
                ((PictureBox)child).Image = null;
                image.Dispose();
            }
            else
            {
                DisposeImages(child);
            }
        }
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

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
