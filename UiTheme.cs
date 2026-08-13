using System.Drawing.Drawing2D;
using System.Reflection;

namespace AVMatrixStudio;

internal static class UiTheme
{
    private static readonly Color LightNavy = Color.FromArgb(15, 23, 42);
    private static readonly Color DarkNavy = Color.FromArgb(7, 12, 24);
    private static readonly Color LightNavySoft = Color.FromArgb(30, 41, 59);
    private static readonly Color DarkNavySoft = Color.FromArgb(17, 28, 48);
    private static readonly Color LightCanvas = Color.FromArgb(244, 247, 251);
    private static readonly Color DarkCanvas = Color.FromArgb(11, 17, 29);
    private static readonly Color LightSurface = Color.White;
    private static readonly Color DarkSurface = Color.FromArgb(20, 29, 44);
    private static readonly Color LightBorder = Color.FromArgb(220, 226, 235);
    private static readonly Color DarkBorder = Color.FromArgb(51, 65, 85);
    private static readonly Color LightText = Color.FromArgb(31, 41, 55);
    private static readonly Color DarkText = Color.FromArgb(226, 232, 240);
    private static readonly Color LightMuted = Color.FromArgb(100, 116, 139);
    private static readonly Color DarkMuted = Color.FromArgb(148, 163, 184);
    private static readonly Color LightBlue = Color.FromArgb(37, 99, 235);
    private static readonly Color DarkBlue = Color.FromArgb(59, 130, 246);
    private static readonly Color LightBlueHover = Color.FromArgb(29, 78, 216);
    private static readonly Color DarkBlueHover = Color.FromArgb(37, 99, 235);
    private static readonly Color LightGreen = Color.FromArgb(22, 163, 74);
    private static readonly Color DarkGreen = Color.FromArgb(74, 222, 128);
    private static readonly Color LightRed = Color.FromArgb(220, 38, 38);
    private static readonly Color DarkRed = Color.FromArgb(248, 113, 113);
    private static readonly Color LightAmber = Color.FromArgb(217, 119, 6);
    private static readonly Color DarkAmber = Color.FromArgb(251, 191, 36);
    private static readonly Color LightYellow = Color.FromArgb(202, 138, 4);
    private static readonly Color DarkYellow = Color.FromArgb(253, 224, 71);
    private static readonly Color LightGrayLed = Color.FromArgb(148, 163, 184);
    private static readonly Color DarkGrayLed = Color.FromArgb(100, 116, 139);
    private static readonly Color LightHeader = Color.FromArgb(248, 250, 252);
    private static readonly Color DarkHeader = Color.FromArgb(30, 41, 59);
    private static readonly Color LightAlternate = Color.FromArgb(242, 244, 247);
    private static readonly Color DarkAlternate = Color.FromArgb(16, 24, 38);
    private static readonly Color LightGridLine = Color.FromArgb(235, 239, 245);
    private static readonly Color DarkGridLine = Color.FromArgb(45, 58, 78);
    private static readonly Color LightSelection = Color.FromArgb(219, 234, 254);
    private static readonly Color DarkSelection = Color.FromArgb(30, 64, 110);
    private static readonly Color LightLogoTile = Color.FromArgb(239, 246, 255);
    private static readonly Color DarkLogoTile = Color.FromArgb(30, 58, 95);

