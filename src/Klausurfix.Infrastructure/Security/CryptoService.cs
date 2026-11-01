namespace Klausurfix.Infrastructure.Security;

using System.Security.Cryptography;
using System.Text;

public static class CryptoService
{
    private const int NonceSize = 12; // 96 bits for AES-GCM
    private const int TagSize = 16;   // 128 bits for AES-GCM
    public const int KeySize = 32;    // 256 bits for AES-256
    public const int SaltSize = 32;   // 256 bits
    public const int Pbkdf2Iterations = 600_000;

    /// <summary>
    /// Generates cryptographically secure random bytes.
    /// </summary>
    public static byte[] GenerateRandomBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    /// <summary>
    /// Derives a 256-bit key from a secret (such as PIN or password) and a salt using PBKDF2-SHA256.
    /// </summary>
    public static byte[] DeriveKey(string secret, byte[] salt, int iterations = Pbkdf2Iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    /// <summary>
    /// Encrypts plaintext bytes using AES-256-GCM with a fresh random nonce.
    /// Returns packed byte array: [12-byte Nonce][16-byte Tag][Ciphertext].
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key, byte[]? associatedData = null)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes (256-bit).", nameof(key));

        var nonce = GenerateRandomBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypts packed byte array [12-byte Nonce][16-byte Tag][Ciphertext] using AES-256-GCM.
    /// Throws CryptographicException if authentication tag or key is invalid.
    /// </summary>
    public static byte[] Decrypt(byte[] packedData, byte[] key, byte[]? associatedData = null)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes (256-bit).", nameof(key));

        if (packedData.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext data is too short.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        int cipherLength = packedData.Length - NonceSize - TagSize;
        var ciphertext = new byte[cipherLength];
        var plaintext = new byte[cipherLength];

        Buffer.BlockCopy(packedData, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(packedData, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(packedData, NonceSize + TagSize, ciphertext, 0, cipherLength);

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }

        return plaintext;
    }

    /// <summary>
    /// Encrypts UTF-8 string to Base64 encoded packed ciphertext.
    /// </summary>
    public static string EncryptString(string plaintext, byte[] key, byte[]? associatedData = null)
    {
        if (string.IsNullOrEmpty(plaintext) && associatedData == null) return string.Empty;
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try { return Convert.ToBase64String(Encrypt(plainBytes, key, associatedData)); }
        finally { ZeroMemory(plainBytes); }
    }

    /// <summary>
    /// Decrypts Base64 encoded packed ciphertext to UTF-8 string.
    /// </summary>
    public static string DecryptString(string base64Ciphertext, byte[] key, byte[]? associatedData = null)
    {
        if (string.IsNullOrEmpty(base64Ciphertext) && associatedData == null) return string.Empty;
        byte[] packed;
        try { packed = Convert.FromBase64String(base64Ciphertext); }
        catch (FormatException ex) { throw new CryptographicException("Ungültiges Format verschlüsselter Daten.", ex); }
        var decrypted = Decrypt(packed, key, associatedData);
        try { return Encoding.UTF8.GetString(decrypted); }
        finally { ZeroMemory(decrypted); }
    }

    /// <summary>
    /// Securely wipes memory buffers.
    /// </summary>
    public static void ZeroMemory(byte[] buffer)
    {
        CryptographicOperations.ZeroMemory(buffer);
    }
}

