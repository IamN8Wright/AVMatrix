# InNasc

InNasc is a native Windows application by InN8 Labs for understanding connected systems in context. It organizes companies, users, clients, locations, rooms, equipment, network interfaces, credentials, configuration files, checkout state, and synchronization.

Website: [InNasc.com](https://InNasc.com)

## InNasc 5.1 two-application architecture

InNasc now ships as two separate Windows programs:

- **InNasc Global Admin** (`InNasc.GlobalAdmin.exe`) is the administrative authority. It creates the encrypted `.nascglobal` catalog, presents companies as cards, grants one or more `.nasc` licenses per company, sets a device tier on each license, manages company users and access levels, resets passwords, and migrates legacy `.avmatrix` companies.
- **InNasc** (`InNasc.exe`) is the user application. Users choose their company's `.nasc` file, sign in directly with the credentials published by Global Admin, and populate or maintain the company workspace according to their Owner, Tech, or Read-only role.

The user application does not create company files, change device tiers, or administer user accounts. Those responsibilities remain isolated in Global Admin. Global Admin accounts and company-user accounts are separate identities: a Global Admin can open `.nascglobal`, while a company user can open only the company's published `.nasc` files.

### File types

- `.nascglobal` is the encrypted Global Admin catalog. Only `InNasc.GlobalAdmin.exe` opens it.
- `.nasc` is an encrypted company database/license and contains the company-local login envelope and device limit published by Global Admin. A company may have multiple independently tiered `.nasc` files.
- `.nascclient` stores optional client payloads and configuration-file contents beside the company database.

Passwords remain non-recoverable. Global Admin stores salted PBKDF2 verifiers and encrypted credential material that lets it publish a company user into the company's `.nasc` files without storing plaintext passwords. Existing 5.1 catalogs are upgraded automatically on the first Global Admin sign-in; an upgraded company user whose legacy publishing credential is unavailable is clearly marked for one password reset.

## Legacy compatibility

Global Admin includes **Migrate .avmatrix…**, which creates a new `.nasc` company while leaving the source untouched. Unprotected, password-protected, and account-protected legacy files are supported, and associated `.avclient` payloads migrate to `.nascclient`.

## Windows build

Requirements: Windows and the .NET 8 SDK.

Run `Build-Windows.bat`, or publish each program directly:

```powershell
dotnet publish InNasc.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output publish\win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true

dotnet publish InNasc.GlobalAdmin.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output publish\global-admin\win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

Outputs:

- `publish\win-x64\InNasc.exe`
- `publish\global-admin\win-x64\InNasc.GlobalAdmin.exe`

## Validation

The dependency-free smoke suites verify both sides of the boundary:

```powershell
dotnet run --project tests\InNasc.SmokeTests\InNasc.SmokeTests.csproj --configuration Release
dotnet run --project tests\InNasc.GlobalAdmin.SmokeTests\InNasc.GlobalAdmin.SmokeTests.csproj --configuration Release
```

Coverage includes direct `.nasc` login, Global Admin/company-user separation, multiple company licenses, device-tier enforcement and unlocking, company generation, user/role publishing and deletion, password-reset propagation, 5.1 catalog upgrades, legacy `.avmatrix`/`.avclient` migration, source preservation, and embedded branding.

## Local data and security

InNasc stores local settings and recovery data under `%LOCALAPPDATA%\InNasc`. Integration tokens remain in Windows Credential Manager. Equipment credentials and configuration files may be present in company databases and exported workbooks; protect and share those artifacts accordingly.

Copyright © 2026 InN8 Labs.
