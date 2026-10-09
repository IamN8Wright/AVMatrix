namespace InNasc;

internal sealed class CheckoutRecoveryPreviewForm : Form
{
    public CheckoutRecoveryPreviewForm(CheckoutInventoryRecovery recovery)
    {
        Text = "Recover missing rooms and devices";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(620, 440);
        Size = new Size(720, 520);
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        shell.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = $"Review {recovery.AddedRecords.Count:N0} missing record(s) for {recovery.Client.Name}. " +
                "Local records, edits and configuration files will be kept. " +
                "This may restore records you intentionally deleted. Recovery changes this PC only; " +
                "Check in & push publishes the result.",
        }, 0, 0);
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        list.Items.AddRange(recovery.AddedRecords.Cast<object>().ToArray());
        shell.Controls.Add(list, 0, 1);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var apply = UiTheme.PrimaryButton("Recover and keep local edits");
        apply.DialogResult = DialogResult.OK;
        actions.Controls.AddRange([cancel, apply]);
        shell.Controls.Add(actions, 0, 2);
        Controls.Add(shell);
        AcceptButton = apply;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
    }
}
