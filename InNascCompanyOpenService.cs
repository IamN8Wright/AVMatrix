namespace AVMatrixStudio;

internal static class InNascCompanyOpenService
{
    public static void Open(
        AppData data,
        DataStore store,
        InNascCompanySelection selection)
    {
        var company = selection.Company;
        var session = selection.CompanySession;
        if (!File.Exists(company.FilePath))
            throw new FileNotFoundException(
                $"The company file for {company.Name} could not be found.", company.FilePath);

        var hasCheckout = data.Settings.ActiveCheckoutClientId.HasValue &&
                          data.Settings.ActiveCheckoutToken.HasValue;
        if (hasCheckout && !SamePath(data.Settings.SharedMasterPath, company.FilePath))
            throw new InvalidOperationException(
                "This PC has an unfinished checkout from another company. Open that company and check the client in before switching companies.");

        _ = SharedSyncService.LinkPath(company.FilePath, data, store);
        var snapshot = SharedSyncService.Inspect(company.FilePath, company.CompanyKeyBase64);
        var canResume = hasCheckout && CanResumeCheckout(data, snapshot.Contents.Data.MasterAccess, session);
        if (!canResume)
            _ = SharedSyncService.Pull(data, store, company.CompanyKeyBase64, session);
        else
        {
            data.MasterAccess = MasterAccessService.Clone(snapshot.Contents.Data.MasterAccess);
            data.Settings.SharedMasterFingerprint = snapshot.Fingerprint;
            data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
            store.Save(data);
        }

        data.ProjectName = company.Name;
        data.Settings.LastMasterTarget = nameof(SyncTarget.SharedFile);
        store.Save(data);
        MasterSessionContext.Set(SyncTarget.SharedFile, company.FilePath, session);
        InNascGlobalSessionContext.Set(
            selection.GlobalSession,
            InNascGlobalSessionContext.CatalogPath,
            company.Id);
    }

    private static bool CanResumeCheckout(
        AppData data,
        MasterAccessControl access,
        MasterSession session)
    {
        if (!data.Settings.ActiveCheckoutClientId.HasValue ||
            !data.Settings.ActiveCheckoutToken.HasValue)
            return false;
        var checkout = access.Checkouts.FirstOrDefault(item =>
            item.ClientId == data.Settings.ActiveCheckoutClientId &&
            item.CheckoutToken == data.Settings.ActiveCheckoutToken);
        return checkout is not null && checkout.UserId == session.UserId;
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
