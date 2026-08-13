namespace InNasc;

internal static class InNascCompanyAccessSyncService
{
    public static void SyncAll(InNascGlobalCatalog catalog, InNascGlobalSession globalSession)
    {
        foreach (var company in catalog.Companies.Where(x => x.Enabled && File.Exists(x.FilePath)))
            SyncCompany(catalog, globalSession, company);
    }

    public static void SyncCompany(
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
        var next = new MasterAccessControl
        {
            MasterId = old.MasterId,
            Checkouts = old.Checkouts,
            ClientSubmatrices = old.ClientSubmatrices
        };

        foreach (var user in catalog.Users.Where(x => x.Enabled))
        {
            var membership = user.Companies.FirstOrDefault(x => x.CompanyId == company.Id);
            if (!user.IsGlobalAdmin && membership is null) continue;
            next.Users.Add(new MasterUserRecord
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.IsGlobalAdmin ? MasterUserRole.Owner : membership!.Role,
                Enabled = true,
                HasAllClientAccess = user.IsGlobalAdmin || membership!.HasAllClientAccess,
                ClientAccessIds = user.IsGlobalAdmin ? [] : membership!.ClientAccessIds.ToList()
            });
        }

        var allowedUsers = next.Users.Select(x => x.Id).ToHashSet();
        next.Checkouts.RemoveAll(x => !allowedUsers.Contains(x.UserId));
        data.MasterAccess = next;
        var companySession = InNascGlobalCoreService.CreateCompanySession(globalSession, catalog, company);
        PortableDataService.ExportMaster(company.FilePath, data, companySession);
    }
}
