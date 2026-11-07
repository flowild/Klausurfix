namespace Klausurfix.Tests;

using Klausurfix.Infrastructure.Backup;

public class UpdatePreparationTests
{
    [Fact]
    public async Task InvalidatedRoundAfterBackupReleasesFreezeAndRejectsSnapshot()
    {
        var gate = new UpdateWriteGate();
        var valid = true;
        var preparation = new UpdatePreparation(gate, () =>
        {
            valid = false;
            return new ManagedBackupInfo { FileName = "synthetisch.kfbak" };
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.PrepareAsync(validate: () =>
        {
            if (!valid) throw new InvalidOperationException("Abstimmung ungültig");
        }));
        using var writer = await gate.EnterWriteAsync();
        Assert.NotNull(writer);
    }

    [Fact]
    public async Task BackupRunsWhileWritesAreFrozenAndLeaseReleasesThem()
    {
        var gate = new UpdateWriteGate();
        var called = false;
        var preparation = new UpdatePreparation(gate, () =>
        {
            called = true;
            Assert.Null(gate.EnterWriteAsync().AsTask().GetAwaiter().GetResult());
            return new ManagedBackupInfo { FileName = "synthetisch.kfbak" };
        });
        using (var ready = await preparation.PrepareAsync())
        {
            Assert.True(called);
            Assert.Equal("synthetisch.kfbak", ready.Backup.FileName);
            Assert.Null(await gate.EnterWriteAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.PrepareAsync());
        }
        using var writer = await gate.EnterWriteAsync();
        Assert.NotNull(writer);
    }

    [Fact]
    public async Task BackupFailureReleasesFreeze()
    {
        var gate = new UpdateWriteGate();
        var preparation = new UpdatePreparation(gate, () => throw new IOException("synthetischer Fehler"));
        await Assert.ThrowsAsync<IOException>(() => preparation.PrepareAsync());
        using var writer = await gate.EnterWriteAsync();
        Assert.NotNull(writer);
    }

    [Fact]
    public async Task CancellationDuringBackupDoesNotLeaveFreezeHeld()
    {
        var gate = new UpdateWriteGate();
        using var cancellation = new CancellationTokenSource();
        var preparation = new UpdatePreparation(gate, () =>
        {
            cancellation.Cancel();
            return new ManagedBackupInfo { FileName = "synthetisch.kfbak" };
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation.PrepareAsync(cancellation.Token));
        using var writer = await gate.EnterWriteAsync();
        Assert.NotNull(writer);
    }
}
