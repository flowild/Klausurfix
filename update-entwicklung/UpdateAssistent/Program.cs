using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Klausurfix.Infrastructure.Backup;

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
    private readonly TextBox _recoveryKey;
    private readonly Button _verifyBackup;
    private readonly Button _install;
    private string? _backupPath;

    public UpdateWindow()
    {
        Text = "Klausurfix Update – Entwicklungsstand";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(540, 300);
        Size = new Size(650, 360);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Klausurfix-Update prüfen", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 15) });
        _status = new Label { Text = "Paket wird geprüft …", Dock = DockStyle.Fill, AutoEllipsis = false };
        layout.Controls.Add(_status);
        var backupControls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var chooseBackup = new Button { Text = "Sicherung auswählen", AutoSize = true };
        chooseBackup.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Klausurfix-Sicherung (*.kfbak)|*.kfbak", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _backupPath = dialog.FileName;
            _status.Text = "Sicherung ausgewählt. Bitte Wiederherstellungsschlüssel eingeben und prüfen.";
        };
        _recoveryKey = new TextBox { Width = 200, UseSystemPasswordChar = true, PlaceholderText = "Wiederherstellungsschlüssel" };
        _verifyBackup = new Button { Text = "Sicherung prüfen", AutoSize = true };
        _verifyBackup.Click += async (_, _) => await VerifyBackupAsync();
        _install = new Button { Text = "Update installieren und starten", AutoSize = true };
        _install.Click += async (_, _) => await InstallAsync();
        backupControls.Controls.Add(chooseBackup);
        backupControls.Controls.Add(_recoveryKey);
        backupControls.Controls.Add(_verifyBackup);
        backupControls.Controls.Add(_install);
        layout.Controls.Add(backupControls);
        var close = new Button { Text = "Schließen", AutoSize = true, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close);
        Controls.Add(layout);
        Shown += async (_, _) => await CheckPackageAsync();
    }

    private async Task InstallAsync()
    {
        if (_backupPath is null) { _status.Text = "Bitte zuerst eine aktuelle Sicherung auswählen."; return; }
        if (string.IsNullOrWhiteSpace(_recoveryKey.Text)) { _status.Text = "Bitte den Wiederherstellungsschlüssel eingeben."; return; }
        using var folder = new FolderBrowserDialog { Description = "Klausurfix-Programmordner mit Klausurfix.exe auswählen", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        var backupPath = _backupPath;
        var recoveryKey = _recoveryKey.Text;
        var root = folder.SelectedPath;
        _recoveryKey.Clear();
        Enabled = false;
        try
        {
            _status.Text = "Sicherung und Update werden geprüft und installiert …";
            var result = await Task.Run(() =>
            {
                using var backupStream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (backupStream.Length > 512L * 1024 * 1024) throw new InvalidDataException("Die Sicherungsdatei ist zu groß.");
                using var backupBuffer = new MemoryStream();
                backupStream.CopyTo(backupBuffer);
                using var package = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klausurfix.Update.zip")
                    ?? throw new InvalidOperationException("Das Update-Paket fehlt.");
                var architecture = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.Arm64 => "win-arm64",
                    Architecture.X64 => "win-x64",
                    _ => throw new InvalidOperationException("Diese Windows-Architektur wird nicht unterstützt.")
                };
                return UpdateWorkflow.InstallAndStart(package, root, architecture, backupBuffer.ToArray(), recoveryKey);
            });
            _status.Text = $"Version {result.Version} wurde aktiviert. Klausurfix wird gestartet.";
        }
        catch (Exception ex) { _status.Text = $"Update nicht abgeschlossen.\n\n{ex.Message}"; }
        finally { Enabled = true; }
    }

    private async Task VerifyBackupAsync()
    {
        if (_backupPath is null) { _status.Text = "Bitte zuerst eine Sicherung auswählen."; return; }
        var path = _backupPath;
        var key = _recoveryKey.Text;
        _recoveryKey.Clear();
        _verifyBackup.Enabled = false;
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
            _status.Text = "✓ Sicherung mit dem Wiederherstellungsschlüssel geprüft. Für die Installation den Wiederherstellungsschlüssel erneut eingeben.";
        }
        catch (Exception ex) { _status.Text = $"Die Sicherung konnte nicht geprüft werden. Es wurde nichts installiert.\n\n{ex.Message}"; }
        finally { _verifyBackup.Enabled = true; }
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
            _status.Text = $"✓ Klausurfix ist beendet und Port 5000 frei\n✓ Paketinhalt geprüft\n✓ Windows-Architektur: {result.Architektur}\n✓ Enthaltene Version: {result.Version}\n\nBitte Sicherung auswählen und Wiederherstellungsschlüssel eingeben.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Das Paket konnte nicht geprüft werden. Es wurde nichts installiert.\n\n{ex.Message}";
        }
    }
}
