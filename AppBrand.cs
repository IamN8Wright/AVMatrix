namespace InNasc;

internal static class AppBrand
{
    private const string IconResource = "InNasc.Assets.InNasc.ico";
    private const string LightLogoResource = "InNasc.Assets.InNascLogoLight.png";
    private const string DarkLogoResource = "InNasc.Assets.InNascLogoDark.png";
    private const string PrimaryHorizontalResource = "InNasc.Assets.InNascPrimaryHorizontal.png";
    private const string InN8LabsMascotResource = "InNasc.Assets.InN8LabsMascot.png";

    public static Icon CreateIcon()
    {
        using var stream = OpenResource(IconResource);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static Bitmap CreateLogo() => CreateImage(UiTheme.IsDarkMode ? DarkLogoResource : LightLogoResource);

    public static Bitmap CreatePrimaryHorizontal() => CreateImage(PrimaryHorizontalResource);

    private static Bitmap CreateImage(string resource)
    {
        using var stream = OpenResource(resource);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    public static Bitmap CreateInN8LabsMascot()
    {
        using var stream = OpenResource(InN8LabsMascotResource);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Stream OpenResource(string name) =>
        typeof(AppBrand).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded brand resource '{name}' was not found.");
}

internal sealed class InNascBrandLogo : PictureBox
{
    private bool? _usingDarkVariant;

    public InNascBrandLogo(int width, int height)
    {
        Size = new Size(width, height);
        SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.Transparent;
        TabStop = false;
        RefreshVariant();
    }

    public void RefreshVariant()
    {
        if (_usingDarkVariant == UiTheme.IsDarkMode) return;
        var previous = Image;
        Image = AppBrand.CreateLogo();
        _usingDarkVariant = UiTheme.IsDarkMode;
        previous?.Dispose();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Image?.Dispose();
            Image = null;
        }
        base.Dispose(disposing);
    }
}
