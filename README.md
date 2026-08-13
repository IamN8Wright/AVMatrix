# InNasc

InNasc is a native Windows application by InN8 Labs for understanding connected systems in context. It organizes companies, users, clients, locations, rooms, equipment, network interfaces, credentials, configuration files, checkout state, and synchronization in one workspace.

Website: [InNasc.com](https://InNasc.com)

## InNasc 5.0 architecture

- `.nascglobal` is the encrypted Global Admin catalog. It contains global users, company records, and user-to-company memberships.
- `.nasc` is the encrypted company database format.
- `.nascclient` stores optional client payloads and configuration-file contents alongside the company database.
- Global users sign in with a username and password, choose an assigned company, and receive the role configured for that company.
- Global Admins create companies and users, assign users to companies, reset passwords, and synchronize access into company files.
- Passwords are stored as salted PBKDF2 verifiers. Each password also derives a key that unwraps the encrypted Global catalog key; readable passwords are never stored.

## Legacy compatibility

InNasc keeps AV Matrix data usable during the 5.0 migration:

- Existing `.avmatrix` files remain readable.
- Global Admin includes **Migrate .avmatrix…**, which creates a new `.nasc` company while leaving the source untouched.
- Unprotected, password-protected, and account-protected legacy files are supported.
- Associated `.avclient` payloads migrate to `.nascclient`.
- Legacy local application data and stored integration credentials are discovered and migrated to their InNasc locations.

New data is always written with the InNasc extensions and identity.

## Branding

- Light and Dark InNasc app marks switch with the application theme.
- The Light app icon is used for the Windows executable and app tile.
- Primary Horizontal artwork appears on About.
- The app product, assembly, updater, executable, checksum, and CI artifact are named InNasc.

## Windows build

Requirements: Windows and the .NET 8 SDK.

Run `Build-Windows.bat`, or publish directly:

```powershell
dotnet publish InNasc.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output publish\win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

The installable test executable is `publish\win-x64\InNasc.exe`.

## Validation

The repository includes a dependency-free Windows smoke suite covering:

- Global catalog creation and login
- Invalid-password rejection
- Company creation and selection
- User membership and role enforcement
- Password reset
- Company-access synchronization
- Legacy `.avmatrix` and `.avclient` migration
- Preservation of the original legacy files

Run it with:

```powershell
dotnet run --project tests\InNasc.SmokeTests\InNasc.SmokeTests.csproj --configuration Release
```

## Local data and security

InNasc stores local settings and recovery data under `%LOCALAPPDATA%\InNasc`. Integration tokens remain in Windows Credential Manager. Equipment credentials and configuration files may be present in company databases and exported workbooks; protect and share those artifacts accordingly.

Copyright © 2026 InN8 Labs.
