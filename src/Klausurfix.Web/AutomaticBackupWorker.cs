using Klausurfix.Infrastructure.Backup;
using Klausurfix.Infrastructure.Security;

public sealed class AutomaticBackupWorker : BackgroundService
{
    private readonly BackupService _backups;
    private readonly KeyManager _keys;
    private readonly UpdateWriteGate _gate;
    private string? _error;
    private DateTimeOffset? _lastCreated;
    private volatile bool _ready;

    public AutomaticBackupWorker(BackupService backups, KeyManager keys, UpdateWriteGate gate) { _backups = backups; _keys = keys; _gate = gate; }
    public string? Error => Volatile.Read(ref _error);
    public void ResetSchedule() { _lastCreated = null; Volatile.Write(ref _error, null); }
    public void SetReady(bool ready) => _ready = ready;

    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await _gate.EnterWriteAsync(cancellationToken);
        if (lease is null) return false;
        var created = false;
        _backups.ExecuteExclusive(() => created = RunIfDueCore());
        return created;
    }

    private bool RunIfDueCore()
    {
        if (!_ready || !_keys.IsUnlocked) return false;
        var settings = _keys.GetAutomaticBackupSettings();
        if (!settings.Enabled) return false;
        var latest = _backups.GetManagedBackups().Where(b => b.FileName.StartsWith("klausurfix_automatisch_", StringComparison.Ordinal))
            .Select(b => (DateTimeOffset?)b.CreatedAt).Max();
        var last = latest.HasValue && (!_lastCreated.HasValue || latest.Value > _lastCreated.Value) ? latest : _lastCreated;
        if (last != null && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(settings.IntervalHours)) return false;
        _backups.CreateAutomaticBackup();
        _lastCreated = DateTimeOffset.UtcNow;
        Volatile.Write(ref _error, null);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { Volatile.Write(ref _error, "Automatische Sicherung fehlgeschlagen. Ein erneuter Versuch erfolgt automatisch."); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
