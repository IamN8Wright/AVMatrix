using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AVMatrixStudio;

internal sealed record MasterSession(
    Guid UserId,
    string Username,
    string DisplayName,
    MasterUserRole Role,
    string MasterKey)
{
    public bool CanWrite => Role is MasterUserRole.Owner or MasterUserRole.Tech;
    public bool IsOwner => Role == MasterUserRole.Owner;
}

internal static class MasterAccessService
{
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int DefaultIterations = 310000;

    public static MasterSession SignIn(
        MasterAccessControl access,
        string username,
        string password)
    {
        if (!access.IsConfigured)
            throw new MasterAuthorizationException(
                "This master does not have an Owner account yet. Set up the first Owner to continue.");
        var normalized = NormalizeUsername(username);
        var user = access.Users.FirstOrDefault(candidate =>
            string.Equals(candidate.Username, normalized, StringComparison.OrdinalIgnoreCase));
        if (user is null || !user.Enabled || !VerifyPassword(user, password))
            throw new MasterAuthorizationException("The username or password is not valid.");
        if (access.Users.Any(candidate =>
                !string.IsNullOrWhiteSpace(candidate.MasterKeyCiphertextBase64)) &&
            string.IsNullOrWhiteSpace(user.MasterKeyCiphertextBase64))
            throw new MasterAuthorizationException(
                "This legacy account has not been migrated for account-based unlocking. " +
                "Ask the Owner to reset its password.");
        return Session(user, UnwrapMasterKey(user, password));
    }

    public static MasterSession UpgradeLegacyOwner(
        MasterAccessControl access,
        string username,
        string password)
    {
        var session = SignIn(access, username, password);
        if (!session.IsOwner)
            throw new MasterAuthorizationException(
                "The Owner must sign in first to migrate this legacy master to account-based unlocking.");
        if (!string.IsNullOrWhiteSpace(session.MasterKey)) return session;
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var owner = RequiredUser(access, session.UserId);
        SetPassword(owner, password, masterKey);
        return Session(owner, masterKey);
    }

    public static bool UsesAccountUnlock(MasterAccessControl access) =>
        access.Users.Any(user => !string.IsNullOrWhiteSpace(user.MasterKeyCiphertextBase64));

    public static MasterUserRecord CreateInitialOwner(
        MasterAccessControl access,
        string username,
        string displayName,
        string password)
    {
        if (access.IsConfigured)
            throw new InvalidOperationException("This master already has an Owner account.");
        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return AddUserCore(
            access, username, displayName, password, MasterUserRole.Owner, masterKey);
    }

    public static MasterUserRecord AddUser(
        MasterAccessControl access,
        MasterSession session,
        string username,
        string displayName,
        string password,
        MasterUserRole role)
    {
        RequireOwner(access, session);
        return AddUserCore(access, username, displayName, password, role, session.MasterKey);
    }

    public static void UpdateUser(
        MasterAccessControl access,
        MasterSession session,
        Guid userId,
        string displayName,
        MasterUserRole role,
        bool enabled)
    {
        RequireOwner(access, session);
        var user = RequiredUser(access, userId);
        if (user.Role == MasterUserRole.Owner &&
            (role != MasterUserRole.Owner || !enabled) &&
            access.Users.Count(candidate => candidate.Enabled &&
                candidate.Role == MasterUserRole.Owner) <= 1)
            throw new InvalidOperationException("A master must keep at least one enabled Owner account.");
        user.DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? user.Username
            : displayName.Trim();
        user.Role = role;
        if (role == MasterUserRole.Owner)
        {
            user.HasAllClientAccess = true;
            user.ClientAccessIds.Clear();
        }
        user.Enabled = enabled;
        if (!enabled || role == MasterUserRole.ReadOnly)
            access.Checkouts.RemoveAll(checkout => checkout.UserId == userId);
    }

