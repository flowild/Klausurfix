using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Klausurfix.Infrastructure.Backup;
using System.Text.RegularExpressions;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerWindow());
    }
}

internal sealed class InstallerWindow : Form
{
    private readonly TextBox _path;
    private readonly Label _status;
    private readonly Button _install;
    private readonly TextBox _key;
    private readonly string? _legacyBackup;
    private readonly ProgressBar _progress;

    public InstallerWindow()
    {
        Text = "Klausurfix installieren";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 560);
        Size = new Size(800, 780);
        BackColor = Color.FromArgb(248, 250, 252);
        ForeColor = Color.FromArgb(30, 41, 59);
        Font = new Font("Segoe UI", 10F);
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(26, 0, 26, 0) };
        var headerContent = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 12, 0, 0) };
        var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (icon is not null) headerContent.Controls.Add(new PictureBox { Image = icon.ToBitmap(), Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) });
        headerContent.Controls.Add(new Label { Text = "Klausurfix", ForeColor = Color.FromArgb(37, 99, 235), Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        header.Controls.Add(headerContent);
        page.Controls.Add(header, 0, 0);
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 600, BackColor = Color.White, Padding = new Padding(28), ColumnCount = 1, RowCount = 8, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Klausurfix installieren", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), Margin = new Padding(0, 0, 0, 12) }, 0, 0);
        layout.Controls.Add(new Label { Text = "Wählen Sie den Programmordner. Ihre Arbeitsdaten und Sicherungen werden getrennt davon in Ihrem lokalen Dokumentenordner gespeichert.", AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 20) }, 0, 1);
        layout.Controls.Add(new Label { Text = "Installationspfad", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) }, 0, 2);
        var pathRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _path = new TextBox { Width = 410, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 8, 10, 0), Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Klausurfix") };
        var browse = new Button { Text = "Durchsuchen …", AutoSize = true };
        StyleButton(browse, false);
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { Description = "Installationsordner wählen", UseDescriptionForTitle = true }; if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.SelectedPath; };
        pathRow.Controls.Add(_path); pathRow.Controls.Add(browse); layout.Controls.Add(pathRow, 0, 3);
        _status = new Label { Text = "Klausurfix vor der Installation beenden. Ein vorhandener Programmordner wird nicht überschrieben.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(71, 85, 105), Padding = new Padding(0, 20, 0, 0) };
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klausurfix");
        _legacyBackup = Directory.Exists(legacy) ? ManagedBackupLocator.FindLatest(Path.Combine(legacy, "backups"))?.FullName : null;
        var keyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Visible = Directory.Exists(legacy) };
        keyRow.Controls.Add(new Label { Text = _legacyBackup is null ? "Ältere Daten gefunden: Bitte zuerst eine Sicherung erstellen." : "Ältere Daten gefunden. Schlüssel zur neuesten Sicherung:", AutoSize = true });
        _key = new TextBox { Width = 260, UseSystemPasswordChar = true, PlaceholderText = "Wiederherstellungsschlüssel" };
        keyRow.Controls.Add(_key);
        var chooseKey = new Button { Text = "Schlüsseldatei …", AutoSize = true };
        StyleButton(chooseKey, false);
        chooseKey.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Textdateien (*.txt)|*.txt", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 65536) throw new InvalidDataException("Die Datei ist zu groß.");
                var matches = Regex.Matches(File.ReadAllText(dialog.FileName), @"\bKF-(?:[0-9A-Fa-f]{4}-){7}[0-9A-Fa-f]{4}\b");
                if (matches.Count != 1) throw new InvalidDataException("Die Datei muss genau einen Wiederherstellungsschlüssel enthalten.");
                _key.Text = matches[0].Value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { _key.Clear(); _status.Text = ex.Message; }
        };
        keyRow.Controls.Add(chooseKey); layout.Controls.Add(keyRow, 0, 4);
        layout.Controls.Add(_status, 0, 5);
        _progress = new ProgressBar { Dock = DockStyle.Fill, Height = 20, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0, Visible = false, Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(_progress, 0, 6);
        _install = new Button { Text = "Installieren", AutoSize = true, Anchor = AnchorStyles.Right };
        StyleButton(_install, true);
        _install.Click += async (_, _) => await InstallAsync();
        layout.Controls.Add(_install, 0, 7);
        body.Controls.Add(layout);
        page.Controls.Add(body, 0, 1);
        Controls.Add(page);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.MinimumSize = new Size(primary ? 145 : 125, 44);
        button.Padding = new Padding(12, 4, 12, 4);
        button.Font = new Font(button.Font, FontStyle.Bold);
        button.BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(51, 65, 85);
        button.FlatAppearance.BorderColor = primary ? button.BackColor : Color.FromArgb(203, 213, 225);
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Color.FromArgb(241, 245, 249);
    }

    private async Task InstallAsync()
    {
        var path = _path.Text.Trim();
        var key = _key.Text;
        var legacyExists = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klausurfix"));
        _install.Enabled = false;
        _path.Enabled = false;
        _key.Enabled = false;
        _progress.Visible = true;
        _progress.MarqueeAnimationSpeed = 30;
        UseWaitCursor = true;
        var installed = false;
        try
        {
            _status.Text = "Die Sicherung und das Installationspaket werden geprüft. Anschließend werden die Programmdateien installiert. Das kann einige Minuten dauern …";
            await Task.Run(() => Install(path, legacyExists, _legacyBackup, key));
            string shortcutMessage;
            try { shortcutMessage = CreateDesktopShortcut(Path.GetFullPath(path)); }
            catch (Exception ex) { shortcutMessage = "Die Desktop-Verknüpfung konnte nicht erstellt werden: " + ex.Message; }
            _status.Text = "Klausurfix wurde installiert. " + shortcutMessage + " Bitte starten Sie Klausurfix mit Ihrem normalen Windows-Konto.";
            MessageBox.Show(this, _status.Text, "Klausurfix – Installation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _install.Enabled = false;
            installed = true;
        }
        catch (Exception ex)
        {
            _status.Text = "Installation nicht abgeschlossen: " + ex.Message;
            MessageBox.Show(this, _status.Text, "Klausurfix – Installation", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _install.Enabled = true;
        }
        finally
        {
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Visible = false;
            UseWaitCursor = false;
            _path.Enabled = true;
            _key.Enabled = true;
        }
        if (installed) Close();
    }

    private static string CreateDesktopShortcut(string root)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new IOException("Der Desktop-Ordner ist nicht verfügbar.");
        var shortcut = Path.Combine(desktop, "Klausurfix.lnk");
        if (File.Exists(shortcut)) return "Eine vorhandene Desktop-Verknüpfung wurde nicht überschrieben; bitte ihr Ziel prüfen.";
        var launcher = Path.Combine(root, "Klausurfix.exe");
        var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!;
        var link = (IShellLinkW)Activator.CreateInstance(type)!;
        try
        {
            link.SetPath(launcher);
            link.SetIconLocation(launcher, 0);
            link.SetWorkingDirectory(root);
            link.SetDescription("Klausurfix lokal starten");
            // Persist to a unique temporary file and create the final name without overwriting.
            var temporary = Path.Combine(desktop, ".klausurfix-" + Guid.NewGuid().ToString("N") + ".lnk");
            try
            {
                ((IPersistFile)link).Save(temporary, false);
                File.Move(temporary, shortcut);
                // Tell Explorer about the final name after the atomic move.
                SHChangeNotify(0x00000002, 0x0005, shortcut, IntPtr.Zero); // SHCNE_CREATE, SHCNF_PATHW
                SHChangeNotify(0x00001000, 0x0005, desktop, IntPtr.Zero); // SHCNE_UPDATEDIR
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { Marshal.FinalReleaseComObject(link); }
        return "Eine Desktop-Verknüpfung wurde erstellt.";
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

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

    internal static void Install(string path, bool legacyExists, string? backupPath, string key)
    {
        EnsureSameInteractiveUser();
        if (!Path.IsPathFullyQualified(path)) throw new IOException("Bitte einen vollständigen Installationspfad wählen.");
        var root = Path.GetFullPath(path);
        if (Path.GetPathRoot(root) == root) throw new IOException("Bitte einen eigenen Programmordner wählen.");
        foreach (var dataRoot in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Klausurfix"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klausurfix") })
        {
            var programPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            var dataPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)) + Path.DirectorySeparatorChar;
            if (programPrefix.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase) || dataPrefix.StartsWith(programPrefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Programmordner und Datenordner müssen getrennt sein.");
        }
        if (InstallationRegistration.Find() is not null) throw new IOException("Klausurfix ist bereits installiert. Bitte zum Aktualisieren die Update-EXE verwenden.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Der Installationsordner ist nicht leer.");
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Verknüpfte Installationsordner werden nicht unterstützt.");
        var architecture = RuntimeInformation.OSArchitecture switch { Architecture.Arm64 => "win-arm64", Architecture.X64 => "win-x64", _ => throw new PlatformNotSupportedException("Diese Windows-Architektur wird nicht unterstützt.") };
        using var package = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klausurfix.Install.zip") ?? throw new InvalidDataException("Das Installationspaket fehlt.");
        using var mutex = new Mutex(false, @"Local\KlausurfixStarter-v1");
        var held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Klausurfix wird gerade gestartet oder aktualisiert.");
            StoppedServiceGuard.EnsureStopped();
            if (legacyExists)
            {
                if (backupPath is null || string.IsNullOrWhiteSpace(key)) throw new IOException("Bitte zuerst eine Sicherung erstellen und ihren Wiederherstellungsschlüssel eingeben.");
                using var input = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > 512L * 1024 * 1024) throw new InvalidDataException("Die Sicherungsdatei ist zu groß.");
                using var bytes = new MemoryStream();
                input.CopyTo(bytes);
                BackupService.VerifyBackup(bytes.ToArray(), key);
            }
            Directory.CreateDirectory(root);
            var staged = VersionStager.Stage(package, root, architecture);
            File.Copy(Path.Combine(staged.Directory, "Klausurfix.exe"), Path.Combine(root, "Klausurfix.exe"));
            File.Copy(Path.Combine(staged.Directory, "update-starter-v1.txt"), Path.Combine(root, "update-starter-v1.txt"));
            File.WriteAllText(Path.Combine(root, "aktive-version.txt"), staged.Version + "|" + architecture);
            InstallationRegistration.Register(root);
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static void EnsureSameInteractiveUser()
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out var processId) == 0)
            throw new InvalidOperationException("Das angemeldete Windows-Konto konnte nicht geprüft werden. Bitte den Installer aus dem Windows-Desktop starten.");
        var process = OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero) throw new InvalidOperationException("Das angemeldete Windows-Konto konnte nicht geprüft werden.");
        try
        {
            if (!OpenProcessToken(process, 0x0008, out var token))
                throw new InvalidOperationException("Das angemeldete Windows-Konto konnte nicht geprüft werden.");
            using (token)
            using (var shellIdentity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var installerIdentity = WindowsIdentity.GetCurrent())
                if (shellIdentity.User is null || !shellIdentity.User.Equals(installerIdentity.User))
                    throw new InvalidOperationException("Die Administratorbestätigung verwendet ein anderes Windows-Konto. Bitte die Installation mit Administratorrechten für das angemeldete Konto ausführen; die Datenablage und Registrierung dürfen nicht in einem anderen Konto angelegt werden.");
        }
        finally { CloseHandle(process); }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
