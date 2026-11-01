namespace Klausurfix.Tests;

using System.Security.Cryptography;
using System.Text;
using Klausurfix.Infrastructure.Security;
using Xunit;

public class CryptoServiceTests
{
    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginalPlaintext()
    {
        var key = CryptoService.GenerateRandomBytes(CryptoService.KeySize);
        var originalText = "Max Mustermann, Punkte: 25.5, Fehler: 3";

        var encryptedBase64 = CryptoService.EncryptString(originalText, key);
        Assert.False(string.IsNullOrEmpty(encryptedBase64));
        Assert.NotEqual(originalText, encryptedBase64);

        var decryptedText = CryptoService.DecryptString(encryptedBase64, key);
        Assert.Equal(originalText, decryptedText);
    }

    [Fact]
    public void TamperedCiphertext_ShouldThrowCryptographicException()
    {
        var key = CryptoService.GenerateRandomBytes(CryptoService.KeySize);
        var originalBytes = Encoding.UTF8.GetBytes("Geheime Klausurdaten");

        var encrypted = CryptoService.Encrypt(originalBytes, key);

        // Tamper with one byte in the ciphertext portion
        encrypted[encrypted.Length - 1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => CryptoService.Decrypt(encrypted, key));
    }

    [Fact]
    public void DecryptWithWrongKey_ShouldThrowCryptographicException()
    {
        var key1 = CryptoService.GenerateRandomBytes(CryptoService.KeySize);
        var key2 = CryptoService.GenerateRandomBytes(CryptoService.KeySize);

        var originalBytes = Encoding.UTF8.GetBytes("Geheime Klausurdaten");
        var encrypted = CryptoService.Encrypt(originalBytes, key1);

        Assert.ThrowsAny<CryptographicException>(() => CryptoService.Decrypt(encrypted, key2));
    }

    [Fact]
    public void Nonce_ShouldBeUniqueForEveryEncryption()
    {
        var key = CryptoService.GenerateRandomBytes(CryptoService.KeySize);
        var data = Encoding.UTF8.GetBytes("Gleicher Text");

        var enc1 = CryptoService.Encrypt(data, key);
        var enc2 = CryptoService.Encrypt(data, key);

        // First 12 bytes are nonce
        var nonce1 = enc1.Take(12).ToArray();
        var nonce2 = enc2.Take(12).ToArray();

        Assert.False(nonce1.SequenceEqual(nonce2));
    }
}
