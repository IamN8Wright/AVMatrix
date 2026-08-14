namespace InNasc;

internal static class InNascCompanyAccessSyncService
{
    public static void SyncAll(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession)
    {
        foreach (var company in catalog.Companies.Where(company => company.Enabled))
            SyncCompany(globalPath, catalog, globalSession, company);
    }

    public static void SyncCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company)
    {
        foreach (var file in company.Files.Where(file => file.Enabled && File.Exists(file.FilePath)))
            SyncFile(globalPath, catalog, globalSession, company, file);
    }

    public static void SyncFile(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company,
        InNascCompanyFileRecord file)
    {
        if (!globalSession.IsGlobalAdmin)
            throw new MasterAuthorizationException("Global Admin access is required.");
        if (!File.Exists(file.FilePath)) return;

        var imported = PortableDataService.ImportBytes(
            File.ReadAllBytes(file.FilePath), file.CompanyKeyBase64);
        var data = imported.Data;
        var old = data.MasterAccess ?? new MasterAccessControl();
        var next = InNascGlobalCoreService.BuildCompanyAccess(
            company, file, globalSession.GlobalKey, old);
        if (!company.Users.Any(user => user.Enabled && user.CredentialReady) && old.Users.Count > 0)
            next.Users = old.Users;

        var allowedUsers = next.Users.Select(user => user.Id).ToHashSet();
        next.Checkouts.RemoveAll(checkout => !allowedUsers.Contains(checkout.UserId));
        data.MasterAccess = next;
        DeviceLimitPolicy.RequireWithinLimit(next, data);
        var companySession = InNascGlobalCoreService.CreateCompanyFileSession(
            globalSession, file);
        PortableDataService.ExportMaster(file.FilePath, data, companySession);
        InNascCompanyEnvelopeMetadataService.Apply(
            file.FilePath, company.Name, file.Name, file.Id, file.DeviceLimit, company.LogoBase64);
        _ = globalPath;
        _ = catalog;
    }
}
