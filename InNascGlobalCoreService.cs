using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InNasc;

internal sealed record InNascGlobalLogin(InNascGlobalSession Session, InNascGlobalCatalog Catalog);

internal static class InNascGlobalCoreService
{
    private const int Iterations = 310000;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static InNascGlobalLogin Create(
        string path,
        string username,
        string displayName,
        string password)
    {
        path = ValidateGlobalPath(path);
        if (File.Exists(path)) throw new IOException("That InNasc Global file already exists.");
        ValidatePassword(password);
        var normalized = NormalizeUsername(username);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var admin = new InNascGlobalAdminRecord
        {
            Username = normalized,
            DisplayName = DisplayName(displayName, normalized)
        };
        var account = new InNascGlobalAccessRecord { Id = admin.Id, Username = normalized };
        SetAdminPassword(account, password, key);
        var envelope = new InNascGlobalEnvelope
        {
            CatalogId = Guid.NewGuid(),
            Accounts = [account]
        };
        var catalog = new InNascGlobalCatalog
        {
            CatalogId = envelope.CatalogId,
            GlobalAdmins = [admin]
        };
        Write(path, envelope, catalog, key);
        return Login(admin, key, catalog);
    }

    public static InNascGlobalLogin SignIn(string path, string username, string password)
    {
        path = ValidateGlobalPath(path);
        var envelope = ReadEnvelope(path);
        var normalized = NormalizeUsername(username);
        var account = envelope.Accounts.FirstOrDefault(candidate =>
            candidate.Enabled &&
            string.Equals(candidate.Username, normalized, StringComparison.OrdinalIgnoreCase));
        if (account is null || !VerifyPassword(account, password))
            throw new MasterAuthorizationException("The username or password is not valid.");

        var key = UnwrapKey(account, password);
        var catalog = ReadCatalog(envelope, key);
        var upgraded = UpgradeCatalog(envelope, catalog);
        var admin = catalog.GlobalAdmins.FirstOrDefault(candidate =>
            candidate.Id == account.Id && candidate.Enabled)
            ?? throw new MasterAuthorizationException(
                "This login belongs to a company user. Open the company's .nasc file in InNasc instead.");
        if (upgraded) Write(path, envelope, catalog, key);
        return Login(admin, key, catalog);
    }

    public static InNascGlobalCatalog Load(string path, InNascGlobalSession session)
    {
        path = ValidateGlobalPath(path);
        var envelope = ReadEnvelope(path);
        var catalog = ReadCatalog(envelope, session.GlobalKey);
        var upgraded = UpgradeCatalog(envelope, catalog);
        RequireSession(envelope, catalog, session);
        if (upgraded) Write(path, envelope, catalog, session.GlobalKey);
        return catalog;
    }

