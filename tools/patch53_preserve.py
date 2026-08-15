from pathlib import Path

p = Path("InNascGlobalCoreService.cs")
s = p.read_text(encoding="utf-8")
old = '''        if (file.RecoveryUser?.CredentialReady == true)
        {
            var recovery = CreatePublishedCompanyUser(file.RecoveryUser, file.CompanyKeyBase64, globalKey);'''
new = '''        // Preserve valid logins already present in an imported .nasc until Global Admin
        // has explicitly reconciled them into the catalog. A catalog entry, including a
        // disabled one, takes ownership of that identity so deletes still publish correctly.
        if (existing is not null)
        {
            foreach (var preserved in existing.Users.Where(candidate => !candidate.IsRecoveryAccount))
            {
                var represented = company.Users.Any(profile =>
                    profile.Id == preserved.Id ||
                    string.Equals(profile.Username, preserved.Username,
                        StringComparison.OrdinalIgnoreCase));
                if (!represented)
                    access.Users.Add(ClonePublishedUser(preserved));
            }
        }

        if (file.RecoveryUser?.CredentialReady == true)
        {
            var recovery = CreatePublishedCompanyUser(file.RecoveryUser, file.CompanyKeyBase64, globalKey);'''
if old not in s:
    raise SystemExit("Missing imported-login preservation anchor")
s = s.replace(old, new, 1)
p.write_text(s, encoding="utf-8")
