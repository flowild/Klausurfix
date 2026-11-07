using System.Security.Cryptography;
using System.Text.Json;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Aufruf: Paketpruefung <Paket.zip> <vertrauenswuerdiger-oeffentlicher-Schluessel.pem>");
    return 2;
}
try
{
    using var stream = File.OpenRead(args[0]);
    var manifest = UpdatePackage.Verify(stream, File.ReadAllText(args[1]));
    if (args.Length == 3 && manifest.Architektur != args[2])
        throw new InvalidDataException("Die Paketarchitektur stimmt nicht mit der gewünschten Architektur überein.");
    Console.WriteLine($"Signiertes Paket geprüft: {manifest.Produkt} {manifest.Version} ({manifest.Architektur}), {manifest.Dateien!.Count} Dateien.");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or OverflowException or ArgumentException)
{
    Console.Error.WriteLine($"Paketprüfung fehlgeschlagen: {ex.Message}");
    return 1;
}
