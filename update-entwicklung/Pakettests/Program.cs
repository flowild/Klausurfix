using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

if (args.Length is < 2 or > 3) throw new ArgumentException("Pfad zu Paketbau.dll und Paketpruefung.dll angeben; optional Pfad zum Buildskript.");
var builder = Path.GetFullPath(args[0]);
var verifier = Path.GetFullPath(args[1]);
var root = Path.Combine(Path.GetTempPath(), "klausurfix-pakettest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var input = Path.Combine(root, "programm");
    Directory.CreateDirectory(input);
    File.WriteAllText(Path.Combine(input, "Klausurfix.exe"), "synthetischer Testinhalt");
    using var rsa = RSA.Create(3072);
    using var otherRsa = RSA.Create(3072);
    var privateKey = Path.Combine(root, "private.pem");
    var publicKey = Path.Combine(root, "public.pem");
    var wrongKey = Path.Combine(root, "wrong.pem");
    File.WriteAllText(privateKey, rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicKey, rsa.ExportSubjectPublicKeyInfoPem());
    File.WriteAllText(wrongKey, otherRsa.ExportSubjectPublicKeyInfoPem());
    var package = Path.Combine(root, "update.zip");
    int Run(string dll, params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(dll);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Testprozess konnte nicht gestartet werden.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Console.Write(stdout);
        Console.Write(stderr);
        return process.ExitCode;
    }
    void Expect(bool condition, string name)
    {
        if (!condition) throw new Exception("Fehlgeschlagen: " + name);
        Console.WriteLine("OK: " + name);
    }
    Expect(Run(builder, input, "0.2.0", "win-x64", privateKey, package) == 0, "Paket erstellen");
    Expect(Run(verifier, package, publicKey) == 0, "gültiges Paket prüfen");
    Expect(Run(verifier, package, publicKey, "win-arm64") != 0, "abweichende Architektur abweisen");
    if (args.Length == 3)
    {
        var script = Path.GetFullPath(args[2]);
        var exe = Path.Combine(root, "Pruefassistent.exe");
        var info = new ProcessStartInfo("powershell") { UseShellExecute = false };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Paket", package,
                     "-OeffentlicherSchluessel", publicKey, "-Architektur", "win-x64", "-Ausgabedatei", exe })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Build konnte nicht gestartet werden.");
        process.WaitForExit();
        Expect(process.ExitCode == 0 && File.Exists(exe), "einzelnen Prüf-Assistenten bauen");
    }
    Expect(Run(verifier, package, wrongKey) != 0, "falschen Schlüssel abweisen");
    Expect(Run(builder, input, "0.2.0", "win-arm64", privateKey, package) != 0, "vorhandenes Paket nicht überschreiben");
    using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
    {
        using var writer = new StreamWriter(zip.CreateEntry("app/../unerlaubt.txt").Open());
        writer.Write("synthetisch");
    }
    Expect(Run(verifier, package, publicKey) != 0, "Pfadumgehung abweisen");
    using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
        zip.GetEntry("app/../unerlaubt.txt")!.Delete();
    using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
    {
        zip.GetEntry("app/Klausurfix.exe")!.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("app/Klausurfix.exe").Open());
        writer.Write("synthetischer TestinhalX");
    }
    Expect(Run(verifier, package, publicKey) != 0, "veränderten Inhalt abweisen");
}
finally { Directory.Delete(root, true); }
