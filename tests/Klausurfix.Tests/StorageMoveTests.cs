using Klausurfix.Infrastructure.Storage;
using Xunit;

public sealed class StorageMoveTests
{
    [Fact]
    public void RedirectedDocumentsUseLocalProfileFolder()
    {
        var profile = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "klausurfix-test-profile"));
        var cloud = Path.Combine(profile, "OneDrive");
        var redirected = Path.Combine(cloud, "Documents");
        Assert.Equal(Path.Combine(profile, "Documents"), AppPaths.SelectDocumentsDirectory(redirected, profile, new[] { cloud }));
        var local = Path.Combine(profile, "MyDocuments");
        Assert.Equal(local, AppPaths.SelectDocumentsDirectory(local, profile, new[] { cloud }));
        Assert.Equal(cloud + "-other", AppPaths.SelectDocumentsDirectory(cloud + "-other", profile, new[] { cloud }));
    }

    [Fact]
    public void MovePreservesAllFilesAndRefusesAnExistingDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-move-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "old");
        var destination = Path.Combine(root, "new");
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "data"));
            Directory.CreateDirectory(Path.Combine(source, "keys"));
            Directory.CreateDirectory(Path.Combine(source, "backups"));
            File.WriteAllText(Path.Combine(source, "data", "klausurfix.db"), "synthetic database");
            File.WriteAllText(Path.Combine(source, "keys", "key_envelope.json"), "synthetic key");
            File.WriteAllText(Path.Combine(source, "backups", "example.kfbak"), "synthetic backup");
            Directory.CreateDirectory(destination);
            Assert.Throws<IOException>(() => AppPaths.MoveLegacyDirectory(source, destination));
            Assert.True(File.Exists(Path.Combine(source, "data", "klausurfix.db")));
            Directory.Delete(destination);
            AppPaths.MoveLegacyDirectory(source, destination);
            Assert.False(Directory.Exists(source));
            Assert.Equal("synthetic database", File.ReadAllText(Path.Combine(destination, "data", "klausurfix.db")));
            Assert.Equal("synthetic key", File.ReadAllText(Path.Combine(destination, "keys", "key_envelope.json")));
            Assert.Equal("synthetic backup", File.ReadAllText(Path.Combine(destination, "backups", "example.kfbak")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
