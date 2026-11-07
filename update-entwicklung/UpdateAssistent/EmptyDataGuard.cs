using Klausurfix.Infrastructure.Storage;

internal static class EmptyDataGuard
{
    public static void EnsureNoData()
    {
        var roots = new[] {
            AppPaths.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Klausurfix"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Klausurfix"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klausurfix")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (File.Exists(root)) throw new IOException("Ein Klausurfix-Datenpfad ist eine Datei. Bitte eine Sicherung verwenden.");
            if (!Directory.Exists(root)) continue;
            var directories = new Stack<string>();
            directories.Push(root);
            while (directories.TryPop(out var dir))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Ein Klausurfix-Datenordner ist verknüpft. Bitte eine Sicherung verwenden.");
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    var attr = File.GetAttributes(entry);
                    if ((attr & FileAttributes.ReparsePoint) != 0 || (attr & FileAttributes.Directory) == 0)
                        throw new IOException("Es wurden vorhandene Klausurfix-Dateien gefunden. Bitte eine Sicherung und den Wiederherstellungsschlüssel verwenden.");
                    directories.Push(entry);
                }
            }
        }
    }
}
