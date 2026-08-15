namespace InNasc;

internal static class InNascCompanyGlobalAdminService
{
    public static InNascCompanyFileRecord ImportExisting(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        InNascCompanyRecord company,
        string sourcePath,
        string companyKeyBase64)
    {
        RequireAdmin(session);
        var fullPath = Path.GetFullPath(sourcePath.Trim());
        if (!string.Equals(Path.GetExtension(fullPath), InNascFileTypes.CompanyExtension,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose an existing .nasc company file.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The selected .nasc file could not be found.", fullPath);
        if (catalog.Companies.SelectMany(candidate => candidate.Files).Any(file =>
                string.Equals(file.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "That .nasc file is already assigned to a company in this Global Admin catalog.");
        if (string.IsNullOrWhiteSpace(companyKeyBase64))
            throw new MasterAuthorizationException(
                "A valid company sign-in is required before this .nasc can be imported.");

        var originalBytes = File.ReadAllBytes(fullPath);
        var imported = PortableDataService.ImportBytes(originalBytes, companyKeyBase64);
        var summary = PortableDataService.ReadCompanySummary(originalBytes);
        var currentCount = DeviceLimitPolicy.CountDevices(imported.Data);
        if (summary.DeviceLimit > 0 && currentCount > summary.DeviceLimit)
            throw new InvalidDataException(
                $"The selected .nasc reports a {summary.DeviceLimit:N0}-device tier but contains {currentCount:N0} devices.");

        var file = new InNascCompanyFileRecord
        {
            Name = FirstNonBlank(summary.LicenseName, Path.GetFileNameWithoutExtension(fullPath), company.Name),
            FilePath = fullPath,
            CompanyKeyBase64 = companyKeyBase64,
            DeviceLimit = summary.DeviceLimit,
            ExpiresUtc = summary.LicenseExpiresUtc
        };

        var addedUserIds = new List<Guid>();
        foreach (var source in imported.Data.MasterAccess.Users
                     .Where(user => user.Enabled && !user.IsRecoveryAccount))
        {
            if (company.Users.Any(user => user.Id == source.Id ||
                    string.Equals(user.Username, source.Username,
                        StringComparison.OrdinalIgnoreCase)))
                continue;
            var profile = new InNascCompanyUserRecord
            {
                Id = source.Id,
                Username = source.Username,
                DisplayName = source.DisplayName,
                Role = source.Role,
                PasswordSaltBase64 = source.PasswordSaltBase64,
                PasswordHashBase64 = source.PasswordHashBase64,
                PasswordIterations = source.PasswordIterations,
                CompanyKeySaltBase64 = source.MasterKeySaltBase64,
                HasAllClientAccess = source.Role == MasterUserRole.Owner || source.HasAllClientAccess,
                ClientAccessIds = source.Role == MasterUserRole.Owner || source.HasAllClientAccess
                    ? []
                    : source.ClientAccessIds.Distinct().ToList(),
                Enabled = true,
                CreatedUtc = source.CreatedUtc
            };
            company.Users.Add(profile);
            addedUserIds.Add(profile.Id);
        }

        company.Files.Add(file);
        try
        {
            ApplyToFile(company, file);
            InNascGlobalCoreService.Save(globalPath, catalog, session);
            return file;
        }
        catch
        {
            company.Files.Remove(file);
            company.Users.RemoveAll(user => addedUserIds.Contains(user.Id));
            try { File.WriteAllBytes(fullPath, originalBytes); } catch { }
            throw;
        }
    }

    public static void RenameCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        InNascCompanyRecord company,
        string newName)
    {
        RequireAdmin(session);
        var name = newName.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Enter a company name.");
        if (catalog.Companies.Any(candidate => candidate.Enabled && candidate.Id != company.Id &&
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A company with that name already exists.");

        var previous = company.Name;
        company.Name = name;
        try
        {
            ApplyAll(company);
            InNascGlobalCoreService.Save(globalPath, catalog, session);
        }
        catch
        {
            company.Name = previous;
            TryApplyAll(company);
            throw;
        }
    }

    public static void RenameLicense(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        InNascCompanyRecord company,
        InNascCompanyFileRecord file,
        string newName)
    {
        RequireAdmin(session);
        var previous = file.Name;
        InNascGlobalCoreService.RenameLicense(
            globalPath, catalog, session, company.Id, file.Id, newName);
        try
        {
            ApplyToFile(company, file);
        }
        catch
        {
            file.Name = previous;
            InNascGlobalCoreService.Save(globalPath, catalog, session);
            TryApplyAll(company);
            throw;
        }
    }

    public static void SetCompanyLogo(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        InNascCompanyRecord company,
        string? logoBase64)
    {
        RequireAdmin(session);
        var previous = company.LogoBase64;
        company.LogoBase64 = logoBase64?.Trim() ?? string.Empty;
        try
        {
            ApplyAll(company);
            InNascGlobalCoreService.Save(globalPath, catalog, session);
        }
        catch
        {
            company.LogoBase64 = previous;
            TryApplyAll(company);
            throw;
        }
    }

    public static void ApplyToFile(InNascCompanyRecord company, InNascCompanyFileRecord file)
    {
        if (!file.Enabled || !File.Exists(file.FilePath)) return;
        InNascCompanyEnvelopeMetadataService.Apply(
            file.FilePath,
            company.Name,
            file.Name,
            file.Id,
            file.DeviceLimit,
            company.LogoBase64,
            file.ExpiresUtc);
    }

    private static void ApplyAll(InNascCompanyRecord company)
    {
        foreach (var file in company.Files.Where(file => file.Enabled && File.Exists(file.FilePath)))
            ApplyToFile(company, file);
    }

    private static void TryApplyAll(InNascCompanyRecord company)
    {
        try { ApplyAll(company); } catch { }
    }

    private static void RequireAdmin(InNascGlobalSession session)
    {
        if (!session.IsGlobalAdmin)
            throw new MasterAuthorizationException("Global Admin access is required.");
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;
}
