namespace AVMatrixStudio;

internal static class N8Brand
{
    private const string DarkLogoResource = "AVMatrixStudio.Assets.N8LogoDark.png";
    private const string LightLogoResource = "AVMatrixStudio.Assets.N8LogoLight.png";

    public static Control CreateLogo(int width, int height, bool onDarkBackground = false) =>
        new N8BrandLogo(width, height, onDarkBackground);

    internal static Bitmap LoadLogo(bool lightVariant)
    {
        var resource = lightVariant ? LightLogoResource : DarkLogoResource;
        using var stream = typeof(N8Brand).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded N8 logo resource '{resource}' was not found.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}

internal sealed class N8BrandLogo : PictureBox
{
    private readonly bool _onDarkBackground;
    private bool? _usingLightVariant;

    public N8BrandLogo(int width, int height, bool onDarkBackground)
    {
        _onDarkBackground = onDarkBackground;
        Size = new Size(width, height);
        SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.Transparent;
        TabStop = false;
        RefreshVariant();
    }

    public void RefreshVariant()
    {
        var useLightVariant = _onDarkBackground || UiTheme.IsDarkMode;
        if (_usingLightVariant == useLightVariant) return;
        var previous = Image;
        Image = N8Brand.LoadLogo(useLightVariant);
        _usingLightVariant = useLightVariant;
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
