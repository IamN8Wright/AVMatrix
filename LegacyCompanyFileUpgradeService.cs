namespace InNasc;

internal sealed record LegacyCompanyUpgradeResult(
    string RecoveryDirectory,
    int ClientPayloadsCreated);

internal static class LegacyCompanyFileUpgradeService
{
    public static bool NeedsEnvelopeUpgrade(byte[] contents) =>
        !PortableDataService.IsAccountProtected(contents) &&
        !PortableDataService.IsPasswordProtected(contents);

    public static LegacyCompanyUpgradeResult UpgradeInPlace(
        string companyPath,
        AppData source,
        DataStore store,
        MasterSession session)
    {
        var fullPath = Path.GetFullPath(companyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The company file could not be found.", fullPath);
        if (PortableDataService.IsAccountProtected(File.ReadAllBytes(fullPath)))
            return new LegacyCompanyUpgradeResult(string.Empty, 0);
        if (string.IsNullOrWhiteSpace(session.MasterKey))
            throw new MasterAuthorizationException(
                "This older company file does not yet have an account-unlock key. " +
                "Sign in with its Owner account or open InNasc Global Admin and sync the company users.");

        var recoveryDirectory = CreateRecoverySnapshot(fullPath, store);
        var payloadsCreated = 0;
        try
        {
            foreach (var client in source.Clients)
            {
                var hasEmbeddedPayload = client.Locations
                    .SelectMany(location => location.Rooms)
                    .SelectMany(room => room.Equipment)
                    .SelectMany(equipment => equipment.ConfigurationFiles ?? [])
                    .Any(file => file.ContentIncluded);
                if (!hasEmbeddedPayload) continue;

                var clientPath = ClientSubmatrixService.SharedClientPath(fullPath, client.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(clientPath)!);
                PortableDataService.Export(
                    clientPath,
                    ClientSubmatrixService.ClientPackage(client),
                    session.MasterKey);
                payloadsCreated++;
            }

            PortableDataService.ExportMaster(
                fullPath,
                ClientSubmatrixService.MasterMetadataOnly(source),
                session);

            var upgradedBytes = File.ReadAllBytes(fullPath);
            if (!PortableDataService.IsAccountProtected(upgradedBytes))
                throw new InvalidDataException(
                    "The company upgrade did not produce a protected InNasc account envelope.");

            var verified = PortableDataService.ImportBytes(upgradedBytes, session.MasterKey).Data;
            if (verified.Clients.Count != source.Clients.Count)
                throw new InvalidDataException(
                    "The upgraded company file did not preserve the complete client inventory.");

            return new LegacyCompanyUpgradeResult(recoveryDirectory, payloadsCreated);
        }
        catch
        {
            RestoreRecoverySnapshot(fullPath, recoveryDirectory);
            throw;
        }
    }

    private static string CreateRecoverySnapshot(string companyPath, DataStore store)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss-fff");
        var recoveryDirectory = Path.Combine(
            store.DataDirectory,
            "LegacyCompanyUpgradeBackups",
            stamp + "-" + Path.GetFileNameWithoutExtension(companyPath));
        Directory.CreateDirectory(recoveryDirectory);

        File.Copy(
            companyPath,
            Path.Combine(recoveryDirectory, Path.GetFileName(companyPath)),
            overwrite: true);

        var clientDirectory = ClientSubmatrixService.SharedDirectory(companyPath);
        if (Directory.Exists(clientDirectory))
            CopyDirectory(
                clientDirectory,
                Path.Combine(recoveryDirectory, Path.GetFileName(clientDirectory)));

        return recoveryDirectory;
    }

    private static void RestoreRecoverySnapshot(string companyPath, string recoveryDirectory)
    {
        try
        {
            var masterBackup = Path.Combine(recoveryDirectory, Path.GetFileName(companyPath));
            if (File.Exists(masterBackup))
                File.Copy(masterBackup, companyPath, overwrite: true);

            var clientDirectory = ClientSubmatrixService.SharedDirectory(companyPath);
            if (Directory.Exists(clientDirectory))
                Directory.Delete(clientDirectory, recursive: true);

            var clientBackup = Path.Combine(recoveryDirectory, Path.GetFileName(clientDirectory));
            if (Directory.Exists(clientBackup))
                CopyDirectory(clientBackup, clientDirectory);
        }
        catch
        {
            // Preserve the original upgrade exception. The recovery directory remains available
            // for manual restoration if Windows blocks an automatic rollback.
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)));
    }
}
