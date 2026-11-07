using System.Runtime.InteropServices;
using System.Text;

/// <summary>Resolves and switches the local program version; does not migrate data.</summary>
public static class ActiveVersion
{
    private const string Marker = "aktive-version.txt";
    public const string PendingUpdate = ".starter-update-ausstehend";

    public static string ResolveBackend(string root)
    {
        root = Path.GetFullPath(root);
        if (File.Exists(Path.Combine(root, PendingUpdate)))
            throw new InvalidOperationException("Ein Update wurde unterbrochen. Bitte dieselbe Update-EXE erneut ausführen, um es abzuschließen.");
        var marker = Path.Combine(root, Marker);
        if (!File.Exists(marker)) return Path.Combine(root, "app", "Klausurfix.Web.exe");
        using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 100) throw new InvalidDataException("Die aktive Programmversion ist ungültig.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var (version, architecture) = Parse(reader.ReadToEnd());
        var actual = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X64 => "win-x64",
            _ => throw new InvalidOperationException("Diese Windows-Architektur wird nicht unterstützt.")
        };
        if (architecture != actual) throw new InvalidDataException("Die aktive Programmversion passt nicht zu Windows.");
        return VersionBackend(root, version, architecture);
    }

    public static void Activate(string root, string version, string architecture, bool allowSameVersion = false)
    {
        root = Path.GetFullPath(root);
        var selection = version + "|" + architecture;
        var parsed = Parse(selection);
        var backend = VersionBackend(root, parsed.Version, parsed.Architecture);
        if (!File.Exists(backend) || !File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(backend)!)!, "Klausurfix.exe")))
            throw new FileNotFoundException("Die neue Programmversion ist nicht vollständig.");
        var marker = Path.Combine(root, Marker);
        EnsureNewer(root, parsed.Version, parsed.Architecture, allowSameVersion);
        var temp = Path.Combine(root, ".aktive-version-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(selection);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, marker, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static void EnsureNewer(string root, string version, string architecture, bool allowSameVersion = false)
    {
        var parsed = Parse(version + "|" + architecture);
        var marker = Path.Combine(root, Marker);
        if (File.Exists(marker))
        {
            var previous = Parse(File.ReadAllText(marker));
            if (previous.Architecture != parsed.Architecture || Version.Parse(previous.Version) > Version.Parse(parsed.Version) ||
                (!allowSameVersion && Version.Parse(previous.Version) == Version.Parse(parsed.Version)))
                throw new InvalidOperationException("Eine gleich alte oder ältere Version wird nicht automatisch aktiviert.");
        }
        else
        {
            var baseline = Path.Combine(root, "basis-version.txt");
            if (!File.Exists(baseline))
                throw new InvalidOperationException("Die installierte Ausgangsversion ist nicht bekannt. Das Update wurde nicht aktiviert.");
            using var input = new FileStream(baseline, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 100) throw new InvalidDataException("Die installierte Ausgangsversion ist ungültig.");
            using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var previous = Parse(reader.ReadToEnd());
            if (previous.Architecture != parsed.Architecture || Version.Parse(previous.Version) > Version.Parse(parsed.Version) ||
                (!allowSameVersion && Version.Parse(previous.Version) == Version.Parse(parsed.Version)))
                throw new InvalidOperationException("Die Ausgangsversion passt nicht oder das Update ist nicht neuer.");
        }
    }

    private static (string Version, string Architecture) Parse(string value)
    {
        var parts = value.Split('|');
        if (parts.Length != 2 || !System.Version.TryParse(parts[0], out var version) || version.Build < 0 ||
            version.Revision != -1 || version.ToString(3) != parts[0] || parts[1] is not ("win-arm64" or "win-x64"))
            throw new InvalidDataException("Die aktive Programmversion ist ungültig.");
        return (parts[0], parts[1]);
    }

    private static string VersionBackend(string root, string version, string architecture)
    {
        var folder = Path.Combine(root, "versionen", version + "-" + architecture);
        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfte Programmordner werden nicht unterstützt.");
        return Path.Combine(folder, "app", "Klausurfix.Web.exe");
    }
}