    public static InNascGlobalAdminRecord AddGlobalAdmin(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        string username,
        string displayName,
        string password)
    {
        RequireAdmin(catalog, session);
        ValidatePassword(password);
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        RequireSession(envelope, catalog, session);
        var normalized = NormalizeUsername(username);
        if (envelope.Accounts.Any(candidate =>
                string.Equals(candidate.Username, normalized, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That Global Admin username already exists.");

        var admin = new InNascGlobalAdminRecord
        {
            Username = normalized,
            DisplayName = DisplayName(displayName, normalized)
        };
        var account = new InNascGlobalAccessRecord { Id = admin.Id, Username = normalized };
        SetAdminPassword(account, password, session.GlobalKey);
        envelope.Accounts.Add(account);
        catalog.GlobalAdmins.Add(admin);
        Write(path, envelope, catalog, session.GlobalKey);
        return admin;
    }

    public static void ResetGlobalAdminPassword(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid adminId,
        string password)
    {
        RequireAdmin(catalog, session);
        ValidatePassword(password);
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        RequireSession(envelope, catalog, session);
        var account = envelope.Accounts.FirstOrDefault(candidate => candidate.Id == adminId)
            ?? throw new InvalidOperationException("The selected Global Admin no longer exists.");
        SetAdminPassword(account, password, session.GlobalKey);
        Write(path, envelope, catalog, session.GlobalKey);
    }

    public static void DeleteGlobalAdmin(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid adminId)
    {
        RequireAdmin(catalog, session);
        if (adminId == session.UserId)
            throw new InvalidOperationException("You cannot delete the Global Admin currently signed in.");
        if (catalog.GlobalAdmins.Count(candidate => candidate.Enabled) <= 1)
            throw new InvalidOperationException("At least one enabled Global Admin must remain.");
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        RequireSession(envelope, catalog, session);
        var admin = RequiredAdmin(catalog, adminId);
        var account = envelope.Accounts.FirstOrDefault(candidate => candidate.Id == adminId)
            ?? throw new InvalidOperationException("The selected Global Admin account no longer exists.");
        catalog.GlobalAdmins.Remove(admin);
        envelope.Accounts.Remove(account);
        Write(path, envelope, catalog, session.GlobalKey);
    }

    public static InNascCompanyRecord CreateCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        string companyName,
        string companyPath,
        int deviceLimit = 250)
    {
        RequireAdmin(catalog, session);
        var name = ValidateCompanyName(catalog, companyName);
        ValidateDeviceLimit(deviceLimit);
        var company = new InNascCompanyRecord { Name = name };
        catalog.Companies.Add(company);
        try
        {
            _ = AddCompanyFileCore(company, name, companyPath, deviceLimit);
            WriteCompanyFile(globalPath, catalog, session, company, company.Files[0]);
            Save(globalPath, catalog, session);
            return company;
        }
        catch
        {
            catalog.Companies.Remove(company);
            foreach (var file in company.Files) TryDeleteFile(file.FilePath);
            throw;
        }
    }

    public static InNascCompanyFileRecord AddCompanyFile(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        string fileName,
        string companyPath,
        int deviceLimit)
    {
        RequireAdmin(catalog, session);
        ValidateDeviceLimit(deviceLimit);
        var company = RequiredCompany(catalog, companyId);
        var file = AddCompanyFileCore(company, fileName, companyPath, deviceLimit);
        try
        {
            WriteCompanyFile(globalPath, catalog, session, company, file);
            Save(globalPath, catalog, session);
            return file;
        }
        catch
        {
            company.Files.Remove(file);
            TryDeleteFile(file.FilePath);
            throw;
        }
    }

    public static InNascCompanyRecord MigrateLegacyCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        string companyName,
        string legacyPath,
        string destinationPath,
        string? legacyPasswordOrKey)
    {
        RequireAdmin(catalog, session);
        var name = ValidateCompanyName(catalog, companyName);
        var source = Path.GetFullPath(legacyPath.Trim());
        if (!InNascFileTypes.IsLegacyCompanyPath(source))
            throw new InvalidDataException("Choose a legacy .avmatrix company file.");
        if (!File.Exists(source))
            throw new FileNotFoundException("The legacy company file could not be found.", source);

        var destination = InNascFileTypes.ValidateNewCompanyPath(destinationPath);
        if (File.Exists(destination))
            throw new IOException("That InNasc company file already exists.");
        var imported = PortableDataService.Import(source, legacyPasswordOrKey).Data;
        var company = new InNascCompanyRecord { Name = name };
        var file = AddCompanyFileCore(company, name, destination, 0);
        catalog.Companies.Add(company);
        try
        {
            imported.ProjectName = name;
            imported.MasterAccess = BuildCompanyAccess(
                company, file, session.GlobalKey, imported.MasterAccess);
            PortableDataService.ExportMaster(
                destination,
                ClientSubmatrixService.MasterMetadataOnly(imported),
                CreateCompanyFileSession(session, file));
            MigrateLegacyClientPayloads(
                source,
                destination,
                imported,
                legacyPasswordOrKey,
                file.CompanyKeyBase64);
            Save(globalPath, catalog, session);
            return company;
        }
        catch
        {
            catalog.Companies.Remove(company);
            TryDeleteMigratedCompany(destination);
            throw;
        }
    }

    public static InNascCompanyUserRecord AddCompanyUser(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        string username,
        string displayName,
        string password,
        MasterUserRole role)
    {
        RequireAdmin(catalog, session);
        ValidatePassword(password);
        var company = RequiredCompany(catalog, companyId);
        var normalized = NormalizeUsername(username);
        if (company.Users.Any(candidate =>
                string.Equals(candidate.Username, normalized, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That username already exists in this company.");
        var user = new InNascCompanyUserRecord
        {
            Username = normalized,
            DisplayName = DisplayName(displayName, normalized),
            Role = role,
            HasAllClientAccess = true
        };
        SetCompanyUserPassword(user, password, session.GlobalKey);
        company.Users.Add(user);
        Save(path, catalog, session);
        return user;
    }

    public static void UpdateCompanyUser(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid userId,
        string displayName,
        MasterUserRole role)
    {
        RequireAdmin(catalog, session);
        var company = RequiredCompany(catalog, companyId);
        var user = RequiredCompanyUser(company, userId);
        if (user.Role == MasterUserRole.Owner && role != MasterUserRole.Owner &&
            company.Users.Count(candidate => candidate.Enabled && candidate.Role == MasterUserRole.Owner) <= 1)
            throw new InvalidOperationException(
                "Assign another Company Owner before changing the last Owner's access level.");
        user.DisplayName = DisplayName(displayName, user.Username);
        user.Role = role;
        if (role == MasterUserRole.Owner)
        {
            user.HasAllClientAccess = true;
            user.ClientAccessIds.Clear();
        }
        Save(path, catalog, session);
    }

    public static void ResetCompanyUserPassword(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid userId,
        string password)
    {
        RequireAdmin(catalog, session);
        ValidatePassword(password);
        var company = RequiredCompany(catalog, companyId);
        SetCompanyUserPassword(RequiredCompanyUser(company, userId), password, session.GlobalKey);
        Save(path, catalog, session);
    }

    public static void DeleteCompanyUser(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid userId)
    {
        RequireAdmin(catalog, session);
        var company = RequiredCompany(catalog, companyId);
        var user = RequiredCompanyUser(company, userId);
        if (user.Role == MasterUserRole.Owner &&
            company.Users.Count(candidate => candidate.Enabled && candidate.Role == MasterUserRole.Owner) <= 1)
            throw new InvalidOperationException(
                "Assign another Company Owner before deleting the last Owner.");
        company.Users.Remove(user);
        Save(path, catalog, session);
    }

    public static IReadOnlyList<string> DeleteCompany(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId)
    {
        RequireAdmin(catalog, session);
        var company = RequiredCompany(catalog, companyId);
        var retainedPaths = company.Files.Select(file => file.FilePath).ToList();
        catalog.Companies.Remove(company);
        Save(path, catalog, session);
        return retainedPaths;
    }

    public static string RemoveCompanyFile(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid fileId)
    {
        RequireAdmin(catalog, session);
        var company = RequiredCompany(catalog, companyId);
        if (company.Files.Count(candidate => candidate.Enabled) <= 1)
            throw new InvalidOperationException("A company must keep at least one .nasc file.");
        var file = RequiredCompanyFile(company, fileId);
        company.Files.Remove(file);
        Save(path, catalog, session);
        return file.FilePath;
    }

    public static void SetDeviceLimit(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        Guid companyId,
        Guid fileId,
        int deviceLimit)
    {
        RequireAdmin(catalog, session);
        ValidateDeviceLimit(deviceLimit);
        var company = RequiredCompany(catalog, companyId);
        var file = RequiredCompanyFile(company, fileId);
        var currentCount = GetDeviceCount(file);
        if (deviceLimit > 0 && currentCount > deviceLimit)
            throw new InvalidOperationException(
                $"This .nasc file already contains {currentCount:N0} devices. " +
                $"Choose a limit of at least {currentCount:N0}, or Unlimited.");
        file.DeviceLimit = deviceLimit;
        Save(path, catalog, session);
    }

    public static int GetDeviceCount(InNascCompanyFileRecord file)
    {
        if (!File.Exists(file.FilePath)) return 0;
        var data = PortableDataService.Import(file.FilePath, file.CompanyKeyBase64).Data;
        return DeviceLimitPolicy.CountDevices(data);
    }

    public static MasterAccessControl BuildCompanyAccess(
        InNascCompanyRecord company,
        InNascCompanyFileRecord file,
        string globalKey,
        MasterAccessControl? existing = null)
    {
        var access = new MasterAccessControl
        {
            MasterId = existing?.MasterId ?? Guid.NewGuid(),
            Checkouts = existing?.Checkouts ?? [],
            ClientSubmatrices = existing?.ClientSubmatrices ?? [],
            LicenseId = file.Id,
            LicenseName = string.IsNullOrWhiteSpace(file.Name) ? company.Name : file.Name,
            DeviceLimit = file.DeviceLimit
        };
        foreach (var user in company.Users.Where(candidate => candidate.Enabled && candidate.CredentialReady))
            access.Users.Add(CreatePublishedCompanyUser(
                user, file.CompanyKeyBase64, globalKey));
        return access;
    }

    public static MasterSession CreateCompanyFileSession(
        InNascGlobalSession globalSession,
        InNascCompanyFileRecord file) =>
        new(globalSession.UserId, globalSession.Username, globalSession.DisplayName,
            MasterUserRole.Owner, file.CompanyKeyBase64);

    public static void Save(
        string path,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session)
    {
        RequireAdmin(catalog, session);
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        RequireSession(envelope, catalog, session);
        Write(path, envelope, catalog, session.GlobalKey);
    }

    private static InNascCompanyFileRecord AddCompanyFileCore(
        InNascCompanyRecord company,
        string fileName,
        string companyPath,
        int deviceLimit)
    {
        var path = InNascFileTypes.ValidateNewCompanyPath(companyPath);
        if (File.Exists(path)) throw new IOException("That company file already exists.");
        if (company.Files.Any(candidate =>
                string.Equals(candidate.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That .nasc file is already assigned to this company.");
        var name = string.IsNullOrWhiteSpace(fileName)
            ? Path.GetFileNameWithoutExtension(path)
            : fileName.Trim();
        var file = new InNascCompanyFileRecord
        {
            Name = name,
            FilePath = path,
            CompanyKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            DeviceLimit = deviceLimit
        };
        company.Files.Add(file);
        return file;
    }

    private static void WriteCompanyFile(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        InNascCompanyRecord company,
        InNascCompanyFileRecord file)
    {
        RequireAdmin(catalog, session);
        _ = globalPath;
        var data = new AppData
        {
            ProjectName = company.Name,
            Clients = [],
            MasterAccess = BuildCompanyAccess(company, file, session.GlobalKey)
        };
        PortableDataService.ExportMaster(file.FilePath, data, CreateCompanyFileSession(session, file));
        InNascCompanyEnvelopeMetadataService.Apply(
            file.FilePath, company.Name, file.Name, file.Id, file.DeviceLimit, company.LogoBase64);
    }

    private static bool UpgradeCatalog(
        InNascGlobalEnvelope envelope,
        InNascGlobalCatalog catalog)
    {
        catalog.GlobalAdmins ??= [];
        catalog.Companies ??= [];
        catalog.Users ??= [];
        var changed = false;

        if (catalog.GlobalAdmins.Count == 0 && catalog.Users.Count > 0)
        {
            foreach (var legacyAdmin in catalog.Users.Where(user => user.IsGlobalAdmin))
            {
                catalog.GlobalAdmins.Add(new InNascGlobalAdminRecord
                {
                    Id = legacyAdmin.Id,
                    Username = legacyAdmin.Username,
                    DisplayName = legacyAdmin.DisplayName,
                    Enabled = legacyAdmin.Enabled,
                    CreatedUtc = legacyAdmin.CreatedUtc
                });
            }
            changed = true;
        }

        foreach (var company in catalog.Companies)
        {
            company.Name ??= string.Empty;
            company.LogoBase64 ??= string.Empty;
            company.Notes ??= string.Empty;
            company.Files ??= [];
            company.Users ??= [];
            if (company.Files.Count == 0 && !string.IsNullOrWhiteSpace(company.FilePath))
            {
                company.Files.Add(new InNascCompanyFileRecord
                {
                    Name = company.Name,
                    FilePath = company.FilePath,
                    CompanyKeyBase64 = company.CompanyKeyBase64,
                    DeviceLimit = 0,
                    CreatedUtc = company.CreatedUtc
                });
                changed = true;
            }

            if (company.Users.Count == 0 && catalog.Users.Count > 0)
            {
                foreach (var legacyUser in catalog.Users.Where(user =>
                             user.Enabled && (user.IsGlobalAdmin ||
                                 user.Companies.Any(membership => membership.CompanyId == company.Id))))
                {
                    var membership = legacyUser.Companies.FirstOrDefault(item => item.CompanyId == company.Id);
                    var account = envelope.Accounts.FirstOrDefault(item => item.Id == legacyUser.Id);
                    if (account is null) continue;
                    company.Users.Add(new InNascCompanyUserRecord
                    {
                        Username = legacyUser.Username,
                        DisplayName = legacyUser.DisplayName,
                        Role = legacyUser.IsGlobalAdmin ? MasterUserRole.Owner : membership!.Role,
                        PasswordSaltBase64 = account.PasswordSaltBase64,
                        PasswordHashBase64 = account.PasswordHashBase64,
                        PasswordIterations = account.PasswordIterations,
                        CompanyKeySaltBase64 = account.CompanyKeySaltBase64,
                        CompanyKeyCredentialNonceBase64 = account.CompanyKeyCredentialNonceBase64,
                        CompanyKeyCredentialCiphertextBase64 = account.CompanyKeyCredentialCiphertextBase64,
                        CompanyKeyCredentialTagBase64 = account.CompanyKeyCredentialTagBase64,
                        HasAllClientAccess = legacyUser.IsGlobalAdmin || membership!.HasAllClientAccess,
                        ClientAccessIds = legacyUser.IsGlobalAdmin ? [] : membership!.ClientAccessIds.ToList(),
                        Enabled = true,
                        CreatedUtc = legacyUser.CreatedUtc
                    });
                }
                changed = true;
            }
        }

        if (catalog.Users.Count > 0)
        {
            var adminIds = catalog.GlobalAdmins.Select(admin => admin.Id).ToHashSet();
            envelope.Accounts.RemoveAll(account => !adminIds.Contains(account.Id));
            catalog.Users.Clear();
            changed = true;
        }
        if (catalog.FormatVersion < 2)
        {
            catalog.FormatVersion = 2;
            changed = true;
        }
        return changed;
    }

    private static void SetAdminPassword(
        InNascGlobalAccessRecord account,
        string password,
        string globalKey)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var wrapSalt = RandomNumberGenerator.GetBytes(16);
        var wrapKey = Rfc2898DeriveBytes.Pbkdf2(
            password, wrapSalt, Iterations, HashAlgorithmName.SHA256, 32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(globalKey);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(wrapKey, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag);
            account.PasswordSaltBase64 = Convert.ToBase64String(salt);
            account.PasswordHashBase64 = Convert.ToBase64String(hash);
            account.PasswordIterations = Iterations;
            account.GlobalKeySaltBase64 = Convert.ToBase64String(wrapSalt);
            account.GlobalKeyNonceBase64 = Convert.ToBase64String(nonce);
            account.GlobalKeyCiphertextBase64 = Convert.ToBase64String(cipher);
            account.GlobalKeyTagBase64 = Convert.ToBase64String(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(wrapKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static void SetCompanyUserPassword(
        InNascCompanyUserRecord user,
        string password,
        string globalKey)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var companySalt = RandomNumberGenerator.GetBytes(16);
        var credential = Rfc2898DeriveBytes.Pbkdf2(
            password, companySalt, Iterations, HashAlgorithmName.SHA256, 32);
        var globalEncryptionKey = Convert.FromBase64String(globalKey);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[credential.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(globalEncryptionKey, tag.Length);
            aes.Encrypt(nonce, credential, cipher, tag);
            user.PasswordSaltBase64 = Convert.ToBase64String(salt);
            user.PasswordHashBase64 = Convert.ToBase64String(hash);
            user.PasswordIterations = Iterations;
            user.CompanyKeySaltBase64 = Convert.ToBase64String(companySalt);
            user.CompanyKeyCredentialNonceBase64 = Convert.ToBase64String(nonce);
            user.CompanyKeyCredentialCiphertextBase64 = Convert.ToBase64String(cipher);
            user.CompanyKeyCredentialTagBase64 = Convert.ToBase64String(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(credential);
            CryptographicOperations.ZeroMemory(globalEncryptionKey);
        }
    }

    private static MasterUserRecord CreatePublishedCompanyUser(
        InNascCompanyUserRecord user,
        string companyKey,
        string globalKey)
    {
        var credential = UnwrapCompanyCredential(user, globalKey);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(companyKey);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(credential, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
            return new MasterUserRecord
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.Role,
                PasswordSaltBase64 = user.PasswordSaltBase64,
                PasswordHashBase64 = user.PasswordHashBase64,
                PasswordIterations = user.PasswordIterations,
                MasterKeySaltBase64 = user.CompanyKeySaltBase64,
                MasterKeyNonceBase64 = Convert.ToBase64String(nonce),
                MasterKeyCiphertextBase64 = Convert.ToBase64String(ciphertext),
                MasterKeyTagBase64 = Convert.ToBase64String(tag),
                Enabled = user.Enabled,
                HasAllClientAccess = user.Role == MasterUserRole.Owner || user.HasAllClientAccess,
                ClientAccessIds = user.Role == MasterUserRole.Owner || user.HasAllClientAccess
                    ? []
                    : user.ClientAccessIds.Distinct().ToList(),
                CreatedUtc = user.CreatedUtc
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] UnwrapCompanyCredential(
        InNascCompanyUserRecord user,
        string globalKey)
    {
        var key = Convert.FromBase64String(globalKey);
        var ciphertext = Convert.FromBase64String(user.CompanyKeyCredentialCiphertextBase64);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                Convert.FromBase64String(user.CompanyKeyCredentialNonceBase64),
                ciphertext,
                Convert.FromBase64String(user.CompanyKeyCredentialTagBase64),
                plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException(
                $"The company login credential for {user.Username} is unreadable.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static bool VerifyPassword(InNascGlobalAccessRecord account, string password)
    {
        try
        {
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                Convert.FromBase64String(account.PasswordSaltBase64),
                account.PasswordIterations,
                HashAlgorithmName.SHA256,
                32);
            try
            {
                return CryptographicOperations.FixedTimeEquals(
                    actual, Convert.FromBase64String(account.PasswordHashBase64));
            }
            finally { CryptographicOperations.ZeroMemory(actual); }
        }
        catch { return false; }
    }

    private static string UnwrapKey(InNascGlobalAccessRecord account, string password)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            Convert.FromBase64String(account.GlobalKeySaltBase64),
            account.PasswordIterations,
            HashAlgorithmName.SHA256,
            32);
        var cipher = Convert.FromBase64String(account.GlobalKeyCiphertextBase64);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                Convert.FromBase64String(account.GlobalKeyNonceBase64),
                cipher,
                Convert.FromBase64String(account.GlobalKeyTagBase64),
                plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            throw new MasterAuthorizationException("The username or password is not valid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static string ValidateGlobalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose an InNasc Global file.");
        var full = Path.GetFullPath(path.Trim());
        if (!string.Equals(
                Path.GetExtension(full),
                InNascFileTypes.GlobalExtension,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("InNasc Global files must use the .nascglobal extension.");
        return full;
    }

    private static InNascGlobalEnvelope ReadEnvelope(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The InNasc Global file could not be found.", path);
        var result = JsonSerializer.Deserialize<InNascGlobalEnvelope>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("The InNasc Global file is unreadable.");
        if (result.Format != "InNasc Global" || result.FormatVersion != 1)
            throw new InvalidDataException("This InNasc Global format is not supported.");
        result.Accounts ??= [];
        return result;
    }

    private static InNascGlobalCatalog ReadCatalog(InNascGlobalEnvelope envelope, string key)
    {
        var protectedBytes = Convert.FromBase64String(envelope.PayloadBase64);
        var plain = JwePasswordProtection.Unprotect(protectedBytes, key);
        var catalog = JsonSerializer.Deserialize<InNascGlobalCatalog>(plain, Json)
            ?? throw new InvalidDataException("The InNasc Global catalog is unreadable.");
        if (catalog.CatalogId != envelope.CatalogId)
            throw new InvalidDataException(
                "The InNasc Global catalog identity does not match its envelope.");
        return catalog;
    }

    private static void Write(
        string path,
        InNascGlobalEnvelope envelope,
        InNascGlobalCatalog catalog,
        string globalKey)
    {
        envelope.PayloadBase64 = Convert.ToBase64String(
            JwePasswordProtection.Protect(
                JsonSerializer.SerializeToUtf8Bytes(catalog, Json), globalKey));
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(envelope, Json));
            File.Move(temp, full, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void RequireAdmin(InNascGlobalCatalog catalog, InNascGlobalSession session)
    {
        if (!session.IsGlobalAdmin ||
            !catalog.GlobalAdmins.Any(admin => admin.Id == session.UserId && admin.Enabled))
            throw new MasterAuthorizationException("Global Admin access is required.");
    }

    private static void RequireSession(
        InNascGlobalEnvelope envelope,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session)
    {
        if (!envelope.Accounts.Any(account => account.Id == session.UserId && account.Enabled) ||
            !catalog.GlobalAdmins.Any(admin => admin.Id == session.UserId && admin.Enabled))
            throw new MasterAuthorizationException("This InNasc Global session is no longer valid.");
    }

    private static InNascGlobalAdminRecord RequiredAdmin(
        InNascGlobalCatalog catalog,
        Guid adminId) =>
        catalog.GlobalAdmins.FirstOrDefault(admin => admin.Id == adminId)
        ?? throw new InvalidOperationException("The selected Global Admin no longer exists.");

    private static InNascCompanyRecord RequiredCompany(
        InNascGlobalCatalog catalog,
        Guid companyId) =>
        catalog.Companies.FirstOrDefault(company => company.Id == companyId && company.Enabled)
        ?? throw new InvalidOperationException("The selected company no longer exists.");

    private static InNascCompanyFileRecord RequiredCompanyFile(
        InNascCompanyRecord company,
        Guid fileId) =>
        company.Files.FirstOrDefault(file => file.Id == fileId && file.Enabled)
        ?? throw new InvalidOperationException("The selected .nasc file no longer exists.");

    private static InNascCompanyUserRecord RequiredCompanyUser(
        InNascCompanyRecord company,
        Guid userId) =>
        company.Users.FirstOrDefault(user => user.Id == userId)
        ?? throw new InvalidOperationException("The selected company user no longer exists.");

    private static string ValidateCompanyName(
        InNascGlobalCatalog catalog,
        string companyName)
    {
        var name = companyName.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Enter a company name.");
        if (catalog.Companies.Any(company =>
                string.Equals(company.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A company with that name already exists.");
        return name;
    }

    private static string NormalizeUsername(string value)
    {
        var result = value.Trim();
        if (result.Length < 2)
            throw new InvalidOperationException("Enter a username with at least two characters.");
        return result;
    }

    private static string DisplayName(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static void ValidatePassword(string password)
    {
        if (password.Length < 8)
            throw new InvalidOperationException("Passwords must contain at least 8 characters.");
    }

    private static void ValidateDeviceLimit(int deviceLimit)
    {
        if (deviceLimit < 0)
            throw new InvalidOperationException("Device limits cannot be negative.");
    }

    private static void MigrateLegacyClientPayloads(
        string legacyPath,
        string destinationPath,
        AppData imported,
        string? legacyPasswordOrKey,
        string companyKey)
    {
        foreach (var client in imported.Clients)
        {
            var sourceClientPath = ClientSubmatrixService.SharedClientPath(legacyPath, client.Id);
            if (!File.Exists(sourceClientPath)) continue;
            var package = PortableDataService.Import(sourceClientPath, legacyPasswordOrKey).Data;
            var destinationClientPath = ClientSubmatrixService.SharedClientPath(destinationPath, client.Id);
            PortableDataService.Export(destinationClientPath, package, companyKey);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static void TryDeleteMigratedCompany(string destinationPath)
    {
        try
        {
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            var payloadDirectory = ClientSubmatrixService.SharedDirectory(destinationPath);
            if (Directory.Exists(payloadDirectory)) Directory.Delete(payloadDirectory, true);
        }
        catch
        {
            // Preserve the original migration error; partial output can be removed manually.
        }
    }

    private static InNascGlobalLogin Login(
        InNascGlobalAdminRecord admin,
        string key,
        InNascGlobalCatalog catalog) =>
        new(new InNascGlobalSession(
            admin.Id, admin.Username, admin.DisplayName, true, key), catalog);

}
