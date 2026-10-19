using System;
using Jellyfin.Plugin.Kids.Enforcement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class DeliveryRegistryTests
{
    private static readonly Guid Kid = Guid.NewGuid();
    private static readonly Guid OtherKid = Guid.NewGuid();

    [Fact]
    public void AbortAll_CutsOnlyThatKidsDeliveries()
    {
        var registry = new DeliveryRegistry();
        var (first, firstLifetime) = NewContext();
        var (second, secondLifetime) = NewContext();
        var (other, otherLifetime) = NewContext();
        using var a = registry.Track(Kid, first);
        using var b = registry.Track(Kid, second);
        using var c = registry.Track(OtherKid, other);

        Assert.Equal(2, registry.AbortAll(Kid));

        Assert.True(firstLifetime.Aborted);
        Assert.True(secondLifetime.Aborted);
        Assert.False(otherLifetime.Aborted);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void DisposedHandle_IsNoLongerTracked()
    {
        var registry = new DeliveryRegistry();
        var (context, lifetime) = NewContext();
        var handle = registry.Track(Kid, context);

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(0, registry.AbortAll(Kid));
        Assert.False(lifetime.Aborted);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void AbortAll_ResponseThatJustEnded_IsSkipped()
    {
        var registry = new DeliveryRegistry();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpRequestLifetimeFeature>(new EndedLifetime());
        var (live, liveLifetime) = NewContext();
        using var ended = registry.Track(Kid, context);
        using var running = registry.Track(Kid, live);

        Assert.Equal(1, registry.AbortAll(Kid));
        Assert.True(liveLifetime.Aborted);
    }

    [Fact]
    public void AbortAll_UnknownKid_CutsNothing()
    {
        Assert.Equal(0, new DeliveryRegistry().AbortAll(Kid));
    }

    private static (HttpContext Context, FakeLifetime Lifetime) NewContext()
    {
        var context = new DefaultHttpContext();
        var lifetime = new FakeLifetime();
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        return (context, lifetime);
    }

    internal sealed class EndedLifetime : IHttpRequestLifetimeFeature
    {
        public System.Threading.CancellationToken RequestAborted { get; set; }

        public void Abort() => throw new ObjectDisposedException("HttpContext");
    }

    internal sealed class FakeLifetime : IHttpRequestLifetimeFeature
    {
        public System.Threading.CancellationToken RequestAborted { get; set; }

        public bool Aborted { get; private set; }

        public void Abort() => Aborted = true;
    }
}
