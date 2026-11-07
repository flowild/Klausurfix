using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

/// <summary>
/// Installs a verified version only while the local service is stopped. The
/// caller must separately coordinate browser inputs, backup and shutdown.
/// </summary>
public static class StoppedVersionInstaller
{
    public static StagedVersion Install(string packagePath, string publicKey, string installationDirectory, string architecture)
        => InstallCore(packagePath, publicKey, installationDirectory, architecture, EnsureStopped);

    internal static StagedVersion InstallCore(string packagePath, string publicKey, string installationDirectory, string architecture, Action ensureStopped)
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
            var staged = VersionStager.Stage(packagePath, publicKey, root, architecture);
            // The launcher uses the same mutex; check again before the switch.
            ensureStopped();
            ActiveVersion.Activate(root, staged.Version, staged.Architecture);
            return staged;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private static void EnsureStopped()
    {
        var processes = Process.GetProcessesByName("Klausurfix.Web");
        try
        {
            if (processes.Length != 0) throw new InvalidOperationException("Klausurfix läuft noch. Bitte den Dienst geordnet beenden.");
        }
        finally { foreach (var process in processes) process.Dispose(); }

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try { socket.Bind(new IPEndPoint(IPAddress.Loopback, 5000)); }
        catch (SocketException ex) { throw new InvalidOperationException("Port 5000 ist belegt. Das Update wurde nicht aktiviert.", ex); }
    }
}
