namespace InNasc;

internal sealed class MergeConflictForm : Form
{
    public MergeConflictPreference? Preference { get; private set; }

    public MergeConflictForm(IReadOnlyList<SyncMergeConflict> conflicts)
    {
        Text = "Resolve merge conflicts";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        Size = new Size(1040, 650);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(26, 22, 26, 20)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        shell.Controls.Add(new Label
        {
            Text = "A few changes overlap",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(20, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        shell.Controls.Add(new Label
        {
            Text = $"{conflicts.Count:N0} field(s) or record(s) were changed differently on this PC and in the master. " +
                   "Independent changes have already been combined. Review the differences below, then choose " +
                   "which side wins only for these overlaps.",
            Dock = DockStyle.Fill,
            Font = UiTheme.Font(10),
            ForeColor = UiTheme.Muted,
            AutoEllipsis = true
        }, 0, 1);

        var grid = BuildGrid();
        foreach (var conflict in conflicts)
            grid.Rows.Add(conflict.Item, conflict.Field, conflict.ThisPcValue, conflict.MasterValue);
        shell.Controls.Add(grid, 0, 2);
        shell.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(shell);
        UiTheme.ApplyTheme(this);
    }

    private static DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackgroundColor = UiTheme.Surface,
            ColumnHeadersHeight = 38
        };
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Item",
            HeaderText = "Item",
            FillWeight = 34,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Field",
            HeaderText = "Conflict",
            FillWeight = 19,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ThisPc",
            HeaderText = "This PC",
            FillWeight = 23,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Master",
            HeaderText = "Master",
            FillWeight = 24,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        return grid;
    }

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var keepHere = UiTheme.PrimaryButton("Keep this PC for overlaps");
        keepHere.AutoSize = false;
        keepHere.Size = new Size(210, 38);
        keepHere.Click += (_, _) => Choose(MergeConflictPreference.ThisPc);
        var keepMaster = UiTheme.SecondaryButton("Keep master for overlaps");
        keepMaster.AutoSize = false;
        keepMaster.Size = new Size(210, 38);
        keepMaster.Click += (_, _) => Choose(MergeConflictPreference.Master);
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.AutoSize = false;
        cancel.Size = new Size(90, 38);
        cancel.Click += (_, _) => Close();
        panel.Controls.Add(keepHere);
        panel.Controls.Add(keepMaster);
        panel.Controls.Add(cancel);
        return panel;
    }

    private void Choose(MergeConflictPreference preference)
    {
        Preference = preference;
        DialogResult = DialogResult.OK;
        Close();
    }
}
