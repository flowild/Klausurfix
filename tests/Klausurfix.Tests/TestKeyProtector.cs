namespace Klausurfix.Tests;

using Klausurfix.Infrastructure.Security;

// A process-local stand-in for DPAPI in the isolated test environment.
public sealed class TestKeyProtector : ILocalKeyProtector
{
    private readonly byte[] _key = CryptoService.GenerateRandomBytes(CryptoService.KeySize);
    public byte[] Protect(byte[] data) => CryptoService.Encrypt(data, _key);
    public byte[] Unprotect(byte[] data) => CryptoService.Decrypt(data, _key);
}
