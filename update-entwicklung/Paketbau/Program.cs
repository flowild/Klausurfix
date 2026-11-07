using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 5)
{
    Console.Error.WriteLine("Aufruf: Paketbau <Programmordner> <Version> <win-arm64|win-x64> <privater-Schluessel.pem> <Ausgabe.zip>");
    return 2;
}

string? temporary = null;
try
{
    var root = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[4]);
    if (!Directory.Exists(root) || !Version.TryParse(args[1], out var version) ||
        version.Revision != -1 || version.Build < 0 || args[2] is not ("win-arm64" or "win-x64"))
        throw new ArgumentException("Programmordner, Version oder Architektur ungültig.");
    if (output.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Die Ausgabedatei darf nicht im Programmordner liegen.");
    if (File.Exists(output)) throw new IOException("Die Ausgabedatei existiert bereits.");
    if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        throw new IOException("Der Programmordner darf keine Verknüpfung sein.");

    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(args[3]));
    if (rsa.KeySize < 3072) throw new CryptographicException("Der RSA-Schlüssel muss mindestens 3072 Bit haben.");
    var files = new List<(string Path, string FullPath, long Length, string Hash)>();
    var pending = new Stack<string>();
    pending.Push(root);
    long total = 0;
    while (pending.TryPop(out var directory))
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Verknüpfungen im Programmordner sind nicht zulässig.");
            if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':')))
                throw new IOException("Ungültiger Dateipfad.");
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 1_000_000_000) throw new IOException("Eine Datei ist zu groß.");
            total = checked(total + stream.Length);
            if (total > 2_000_000_000) throw new IOException("Das Paket ist zu groß.");
            files.Add(("app/" + relative, path, stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
        }
    }
    if (files.Count == 0 || files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
        throw new IOException("Der Programmordner ist leer oder enthält mehrdeutige Dateinamen.");
    files.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
    var manifest = JsonSerializer.SerializeToUtf8Bytes(new
    {
        Format = 1, Produkt = "Klausurfix", Version = args[1], Architektur = args[2],
        Dateien = files.Select(f => new { Pfad = f.Path, Groesse = f.Length, Sha256 = f.Hash }).ToArray()
    });
    if (manifest.Length > 1_000_000) throw new IOException("Das Manifest ist zu groß.");
    var signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
    {
        void Write(string name, byte[] bytes)
        {
            using var target = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
            target.Write(bytes);
        }
        Write("manifest.json", manifest);
        Write("manifest.sig", signature);
        foreach (var file in files)
        {
            using var input = File.Open(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var target = archive.CreateEntry(file.Path, CompressionLevel.Optimal).Open();
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long length = 0;
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                length = checked(length + count);
                if (length > file.Length) throw new IOException("Eine Eingabedatei wurde während des Paketbaus verändert.");
                hasher.AppendData(buffer, 0, count);
                target.Write(buffer, 0, count);
            }
            if (length != file.Length || !CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), Convert.FromHexString(file.Hash)))
                throw new IOException("Eine Eingabedatei wurde während des Paketbaus verändert.");
        }
    }
    File.Move(temporary, output, false);
    temporary = null;
    Console.WriteLine($"Signiertes Paket erstellt: {output}");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or OverflowException)
{
    Console.Error.WriteLine($"Paketbau fehlgeschlagen: {ex.Message}");
    return 1;
}
finally
{
    if (temporary is not null) try { File.Delete(temporary); } catch (IOException) { }
}
