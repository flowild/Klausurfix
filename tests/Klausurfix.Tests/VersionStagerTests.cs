namespace Klausurfix.Tests;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

public class VersionStagerTests
{
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
            using var key = RSA.Create(3072);
            var package = CreatePackage(root, key, arch);
            var result = StoppedVersionInstaller.InstallCore(package, key.ExportSubjectPublicKeyInfoPem(), install, arch, () => { });
            Assert.Equal(Path.Combine(result.Directory, "app", "Klausurfix.Web.exe"), ActiveVersion.ResolveBackend(install));
            Assert.Equal("dienst", File.ReadAllText(ActiveVersion.ResolveBackend(install)));
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
            Assert.Throws<InvalidOperationException>(() => StoppedVersionInstaller.InstallCore("nicht-vorhanden", "", root, arch, () => throw new InvalidOperationException("Port belegt")));
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
            using var key = RSA.Create(3072);
            var install = Path.Combine(root, "installation");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "bestehend.txt"), "unverändert");
            var package = CreatePackage(root, key);
            var result = VersionStager.Stage(package, key.ExportSubjectPublicKeyInfoPem(), install, "win-x64");
            Assert.Equal("0.2.0", result.Version);
            Assert.Equal("starter", File.ReadAllText(Path.Combine(result.Directory, "Klausurfix.exe")));
            Assert.Equal("dienst", File.ReadAllText(Path.Combine(result.Directory, "app", "Klausurfix.Web.exe")));
            Assert.Equal("unverändert", File.ReadAllText(Path.Combine(install, "bestehend.txt")));
            Assert.Throws<IOException>(() => VersionStager.Stage(package, key.ExportSubjectPublicKeyInfoPem(), install, "win-x64"));
            Assert.Equal("starter", File.ReadAllText(Path.Combine(result.Directory, "Klausurfix.exe")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void WrongKeyArchitectureOrTamperingDoesNotCreateVersionDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "klausurfix-stage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var key = RSA.Create(3072);
            using var wrongKey = RSA.Create(3072);
            var install = Path.Combine(root, "installation");
            Directory.CreateDirectory(install);
            var package = CreatePackage(root, key);
            Assert.Throws<CryptographicException>(() => VersionStager.Stage(package, wrongKey.ExportSubjectPublicKeyInfoPem(), install, "win-x64"));
            Assert.Throws<InvalidDataException>(() => VersionStager.Stage(package, key.ExportSubjectPublicKeyInfoPem(), install, "win-arm64"));
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
            {
                zip.GetEntry("app/Klausurfix.exe")!.Delete();
                using var entry = zip.CreateEntry("app/Klausurfix.exe").Open();
                entry.Write(Encoding.UTF8.GetBytes("StarTer"));
            }
            Assert.Throws<InvalidDataException>(() => VersionStager.Stage(package, key.ExportSubjectPublicKeyInfoPem(), install, "win-x64"));
            Assert.False(Directory.Exists(Path.Combine(install, "versionen")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreatePackage(string directory, RSA key, string architecture = "win-x64")
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
        var signature = key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var path = Path.Combine(directory, "paket.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Write(string name, byte[] value) { using var entry = zip.CreateEntry(name).Open(); entry.Write(value); }
        Write("manifest.json", manifest);
        Write("manifest.sig", signature);
        foreach (var file in files) Write(file.Key, file.Value);
        return path;
    }
}
