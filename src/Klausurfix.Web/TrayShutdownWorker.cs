using Klausurfix.Infrastructure.Security;

// A process-specific local Windows event lets the tray request a graceful stop
// without exposing an unauthenticated HTTP endpoint.
internal sealed class TrayShutdownWorker(IHostApplicationLifetime lifetime, KeyManager keys) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\KlausurfixShutdown-{Environment.ProcessId}");
        var signaled = await Task.Run(() => WaitHandle.WaitAny([signal, stoppingToken.WaitHandle]) == 0);
        if (signaled && !stoppingToken.IsCancellationRequested)
        {
            keys.Lock();
            lifetime.StopApplication();
        }
    }
}
