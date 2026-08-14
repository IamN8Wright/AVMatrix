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

    public static InNascGlobalLogin Create(string path, string username, string displayName, string password)
    {
        path = ValidateGlobalPath(path);
        if (File.Exists(path)) throw new IOException("That InNasc Global file already exists.");
        ValidatePassword(password);
        var name = NormalizeUsername(username);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var id = Guid.NewGuid();
        var envelope = new InNascGlobalEnvelope { CatalogId = Guid.NewGuid() };
        var account = new InNascGlobalAccessRecord { Id = id, Username = name };
        SetPassword(account, password, key);
        envelope.Accounts.Add(account);
        var profile = new InNascGlobalUserRecord
        {
            Id = id,
            Username = name,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim(),
            IsGlobalAdmin = true
        };
        var catalog = new InNascGlobalCatalog { CatalogId = envelope.CatalogId, Users = [profile] };
        Write(path, envelope, catalog, key);
        return Login(profile, key, catalog);
    }

    public static InNascGlobalLogin SignIn(string path, string username, string password)
    {
        path = ValidateGlobalPath(path);
        var envelope = ReadEnvelope(path);
        var normalized = NormalizeUsername(username);
        var account = envelope.Accounts.FirstOrDefault(x =>
            x.Enabled && string.Equals(x.Username, normalized, StringComparison.OrdinalIgnoreCase));
        if (account is null || !VerifyPassword(account, password))
            throw new MasterAuthorizationException("The username or password is not valid.");
        var key = UnwrapKey(account, password);
        var catalog = ReadCatalog(envelope, key);
        var profile = catalog.Users.FirstOrDefault(x => x.Id == account.Id && x.Enabled)
            ?? throw new MasterAuthorizationException("This InNasc account is disabled or no longer exists.");
        if (!HasCompanyCredential(account))
        {
            SetCompanyCredential(account, password, key);
            Write(path, envelope, catalog, key);
        }
        return Login(profile, key, catalog);
    }

    public static InNascGlobalCatalog Load(string path, InNascGlobalSession session)
    {
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        var catalog = ReadCatalog(envelope, session.GlobalKey);
        RequireSession(envelope, catalog, session);
        return catalog;
    }

    public static InNascGlobalUserRecord AddUser(
        string path, InNascGlobalCatalog catalog, InNascGlobalSession session,
        string username, string displayName, string temporaryPassword, bool globalAdmin)
    {
        RequireAdmin(catalog, session);
        ValidatePassword(temporaryPassword);
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        RequireSession(envelope, catalog, session);
        var normalized = NormalizeUsername(username);
        if (envelope.Accounts.Any(x => string.Equals(x.Username, normalized, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That username already exists.");
        var id = Guid.NewGuid();
        var account = new InNascGlobalAccessRecord { Id = id, Username = normalized };
        SetPassword(account, temporaryPassword, session.GlobalKey);
        envelope.Accounts.Add(account);
        var user = new InNascGlobalUserRecord
        {
            Id = id,
            Username = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim(),
            IsGlobalAdmin = globalAdmin
        };
        catalog.Users.Add(user);
        Write(path, envelope, catalog, session.GlobalKey);
        return user;
    }

    public static void ResetPassword(
        string path, InNascGlobalCatalog catalog, InNascGlobalSession session, Guid userId, string password)
    {
        RequireAdmin(catalog, session);
        ValidatePassword(password);
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        RequireSession(envelope, catalog, session);
        var account = envelope.Accounts.FirstOrDefault(x => x.Id == userId)
            ?? throw new InvalidOperationException("The selected account no longer exists.");
        SetPassword(account, password, session.GlobalKey);
        Write(path, envelope, catalog, session.GlobalKey);
    }

    public static InNascCompanyRecord CreateCompany(
        string globalPath, InNascGlobalCatalog catalog, InNascGlobalSession session,
        string companyName, string companyPath)
    {
        RequireAdmin(catalog, session);
        var name = companyName.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Enter a company name.");
        if (catalog.Companies.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A company with that name already exists.");
        var path = InNascFileTypes.ValidateNewCompanyPath(companyPath);
        if (File.Exists(path)) throw new IOException("That company file already exists.");
        var company = new InNascCompanyRecord
        {
            Name = name,
            FilePath = path,
            CompanyKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        catalog.Companies.Add(company);
        try
        {
            var masterSession = CreateCompanySession(session, catalog, company);
            var data = new AppData
            {
                ProjectName = name,
                Clients = [],
                MasterAccess = BuildCompanyAccess(globalPath, catalog, session, company)
            };
            PortableDataService.ExportMaster(path, data, masterSession);
            Save(globalPath, catalog, session);
            return company;
        }
        catch
        {
            catalog.Companies.Remove(company);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
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
        var company = new InNascCompanyRecord
        {
            Name = name,
            FilePath = destination,
            CompanyKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        catalog.Companies.Add(company);
        try
        {
            imported.ProjectName = name;
            imported.MasterAccess = BuildCompanyAccess(globalPath, catalog, session, company);
            var companySession = CreateCompanySession(session, catalog, company);
            PortableDataService.ExportMaster(
                destination,
                ClientSubmatrixService.MasterMetadataOnly(imported),
                companySession);
            MigrateLegacyClientPayloads(
                source,
                destination,
                imported,
                legacyPasswordOrKey,
                company.CompanyKeyBase64);
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

    public static void SetMembership(
        string path, InNascGlobalCatalog catalog, InNascGlobalSession session,
        Guid userId, Guid companyId, bool assigned, MasterUserRole role)
    {
        RequireAdmin(catalog, session);
        var user = catalog.Users.FirstOrDefault(x => x.Id == userId)
            ?? throw new InvalidOperationException("The selected user no longer exists.");
        if (user.IsGlobalAdmin) return;
        var membership = user.Companies.FirstOrDefault(x => x.CompanyId == companyId);
        if (!assigned)
        {
            if (membership is not null) user.Companies.Remove(membership);
        }
        else if (membership is null)
            user.Companies.Add(new InNascCompanyMembership { CompanyId = companyId, Role = role });
        else membership.Role = role;
        Save(path, catalog, session);
    }

    public static IReadOnlyList<InNascCompanyRecord> CompaniesFor(
        InNascGlobalCatalog catalog, InNascGlobalSession session)
    {
        var user = catalog.Users.First(x => x.Id == session.UserId);
        var allowed = user.Companies.Select(x => x.CompanyId).ToHashSet();
        return catalog.Companies
            .Where(x => x.Enabled && (user.IsGlobalAdmin || allowed.Contains(x.Id)))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static MasterSession CreateCompanySession(
        InNascGlobalSession global, InNascGlobalCatalog catalog, InNascCompanyRecord company)
    {
        var user = catalog.Users.First(x => x.Id == global.UserId);
        var membership = user.Companies.FirstOrDefault(x => x.CompanyId == company.Id);
        if (!user.IsGlobalAdmin && membership is null)
            throw new MasterAuthorizationException("This account is not assigned to that company.");
        return new MasterSession(
            user.Id, user.Username, user.DisplayName,
            user.IsGlobalAdmin ? MasterUserRole.Owner : membership!.Role,
            company.CompanyKeyBase64);
    }

    public static void Save(string path, InNascGlobalCatalog catalog, InNascGlobalSession session)
    {
        RequireAdmin(catalog, session);
        var envelope = ReadEnvelope(ValidateGlobalPath(path));
        RequireSession(envelope, catalog, session);
        Write(path, envelope, catalog, session.GlobalKey);
    }

    public static MasterAccessControl BuildCompanyAccess(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession session,
        InNascCompanyRecord company,
        MasterAccessControl? existing = null)
    {
        RequireAdmin(catalog, session);
        var envelope = ReadEnvelope(ValidateGlobalPath(globalPath));
        RequireSession(envelope, catalog, session);
        var access = new MasterAccessControl
        {
            MasterId = existing?.MasterId ?? Guid.NewGuid(),
            Checkouts = existing?.Checkouts ?? [],
            ClientSubmatrices = existing?.ClientSubmatrices ?? []
        };
        foreach (var user in catalog.Users.Where(x => x.Enabled))
        {
            var membership = user.Companies.FirstOrDefault(x => x.CompanyId == company.Id);
            if (!user.IsGlobalAdmin && membership is null) continue;
            var account = envelope.Accounts.FirstOrDefault(x => x.Id == user.Id && x.Enabled)
                ?? throw new InvalidOperationException(
                    $"The Global account for {user.DisplayName} is missing or disabled.");
            if (!HasCompanyCredential(account))
                continue;
            access.Users.Add(CreateCompanyUser(
                account,
                user,
                user.IsGlobalAdmin ? MasterUserRole.Owner : membership!.Role,
                user.IsGlobalAdmin || membership!.HasAllClientAccess,
                user.IsGlobalAdmin ? [] : membership!.ClientAccessIds,
                company.CompanyKeyBase64,
                session.GlobalKey));
        }
        return access;
    }

    public static bool HasCompanyCredential(
        string globalPath,
        Guid userId)
    {
        var envelope = ReadEnvelope(ValidateGlobalPath(globalPath));
        var account = envelope.Accounts.FirstOrDefault(x => x.Id == userId);
        return account is not null && HasCompanyCredential(account);
    }

    private static string ValidateCompanyName(InNascGlobalCatalog catalog, string companyName)
    {
        var name = companyName.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Enter a company name.");
        if (catalog.Companies.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A company with that name already exists.");
        return name;
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

    private static InNascGlobalLogin Login(InNascGlobalUserRecord user, string key, InNascGlobalCatalog catalog) =>
        new(new InNascGlobalSession(user.Id, user.Username, user.DisplayName, user.IsGlobalAdmin, key), catalog);

    private static string ValidateGlobalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Choose an InNasc Global file.");
        var full = Path.GetFullPath(path.Trim());
        if (!string.Equals(Path.GetExtension(full), InNascFileTypes.GlobalExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("InNasc Global files must use the .nascglobal extension.");
        return full;
    }

    private static InNascGlobalEnvelope ReadEnvelope(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The InNasc Global file could not be found.", path);
        var result = JsonSerializer.Deserialize<InNascGlobalEnvelope>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("The InNasc Global file is unreadable.");
        if (result.Format != "InNasc Global" || result.FormatVersion != 1)
            throw new InvalidDataException("This InNasc Global format is not supported.");
        return result;
    }

    private static InNascGlobalCatalog ReadCatalog(InNascGlobalEnvelope envelope, string key)
    {
        var protectedBytes = Convert.FromBase64String(envelope.PayloadBase64);
        var plain = JwePasswordProtection.Unprotect(protectedBytes, key);
        var catalog = JsonSerializer.Deserialize<InNascGlobalCatalog>(plain, Json)
            ?? throw new InvalidDataException("The InNasc Global catalog is unreadable.");
        if (catalog.CatalogId != envelope.CatalogId)
            throw new InvalidDataException("The InNasc Global catalog identity does not match its envelope.");
        return catalog;
    }

    private static void Write(
        string path, InNascGlobalEnvelope envelope, InNascGlobalCatalog catalog, string globalKey)
    {
        envelope.PayloadBase64 = Convert.ToBase64String(
            JwePasswordProtection.Protect(JsonSerializer.SerializeToUtf8Bytes(catalog, Json), globalKey));
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(envelope, Json));
        File.Move(temp, full, true);
    }

    private static void RequireAdmin(InNascGlobalCatalog catalog, InNascGlobalSession session)
    {
        var user = catalog.Users.FirstOrDefault(x => x.Id == session.UserId && x.Enabled);
        if (user is null || !user.IsGlobalAdmin)
            throw new MasterAuthorizationException("Global Admin access is required.");
    }

    private static void RequireSession(
        InNascGlobalEnvelope envelope, InNascGlobalCatalog catalog, InNascGlobalSession session)
    {
        if (!envelope.Accounts.Any(x => x.Id == session.UserId && x.Enabled) ||
            !catalog.Users.Any(x => x.Id == session.UserId && x.Enabled))
            throw new MasterAuthorizationException("This InNasc Global session is no longer valid.");
    }

    private static string NormalizeUsername(string value)
    {
        var result = value.Trim();
        if (result.Length < 2) throw new InvalidOperationException("Enter a username with at least two characters.");
        return result;
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 8) throw new InvalidOperationException("Passwords must contain at least 8 characters.");
    }

    private static void SetPassword(InNascGlobalAccessRecord account, string password, string globalKey)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var wrapSalt = RandomNumberGenerator.GetBytes(16);
        var wrapKey = Rfc2898DeriveBytes.Pbkdf2(password, wrapSalt, Iterations, HashAlgorithmName.SHA256, 32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(globalKey);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(wrapKey, tag.Length)) aes.Encrypt(nonce, plain, cipher, tag);
        account.PasswordSaltBase64 = Convert.ToBase64String(salt);
        account.PasswordHashBase64 = Convert.ToBase64String(hash);
        account.PasswordIterations = Iterations;
        account.GlobalKeySaltBase64 = Convert.ToBase64String(wrapSalt);
        account.GlobalKeyNonceBase64 = Convert.ToBase64String(nonce);
        account.GlobalKeyCiphertextBase64 = Convert.ToBase64String(cipher);
        account.GlobalKeyTagBase64 = Convert.ToBase64String(tag);
        CryptographicOperations.ZeroMemory(wrapKey);
        SetCompanyCredential(account, password, globalKey);
    }

    private static void SetCompanyCredential(
        InNascGlobalAccessRecord account,
        string password,
        string globalKey)
    {
        var companySalt = RandomNumberGenerator.GetBytes(16);
        var companyKeyCredential = Rfc2898DeriveBytes.Pbkdf2(
            password,
            companySalt,
            account.PasswordIterations,
            HashAlgorithmName.SHA256,
            32);
        var globalEncryptionKey = Convert.FromBase64String(globalKey);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[companyKeyCredential.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(globalEncryptionKey, tag.Length);
            aes.Encrypt(nonce, companyKeyCredential, cipher, tag);
            account.CompanyKeySaltBase64 = Convert.ToBase64String(companySalt);
            account.CompanyKeyCredentialNonceBase64 = Convert.ToBase64String(nonce);
            account.CompanyKeyCredentialCiphertextBase64 = Convert.ToBase64String(cipher);
            account.CompanyKeyCredentialTagBase64 = Convert.ToBase64String(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(companyKeyCredential);
            CryptographicOperations.ZeroMemory(globalEncryptionKey);
        }
    }

    private static MasterUserRecord CreateCompanyUser(
        InNascGlobalAccessRecord account,
        InNascGlobalUserRecord user,
        MasterUserRole role,
        bool hasAllClientAccess,
        IEnumerable<Guid> clientAccessIds,
        string companyKey,
        string globalKey)
    {
        if (!HasCompanyCredential(account))
            throw new InvalidOperationException(
                $"Reset the password for {user.DisplayName} before publishing company access. " +
                "This upgrades the 5.0.x account for direct .nasc login.");

        var credential = UnwrapCompanyCredential(account, globalKey);
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
                Role = role,
                PasswordSaltBase64 = account.PasswordSaltBase64,
                PasswordHashBase64 = account.PasswordHashBase64,
                PasswordIterations = account.PasswordIterations,
                MasterKeySaltBase64 = account.CompanyKeySaltBase64,
                MasterKeyNonceBase64 = Convert.ToBase64String(nonce),
                MasterKeyCiphertextBase64 = Convert.ToBase64String(ciphertext),
                MasterKeyTagBase64 = Convert.ToBase64String(tag),
                Enabled = true,
                HasAllClientAccess = hasAllClientAccess,
                ClientAccessIds = hasAllClientAccess ? [] : clientAccessIds.Distinct().ToList()
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] UnwrapCompanyCredential(
        InNascGlobalAccessRecord account,
        string globalKey)
    {
        var key = Convert.FromBase64String(globalKey);
        var ciphertext = Convert.FromBase64String(account.CompanyKeyCredentialCiphertextBase64);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                Convert.FromBase64String(account.CompanyKeyCredentialNonceBase64),
                ciphertext,
                Convert.FromBase64String(account.CompanyKeyCredentialTagBase64),
                plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException(
                $"The company login credential for {account.Username} is unreadable.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool HasCompanyCredential(InNascGlobalAccessRecord account) =>
        !string.IsNullOrWhiteSpace(account.CompanyKeySaltBase64) &&
        !string.IsNullOrWhiteSpace(account.CompanyKeyCredentialNonceBase64) &&
        !string.IsNullOrWhiteSpace(account.CompanyKeyCredentialCiphertextBase64) &&
        !string.IsNullOrWhiteSpace(account.CompanyKeyCredentialTagBase64);

    private static bool VerifyPassword(InNascGlobalAccessRecord account, string password)
    {
        try
        {
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password, Convert.FromBase64String(account.PasswordSaltBase64),
                account.PasswordIterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(account.PasswordHashBase64));
        }
        catch { return false; }
    }

    private static string UnwrapKey(InNascGlobalAccessRecord account, string password)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password, Convert.FromBase64String(account.GlobalKeySaltBase64),
            account.PasswordIterations, HashAlgorithmName.SHA256, 32);
        var cipher = Convert.FromBase64String(account.GlobalKeyCiphertextBase64);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                Convert.FromBase64String(account.GlobalKeyNonceBase64), cipher,
                Convert.FromBase64String(account.GlobalKeyTagBase64), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            throw new MasterAuthorizationException("The username or password is not valid.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
