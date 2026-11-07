using Klausurfix.Infrastructure.Backup;

namespace Klausurfix.Tests;

public class ManagedBackupLocatorTests
{
    [Fact]
    public void SelectsNewestManagedBackupAndIgnoresTemporaryAndNestedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-backup-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var old = Path.Combine(root, "alt.kfbak");
            var latest = Path.Combine(root, "neu.kfbak");
            File.WriteAllText(old, "synthetisch");
            File.WriteAllText(latest, "synthetisch");
            File.WriteAllText(Path.Combine(root, "unvollstaendig.kfbak.tmp"), "synthetisch");
            File.SetCreationTimeUtc(old, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetCreationTimeUtc(latest, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var nested = Directory.CreateDirectory(Path.Combine(root, "unterordner"));
            File.WriteAllText(Path.Combine(nested.FullName, "fremd.kfbak"), "synthetisch");
            Assert.Equal(latest, ManagedBackupLocator.FindLatest(root)?.FullName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingOrEmptyFolderHasNoSelectionAndIsNotCreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-backup-locator-" + Guid.NewGuid().ToString("N"));
        Assert.Null(ManagedBackupLocator.FindLatest(root));
        Assert.False(Directory.Exists(root));
        Directory.CreateDirectory(root);
        try { Assert.Null(ManagedBackupLocator.FindLatest(root)); }
        finally { Directory.Delete(root); }
    }
}