    public static void UpdateClientAccess(
        MasterAccessControl access,
        MasterSession session,
        Guid userId,
        bool hasAllClientAccess,
        IEnumerable<Guid> clientIds)
    {
        RequireOwner(access, session);
        var user = RequiredUser(access, userId);
        if (user.Role == MasterUserRole.Owner)
        {
            user.HasAllClientAccess = true;
            user.ClientAccessIds.Clear();
            return;
        }

        user.HasAllClientAccess = hasAllClientAccess;
        user.ClientAccessIds = hasAllClientAccess
            ? []
            : clientIds.Distinct().ToList();
        if (!hasAllClientAccess)
        {
            var allowed = user.ClientAccessIds.ToHashSet();
            access.Checkouts.RemoveAll(checkout =>
                checkout.UserId == userId && !allowed.Contains(checkout.ClientId));
        }
    }

    public static void ResetPassword(
        MasterAccessControl access,
        MasterSession session,
        Guid userId,
        string password)
    {
        RequireOwner(access, session);
        ValidatePassword(password);
        SetPassword(RequiredUser(access, userId), password, session.MasterKey);
    }

    public static void ChangeOwnPassword(
        MasterAccessControl access,
        MasterSession session,
        string currentPassword,
        string newPassword)
    {
        var user = ValidateSession(access, session);
        if (!VerifyPassword(user, currentPassword))
            throw new MasterAuthorizationException("The current password is not valid.");
        ValidatePassword(newPassword);
        SetPassword(user, newPassword, session.MasterKey);
    }

    public static void DeleteUser(
        MasterAccessControl access,
        MasterSession session,
        Guid userId)
    {
        RequireOwner(access, session);
        var user = RequiredUser(access, userId);
        if (user.Id == session.UserId)
            throw new InvalidOperationException("You cannot delete the account currently signed in.");
        if (user.Role == MasterUserRole.Owner &&
            access.Users.Count(candidate => candidate.Enabled &&
                candidate.Role == MasterUserRole.Owner) <= 1)
            throw new InvalidOperationException("A master must keep at least one enabled Owner account.");
        access.Users.Remove(user);
        access.Checkouts.RemoveAll(checkout => checkout.UserId == userId);
    }

    public static void RequireRead(MasterAccessControl access, MasterSession? session)
    {
        if (!access.IsConfigured) return;
        ValidateSession(access, session);
    }

    public static void RequireWrite(MasterAccessControl access, MasterSession? session)
    {
        if (!access.IsConfigured) return;
        var current = ValidateSession(access, session);
        if (current.Role == MasterUserRole.ReadOnly)
            throw new MasterAuthorizationException("This account is read-only and cannot change the master.");
    }

    public static bool CanAccessClient(
        MasterAccessControl access,
        MasterSession? session,
        Guid clientId)
    {
        if (!access.IsConfigured) return true;
        var user = ValidateSession(access, session);
        return user.Role == MasterUserRole.Owner ||
               user.HasAllClientAccess ||
               user.ClientAccessIds.Contains(clientId);
    }

    public static bool HasAllClientAccess(
        MasterAccessControl access,
        MasterSession? session)
    {
        if (!access.IsConfigured) return true;
        var user = ValidateSession(access, session);
        return user.Role == MasterUserRole.Owner || user.HasAllClientAccess;
    }

    public static void RequireClientRead(
        MasterAccessControl access,
        MasterSession? session,
        Guid clientId)
    {
        RequireRead(access, session);
        if (!CanAccessClient(access, session, clientId))
            throw new MasterAuthorizationException(
                "This account does not have access to the selected client.");
    }

    public static void RequireClientWrite(
        MasterAccessControl access,
        MasterSession? session,
        Guid clientId)
    {
        RequireWrite(access, session);
        if (!CanAccessClient(access, session, clientId))
            throw new MasterAuthorizationException(
                "This account does not have write access to the selected client.");
    }

    public static void RequireOwner(MasterAccessControl access, MasterSession? session)
    {
        if (!access.IsConfigured)
            throw new MasterAuthorizationException("Set up the first Owner account before managing users.");
        var current = ValidateSession(access, session);
        if (current.Role != MasterUserRole.Owner)
            throw new MasterAuthorizationException("Only an Owner can manage master accounts.");
    }

    public static MasterSession RefreshSession(MasterAccessControl access, MasterSession session) =>
        Session(ValidateSession(access, session), session.MasterKey);

    public static MasterAccessControl Clone(MasterAccessControl access) =>
        JsonSerializer.Deserialize<MasterAccessControl>(JsonSerializer.Serialize(access))
        ?? throw new InvalidOperationException("The master access list could not be copied.");

