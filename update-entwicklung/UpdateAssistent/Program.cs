using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

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

    public UpdateWindow()
    {
        Text = "Klausurfix Update – Entwicklungsstand";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(540, 300);
        Size = new Size(650, 360);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Klausurfix-Update prüfen", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 15) });
        _status = new Label { Text = "Paket wird geprüft …", Dock = DockStyle.Fill, AutoEllipsis = false };
        layout.Controls.Add(_status);
        var close = new Button { Text = "Schließen", AutoSize = true, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close);
        Controls.Add(layout);
        Shown += async (_, _) => await CheckPackageAsync();
    }

    private async Task CheckPackageAsync()
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var package = assembly.GetManifestResourceStream("Klausurfix.Update.zip") ?? throw new InvalidOperationException("In dieser Entwicklungsdatei ist kein Update-Paket enthalten.");
                using var key = assembly.GetManifestResourceStream("Klausurfix.Update.public.pem") ?? throw new InvalidOperationException("Der vertrauenswürdige öffentliche Schlüssel fehlt.");
                using var reader = new StreamReader(key, Encoding.UTF8, true, leaveOpen: false);
                var manifest = UpdatePackage.Verify(package, reader.ReadToEnd());
                var expected = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.Arm64 => "win-arm64",
                    Architecture.X64 => "win-x64",
                    _ => throw new InvalidOperationException("Diese Windows-Architektur wird nicht unterstützt.")
                };
                if (manifest.Architektur != expected) throw new InvalidOperationException($"Dieses Paket ist für {manifest.Architektur}, dieses Windows benötigt {expected}.");
                return manifest;
            });
            _status.Text = $"✓ Signatur und Paketinhalt geprüft\n✓ Windows-Architektur: {result.Architektur}\n✓ Enthaltene Version: {result.Version}\n\nDiese Entwicklungsfassung installiert noch kein Update. Klausurfix und Ihre Daten wurden nicht verändert.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Das Paket konnte nicht geprüft werden. Es wurde nichts installiert.\n\n{ex.Message}";
        }
    }
}
