namespace AVMatrixStudio;

internal static class AppBrand
{
    private const string IconResource = "AVMatrixStudio.Assets.AVMatrixStudio.ico";
    private const string LogoResource = "AVMatrixStudio.Assets.AVMatrixStudioLogo.png";
    private const string InN8LabsMascotResource = "AVMatrixStudio.Assets.InN8LabsMascot.png";

    public static Icon CreateIcon()
    {
        using var stream = OpenResource(IconResource);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static Bitmap CreateLogo()
    {
        using var stream = OpenResource(LogoResource);
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