    public static MasterAccessControl ApplyOwnPasswordChange(
        MasterAccessControl current,
        MasterAccessControl proposed,
        MasterSession session)
    {
        var currentUser = ValidateSession(current, session);
        var proposedUser = proposed.Users.FirstOrDefault(user => user.Id == session.UserId)
            ?? throw new MasterAuthorizationException("Your account is missing from the updated access list.");
        if (proposed.Users.Count != current.Users.Count ||
            current.Users.Any(user => proposed.Users.All(candidate => candidate.Id != user.Id)))
            throw new MasterAuthorizationException("Only an Owner can add or remove accounts.");

        foreach (var user in current.Users)
        {
            var candidate = proposed.Users.Single(item => item.Id == user.Id);
            if (user.Id == session.UserId)
            {
                if (!SameProfile(user, candidate))
                    throw new MasterAuthorizationException(
                        "Tech and Read-only accounts can change only their own password.");
                continue;
            }
            if (!SameCompleteUser(user, candidate))
                throw new MasterAuthorizationException(
                    "Only an Owner can change another account.");
        }

        var result = Clone(current);
        var destination = result.Users.Single(user => user.Id == currentUser.Id);
        CopyPasswordFields(proposedUser, destination);
        return result;
    }

    public static void ValidateForSave(MasterAccessControl access)
    {
        if (!access.Users.Any(user => user.Enabled && user.Role == MasterUserRole.Owner))
            throw new InvalidOperationException("A master must keep at least one enabled Owner account.");
        var duplicate = access.Users.GroupBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"The username '{duplicate.Key}' is duplicated.");
    }

    private static MasterUserRecord AddUserCore(
        MasterAccessControl access,
        string username,
        string displayName,
        string password,
        MasterUserRole role,
        string masterKey)
    {
        var normalized = NormalizeUsername(username);
        if (access.Users.Any(user =>
                string.Equals(user.Username, normalized, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That username already exists in this master.");
        ValidatePassword(password);
        var user = new MasterUserRecord
        {
            Username = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim(),
            Role = role,
            HasAllClientAccess = true,
            PasswordIterations = DefaultIterations
        };
        SetPassword(user, password, masterKey);
        access.Users.Add(user);
        return user;
    }

    private static void SetPassword(MasterUserRecord user, string password, string masterKey)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Math.Max(DefaultIterations, user.PasswordIterations),
            HashAlgorithmName.SHA256,
            HashLength);
        user.PasswordIterations = Math.Max(DefaultIterations, user.PasswordIterations);
        user.PasswordSaltBase64 = Convert.ToBase64String(salt);
        user.PasswordHashBase64 = Convert.ToBase64String(hash);
        WrapMasterKey(user, password, masterKey);
    }

    private static void WrapMasterKey(
        MasterUserRecord user,
        string password,
        string masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey))
        {
            user.MasterKeySaltBase64 = string.Empty;
            user.MasterKeyNonceBase64 = string.Empty;
            user.MasterKeyCiphertextBase64 = string.Empty;
            user.MasterKeyTagBase64 = string.Empty;
            return;
        }

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            32);
        var plaintext = Encoding.UTF8.GetBytes(masterKey);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        CryptographicOperations.ZeroMemory(key);
        user.MasterKeySaltBase64 = Convert.ToBase64String(salt);
        user.MasterKeyNonceBase64 = Convert.ToBase64String(nonce);
        user.MasterKeyCiphertextBase64 = Convert.ToBase64String(ciphertext);
        user.MasterKeyTagBase64 = Convert.ToBase64String(tag);
    }

    private static string UnwrapMasterKey(MasterUserRecord user, string password)
    {
        if (string.IsNullOrWhiteSpace(user.MasterKeyCiphertextBase64))
            return string.Empty;
        try
        {
            var salt = Convert.FromBase64String(user.MasterKeySaltBase64);
            var nonce = Convert.FromBase64String(user.MasterKeyNonceBase64);
            var ciphertext = Convert.FromBase64String(user.MasterKeyCiphertextBase64);
            var tag = Convert.FromBase64String(user.MasterKeyTagBase64);
            var key = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                DefaultIterations,
                HashAlgorithmName.SHA256,
                32);
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, tag.Length))
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            CryptographicOperations.ZeroMemory(key);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception) when (
            exception is FormatException or CryptographicException)
        {
            throw new MasterAuthorizationException(
                "This account could not unlock the Master Matrix. Ask the Owner to reset its password.");
        }
    }

    private static bool VerifyPassword(MasterUserRecord user, string password)
    {
        try
        {
            var salt = Convert.FromBase64String(user.PasswordSaltBase64);
            var expected = Convert.FromBase64String(user.PasswordHashBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Math.Max(100000, user.PasswordIterations),
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static MasterUserRecord ValidateSession(
        MasterAccessControl access,
        MasterSession? session)
    {
        if (session is null)
            throw new MasterAuthorizationException("Sign in to this master to continue.");
        var user = access.Users.FirstOrDefault(candidate => candidate.Id == session.UserId);
        if (user is null || !user.Enabled ||
            !string.Equals(user.Username, session.Username, StringComparison.OrdinalIgnoreCase))
            throw new MasterAuthorizationException("This sign-in is no longer valid. Sign in again.");
        return user;
    }

    private static MasterUserRecord RequiredUser(MasterAccessControl access, Guid userId) =>
        access.Users.FirstOrDefault(user => user.Id == userId)
        ?? throw new InvalidOperationException("The selected user no longer exists.");

    private static MasterSession Session(MasterUserRecord user, string masterKey) => new(
        user.Id,
        user.Username,
        user.DisplayName,
        user.Role,
        masterKey);

    private static bool SameProfile(MasterUserRecord left, MasterUserRecord right) =>
        left.Id == right.Id &&
        string.Equals(left.Username, right.Username, StringComparison.Ordinal) &&
        string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        left.Role == right.Role &&
        left.HasAllClientAccess == right.HasAllClientAccess &&
        left.ClientAccessIds.OrderBy(id => id).SequenceEqual(right.ClientAccessIds.OrderBy(id => id)) &&
        left.Enabled == right.Enabled &&
        left.CreatedUtc == right.CreatedUtc;

    private static bool SameCompleteUser(MasterUserRecord left, MasterUserRecord right) =>
        SameProfile(left, right) &&
        left.PasswordIterations == right.PasswordIterations &&
        string.Equals(left.PasswordSaltBase64, right.PasswordSaltBase64, StringComparison.Ordinal) &&
        string.Equals(left.PasswordHashBase64, right.PasswordHashBase64, StringComparison.Ordinal) &&
        string.Equals(left.MasterKeySaltBase64, right.MasterKeySaltBase64, StringComparison.Ordinal) &&
        string.Equals(left.MasterKeyNonceBase64, right.MasterKeyNonceBase64, StringComparison.Ordinal) &&
        string.Equals(left.MasterKeyCiphertextBase64, right.MasterKeyCiphertextBase64, StringComparison.Ordinal) &&
        string.Equals(left.MasterKeyTagBase64, right.MasterKeyTagBase64, StringComparison.Ordinal);

    private static void CopyPasswordFields(MasterUserRecord source, MasterUserRecord destination)
    {
        destination.PasswordIterations = source.PasswordIterations;
        destination.PasswordSaltBase64 = source.PasswordSaltBase64;
        destination.PasswordHashBase64 = source.PasswordHashBase64;
        destination.MasterKeySaltBase64 = source.MasterKeySaltBase64;
        destination.MasterKeyNonceBase64 = source.MasterKeyNonceBase64;
        destination.MasterKeyCiphertextBase64 = source.MasterKeyCiphertextBase64;
        destination.MasterKeyTagBase64 = source.MasterKeyTagBase64;
    }

    private static string NormalizeUsername(string username)
    {
        var value = username.Trim();
        if (value.Length is < 3 or > 64)
            throw new InvalidOperationException("Use a username between 3 and 64 characters.");
        if (value.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
            throw new InvalidOperationException(
                "Usernames can contain letters, numbers, periods, underscores, and hyphens.");
        return value;
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 10)
            throw new InvalidOperationException("Use a password of at least 10 characters.");
    }
}

internal sealed class MasterAuthorizationException : InvalidOperationException
{
    public MasterAuthorizationException(string message) : base(message)
    {
    }
}