    public static bool IsDarkMode { get; private set; }
    public static Color Navy => IsDarkMode ? DarkNavy : LightNavy;
    public static Color NavySoft => IsDarkMode ? DarkNavySoft : LightNavySoft;
    public static Color Canvas => IsDarkMode ? DarkCanvas : LightCanvas;
    public static Color Surface => IsDarkMode ? DarkSurface : LightSurface;
    public static Color Border => IsDarkMode ? DarkBorder : LightBorder;
    public static Color Text => IsDarkMode ? DarkText : LightText;
    public static Color Muted => IsDarkMode ? DarkMuted : LightMuted;
    public static Color Blue => IsDarkMode ? DarkBlue : LightBlue;
    public static Color BlueHover => IsDarkMode ? DarkBlueHover : LightBlueHover;
    public static Color Green => IsDarkMode ? DarkGreen : LightGreen;
    public static Color Red => IsDarkMode ? DarkRed : LightRed;
    public static Color Amber => IsDarkMode ? DarkAmber : LightAmber;
    public static Color Yellow => IsDarkMode ? DarkYellow : LightYellow;
    public static Color GrayLed => IsDarkMode ? DarkGrayLed : LightGrayLed;
    public static Color HeaderSurface => IsDarkMode ? DarkHeader : LightHeader;
    public static Color AlternateSurface => IsDarkMode ? DarkAlternate : LightAlternate;
    public static Color GridLine => IsDarkMode ? DarkGridLine : LightGridLine;
    public static Color Selection => IsDarkMode ? DarkSelection : LightSelection;
    public static Color LogoTile => IsDarkMode ? DarkLogoTile : LightLogoTile;
    public static Color InputSurface => IsDarkMode ? Color.FromArgb(15, 23, 42) : Color.White;
    public static Color SidebarMuted => Color.FromArgb(148, 163, 184);

    public static void SetDarkMode(bool darkMode) => IsDarkMode = darkMode;

    public static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    public static Button PrimaryButton(string text)
    {
        var button = BaseButton(text);
        button.BackColor = Blue;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = BlueHover;
        return button;
    }

    public static Button SecondaryButton(string text)
    {
        var button = BaseButton(text);
        button.BackColor = Surface;
        button.ForeColor = Text;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = HeaderSurface;
        return button;
    }

    public static Button DangerButton(string text)
    {
        var button = BaseButton(text);
        button.BackColor = Red;
        button.ForeColor = IsDarkMode ? Color.FromArgb(15, 23, 42) : Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = IsDarkMode
            ? Color.FromArgb(252, 165, 165)
            : Color.FromArgb(185, 28, 28);
        return button;
    }

    public static Button SidebarButton(string text)
    {
        var button = BaseButton(text);
        button.BackColor = NavySoft;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = DarkBorder;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
        button.Margin = new Padding(4, 0, 4, 0);
        return button;
    }

    public static Button SidebarIconButton(Image image, string accessibleName)
    {
        var button = SidebarButton(string.Empty);
        button.AutoSize = false;
        button.Size = new Size(50, 40);
        button.Padding = Padding.Empty;
        button.Image = image;
        button.ImageAlign = ContentAlignment.MiddleCenter;
        button.AccessibleName = accessibleName;
        button.TabStop = true;
        return button;
    }

