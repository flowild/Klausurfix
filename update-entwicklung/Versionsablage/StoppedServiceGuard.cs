using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

/// <summary>Checks that no Klausurfix service or listener occupies the local port.</summary>
public static class StoppedServiceGuard
{
    public static void EnsureStopped()
    {
        var processes = Process.GetProcessesByName("Klausurfix.Web");
        try
        {
            if (processes.Length != 0) throw new InvalidOperationException("Klausurfix läuft noch. Bitte die Anwendung über 'Beenden' schließen und erneut versuchen.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
        EnsurePortAvailable(5000);
    }

    internal static void EnsurePortAvailable(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try { socket.Bind(new IPEndPoint(IPAddress.Loopback, port)); }
        catch (SocketException ex) { throw new InvalidOperationException($"Port {port} ist belegt. Bitte Klausurfix beenden und erneut versuchen.", ex); }
    }
}
