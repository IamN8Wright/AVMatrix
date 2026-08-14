namespace InNasc;

internal static class InNascCompanyAccessSyncService
{
    public static void SyncAll(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession)
    {
        foreach (var company in catalog.Companies.Where(x => x.Enabled && File.Exists(x.FilePath)))
            SyncCompany(globalPath, catalog, globalSession, company);
    }

    public static void SyncCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company)
    {
        if (!globalSession.IsGlobalAdmin)
            throw new MasterAuthorizationException("Global Admin access is required.");
        if (!File.Exists(company.FilePath)) return;

        var imported = PortableDataService.ImportBytes(
            File.ReadAllBytes(company.FilePath), company.CompanyKeyBase64);
        var data = imported.Data;
        var old = data.MasterAccess ?? new MasterAccessControl();
        var next = InNascGlobalCoreService.BuildCompanyAccess(
            globalPath, catalog, globalSession, company, old);

        var allowedUsers = next.Users.Select(x => x.Id).ToHashSet();
        next.Checkouts.RemoveAll(x => !allowedUsers.Contains(x.UserId));
        data.MasterAccess = next;
        var companySession = InNascGlobalCoreService.CreateCompanySession(globalSession, catalog, company);
        PortableDataService.ExportMaster(company.FilePath, data, companySession);
    }
}
