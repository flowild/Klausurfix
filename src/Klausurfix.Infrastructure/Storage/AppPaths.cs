namespace Klausurfix.Infrastructure.Storage;

public static class AppPaths
{
    private static string? _customBaseDir;

    /// <summary>
    /// For testing purposes or custom setups, allow setting a custom base directory.
    /// Otherwise defaults to the local Documents folder.
    /// </summary>
    public static void SetCustomBaseDirectory(string? path)
    {
        _customBaseDir = path;
    }

    public static string BaseDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_customBaseDir))
                return _customBaseDir;

            var envPath = Environment.GetEnvironmentVariable("KLAUSURFIX_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(envPath))
                return envPath;

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) throw new IOException("Der Dokumentenordner ist nicht verfügbar.");
            var chosen = SelectDocumentsDirectory(documents, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), CloudRoots());
            return Path.Combine(chosen, "Klausurfix");
        }
    }

    public static string DataDirectory => Path.Combine(BaseDirectory, "data");
    public static string DatabaseFile => Path.Combine(DataDirectory, "klausurfix.db");
    public static string KeysDirectory => Path.Combine(BaseDirectory, "keys");
    public static string KeyEnvelopeFile => Path.Combine(KeysDirectory, "key_envelope.json");
    public static string BackupsDirectory => Path.Combine(BaseDirectory, "backups");

    public static void EnsureDirectoriesExist()
    {
        if (string.IsNullOrWhiteSpace(_customBaseDir) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KLAUSURFIX_DATA_DIR")))
            PrepareDefaultDirectory();
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(KeysDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }

    private static void PrepareDefaultDirectory()
    {
        var destination = Path.GetFullPath(BaseDirectory);
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klausurfix");
        if (destination.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Der Dokumentenordner liegt im Netzwerk. Klausurfix benötigt einen lokalen, nicht synchronisierten Ordner.");
        if (IsKnownCloudPath(destination)) throw new IOException("Der lokale Dokumentenordner wird über OneDrive synchronisiert. Klausurfix benötigt einen nicht synchronisierten Ordner.");
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(destination)!); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der Dokumentenordner enthält eine Umleitung. Klausurfix benötigt einen lokalen, nicht synchronisierten Ordner.");
        if (Directory.Exists(legacy) && !string.Equals(destination, legacy, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(destination))
                throw new IOException("Sowohl im alten als auch im neuen Datenordner liegen Klausurfix-Daten. Die Daten wurden nicht verändert; bitte die Ablage vor dem Start klären.");
            var otherProcesses = System.Diagnostics.Process.GetProcessesByName("Klausurfix.Web");
            try
            {
                if (otherProcesses.Any(p => p.Id != Environment.ProcessId))
                    throw new IOException("Eine andere Klausurfix-Version läuft noch. Bitte diese zuerst beenden.");
            }
            finally { foreach (var process in otherProcesses) process.Dispose(); }
            using (var probe = new System.Net.Sockets.TcpClient())
            {
                try { probe.Connect(System.Net.IPAddress.Loopback, 5000); }
                catch (System.Net.Sockets.SocketException) { }
                if (probe.Connected) throw new IOException("Port 5000 ist belegt. Der Datenordner wurde nicht verschoben.");
            }
            if ((File.GetAttributes(legacy) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der bisherige Datenordner ist eine Verknüpfung und kann nicht automatisch verschoben werden.");
            MoveLegacyDirectory(legacy, destination);
        }
    }

    public static string SelectDocumentsDirectory(string documents, string userProfile, IEnumerable<string> cloudRoots)
    {
        if (!IsCloudPath(documents, cloudRoots)) return documents;
        if (string.IsNullOrWhiteSpace(userProfile)) throw new IOException("Das lokale Benutzerprofil ist nicht verfügbar.");
        return Path.Combine(userProfile, "Documents");
    }

    private static IEnumerable<string> CloudRoots() => new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
        .Select(Environment.GetEnvironmentVariable).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);

    private static bool IsKnownCloudPath(string path) => IsCloudPath(path, CloudRoots());

    private static bool IsCloudPath(string path, IEnumerable<string> cloudRoots)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        foreach (var cloud in cloudRoots)
        {
            if (string.IsNullOrWhiteSpace(cloud)) continue;
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cloud)) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static void MoveLegacyDirectory(string legacy, string destination)
    {
        if (!Directory.Exists(legacy)) throw new DirectoryNotFoundException("Der bisherige Datenordner fehlt.");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Der neue Datenordner existiert bereits. Die Daten wurden nicht verändert.");
        Directory.Move(legacy, destination);
    }
}

