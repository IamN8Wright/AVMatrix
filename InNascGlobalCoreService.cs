using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AVMatrixStudio;

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
                MasterAccess = BuildCompanyAccess(catalog, company.Id)
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

    private static MasterAccessControl BuildCompanyAccess(InNascGlobalCatalog catalog, Guid companyId)
    {
        var access = new MasterAccessControl();
        foreach (var user in catalog.Users.Where(x => x.Enabled))
        {
            var membership = user.Companies.FirstOrDefault(x => x.CompanyId == companyId);
            if (!user.IsGlobalAdmin && membership is null) continue;
            access.Users.Add(new MasterUserRecord
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.IsGlobalAdmin ? MasterUserRole.Owner : membership!.Role,
                Enabled = true,
                HasAllClientAccess = user.IsGlobalAdmin || membership!.HasAllClientAccess,
                ClientAccessIds = user.IsGlobalAdmin ? [] : membership!.ClientAccessIds.ToList()
            });
        }
        return access;
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
    }

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
