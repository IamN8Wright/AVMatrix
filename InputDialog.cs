namespace InNasc;

internal sealed class InputDialog : Form
{
    private readonly TextBox _textBox;

    private InputDialog(string title, string prompt, string initialValue)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 142);
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Font();
        Icon = AppBrand.CreateIcon();

        var label = new Label
        {
            Text = prompt,
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Location = new Point(18, 16)
        };

        _textBox = new TextBox
        {
            Text = initialValue,
            Location = new Point(18, 42),
            Width = 394,
            Font = UiTheme.Font(10)
        };

        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Location = new Point(244, 94);
        cancel.Size = new Size(80, 34);
        cancel.AutoSize = false;

        var ok = UiTheme.PrimaryButton("Save");
        ok.DialogResult = DialogResult.OK;
        ok.Location = new Point(332, 94);
        ok.Size = new Size(80, 34);
        ok.AutoSize = false;

        Controls.AddRange([label, _textBox, cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;
        UiTheme.ApplyTheme(this);
        Shown += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    public static string? Show(IWin32Window owner, string title, string prompt, string initialValue = "")
    {
        using var dialog = new InputDialog(title, prompt, initialValue);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;

        var value = dialog._textBox.Text.Trim();
        return value.Length == 0 ? null : value;
    }
}
