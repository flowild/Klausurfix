namespace Klausurfix.Infrastructure.Backup;

/// <summary>Finds backups in the configured managed folder without creating or changing files.</summary>
public static class ManagedBackupLocator
{
    public static FileInfo? FindLatest(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        return new DirectoryInfo(directory).EnumerateFiles("*.kfbak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
