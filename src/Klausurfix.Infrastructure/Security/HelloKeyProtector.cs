namespace Klausurfix.Infrastructure.Security;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Protects the database key with a WebAuthn PRF result. Authentication and
/// verification of the WebAuthn response must happen before this is used.
/// </summary>
public static class HelloKeyProtector
{
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("Klausurfix/Hello/DEK/v1");

    public static byte[] Protect(byte[] databaseKey, byte[] prfResult, byte[] credentialId)
    {
        if (databaseKey.Length != CryptoService.KeySize)
            throw new ArgumentException("Ungültiger Datenschlüssel.", nameof(databaseKey));
        var key = DeriveKey(prfResult, credentialId);
        try { return CryptoService.Encrypt(databaseKey, key, credentialId); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static byte[] Unprotect(byte[] protectedKey, byte[] prfResult, byte[] credentialId)
    {
        var key = DeriveKey(prfResult, credentialId);
        try
        {
            var databaseKey = CryptoService.Decrypt(protectedKey, key, credentialId);
            if (databaseKey.Length == CryptoService.KeySize) return databaseKey;
            CryptographicOperations.ZeroMemory(databaseKey);
            throw new CryptographicException("Ungültiger Datenschlüssel.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] DeriveKey(byte[] prfResult, byte[] credentialId)
    {
        if (prfResult.Length != 32)
            throw new ArgumentException("Ungültiges PRF-Ergebnis.", nameof(prfResult));
        if (credentialId.Length == 0)
            throw new ArgumentException("Ungültige Anmeldekennung.", nameof(credentialId));
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, prfResult, CryptoService.KeySize,
            salt: credentialId, info: Context);
    }
}
