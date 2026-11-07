/// <summary>
/// Coordinates HTTP mutations with a future update preparation. This gate is not
/// an update authorization and cannot detect unsaved data in browser tabs.
/// </summary>
public sealed class UpdateWriteGate
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private bool _frozen;

    public async ValueTask<IDisposable?> EnterWriteAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        if (_frozen)
        {
            _mutex.Release();
            return null;
        }
        return new Lease(() => _mutex.Release());
    }

    // Only a future, fully coordinated update workflow may hold this lease.
    // Acquisition waits for the current HTTP mutation to finish.
    public async ValueTask<IDisposable?> FreezeAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_frozen) return null;
            _frozen = true;
            return new Lease(Unfreeze);
        }
        finally { _mutex.Release(); }
    }

    private void Unfreeze()
    {
        _mutex.Wait();
        try { _frozen = false; }
        finally { _mutex.Release(); }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
