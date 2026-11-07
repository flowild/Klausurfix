namespace Klausurfix.Tests;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

public class VersionStagerTests
{
    [Fact]
    public void WorkflowDoesNotInstallWhenBackupVerificationFails()
    {
        var called = false;
        using var package = new MemoryStream();
        Assert.Throws<UnauthorizedAccessException>(() => UpdateWorkflow.Run(package, "", "win-x64", [], "falsch",
            () => { }, (_, _) => throw new UnauthorizedAccessException(),
            (_, _, _) => { called = true; throw new InvalidOperationException(); }));
        Assert.False(called);
    }

    [Fact]
    public void WorkflowDoesNotInstallWhenServiceIsRunning()
    {
        var checkedBackup = false;
        using var package = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => UpdateWorkflow.Run(package, "", "win-x64", [], "",
            () => throw new InvalidOperationException(), (_, _) => checkedBackup = true,
            (_, _, _) => throw new InvalidOperationException()));
        Assert.False(checkedBackup);
    }

    [Fact]
    public void StoppedServiceGuardRejectsOccupiedLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Throws<InvalidOperationException>(() => StoppedServiceGuard.EnsurePortAvailable(port));
        listener.Stop();
        StoppedServiceGuard.EnsurePortAvailable(port);
    }

    [Fact]
    public void InstallerStagesAndActivatesVerifiedPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-install-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            var install = Path.Combine(root, "installation");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "Klausurfix.exe"), "synthetisch");
            File.WriteAllText(Path.Combine(install, "update-starter-v1.txt"), "synthetisch");
            File.WriteAllText(Path.Combine(install, "basis-version.txt"), "0.1.5|" + arch);
            var package = CreatePackage(root, arch);
            using var packageStream = File.OpenRead(package);
            var started = false;
            var result = StoppedVersionInstaller.InstallCore(packageStream, install, arch, () => { }, rootDirectory =>
            {
                Assert.Equal(install, rootDirectory);
                Assert.Contains("0.2.0", ActiveVersion.ResolveBackend(rootDirectory));
                started = true;
            });
            Assert.True(started);
            Assert.Equal("starter", File.ReadAllText(Path.Combine(install, "Klausurfix.exe")));
            Assert.False(File.Exists(Path.Combine(install, ActiveVersion.PendingUpdate)));
            Assert.Equal(Path.Combine(result.Directory, "app", "Klausurfix.Web.exe"), ActiveVersion.ResolveBackend(install));
            Assert.Equal("dienst", File.ReadAllText(ActiveVersion.ResolveBackend(install)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedStarterSwitchCanBeResumed(bool activationAlreadyCompleted)
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-resume-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            var install = Path.Combine(root, "installation");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "Klausurfix.exe"), "starter");
            File.WriteAllText(Path.Combine(install, ".starter-vor-update.bak"), "vorher");
            File.WriteAllText(Path.Combine(install, "update-starter-v1.txt"), "synthetisch");
            File.WriteAllText(Path.Combine(install, "basis-version.txt"), "0.1.5|" + arch);
            var package = CreatePackage(root, arch);
            VersionStager.Stage(package, install, arch);
            if (activationAlreadyCompleted) ActiveVersion.Activate(install, "0.2.0", arch);
            File.WriteAllText(Path.Combine(install, ActiveVersion.PendingUpdate), "0.2.0|" + arch);
            Assert.Throws<InvalidOperationException>(() => ActiveVersion.ResolveBackend(install));
            using var stream = File.OpenRead(package);
            var started = false;
            StoppedVersionInstaller.InstallCore(stream, install, arch, () => { }, _ => started = true);
            Assert.True(started);
            Assert.Equal("starter", File.ReadAllText(Path.Combine(install, "Klausurfix.exe")));
            Assert.False(File.Exists(Path.Combine(install, ActiveVersion.PendingUpdate)));
            Assert.False(File.Exists(Path.Combine(install, ".starter-vor-update.bak")));
            Assert.Contains("0.2.0", ActiveVersion.ResolveBackend(install));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RetryRejectsChangedStagedFilesBeforeReplacingStarter()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-resume-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            var install = Path.Combine(root, "installation");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "Klausurfix.exe"), "vorher");
            File.WriteAllText(Path.Combine(install, "update-starter-v1.txt"), "synthetisch");
            File.WriteAllText(Path.Combine(install, "basis-version.txt"), "0.1.5|" + arch);
            var package = CreatePackage(root, arch);
            var staged = VersionStager.Stage(package, install, arch);
            File.WriteAllText(Path.Combine(staged.Directory, "Klausurfix.exe"), "verändert");
            using var stream = File.OpenRead(package);
            Assert.Throws<InvalidDataException>(() => StoppedVersionInstaller.InstallCore(stream, install, arch, () => { }, null));
            Assert.Equal("vorher", File.ReadAllText(Path.Combine(install, "Klausurfix.exe")));
            Assert.False(File.Exists(Path.Combine(install, "aktive-version.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InstallerRejectsOccupiedPortWithoutChangingVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-install-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Klausurfix.exe"), "synthetisch");
        File.WriteAllText(Path.Combine(root, "update-starter-v1.txt"), "synthetisch");
        try
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            using var package = new MemoryStream();
            Assert.Throws<InvalidOperationException>(() => StoppedVersionInstaller.InstallCore(package, root, arch, () => throw new InvalidOperationException("Port belegt"), null));
            Assert.False(File.Exists(Path.Combine(root, "aktive-version.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, "versionen")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void VerifiedPackageIsStagedWithoutChangingExistingFilesOrOverwritingVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-stage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var install = Path.Combine(root, "installation");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "bestehend.txt"), "unverändert");
            var package = CreatePackage(root);
            var result = VersionStager.Stage(package, install, "win-x64");
            Assert.Equal("0.2.0", result.Version);
            Assert.Equal("starter", File.ReadAllText(Path.Combine(result.Directory, "Klausurfix.exe")));
            Assert.Equal("dienst", File.ReadAllText(Path.Combine(result.Directory, "app", "Klausurfix.Web.exe")));
            Assert.Equal("unverändert", File.ReadAllText(Path.Combine(install, "bestehend.txt")));
            Assert.Throws<IOException>(() => VersionStager.Stage(package, install, "win-x64"));
            Assert.Equal("starter", File.ReadAllText(Path.Combine(result.Directory, "Klausurfix.exe")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void WrongArchitectureOrDamagedContentDoesNotCreateVersionDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-stage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var install = Path.Combine(root, "installation");
            Directory.CreateDirectory(install);
            var package = CreatePackage(root);
            Assert.Throws<InvalidDataException>(() => VersionStager.Stage(package, install, "win-arm64"));
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
            {
                zip.GetEntry("app/Klausurfix.exe")!.Delete();
                using var entry = zip.CreateEntry("app/Klausurfix.exe").Open();
                entry.Write(Encoding.UTF8.GetBytes("StarTer"));
            }
            Assert.Throws<InvalidDataException>(() => VersionStager.Stage(package, install, "win-x64"));
            Assert.False(Directory.Exists(Path.Combine(install, "versionen")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static string CreatePackage(string directory, string architecture = "win-x64")
    {
        var files = new Dictionary<string, byte[]>
        {
            ["app/Klausurfix.exe"] = Encoding.UTF8.GetBytes("starter"),
            ["app/app/Klausurfix.Web.exe"] = Encoding.UTF8.GetBytes("dienst")
        };
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Format = 1, Produkt = "Klausurfix", Version = "0.2.0", Architektur = architecture,
            Dateien = files.Select(f => new { Pfad = f.Key, Groesse = f.Value.Length, Sha256 = Convert.ToHexString(SHA256.HashData(f.Value)) }).ToArray()
        });
        var path = Path.Combine(directory, "paket.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Write(string name, byte[] value) { using var entry = zip.CreateEntry(name).Open(); entry.Write(value); }
        Write("manifest.json", manifest);
        foreach (var file in files) Write(file.Key, file.Value);
        return path;
    }
}
