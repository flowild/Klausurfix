namespace Klausurfix.Tests;

public class UpdateViewRegistryTests
{
    [Fact]
    public void AllRegisteredViewsMustRespondOnceToTheCurrentRound()
    {
        var registry = new UpdateViewRegistry();
        var first = registry.Register();
        var second = registry.Register();
        var round = registry.Begin();
        Assert.Equal(round, registry.PendingFor(first));
        Assert.False(registry.Respond(first, "alt", true));
        Assert.True(registry.Respond(first, round, true));
        Assert.Equal(round, registry.PendingFor(first)); // Remains held until cancellation or expiry.
        Assert.False(registry.Respond(first, round, true));
        Assert.False(registry.Status(round).Complete);
        Assert.True(registry.Respond(second, round, true));
        Assert.True(registry.Status(round).Complete);
        Assert.True(registry.Cancel(round));
        Assert.Null(registry.PendingFor(first));
        Assert.False(registry.Cancel(round));
    }

    [Fact]
    public void FailedResponseOrChangingViewSetInvalidatesTheRound()
    {
        var registry = new UpdateViewRegistry();
        var view = registry.Register();
        var round = registry.Begin();
        Assert.True(registry.Respond(view, round, false));
        Assert.True(registry.Status(round).Failed);
        Assert.False(registry.Status(round).Complete);
        registry.Cancel(round);
        round = registry.Begin();
        registry.Register();
        Assert.True(registry.Status(round).Failed);
        registry.Cancel(round);
        round = registry.Begin();
        registry.Unregister(view);
        Assert.True(registry.Status(round).Failed);
    }
}
