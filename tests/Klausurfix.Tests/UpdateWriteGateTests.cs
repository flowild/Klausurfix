namespace Klausurfix.Tests;

public class UpdateWriteGateTests
{
    [Fact]
    public async Task FreezeWaitsForWriterAndRejectsNewWritesUntilDisposed()
    {
        var gate = new UpdateWriteGate();
        var writer = await gate.EnterWriteAsync();
        Assert.NotNull(writer);
        var pendingFreeze = gate.FreezeAsync().AsTask();
        Assert.False(pendingFreeze.IsCompleted);
        writer.Dispose();
        var freeze = await pendingFreeze;
        Assert.NotNull(freeze);
        Assert.Null(await gate.EnterWriteAsync());
        Assert.Null(await gate.FreezeAsync());
        freeze.Dispose();
        freeze.Dispose(); // A repeated disposal cannot release a different lease.
        using var nextWriter = await gate.EnterWriteAsync();
        Assert.NotNull(nextWriter);
    }

    [Fact]
    public async Task CancelledFreezeDoesNotPreventSubsequentWrites()
    {
        var gate = new UpdateWriteGate();
        using var writer = await gate.EnterWriteAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = gate.FreezeAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        writer!.Dispose();
        using var nextWriter = await gate.EnterWriteAsync();
        Assert.NotNull(nextWriter);
    }
}
