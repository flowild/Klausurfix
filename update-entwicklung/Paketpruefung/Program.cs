using System.Security.Cryptography;
using System.Text.Json;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Aufruf: Paketpruefung <Paket.zip> [win-arm64|win-x64]");
    return 2;
}
try
{
    using var stream = File.OpenRead(args[0]);
    var manifest = UpdatePackage.Verify(stream);
    if (args.Length == 2 && manifest.Architektur != args[1])
        throw new InvalidDataException("Die Paketarchitektur stimmt nicht mit der gewünschten Architektur überein.");
    Console.WriteLine($"Paket geprüft: {manifest.Produkt} {manifest.Version} ({manifest.Architektur}), {manifest.Dateien!.Count} Dateien.");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or OverflowException or ArgumentException)
{
    Console.Error.WriteLine($"Paketprüfung fehlgeschlagen: {ex.Message}");
    return 1;
}
