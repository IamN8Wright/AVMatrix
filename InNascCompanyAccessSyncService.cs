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
        var allowedUsers = next.Users.Select(user => user.Id).ToHashSet();
        next.Checkouts.RemoveAll(checkout => !allowedUsers.Contains(checkout.UserId));
        data.MasterAccess = next;
        data.ProjectName = company.Name;

        var companySession = InNascGlobalCoreService.CreateCompanyFileSession(globalSession, file);
        PortableDataService.ExportMaster(file.FilePath, data, companySession);
        InNascCompanyEnvelopeMetadataService.Apply(
            file.FilePath, company.Name, file.Name, file.Id, file.DeviceLimit,
            company.LogoBase64, file.ExpiresUtc);

        file.LastCatalogSyncUtc = DateTime.UtcNow;
        file.LastObservedDeviceCount = DeviceLimitPolicy.CountDevices(data);
        file.LastObservedRevisionId = imported.RevisionId;
        file.RemoteSyncState = "LocalOnly";
        InNascGlobalCoreService.Save(globalPath, catalog, globalSession);
    }

    public static void ReconcileFile(
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
        PullCurrentUsers(company, imported.Data.MasterAccess ?? new MasterAccessControl());
        InNascGlobalCoreService.Save(globalPath, catalog, globalSession);
        SyncFile(globalPath, catalog, globalSession, company, file);
    }

    public static void ReconcileCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company)
    {
        foreach (var file in company.Files.Where(file => file.Enabled && File.Exists(file.FilePath)))
            ReconcileFile(globalPath, catalog, globalSession, company, file);
    }

    private static void PullCurrentUsers(
        InNascCompanyRecord company,
        MasterAccessControl access)
    {
        foreach (var source in access.Users.Where(user => user.Enabled && !user.IsRecoveryAccount))
        {
            var target = company.Users.FirstOrDefault(user => user.Id == source.Id)
                ?? company.Users.FirstOrDefault(user =>
                    string.Equals(user.Username, source.Username, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                // The password itself is not recoverable from the .nasc. Keep enough identity/hash
                // information to show the account in Global Admin; the encrypted .nasc credential
                // remains authoritative until Global Admin resets that user's password.
                target = new InNascCompanyUserRecord
                {
                    Id = source.Id,
                    Username = source.Username,
                    DisplayName = source.DisplayName,
                    Role = source.Role,
                    PasswordSaltBase64 = source.PasswordSaltBase64,
                    PasswordHashBase64 = source.PasswordHashBase64,
                    PasswordIterations = source.PasswordIterations,
                    CompanyKeySaltBase64 = source.MasterKeySaltBase64,
                    HasAllClientAccess = source.HasAllClientAccess,
                    ClientAccessIds = source.ClientAccessIds.ToList(),
                    Enabled = true,
                    CreatedUtc = source.CreatedUtc
                };
                company.Users.Add(target);
            }
            else
            {
                target.Username = source.Username;
                target.DisplayName = source.DisplayName;
                target.Role = source.Role;
                target.HasAllClientAccess = source.Role == MasterUserRole.Owner || source.HasAllClientAccess;
                target.ClientAccessIds = target.HasAllClientAccess
                    ? []
                    : source.ClientAccessIds.Distinct().ToList();
                target.Enabled = source.Enabled;
                if (!target.CredentialReady)
                {
                    target.PasswordSaltBase64 = source.PasswordSaltBase64;
                    target.PasswordHashBase64 = source.PasswordHashBase64;
                    target.PasswordIterations = source.PasswordIterations;
                    target.CompanyKeySaltBase64 = source.MasterKeySaltBase64;
                }
            }
        }
    }
}
