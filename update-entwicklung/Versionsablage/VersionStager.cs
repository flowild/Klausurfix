using System.IO.Compression;
using System.Security.Cryptography;

/// <summary>Checks package contents and stages them without activating them or touching application data.</summary>
public static class VersionStager
{
    public static StagedVersion Stage(string packagePath, string installationDirectory, string architecture)
    {
        using var package = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Stage(package, installationDirectory, architecture);
    }

    public static StagedVersion Stage(Stream package, string installationDirectory, string architecture)
    {
        if (!package.CanRead || !package.CanSeek) throw new ArgumentException("Das Update-Paket muss lesbar und positionierbar sein.", nameof(package));
        package.Position = 0;
        if (architecture is not ("win-x64" or "win-arm64")) throw new ArgumentException("Unbekannte Architektur.");
        var root = Path.GetFullPath(installationDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Der Programmordner existiert nicht.");
        CheckExistingParents(root);
        var manifest = UpdatePackage.Verify(package);
        if (manifest.Architektur != architecture) throw new InvalidDataException("Falsche Paketarchitektur.");
        var version = Version.Parse(manifest.Version!).ToString(3);
        var files = manifest.Dateien!;
        if (!files.Any(f => f.Pfad == "app/Klausurfix.exe") ||
            !files.Any(f => f.Pfad == "app/app/Klausurfix.Web.exe"))
            throw new InvalidDataException("Starter oder lokaler Dienst fehlt im Paket.");

        var versions = Path.Combine(root, "versionen");
        if (File.Exists(versions)) throw new IOException("Der Versionsordner ist eine Datei.");
        Directory.CreateDirectory(versions);
        CheckExistingParents(versions);
        var target = Path.Combine(versions, version + "-" + architecture);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Diese Version ist bereits vorhanden.");
        var stage = Path.Combine(versions, ".vorbereitung-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        CheckExistingParents(stage);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entries = archive.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var relative = file.Pfad![4..].Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.GetFullPath(Path.Combine(stage, relative));
                var stagePrefix = Path.TrimEndingDirectorySeparator(stage) + Path.DirectorySeparatorChar;
                if (!destination.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Ungültiger Zielpfad.");
                var parent = Path.GetDirectoryName(destination)!;
                Directory.CreateDirectory(parent);
                CheckExistingParents(parent);
                using var source = entries[file.Pfad].Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long written = 0;
                int count;
                while ((count = source.Read(buffer)) > 0)
                {
                    written = checked(written + count);
                    if (written > file.Groesse) throw new InvalidDataException("Dateigröße überschritten.");
                    hasher.AppendData(buffer, 0, count);
                    output.Write(buffer, 0, count);
                }
                if (written != file.Groesse || !CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), Convert.FromHexString(file.Sha256!)))
                    throw new InvalidDataException("Die bereitgestellte Datei ist beschädigt.");
                output.Flush(flushToDisk: true);
            }
        }
        CheckExistingParents(versions);
        CheckExistingParents(stage);
        Directory.Move(stage, target);
        return new StagedVersion(version, architecture, target);
    }

    private static void CheckExistingParents(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfte Programmordner werden nicht unterstützt.");
    }
}

public sealed record StagedVersion(string Version, string Architecture, string Directory);
