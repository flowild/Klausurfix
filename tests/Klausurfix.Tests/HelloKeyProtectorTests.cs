namespace Klausurfix.Tests;

using System.Security.Cryptography;
using Klausurfix.Infrastructure.Security;

public class HelloKeyProtectorTests
{
    [Fact]
    public void OnlyTheCorrectPrfAndCredentialCanUnwrapTheDatabaseKey()
    {
        var databaseKey = RandomNumberGenerator.GetBytes(32);
        var prf = RandomNumberGenerator.GetBytes(32);
        var credential = RandomNumberGenerator.GetBytes(32);
        var wrapped = HelloKeyProtector.Protect(databaseKey, prf, credential);

        Assert.Equal(databaseKey, HelloKeyProtector.Unprotect(wrapped, prf, credential));
        Assert.ThrowsAny<CryptographicException>(() => HelloKeyProtector.Unprotect(wrapped, RandomNumberGenerator.GetBytes(32), credential));
        Assert.ThrowsAny<CryptographicException>(() => HelloKeyProtector.Unprotect(wrapped, prf, RandomNumberGenerator.GetBytes(32)));
        wrapped[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => HelloKeyProtector.Unprotect(wrapped, prf, credential));
    }

    [Fact]
    public void RejectsMissingOrMalformedPrfResults()
    {
        Assert.Throws<ArgumentException>(() => HelloKeyProtector.Protect(new byte[32], Array.Empty<byte>(), new byte[32]));
        Assert.Throws<ArgumentException>(() => HelloKeyProtector.Protect(new byte[32], new byte[32], Array.Empty<byte>()));
    }
}
