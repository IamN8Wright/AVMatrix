# InNasc 5.0.0

- Rebrands AV Matrix Studio as InNasc, including the executable, assembly metadata, updater assets, About page, website, Windows icon, and theme-aware app marks.
- Adds encrypted `.nascglobal` Global Admin catalogs for companies, global users, and company memberships.
- Adds username/password startup, assigned-company selection, and company-role enforcement.
- Adds company-card and user-management workflows for Global Admins.
- Writes company data as `.nasc` and client payloads as `.nascclient`.
- Adds explicit migration from `.avmatrix`/`.avclient`, preserving the original legacy files.
- Preserves legacy local data, stored credentials, password-protected files, account-protected files, and private GitHub storage paths.
- Adds Windows CI smoke tests and produces a branch-only testable `InNasc.exe` artifact without publishing a release.