    private static Button BaseButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(0, 34),
        Padding = new Padding(12, 0, 12, 0),
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Font = Font(9, FontStyle.Bold),
        UseVisualStyleBackColor = false
    };

    public static void EnableDoubleBuffer(Control control)
    {
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(control, true);
    }

    public static void ConfigureUniformComboBox(ComboBox comboBox, int itemHeight = 28)
    {
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.ItemHeight = itemHeight;
        comboBox.DrawItem += (_, eventArgs) =>
        {
            if (eventArgs.Index < 0) return;
            var selected = (eventArgs.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? Selection : InputSurface);
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
            var text = comboBox.GetItemText(comboBox.Items[eventArgs.Index]);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                text,
                comboBox.Font,
                eventArgs.Bounds,
                Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            eventArgs.DrawFocusRectangle();
        };
    }

    public static void ApplyTheme(Control root)
    {
        ApplyToControl(root);
        foreach (Control child in root.Controls) ApplyTheme(child);
        root.Invalidate();
    }

    private static void ApplyToControl(Control control)
    {
        switch (control)
        {
            case N8BrandLogo logo:
                logo.BackColor = Color.Transparent;
                logo.RefreshVariant();
                break;
            case RoundedPanel rounded:
                rounded.BackColor = Surface;
                rounded.BorderColor = Border;
                break;
            case TreeView tree:
                tree.BackColor = Navy;
                tree.ForeColor = DarkText;
                break;
            case DataGridView grid:
                ApplyGridTheme(grid);
                break;
            case Button button:
                ApplyButtonTheme(button);
                break;
            case TextBox textBox:
                textBox.BackColor = InputSurface;
                textBox.ForeColor = Text;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = InputSurface;
                comboBox.ForeColor = Text;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = InputSurface;
                numeric.ForeColor = Text;
                break;
            default:
                control.BackColor = MapBackground(control.BackColor);
                control.ForeColor = MapForeground(control.ForeColor);
                break;
        }
    }

    private static void ApplyButtonTheme(Button button)
    {
        if (Matches(button.BackColor, LightBlue, DarkBlue))
        {
            button.BackColor = Blue;
            button.ForeColor = Color.White;
            button.FlatAppearance.MouseOverBackColor = BlueHover;
        }
        else if (Matches(button.BackColor, LightRed, DarkRed))
        {
            button.BackColor = Red;
            button.ForeColor = IsDarkMode ? DarkNavy : Color.White;
            button.FlatAppearance.MouseOverBackColor = IsDarkMode
                ? Color.FromArgb(252, 165, 165)
                : Color.FromArgb(185, 28, 28);
        }
        else if (Matches(button.BackColor, LightNavySoft, DarkNavySoft))
        {
            button.BackColor = NavySoft;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = DarkBorder;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
        }
        else
        {
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = HeaderSurface;
        }
    }

    private static void ApplyGridTheme(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BackColor = Surface;
        grid.ForeColor = Text;
        grid.GridColor = GridLine;
        grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderSurface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderSurface;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = AlternateSurface;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
        foreach (DataGridViewRow row in grid.Rows)
        foreach (DataGridViewCell cell in row.Cells)
            if (!cell.Style.ForeColor.IsEmpty)
                cell.Style.ForeColor = MapForeground(cell.Style.ForeColor);
    }

    private static Color MapBackground(Color current)
    {
        if (current == Color.Transparent) return current;
        if (Matches(current, LightNavy, DarkNavy)) return Navy;
        if (Matches(current, LightNavySoft, DarkNavySoft)) return NavySoft;
        if (Matches(current, LightCanvas, DarkCanvas)) return Canvas;
        if (Matches(current, LightSurface, DarkSurface)) return Surface;
        if (Matches(current, LightHeader, DarkHeader)) return HeaderSurface;
        if (Matches(current, LightAlternate, DarkAlternate)) return AlternateSurface;
        if (Matches(current, LightLogoTile, DarkLogoTile)) return LogoTile;
        if (Matches(current, LightBlue, DarkBlue)) return Blue;
        if (Matches(current, LightGreen, DarkGreen)) return Green;
        if (Matches(current, LightRed, DarkRed)) return Red;
        if (Matches(current, LightAmber, DarkAmber)) return Amber;
        return current;
    }

    private static Color MapForeground(Color current)
    {
        if (Matches(current, LightText, DarkText)) return Text;
        if (Matches(current, LightMuted, DarkMuted)) return Muted;
        if (Matches(current, LightGrayLed, DarkGrayLed)) return GrayLed;
        if (Matches(current, LightBlue, DarkBlue)) return Blue;
        if (Matches(current, LightGreen, DarkGreen)) return Green;
        if (Matches(current, LightRed, DarkRed)) return Red;
        if (Matches(current, LightAmber, DarkAmber)) return Amber;
        return current;
    }

    private static bool Matches(Color color, Color light, Color dark) => color == light || color == dark;
}

internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 12;
    public Color BorderColor { get; set; } = UiTheme.Border;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Surface;
        Padding = new Padding(1);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = RoundedRectangle(ClientRectangle, Radius);
        Region = new Region(path);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using var path = RoundedRectangle(rectangle, Radius);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
