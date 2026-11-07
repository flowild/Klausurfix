using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

internal static class UpdatePackage
{
    private static bool IsSafePart(string part)
    {
        if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
            part.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return false;
        var stem = part.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return false;
        if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            stem[3] is >= '1' and <= '9') return false;
        return true;
    }

    public static Manifest Verify(Stream package)
    {
    using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
    var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in archive.Entries)
    {
        var name = entry.FullName;
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.StartsWith('/') ||
            name.Split('/').Any(part => !IsSafePart(part)) ||
            !entries.TryAdd(name, entry))
            throw new InvalidDataException("Ungültiger oder mehrfach vorhandener Paketpfad.");
    }

    byte[] ReadBounded(string name, int max)
    {
        if (!entries.TryGetValue(name, out var entry) || entry.Length > max)
            throw new InvalidDataException($"Datei fehlt oder ist zu groß: {name}");
        using var stream = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (output.Length + count > max) throw new InvalidDataException("Datei ist zu groß.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    var manifestBytes = ReadBounded("manifest.json", 1_000_000);
    var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = false })
        ?? throw new InvalidDataException("Manifest fehlt.");
    if (manifest.Format != 1 || manifest.Produkt != "Klausurfix" ||
        !Version.TryParse(manifest.Version, out var version) || version.Revision != -1 || version.Build < 0 ||
        manifest.Architektur is not ("win-arm64" or "win-x64") || manifest.Dateien is null || manifest.Dateien.Count == 0)
        throw new InvalidDataException("Nicht unterstütztes Manifest.");

    var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
    long total = 0;
    foreach (var file in manifest.Dateien)
    {
        if (file is null || file.Pfad is null || !file.Pfad.StartsWith("app/", StringComparison.Ordinal) ||
            !expected.Add(file.Pfad) || !entries.TryGetValue(file.Pfad, out var entry) ||
            file.Groesse < 0 || file.Groesse > 1_000_000_000 || entry.Length != file.Groesse ||
            file.Sha256 is null || file.Sha256.Length != 64)
            throw new InvalidDataException("Ungültiger Dateieintrag im Manifest.");
        total = checked(total + file.Groesse);
        if (total > 2_000_000_000) throw new InvalidDataException("Paket ist zu groß.");
        var expectedHash = Convert.FromHexString(file.Sha256);
        using var stream = entry.Open();
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long readTotal = 0;
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            readTotal = checked(readTotal + count);
            if (readTotal > file.Groesse) throw new InvalidDataException("Datei ist größer als angegeben.");
            hasher.AppendData(buffer, 0, count);
        }
        if (readTotal != file.Groesse) throw new InvalidDataException("Datei ist kürzer als angegeben.");
        var actualHash = hasher.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            throw new InvalidDataException($"Prüfsumme stimmt nicht: {file.Pfad}");
    }
    if (entries.Count != expected.Count) throw new InvalidDataException("Paket enthält nicht aufgeführte Dateien.");
        return manifest;
    }
}

internal sealed record Manifest(int Format, string? Produkt, string? Version, string? Architektur, List<Paketdatei>? Dateien);
internal sealed record Paketdatei(string? Pfad, long Groesse, string? Sha256);
