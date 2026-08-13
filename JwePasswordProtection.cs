using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InNasc;

/// <summary>
/// Password protection for portable InNasc files. The output is an RFC 7516
/// compact JWE using PBES2-HS256+A128KW and A256GCM.
/// </summary>
internal static class JwePasswordProtection
{
    private const string Algorithm = "PBES2-HS256+A128KW";
    private const string Encryption = "A256GCM";
    private const int Iterations = 210_000;
    private static readonly byte[] KeyWrapDefaultIv =
        [0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6];

    public static byte[] Protect(byte[] plaintext, string password)
    {
        ValidatePassword(password);
        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var header = new JweHeader
        {
            Alg = Algorithm,
            Enc = Encryption,
            P2s = Base64UrlEncode(passwordSalt),
            P2c = Iterations,
            Typ = "InNasc"
        };
        var protectedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var keyEncryptionKey = DeriveKey(password, passwordSalt, Iterations);
        var contentEncryptionKey = RandomNumberGenerator.GetBytes(32);
        var encryptedKey = WrapKey(keyEncryptionKey, contentEncryptionKey);
        var iv = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(contentEncryptionKey, tag.Length))
        {
            aes.Encrypt(iv, plaintext, ciphertext, tag, Encoding.ASCII.GetBytes(protectedHeader));
        }

