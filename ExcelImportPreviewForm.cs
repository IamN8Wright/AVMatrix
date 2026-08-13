namespace AVMatrixStudio;

internal sealed class ExcelImportPreviewForm : Form
{
    private readonly ExcelImportPlan _plan;
    private readonly IReadOnlyList<ExcelWorkbookImportScan> _scans;
    private readonly DataGridView _grid = new();

    public ExcelImportPreviewForm(
        ExcelImportPlan plan,
        IReadOnlyList<ExcelWorkbookImportScan> scans)
    {
        _plan = plan;
        _scans = scans;

        var skippedRows = scans.Sum(scan => scan.SkippedRows.Count);
        var candidateRows = scans.Sum(scan => scan.CandidateRowsScanned);
        if (candidateRows != plan.ImportedRows + skippedRows)
            throw new InvalidDataException(
                "The Excel preview could not reconcile every scanned row. The import was canceled.");

        Text = "Review Excel import";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 620);
        Size = new Size(1320, 780);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        shell.Controls.Add(BuildHeading(candidateRows, skippedRows), 0, 0);
        shell.Controls.Add(BuildSummary(), 0, 1);
        ConfigureGrid();
        shell.Controls.Add(_grid, 0, 2);
        shell.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(shell);

        PopulateGrid();
        UiTheme.ApplyTheme(this);
    }

    private Control BuildHeading(int candidateRows, int skippedRows)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "Review before importing",
            AutoSize = true,
            Font = UiTheme.Font(19, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Location = new Point(0, 0)
        });

        var unrecognizedRows = _scans.Sum(scan => scan.UnrecognizedSheetRows);
        var note = $"{candidateRows:N0} non-empty data row(s) were reconciled: " +
                   $"{_plan.ImportedRows:N0} parsed + {skippedRows:N0} skipped.";
        if (unrecognizedRows > 0)
            note += $" Another {unrecognizedRows:N0} non-empty row(s) are on sheets with no recognized equipment header.";
        panel.Controls.Add(new Label
        {
            Text = note,
            AutoSize = false,
            AutoEllipsis = true,
            Size = new Size(1220, 42),
            Font = UiTheme.Font(9.5f),
            ForeColor = unrecognizedRows > 0 ? UiTheme.Amber : UiTheme.Muted,
            Location = new Point(2, 34)
        });
        return panel;
    }

    private Control BuildSummary()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 12)
        };
        for (var index = 0; index < 7; index++)
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7f));

        var totalSheets = _scans.Sum(scan => scan.WorksheetCount);
        var recognizedSheets = _scans.Sum(scan => scan.RecognizedWorksheetCount);
        AddSummary(summary, 0, "FILES", _scans.Count, UiTheme.Blue);
        AddSummary(summary, 1, "SHEETS READ", recognizedSheets, UiTheme.Blue,
            $"{recognizedSheets:N0} of {totalSheets:N0}");
        AddSummary(summary, 2, "NEW", _plan.AddedDevices, UiTheme.Green);
        AddSummary(summary, 3, "MERGES", _plan.MergedDevices, UiTheme.Blue);
        AddSummary(summary, 4, "DUPLICATES", _plan.UnchangedDuplicates, UiTheme.Muted);
        AddSummary(summary, 5, "AMBIGUOUS", _plan.AmbiguousRows, UiTheme.Amber);
        AddSummary(summary, 6, "NOT IMPORTED",
            _scans.Sum(scan => scan.SkippedRows.Count) +
            _scans.Sum(scan => scan.UnrecognizedSheetRows),
            UiTheme.Red);
        return summary;
    }

    private static void AddSummary(
        TableLayoutPanel parent,
        int column,
        string title,
        int count,
        Color color,
        string? countText = null)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Margin = new Padding(column == 0 ? 0 : 5, 0, column == 6 ? 0 : 5, 0),
            Padding = new Padding(12, 9, 12, 8)
        };
        card.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = UiTheme.Font(7.8f, FontStyle.Bold),
            ForeColor = UiTheme.Muted,
            Location = new Point(12, 9)
        });
        card.Controls.Add(new Label
        {
            Text = countText ?? count.ToString("N0"),
            AutoSize = true,
            Font = UiTheme.Font(15, FontStyle.Bold),
            ForeColor = color,
            Location = new Point(12, 31)
        });
        parent.Controls.Add(card, column, 0);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.BackgroundColor = UiTheme.Surface;
        _grid.ColumnHeadersHeight = 40;
        _grid.RowTemplate.Height = 42;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Action",
            HeaderText = "ACTION",
            Width = 104,
            Frozen = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Source",
            HeaderText = "WORKBOOK",
            Width = 166
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Sheet",
            HeaderText = "SHEET",
            Width = 130
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Row",
            HeaderText = "ROW",
            Width = 58
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Device",
            HeaderText = "SPREADSHEET ITEM",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 23
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Match",
            HeaderText = "EXISTING MATCH / TARGET",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 27
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Details",
            HeaderText = "WHAT WILL HAPPEN",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 50
        });
    }

    private void PopulateGrid()
    {
        var rows = new List<PreviewDisplayRow>();
        rows.AddRange(_plan.Entries.Select(entry => new PreviewDisplayRow(
            entry.ActionLabel,
            entry.ImportedRow.SourceFile,
            entry.ImportedRow.Worksheet,
            entry.ImportedRow.RowNumber,
            entry.DeviceName,
            string.IsNullOrWhiteSpace(entry.ExistingMatch) ? entry.Target : entry.ExistingMatch,
            entry.Details,
            ActionColor(entry.Action))));
        rows.AddRange(_scans.SelectMany(scan => scan.SkippedRows).Select(item =>
            new PreviewDisplayRow(
                "SKIPPED",
                item.SourceFile,
                item.Worksheet,
                item.RowNumber,
                item.RowPreview,
                string.Empty,
                item.Reason,
                UiTheme.Red)));
        rows.AddRange(_scans.SelectMany(scan => scan.SheetIssues).Select(item =>
            new PreviewDisplayRow(
                "SHEET SKIPPED",
                item.SourceFile,
                item.Worksheet,
                null,
                $"{item.NonEmptyRows:N0} non-empty row(s)",
                string.Empty,
                item.Reason,
                UiTheme.Red)));

        foreach (var item in rows
                     .OrderBy(item => item.Source, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(item => item.Sheet, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(item => item.RowNumber ?? int.MaxValue))
        {
            var rowIndex = _grid.Rows.Add(
                item.Action,
                item.Source,
                item.Sheet,
                item.RowNumber?.ToString() ?? "—",
                item.Device,
                item.Match,
                item.Details);
            var row = _grid.Rows[rowIndex];
            row.Cells["Action"].Style.ForeColor = item.Color;
            row.Cells["Action"].Style.Font = UiTheme.Font(8.5f, FontStyle.Bold);
            row.Cells["Details"].ToolTipText = item.Details;
            row.Cells["Device"].ToolTipText = item.Device;
            row.Cells["Match"].ToolTipText = item.Match;
        }
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(new Label
        {
            Text = "Nothing has been changed yet. Existing values always take priority; merges fill blank fields only. " +
                   "Duplicates, ambiguous matches, and skipped rows will not be imported.",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Font(8.8f),
            Margin = new Padding(0, 0, 14, 0)
        }, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var import = UiTheme.PrimaryButton(
            _plan.ActionableRows == 1
                ? "Import 1 reviewed item"
                : $"Import {_plan.ActionableRows:N0} reviewed items");
        import.AutoSize = false;
        import.Size = new Size(208, 38);
        import.Enabled = _plan.ActionableRows > 0;
        import.DialogResult = DialogResult.OK;
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(94, 38);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(import);
        buttons.Controls.Add(cancel);
        footer.Controls.Add(buttons, 1, 0);
        AcceptButton = import;
        CancelButton = cancel;
        return footer;
    }

    private static Color ActionColor(ExcelImportAction action) => action switch
    {
        ExcelImportAction.AddNew => UiTheme.Green,
        ExcelImportAction.Merge => UiTheme.Blue,
        ExcelImportAction.UnchangedDuplicate => UiTheme.Muted,
        ExcelImportAction.Ambiguous => UiTheme.Amber,
        _ => UiTheme.Text
    };

    private sealed record PreviewDisplayRow(
        string Action,
        string Source,
        string Sheet,
        int? RowNumber,
        string Device,
        string Match,
        string Details,
        Color Color);
}
