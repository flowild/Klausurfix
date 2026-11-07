namespace Klausurfix.Tests;

using System.Runtime.InteropServices;

public class ActiveVersionTests
{
    [Fact]
    public void SwitchesStarterToPreparedVersionAndRejectsDowngrade()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-aktive-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            File.WriteAllText(Path.Combine(root, "basis-version.txt"), "0.1.5|" + architecture);
            Assert.Equal(Path.Combine(root, "app", "Klausurfix.Web.exe"), ActiveVersion.ResolveBackend(root));
            var folder = Path.Combine(root, "versionen", "0.2.0-" + architecture);
            Directory.CreateDirectory(Path.Combine(folder, "app"));
            File.WriteAllText(Path.Combine(folder, "Klausurfix.exe"), "synthetisch");
            var backend = Path.Combine(folder, "app", "Klausurfix.Web.exe");
            File.WriteAllText(backend, "synthetisch");
            ActiveVersion.Activate(root, "0.2.0", architecture);
            Assert.Equal(backend, ActiveVersion.ResolveBackend(root));
            Assert.Throws<InvalidOperationException>(() => ActiveVersion.Activate(root, "0.2.0", architecture));
            Assert.Equal(backend, ActiveVersion.ResolveBackend(root));
            File.WriteAllText(Path.Combine(root, "aktive-version.txt"), "../unzulässig|" + architecture);
            Assert.Throws<InvalidDataException>(() => ActiveVersion.ResolveBackend(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FirstActivationRequiresKnownOlderBaseVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-basis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "versionen", "0.1.5-win-x64", "app"));
        try
        {
            var version = Path.Combine(root, "versionen", "0.1.5-win-x64");
            File.WriteAllText(Path.Combine(version, "Klausurfix.exe"), "starter");
            File.WriteAllText(Path.Combine(version, "app", "Klausurfix.Web.exe"), "dienst");
            Assert.Throws<InvalidOperationException>(() => ActiveVersion.Activate(root, "0.1.5", "win-x64"));
            File.WriteAllText(Path.Combine(root, "basis-version.txt"), "0.1.5|win-x64");
            Assert.Throws<InvalidOperationException>(() => ActiveVersion.Activate(root, "0.1.5", "win-x64"));
            Assert.False(File.Exists(Path.Combine(root, "aktive-version.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void IncompleteVersionDoesNotChangeSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-aktive-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<FileNotFoundException>(() => ActiveVersion.Activate(root, "0.2.0", "win-x64"));
            Assert.False(File.Exists(Path.Combine(root, "aktive-version.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