        CryptographicOperations.ZeroMemory(keyEncryptionKey);
        CryptographicOperations.ZeroMemory(contentEncryptionKey);
        var compact = string.Join('.',
            protectedHeader,
            Base64UrlEncode(encryptedKey),
            Base64UrlEncode(iv),
            Base64UrlEncode(ciphertext),
            Base64UrlEncode(tag));
        return Encoding.ASCII.GetBytes(compact);
    }

    public static byte[] Unprotect(byte[] compactBytes, string password)
    {
        ValidatePassword(password);
        try
        {
            var compact = Encoding.ASCII.GetString(compactBytes).Trim();
            var segments = compact.Split('.');
            if (segments.Length != 5)
                throw new InvalidDataException("The password-protected file is not a valid compact JWE.");

            var header = JsonSerializer.Deserialize<JweHeader>(Base64UrlDecode(segments[0]))
                ?? throw new InvalidDataException("The password-protected file has no JWE header.");
            if (!string.Equals(header.Alg, Algorithm, StringComparison.Ordinal) ||
                !string.Equals(header.Enc, Encryption, StringComparison.Ordinal))
                throw new InvalidDataException("This JWE encryption method is not supported by InNasc.");
            if (header.P2c is < 10_000 or > 2_000_000 || string.IsNullOrWhiteSpace(header.P2s))
                throw new InvalidDataException("The password-protected file has invalid key settings.");

            var passwordSalt = Base64UrlDecode(header.P2s);
            var keyEncryptionKey = DeriveKey(password, passwordSalt, header.P2c);
            var contentEncryptionKey = UnwrapKey(keyEncryptionKey, Base64UrlDecode(segments[1]));
            var iv = Base64UrlDecode(segments[2]);
            var ciphertext = Base64UrlDecode(segments[3]);
            var tag = Base64UrlDecode(segments[4]);
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(contentEncryptionKey, tag.Length))
            {
                aes.Decrypt(iv, ciphertext, tag, plaintext, Encoding.ASCII.GetBytes(segments[0]));
            }

            CryptographicOperations.ZeroMemory(keyEncryptionKey);
            CryptographicOperations.ZeroMemory(contentEncryptionKey);
            return plaintext;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            throw new PasswordProtectionException(
                "The password is incorrect, or the protected file is damaged.", exception);
        }
    }

    public static bool IsCompactJwe(ReadOnlySpan<byte> contents)
    {
        if (contents.IsEmpty || contents[0] == (byte)'{' || contents[0] == (byte)'[') return false;
        var dots = 0;
        foreach (var value in contents)
        {
            if (value == (byte)'.') dots++;
            else if (value is (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)' ') continue;
            else if (!(value is >= (byte)'A' and <= (byte)'Z' ||
                       value is >= (byte)'a' and <= (byte)'z' ||
                       value is >= (byte)'0' and <= (byte)'9' ||
                       value is (byte)'-' or (byte)'_')) return false;
        }
        return dots == 4;
    }

    private static byte[] DeriveKey(string password, byte[] p2s, int iterations)
    {
        var algorithmBytes = Encoding.ASCII.GetBytes(Algorithm);
        var salt = new byte[algorithmBytes.Length + 1 + p2s.Length];
        Buffer.BlockCopy(algorithmBytes, 0, salt, 0, algorithmBytes.Length);
        Buffer.BlockCopy(p2s, 0, salt, algorithmBytes.Length + 1, p2s.Length);
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 16);
    }

    private static byte[] WrapKey(byte[] kek, byte[] key)
    {
        if (key.Length < 16 || key.Length % 8 != 0)
            throw new CryptographicException("Invalid content-encryption key length.");
        var n = key.Length / 8;
        var a = KeyWrapDefaultIv.ToArray();
        var r = new byte[n][];
        for (var index = 0; index < n; index++) r[index] = key.AsSpan(index * 8, 8).ToArray();

        using var aes = Aes.Create();
        aes.Key = kek;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        var block = new byte[16];
        var tBytes = new byte[8];
        for (var j = 0; j <= 5; j++)
        {
            for (var i = 0; i < n; i++)
            {
                Buffer.BlockCopy(a, 0, block, 0, 8);
                Buffer.BlockCopy(r[i], 0, block, 8, 8);
                encryptor.TransformBlock(block, 0, block.Length, block, 0);
                var t = (ulong)(n * j + i + 1);
                BinaryPrimitives.WriteUInt64BigEndian(tBytes, t);
                for (var k = 0; k < 8; k++) a[k] = (byte)(block[k] ^ tBytes[k]);
                Buffer.BlockCopy(block, 8, r[i], 0, 8);
            }
        }

        var result = new byte[(n + 1) * 8];
        Buffer.BlockCopy(a, 0, result, 0, 8);
        for (var i = 0; i < n; i++) Buffer.BlockCopy(r[i], 0, result, (i + 1) * 8, 8);
        return result;
    }

    private static byte[] UnwrapKey(byte[] kek, byte[] wrapped)
    {
        if (wrapped.Length < 24 || wrapped.Length % 8 != 0)
            throw new CryptographicException("Invalid wrapped key length.");
        var n = wrapped.Length / 8 - 1;
        var a = wrapped.AsSpan(0, 8).ToArray();
        var r = new byte[n][];
        for (var index = 0; index < n; index++)
            r[index] = wrapped.AsSpan((index + 1) * 8, 8).ToArray();

        using var aes = Aes.Create();
        aes.Key = kek;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var decryptor = aes.CreateDecryptor();
        var block = new byte[16];
        var tBytes = new byte[8];
        for (var j = 5; j >= 0; j--)
        {
            for (var i = n - 1; i >= 0; i--)
            {
                var t = (ulong)(n * j + i + 1);
                BinaryPrimitives.WriteUInt64BigEndian(tBytes, t);
                for (var k = 0; k < 8; k++) block[k] = (byte)(a[k] ^ tBytes[k]);
                Buffer.BlockCopy(r[i], 0, block, 8, 8);
                decryptor.TransformBlock(block, 0, block.Length, block, 0);
                Buffer.BlockCopy(block, 0, a, 0, 8);
                Buffer.BlockCopy(block, 8, r[i], 0, 8);
            }
        }
        if (!CryptographicOperations.FixedTimeEquals(a, KeyWrapDefaultIv))
            throw new CryptographicException("Key verification failed.");

        var result = new byte[n * 8];
        for (var i = 0; i < n; i++) Buffer.BlockCopy(r[i], 0, result, i * 8, 8);
        return result;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("A password is required for a protected InNasc file.", nameof(password));
    }

    private sealed class JweHeader
    {
        [JsonPropertyName("alg")]
        public string Alg { get; set; } = string.Empty;
        [JsonPropertyName("enc")]
        public string Enc { get; set; } = string.Empty;
        [JsonPropertyName("p2s")]
        public string P2s { get; set; } = string.Empty;
        [JsonPropertyName("p2c")]
        public int P2c { get; set; }
        [JsonPropertyName("typ")]
        public string Typ { get; set; } = string.Empty;
    }
}

internal sealed class PasswordProtectionException : CryptographicException
{
    public PasswordProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
