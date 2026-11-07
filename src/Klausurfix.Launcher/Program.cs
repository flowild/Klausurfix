using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

internal static class Program
{
    private const string AppUrl = "http://localhost:5000/";
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };

    [STAThread]
    private static int Main()
    {
        Process? backend = null;
        bool ready = false;
        using var mutex = new Mutex(false, @"Local\KlausurfixStarter-v1");
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(120)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new InvalidOperationException("Der Start dauert zu lange. Bitte erneut versuchen.");

            var appPath = ActiveVersion.ResolveBackend(AppContext.BaseDirectory);
            var appDirectory = Path.GetDirectoryName(appPath)!;
            if (!File.Exists(appPath)) throw new InvalidOperationException("Das Programm wurde nicht gefunden. Bitte das gesamte ZIP-Archiv erneut entpacken.");

            if (IsReady())
            {
                if (!IsPageAvailable()) throw new InvalidOperationException("Eine ältere Klausurfix-Version läuft noch, aber ihre Oberfläche ist nicht erreichbar. Bitte diese Version beenden und erneut starten.");
                var currentService = Process.GetProcessesByName("Klausurfix.Web");
                try
                {
                    var matching = currentService.FirstOrDefault(p => string.Equals(p.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase));
                    if (matching is null)
                        throw new InvalidOperationException("Eine andere Klausurfix-Version läuft bereits. Bitte diese in der Anwendung über 'Beenden' schließen und erneut starten.");
                    backend = Process.GetProcessById(matching.Id);
                }
                finally { foreach (var process in currentService) process.Dispose(); }
            }
            else
            {
                if (IsPortOccupied()) throw new InvalidOperationException("Port 5000 wird bereits von einem anderen Programm verwendet. Bitte dieses Programm beenden und erneut starten.");
                backend = Process.Start(new ProcessStartInfo(appPath) { WorkingDirectory = appDirectory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })
                    ?? throw new InvalidOperationException("Der lokale Dienst konnte nicht gestartet werden.");
                var deadline = Stopwatch.StartNew();
                while (deadline.Elapsed < TimeSpan.FromSeconds(120) && !IsReady())
                {
                    if (backend.HasExited) throw new InvalidOperationException("Der lokale Klausurfix-Dienst wurde beim Start beendet. Bitte prüfen Sie, ob der lokale Datenordner verfügbar und nicht synchronisiert ist und ob eine weitere Klausurfix-Version läuft.");
                    Thread.Sleep(300);
                }
                if (!IsReady() || !IsPageAvailable()) throw new InvalidOperationException("Klausurfix wurde nicht rechtzeitig bereit. Bitte erneut versuchen.");
            }
            ready = true;
            TryCreateDesktopShortcut();
            using var trayMutex = new Mutex(true, $@"Local\KlausurfixTray-{backend!.Id}", out bool ownsTray);
            if (!ownsTray)
            {
                OpenBrowser();
                return 0;
            }
            mutex.ReleaseMutex();
            held = false;
            using var tray = new TrayApplicationContext(backend!, OpenBrowser);
            OpenBrowser();
            System.Windows.Forms.Application.Run(tray);
            return 0;
        }
        catch (Exception ex)
        {
            if (!ready && backend is { HasExited: false }) { try { backend.Kill(); } catch { } }
            MessageBoxW(IntPtr.Zero, ex.Message, "Klausurfix", 0x10);
            return 1;
        }
        finally { backend?.Dispose(); if (held) mutex.ReleaseMutex(); }
    }

    private static bool IsReady()
    {
        try { return Client.GetStringAsync("http://127.0.0.1:5000/api/health").GetAwaiter().GetResult() == "Klausurfix/1"; }
        catch { return false; }
    }

    private static bool IsPageAvailable()
    {
        try { using var response = Client.GetAsync("http://127.0.0.1:5000/").GetAwaiter().GetResult(); return response.IsSuccessStatusCode; }
        catch { return false; }
    }

    private static bool IsPortOccupied()
    {
        try { using var socket = new TcpClient(); return socket.ConnectAsync(IPAddress.Loopback, 5000).Wait(500) && socket.Connected; }
        catch { return false; }
    }

    private static void OpenBrowser()
    {
        var edge = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }
            .Select(path => Path.Combine(path, "Microsoft", "Edge", "Application", "msedge.exe"))
            .FirstOrDefault(File.Exists);
        if (edge != null) Process.Start(new ProcessStartInfo(edge, AppUrl) { UseShellExecute = true });
        else Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true });
    }

    private static void TryCreateDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) return;
            var path = Path.Combine(desktop, "Klausurfix.lnk");
            if (File.Exists(path)) return;
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!;
            var link = (IShellLinkW)Activator.CreateInstance(type)!;
            try
            {
                link.SetPath(Environment.ProcessPath!);
                link.SetIconLocation(Environment.ProcessPath!, 0);
                link.SetWorkingDirectory(AppContext.BaseDirectory);
                link.SetDescription("Klausurfix lokal starten");
                ((IPersistFile)link).Save(path, false);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        catch { /* A shortcut is optional; startup still works. */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(); void GetIDList(); void SetIDList(); void GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory();
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(); void SetArguments(); void GetHotkey(); void SetHotkey();
        void GetShowCmd(); void SetShowCmd(); void GetIconLocation();
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int iconIndex);
        void SetRelativePath(); void Resolve();
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
