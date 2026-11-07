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
            var staged = VersionStager.Stage(package, root, architecture);
            // The launcher uses the same mutex; check again before the switch.
            ensureStopped();
            ActiveVersion.Activate(root, staged.Version, staged.Architecture);
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
