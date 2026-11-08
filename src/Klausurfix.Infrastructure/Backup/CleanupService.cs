namespace Klausurfix.Infrastructure.Backup;

using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;

public class CleanupService
{
    private readonly DatabaseContext _dbContext;
    private readonly KeyManager _keyManager;
    private readonly BackupService _backupService;

    public CleanupService(DatabaseContext dbContext, KeyManager keyManager, BackupService backupService)
    {
        _dbContext = dbContext;
        _keyManager = keyManager;
        _backupService = backupService;
    }

    /// <summary>
    /// Deletes all active application data (database tables), but preserves backups and keys.
    /// </summary>
    public void ClearActiveApplicationData()
    {
        _backupService.ExecuteExclusive(ClearActiveApplicationDataCore);
    }

    private void ClearActiveApplicationDataCore()
    {
        // Re-initialize empty database
        if (File.Exists(AppPaths.DatabaseFile))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(AppPaths.DatabaseFile);
        }

        var walFile = $"{AppPaths.DatabaseFile}-wal";
        if (File.Exists(walFile)) File.Delete(walFile);

        var shmFile = $"{AppPaths.DatabaseFile}-shm";
        if (File.Exists(shmFile)) File.Delete(shmFile);

        _dbContext.InitializeDatabase();
    }

    /// <summary>
    /// Deletes all managed backup files in the local backups folder.
    /// </summary>
    public int DeleteAllManagedBackups()
    {
        var count = 0;
        _backupService.ExecuteExclusive(() => { count = _backupService.GetManagedBackups().Count; _backupService.DeleteAllManagedBackups(); });
        return count;
    }

    /// <summary>
    /// Performs a full wipe: active database, all backups, keys, and session state.
    /// Returns the app to the initial setup state.
    /// </summary>
    public void FullReset()
    {
        _backupService.ExecuteExclusive(FullResetCore);
    }

    private void FullResetCore()
    {
        _keyManager.Lock();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(AppPaths.BaseDirectory))
        {
            var dir = new DirectoryInfo(AppPaths.BaseDirectory);
            CheckNoLinks(dir);
            foreach (var file in dir.GetFiles()) file.Delete();
            foreach (var subDir in dir.GetDirectories()) subDir.Delete(recursive: true);
        }
    }

    private static void CheckNoLinks(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Der Datenordner enthält eine Verknüpfung. Das Zurücksetzen wurde abgebrochen.");
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der Datenordner enthält eine Verknüpfung. Das Zurücksetzen wurde abgebrochen.");
            if (entry is DirectoryInfo child) CheckNoLinks(child);
        }
    }
}

