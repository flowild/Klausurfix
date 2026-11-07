using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Process _backend;
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly System.Windows.Forms.Timer _timer;

    public TrayApplicationContext(Process backend, Action openBrowser)
    {
        _backend = backend;
        _image = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? throw new InvalidOperationException("Das Klausurfix-Icon konnte nicht geladen werden.");
        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) =>
        {
            try { openBrowser(); }
            catch { MessageBox.Show("Klausurfix konnte nicht im Browser geöffnet werden.", "Klausurfix", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        menu.Items.Add("Beenden", null, (_, _) => StopBackend());
        _icon = new NotifyIcon { Icon = _image, Text = "Klausurfix", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => menu.Items[0].PerformClick();
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => { if (_backend.HasExited) ExitThread(); };
        _timer.Start();
    }

    private void StopBackend()
    {
        if (MessageBox.Show("Bitte zuerst alle offenen Eingaben und Formulare in allen Klausurfix-Fenstern speichern. Nicht gespeicherte Änderungen können verloren gehen.\n\nKlausurfix jetzt beenden?",
            "Klausurfix beenden", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            if (_backend.HasExited) { ExitThread(); return; }
            using var stop = EventWaitHandle.OpenExisting($@"Local\KlausurfixShutdown-{_backend.Id}");
            stop.Set();
        }
        catch (Exception)
        {
            MessageBox.Show("Der Dienst konnte nicht über das Tray beendet werden. Bitte in der Anwendung „Beenden“ wählen.", "Klausurfix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
            _image.Dispose();
        }
        base.Dispose(disposing);
    }
}
