using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Klausurfix.Infrastructure.Backup;
using Klausurfix.Infrastructure.Storage;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new UpdateWindow());
    }
}

internal sealed class UpdateWindow : Form
{
    private readonly Label _status;
    private readonly Label _selectedBackup;
    private readonly TextBox _recoveryKey;
    private readonly Button _install;
    private readonly CheckBox _noData;
    private readonly CheckBox _sameVersion;
    private string? _backupPath;
    private readonly System.Windows.Forms.Timer _verificationTimer = new() { Interval = 800 };
    private int _verificationGeneration;
    private bool _packageValid;

    public UpdateWindow()
    {
        Text = "Klausurfix Update – Entwicklungsstand";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 520);
        Size = new Size(800, 850);
        BackColor = Color.FromArgb(248, 250, 252);
        ForeColor = Color.FromArgb(30, 41, 59);
        Font = new Font("Segoe UI", 10F);
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, WrapContents = false, Padding = new Padding(26, 12, 26, 0) };
        var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (icon is not null) header.Controls.Add(new PictureBox { Image = icon.ToBitmap(), Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) });
        header.Controls.Add(new Label { Text = "Klausurfix", ForeColor = Color.FromArgb(37, 99, 235), Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        page.Controls.Add(header, 0, 0);
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.White, Padding = new Padding(28), ColumnCount = 1, RowCount = 12 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Klausurfix aktualisieren", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42), Margin = new Padding(0, 0, 0, 12) }, 0, 0);
        layout.Controls.Add(new Label { Text = "Bitte Klausurfix vor dem Update sichern und beenden.", AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 0, 0, 20) }, 0, 1);
        layout.Controls.Add(new Label { Text = "Prüfstatus", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) }, 0, 2);
        _status = new Label { Text = "Paket wird geprüft …", AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 0, 0, 20) };
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(new Label { Text = "Sicherung", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) }, 0, 4);
        _selectedBackup = new Label { AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(_selectedBackup, 0, 5);
        layout.SizeChanged += (_, _) =>
        {
            var width = Math.Max(200, layout.ClientSize.Width - layout.Padding.Horizontal - 10);
            var maximum = new Size(width, 0);
            if (_status.MaximumSize != maximum) _status.MaximumSize = maximum;
            if (_selectedBackup.MaximumSize != maximum) _selectedBackup.MaximumSize = maximum;
        };
        try
        {
            var latest = new[] { AppPaths.BackupsDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klausurfix", "backups") }
                .Select(ManagedBackupLocator.FindLatest).Where(f => f is not null).OrderByDescending(f => f!.CreationTimeUtc).FirstOrDefault();
            if (latest is not null) SelectBackup(latest.FullName, true);
            else _selectedBackup.Text = "Keine verwaltete Sicherung gefunden. Bitte eine Sicherung auswählen.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _selectedBackup.Text = "Der Sicherungsordner konnte nicht gelesen werden. Bitte eine Sicherung auswählen.";
        }
        _noData = new CheckBox { Text = "Es gibt noch keine Klausurfix-Daten", AutoSize = true };
        _noData.CheckedChanged += (_, _) => ScheduleVerification();
        var chooseBackup = new Button { Text = "Sicherung ändern …", AutoSize = true };
        StyleButton(chooseBackup, false);
        chooseBackup.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Klausurfix-Sicherung (*.kfbak)|*.kfbak", CheckFileExists = true };
            if (Directory.Exists(AppPaths.BackupsDirectory)) dialog.InitialDirectory = AppPaths.BackupsDirectory;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            SelectBackup(dialog.FileName, false);
            ScheduleVerification();
        };
        _recoveryKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "Wiederherstellungsschlüssel eingeben", BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 8, 12, 0) };
        _recoveryKey.TextChanged += (_, _) => ScheduleVerification();
        _verificationTimer.Tick += async (_, _) => { _verificationTimer.Stop(); await VerifyBackupAsync(); };
        var chooseKey = new Button { Text = "Schlüsseldatei auswählen …", AutoSize = true };
        StyleButton(chooseKey, false);
        chooseKey.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Textdateien (*.txt)|*.txt", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var info = new FileInfo(dialog.FileName);
                if (info.Length > 65536) throw new InvalidDataException("Die Schlüsseldatei ist zu groß.");
                var matches = Regex.Matches(File.ReadAllText(info.FullName), @"\bKF-(?:[0-9A-Fa-f]{4}-){7}[0-9A-Fa-f]{4}\b");
                if (matches.Count != 1) throw new InvalidDataException("Die Datei muss genau einen Klausurfix-Wiederherstellungsschlüssel enthalten.");
                _recoveryKey.Text = matches[0].Value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _recoveryKey.Clear();
                _status.Text = $"Schlüsseldatei konnte nicht gelesen werden. {ex.Message}";
            }
        };
        _install = new Button { Text = "Update installieren", AutoSize = true, Enabled = false };
        StyleButton(_install, true);
        _install.Click += async (_, _) => await InstallAsync();
        var backupRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 20) };
        backupRow.Controls.Add(chooseBackup);
        layout.Controls.Add(backupRow, 0, 6);
        layout.Controls.Add(new Label { Text = "Wiederherstellungsschlüssel", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) }, 0, 7);
        var keyRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        keyRow.Controls.Add(_recoveryKey, 0, 0);
        keyRow.Controls.Add(chooseKey, 1, 0);
        layout.Controls.Add(keyRow, 0, 8);
        _noData.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(_noData, 0, 9);
        _sameVersion = new CheckBox { Text = "Gleiche Version zu Testzwecken erneut installieren", AutoSize = true, Margin = new Padding(0, 0, 0, 24) };
        layout.Controls.Add(_sameVersion, 0, 10);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        var close = new Button { Text = "Schließen", AutoSize = true };
        StyleButton(close, false);
        close.Click += (_, _) => Close();
        actions.Controls.Add(_install);
        actions.Controls.Add(close);
        layout.Controls.Add(actions, 0, 11);
        body.Controls.Add(layout);
        page.Controls.Add(body, 0, 1);
        Controls.Add(page);
        Shown += async (_, _) =>
        {
            ActiveControl = null;
            body.AutoScrollPosition = Point.Empty;
            await CheckPackageAsync();
        };
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.MinimumSize = new Size(primary ? 170 : 125, 44);
        button.Padding = new Padding(12, 4, 12, 4);
        button.Font = new Font(button.Font, FontStyle.Bold);
        button.BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(51, 65, 85);
        button.FlatAppearance.BorderColor = primary ? button.BackColor : Color.FromArgb(203, 213, 225);
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Color.FromArgb(241, 245, 249);
    }

    private void SelectBackup(string path, bool automatic)
    {
        _backupPath = path;
        var date = File.GetCreationTime(path);
        _selectedBackup.Text = $"{(automatic ? "Neueste verwaltete Sicherung vorausgewählt" : "Ausgewählte Sicherung")}:\n{path}\nErstellt: {date:g} · Vor der Installation wird die Datei geprüft.";
    }

    private async Task InstallAsync()
    {
        try { InteractiveUser.EnsureSame(); }
        catch (Exception ex) { _status.Text = ex.Message; return; }
        var noData = _noData.Checked;
        var allowSameVersion = _sameVersion.Checked;
        if (!noData && _backupPath is null) { _status.Text = "Bitte zuerst eine aktuelle Sicherung auswählen."; return; }
        if (!noData && string.IsNullOrWhiteSpace(_recoveryKey.Text)) { _status.Text = "Bitte den Wiederherstellungsschlüssel eingeben."; return; }
        var root = InstallationRegistration.Find();
        if (root is null) { _status.Text = "Keine registrierte Klausurfix-Installation gefunden. Bitte Klausurfix zuerst mit dem Installer einrichten. Ältere portable Installationen sind nicht registriert."; return; }
        var backupPath = _backupPath;
        var recoveryKey = _recoveryKey.Text;
        _verificationTimer.Stop();
        Enabled = false;
        try
        {
            _status.Text = "Sicherung und Update werden geprüft und installiert …";
            var result = await Task.Run(() =>
            {
                using var package = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klausurfix.Update.zip")
                    ?? throw new InvalidOperationException("Das Update-Paket fehlt.");
                var architecture = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.Arm64 => "win-arm64",
                    Architecture.X64 => "win-x64",
                    _ => throw new InvalidOperationException("Diese Windows-Architektur wird nicht unterstützt.")
                };
                if (noData)
                {
                    StoppedServiceGuard.EnsureStopped();
                    EmptyDataGuard.EnsureNoData();
                    return StoppedVersionInstaller.Install(package, root, architecture, allowSameVersion);
                }
                using var backupStream = new FileStream(backupPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (backupStream.Length > 512L * 1024 * 1024) throw new InvalidDataException("Die Sicherungsdatei ist zu groß.");
                using var backupBuffer = new MemoryStream();
                backupStream.CopyTo(backupBuffer);
                return UpdateWorkflow.Install(package, root, architecture, backupBuffer.ToArray(), recoveryKey, allowSameVersion);
            });
            _status.Text = $"Version {result.Version} wurde aktiviert. Bitte Klausurfix über die Desktop-Verknüpfung mit dem normalen Benutzerkonto starten.";
            MessageBox.Show(this, _status.Text, "Klausurfix Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { _status.Text = $"Update nicht abgeschlossen.\n\n{ex.Message}"; }
        finally { Enabled = true; }
    }

    private void ScheduleVerification()
    {
        _verificationGeneration++;
        _install.Enabled = false;
        _verificationTimer.Stop();
        if (_noData.Checked)
        {
            try { EmptyDataGuard.EnsureNoData(); _status.Text = "Keine vorhandenen Klausurfix-Dateien gefunden."; _install.Enabled = _packageValid; }
            catch (Exception ex) { _status.Text = ex.Message; }
            return;
        }
        if (_backupPath is not null && !string.IsNullOrWhiteSpace(_recoveryKey.Text))
            _verificationTimer.Start();
    }

    private async Task VerifyBackupAsync()
    {
        if (_backupPath is null) { _status.Text = "Bitte zuerst eine Sicherung auswählen."; return; }
        var path = _backupPath;
        var key = _recoveryKey.Text;
        var generation = _verificationGeneration;
        try
        {
            _status.Text = "Sicherung wird geprüft …";
            await Task.Run(() =>
            {
                StoppedServiceGuard.EnsureStopped();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 512L * 1024 * 1024) throw new InvalidDataException("Die Sicherungsdatei ist zu groß.");
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                BackupService.VerifyBackup(buffer.ToArray(), key);
            });
            if (generation != _verificationGeneration) return;
            _status.Text = "✓ Sicherung mit dem Wiederherstellungsschlüssel geprüft.";
            _install.Enabled = _packageValid;
        }
        catch (Exception ex) { if (generation == _verificationGeneration) _status.Text = $"Die Sicherung konnte nicht geprüft werden. Es wurde nichts installiert.\n\n{ex.Message}"; }
    }

    private async Task CheckPackageAsync()
    {
        try
        {
            var result = await Task.Run(() =>
            {
                StoppedServiceGuard.EnsureStopped();
                var assembly = Assembly.GetExecutingAssembly();
                using var package = assembly.GetManifestResourceStream("Klausurfix.Update.zip") ?? throw new InvalidOperationException("In dieser Entwicklungsdatei ist kein Update-Paket enthalten.");
                var manifest = UpdatePackage.Verify(package);
                var expected = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.Arm64 => "win-arm64",
                    Architecture.X64 => "win-x64",
                    _ => throw new InvalidOperationException("Diese Windows-Architektur wird nicht unterstützt.")
                };
                if (manifest.Architektur != expected) throw new InvalidOperationException($"Dieses Paket ist für {manifest.Architektur}, dieses Windows benötigt {expected}.");
                return manifest;
            });
            _status.Text = $"✓ Klausurfix ist beendet und Port 5000 frei\n✓ Paket geprüft: Version {result.Version} · {result.Architektur}";
            _packageValid = true;
            ScheduleVerification();
        }
        catch (Exception ex)
        {
            _status.Text = $"Das Paket konnte nicht geprüft werden. Es wurde nichts installiert.\n\n{ex.Message}";
        }
    }
}
