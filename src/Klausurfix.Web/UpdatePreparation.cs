using Klausurfix.Infrastructure.Backup;

/// <summary>
/// Internal building block, not an authorization to install an update. The caller
/// must first establish that all browser inputs were saved and cannot change.
/// </summary>
public sealed class UpdatePreparation(UpdateWriteGate gate, Func<ManagedBackupInfo> createBackup)
{
    public async Task<PreparedUpdate> PrepareAsync(CancellationToken cancellationToken = default)
    {
        var freeze = await gate.FreezeAsync(cancellationToken)
            ?? throw new InvalidOperationException("Eine Update-Vorbereitung läuft bereits.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backup = createBackup();
            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedUpdate(backup, freeze);
        }
        catch
        {
            freeze.Dispose();
            throw;
        }
    }
}

/// <summary>Holds the HTTP write freeze until the owner explicitly releases it.</summary>
public sealed class PreparedUpdate(ManagedBackupInfo backup, IDisposable freeze) : IDisposable
{
    private IDisposable? _freeze = freeze;
    public ManagedBackupInfo Backup { get; } = backup;
    public void Dispose() => Interlocked.Exchange(ref _freeze, null)?.Dispose();
}
