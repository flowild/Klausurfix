using System.Runtime.InteropServices;
using System.Diagnostics;

/// <summary>
/// Installs a structurally checked version only while the local service is stopped. The
/// caller must separately coordinate browser inputs, backup and shutdown.
/// </summary>
public static class StoppedVersionInstaller
{
    public static StagedVersion Install(string packagePath, string installationDirectory, string architecture)
        => InstallCore(packagePath, installationDirectory, architecture, StoppedServiceGuard.EnsureStopped);

    public static StagedVersion InstallAndStart(Stream package, string installationDirectory, string architecture)
        => InstallCore(package, installationDirectory, architecture, StoppedServiceGuard.EnsureStopped, root =>
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(root, "Klausurfix.exe"))
                { WorkingDirectory = root, UseShellExecute = true })
                ?? throw new InvalidOperationException("Die Version wurde aktiviert, aber Klausurfix konnte nicht gestartet werden. Bitte über die Verknüpfung starten.");
        });

    internal static StagedVersion InstallCore(string packagePath, string installationDirectory, string architecture, Action ensureStopped)
    {
        using var package = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return InstallCore(package, installationDirectory, architecture, ensureStopped, null);
    }

    internal static StagedVersion InstallCore(Stream package, string installationDirectory, string architecture,
        Action ensureStopped, Action<string>? start)
    {
        var expected = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X64 => "win-x64",
            _ => throw new PlatformNotSupportedException("Diese Windows-Architektur wird nicht unterstützt.")
        };
        if (architecture != expected) throw new InvalidDataException("Das Update passt nicht zur Windows-Architektur.");
        var root = Path.GetFullPath(installationDirectory);
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfte Programmordner werden nicht unterstützt.");
        if (!File.Exists(Path.Combine(root, "Klausurfix.exe")) ||
            !File.Exists(Path.Combine(root, "update-starter-v1.txt")))
            throw new InvalidOperationException("Dieser Programmordner besitzt noch keinen Starter für Versionsupdates.");
        using var mutex = new Mutex(false, @"Local\KlausurfixStarter-v1");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Klausurfix wird gerade gestartet oder aktualisiert.");
            ensureStopped();
            var manifest = UpdatePackage.Verify(package);
            if (manifest.Architektur != architecture) throw new InvalidDataException("Falsche Paketarchitektur.");
            var selection = manifest.Version + "|" + architecture;
            var pending = Path.Combine(root, ActiveVersion.PendingUpdate);
            var backup = Path.Combine(root, ".starter-vor-update.bak");
            var starter = Path.Combine(root, "Klausurfix.exe");
            foreach (var path in new[] { pending, backup, Path.Combine(root, "aktive-version.txt"), Path.Combine(root, "basis-version.txt") })
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Verknüpfte Update-Dateien werden nicht unterstützt.");
            if ((File.GetAttributes(starter) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Ein verknüpfter Starter wird nicht ersetzt.");
            var resumed = File.Exists(pending);
            if (resumed)
            {
                if (new FileInfo(pending).Length > 100 || File.ReadAllText(pending) != selection)
                    throw new InvalidOperationException("Ein anderes Update wurde unterbrochen. Bitte zuerst dessen Update-EXE erneut ausführen.");
                var active = Path.Combine(root, "aktive-version.txt");
                if (File.Exists(active) && File.ReadAllText(active) == selection)
                {
                    var completed = VersionStager.Stage(package, root, architecture, true);
                    using (var current = File.OpenRead(starter))
                    using (var expectedStarter = File.OpenRead(Path.Combine(completed.Directory, "Klausurfix.exe")))
                        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                            System.Security.Cryptography.SHA256.HashData(current), System.Security.Cryptography.SHA256.HashData(expectedStarter)))
                            throw new InvalidDataException("Der aktive Starter stimmt nicht mit dem geprüften Update überein.");
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Delete(pending);
                    start?.Invoke(root);
                    return completed;
                }
                if (File.Exists(backup)) File.Replace(backup, starter, null);
                File.Delete(pending);
            }
            else if (File.Exists(backup))
                throw new IOException("Eine nicht zugeordnete Starter-Sicherung liegt im Programmordner. Update abgebrochen.");

            ActiveVersion.EnsureNewer(root, manifest.Version!, architecture);
            var staged = VersionStager.Stage(package, root, architecture, true);
            // The launcher uses the same mutex; check again before the switch.
            ensureStopped();
            var candidate = Path.Combine(root, ".starter-neu-" + Guid.NewGuid().ToString("N") + ".tmp");
            var journalTemp = Path.Combine(root, ".starter-update-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(Path.Combine(staged.Directory, "Klausurfix.exe"), candidate);
                using (var journal = new FileStream(journalTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    journal.Write(System.Text.Encoding.UTF8.GetBytes(selection));
                    journal.Flush(flushToDisk: true);
                }
                File.Move(journalTemp, pending);
                File.Replace(candidate, starter, backup);
                ActiveVersion.Activate(root, staged.Version, staged.Architecture);
                File.Delete(backup);
                File.Delete(pending);
            }
            catch (Exception ex)
            {
                // Leave the journal and any backup for a safe retry of this same EXE.
                throw new IOException("Die Umschaltung wurde unterbrochen. Bitte diese Update-EXE nach Beheben der Ursache erneut ausführen. Ursache: " + ex.Message, ex);
            }
            finally
            {
                if (File.Exists(candidate)) File.Delete(candidate);
                if (File.Exists(journalTemp)) File.Delete(journalTemp);
            }
            try { start?.Invoke(root); }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Die neue Version wurde aktiviert, aber Klausurfix konnte nicht gestartet werden. Bitte über die Verknüpfung starten.", ex);
            }
            return staged;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

}
