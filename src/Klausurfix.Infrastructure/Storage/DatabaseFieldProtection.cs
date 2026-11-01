namespace Klausurfix.Infrastructure.Storage;

using System.Security.Cryptography;
using System.Text.Json;
using Klausurfix.Infrastructure.Security;

internal static class DatabaseFieldProtection
{
    internal const string Prefix = "kf2:";

    // JSON arrays preserve boundaries even for IDs containing separators. The
    // format version and purpose are authenticated alongside the full primary key.
    private static byte[] Context(string table, string column, string[] ids) =>
        JsonSerializer.SerializeToUtf8Bytes(new[] { "Klausurfix.DatabaseField", "2", table, column }.Concat(ids));

    internal static string Protect(string value, byte[] key, string table, string column, params string[] ids) =>
        Prefix + CryptoService.EncryptString(value, key, Context(table, column, ids));

    internal static string Unprotect(string value, byte[] key, string table, string column, params string[] ids)
    {
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Ungültiges verschlüsseltes Datenfeld.");
        return CryptoService.DecryptString(value[Prefix.Length..], key, Context(table, column, ids));
    }
}
