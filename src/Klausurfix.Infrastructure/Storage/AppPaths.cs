namespace Klausurfix.Infrastructure.Storage;

public static class AppPaths
{
    private static string? _customBaseDir;

    /// <summary>
    /// For testing purposes or custom setups, allow setting a custom base directory.
    /// Otherwise defaults to %LOCALAPPDATA%\Klausurfix.
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

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Klausurfix");
        }
    }

    public static string DataDirectory => Path.Combine(BaseDirectory, "data");
    public static string DatabaseFile => Path.Combine(DataDirectory, "klausurfix.db");
    public static string KeysDirectory => Path.Combine(BaseDirectory, "keys");
    public static string KeyEnvelopeFile => Path.Combine(KeysDirectory, "key_envelope.json");
    public static string BackupsDirectory => Path.Combine(BaseDirectory, "backups");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(KeysDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}

