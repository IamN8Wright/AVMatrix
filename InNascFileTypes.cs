namespace InNasc;

internal static class InNascFileTypes
{
    public const string CompanyExtension = ".nasc";
    public const string GlobalExtension = ".nascglobal";
    public const string LegacyCompanyExtension = ".avmatrix";

    public const string CompanyFilter =
        "InNasc company files (*.nasc)|*.nasc|Legacy AV Matrix files (*.avmatrix)|*.avmatrix|All files (*.*)|*.*";
    public const string GlobalFilter =
        "InNasc Global files (*.nascglobal)|*.nascglobal|All files (*.*)|*.*";

    public static bool IsCompanyPath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, CompanyExtension, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, LegacyCompanyExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static string ValidateCompanyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose an InNasc company file.");
        var fullPath = Path.GetFullPath(path.Trim());
        if (!IsCompanyPath(fullPath))
            throw new InvalidDataException(
                $"InNasc company files must use {CompanyExtension}. Legacy {LegacyCompanyExtension} files remain readable for migration.");
        return fullPath;
    }

    public static string ValidateNewCompanyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose a location for the InNasc company file.");
        var fullPath = Path.GetFullPath(path.Trim());
        if (!string.Equals(Path.GetExtension(fullPath), CompanyExtension, StringComparison.OrdinalIgnoreCase))
            fullPath = Path.ChangeExtension(fullPath, CompanyExtension);
        return fullPath;
    }

    public static bool IsLegacyCompanyPath(string path) =>
        string.Equals(Path.GetExtension(path), LegacyCompanyExtension, StringComparison.OrdinalIgnoreCase);
}
