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
        var backups = _backupService.GetManagedBackups();
        _backupService.DeleteAllManagedBackups();
        return backups.Count;
    }

    /// <summary>
    /// Performs a full wipe: active database, all backups, keys, and session state.
    /// Returns the app to the initial setup state.
    /// </summary>
    public void FullReset()
    {
        _keyManager.Lock();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(AppPaths.BaseDirectory))
        {
            // Delete files inside AppPaths.BaseDirectory safely
            var dir = new DirectoryInfo(AppPaths.BaseDirectory);
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                try { file.Delete(); } catch { }
            }

            foreach (var subDir in dir.GetDirectories())
            {
                try { subDir.Delete(recursive: true); } catch { }
            }
        }
    }
}

