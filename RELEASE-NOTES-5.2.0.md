# InNasc 5.2.0

- Rebuilds InNasc Global Admin around a company-card directory that matches the InNasc user application.
- Opens each company into a dedicated view for its `.nasc` licenses and company users.
- Lets Global Admin grant multiple independent `.nasc` files to one company.
- Adds per-file device tiers: 250, 500, 1,000, custom, or Unlimited.
- Enforces device limits in the InNasc user application across manual entry, duplication, Excel imports, merges, and saves.
- Lets Global Admin unlock a higher tier while preventing a limit from being reduced below current usage.
- Separates Global Admin identities from company-user identities and credentials.
- Adds company-user creation, access-level editing, password reset, and deletion inside each company.
- Adds Global Admin creation, password reset, and deletion in a separate account screen.
- Makes company deletion recoverable by retaining physical `.nasc` files on disk.
- Automatically upgrades 5.1 `.nascglobal` catalogs and preserves existing company paths, keys, and access records.
- Preserves legacy `.avmatrix` and `.avclient` migration, with migrated licenses starting as Unlimited.
