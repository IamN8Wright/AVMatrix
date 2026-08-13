using System.Drawing.Drawing2D;

namespace InNasc;

internal static class AppIcons
{
    public static Bitmap Home(int size = 22) => Draw(size, graphics =>
    {
        using var pen = IconPen();
        using var brush = new SolidBrush(Color.White);
        var points = new[]
        {
            new PointF(3, 10), new PointF(11, 3), new PointF(19, 10)
        };
        graphics.DrawLines(pen, points);
        graphics.DrawRectangle(pen, 5.5f, 9.5f, 11, 9);
        graphics.FillRectangle(brush, 9.2f, 13, 3.6f, 5.5f);
    });

    public static Bitmap Settings(int size = 22) => Draw(size, graphics =>
    {
        using var pen = IconPen(2.1f);
        using var toothPen = IconPen(2.8f);
        var center = new PointF(11, 11);
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Math.PI / 4;
            var start = new PointF(
                center.X + (float)Math.Cos(angle) * 7,
                center.Y + (float)Math.Sin(angle) * 7);
            var end = new PointF(
                center.X + (float)Math.Cos(angle) * 9,
                center.Y + (float)Math.Sin(angle) * 9);
            graphics.DrawLine(toothPen, start, end);
        }
        graphics.DrawEllipse(pen, 5, 5, 12, 12);
        graphics.DrawEllipse(pen, 9, 9, 4, 4);
    });

    public static Bitmap Scanner(int size = 22) => Draw(size, graphics =>
    {
        using var pen = IconPen(1.8f);
        using var brush = new SolidBrush(Color.White);
        graphics.DrawArc(pen, 3, 3, 16, 16, 205, 250);
        graphics.DrawArc(pen, 6, 6, 10, 10, 205, 250);
        graphics.DrawLine(pen, 11, 11, 18, 6);
        graphics.FillEllipse(brush, 9, 9, 4, 4);
        graphics.FillEllipse(brush, 16.5f, 4.5f, 3, 3);
    });

    public static Bitmap About(int size = 22) => Draw(size, graphics =>
    {
        using var pen = IconPen(1.9f);
        using var brush = new SolidBrush(Color.White);
        graphics.DrawEllipse(pen, 2.5f, 2.5f, 17, 17);
        graphics.FillEllipse(brush, 9.4f, 6, 3.2f, 3.2f);
        graphics.FillRoundedRectangle(brush, new RectangleF(9.5f, 10.5f, 3, 6.2f), 1.2f);
    });

    public static Bitmap Sync(int size = 22, Color? color = null) => Draw(size, graphics =>
    {
        var iconColor = color ?? Color.White;
        using var pen = IconPen(1.9f, iconColor);
        using var brush = new SolidBrush(iconColor);
        graphics.DrawArc(pen, 3.2f, 4.3f, 15.5f, 13.5f, 205, 155);
        graphics.DrawArc(pen, 3.2f, 4.3f, 15.5f, 13.5f, 25, 155);
        graphics.FillPolygon(brush,
        [
            new PointF(17.8f, 3.8f),
            new PointF(19.1f, 9.2f),
            new PointF(14.2f, 7.1f)
        ]);
        graphics.FillPolygon(brush,
        [
            new PointF(4.2f, 18.2f),
            new PointF(2.9f, 12.8f),
            new PointF(7.8f, 14.9f)
        ]);
    });

    private static Bitmap Draw(int size, Action<Graphics> paint)
    {
        var bitmap = new Bitmap(22, 22);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            paint(graphics);
        }
        if (size == 22) return bitmap;
        var resized = new Bitmap(bitmap, new Size(size, size));
        bitmap.Dispose();
        return resized;
    }

    private static Pen IconPen(float width = 1.9f, Color? color = null) => new(color ?? Color.White, width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round
    };
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(
        this Graphics graphics,
        Brush brush,
        RectangleF bounds,
        float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
