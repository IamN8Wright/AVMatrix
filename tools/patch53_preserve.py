from pathlib import Path

# Importing an existing .nasc should immediately seed its user identities/access levels
# into the Global Admin catalog. Their current .nasc credentials remain authoritative
# until Global Admin explicitly resets a password, because the original password cannot
# be recovered from its verifier. Once represented in the catalog, normal delete/edit
# semantics remain authoritative and deleted users are not resurrected.
p = Path("InNascCompanyGlobalAdminService.cs")
s = p.read_text(encoding="utf-8")
old = '''        var file = new InNascCompanyFileRecord
        {
            Name = FirstNonBlank(summary.LicenseName, Path.GetFileNameWithoutExtension(fullPath), company.Name),
            FilePath = fullPath,
            CompanyKeyBase64 = companyKeyBase64,
            DeviceLimit = summary.DeviceLimit,
            ExpiresUtc = summary.LicenseExpiresUtc
        };
        company.Files.Add(file);
        try
        {
            ApplyToFile(company, file);'''
new = '''        var file = new InNascCompanyFileRecord
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
            ApplyToFile(company, file);'''
if old not in s:
    raise SystemExit("Missing ImportExisting user-seeding anchor")
s = s.replace(old, new, 1)
old = '''        catch
        {
            company.Files.Remove(file);
            try { File.WriteAllBytes(fullPath, originalBytes); } catch { }
            throw;
        }'''
new = '''        catch
        {
            company.Files.Remove(file);
            company.Users.RemoveAll(user => addedUserIds.Contains(user.Id));
            try { File.WriteAllBytes(fullPath, originalBytes); } catch { }
            throw;
        }'''
if old not in s:
    raise SystemExit("Missing ImportExisting rollback anchor")
s = s.replace(old, new, 1)
p.write_text(s, encoding="utf-8")
