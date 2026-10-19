using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// The media responses being delivered to kids right now, so a stop can cut them (research R1, R9): a direct-play
/// response keeps streaming after the client is told to stop, and a client that ignores the stop keeps playing it.
/// </summary>
public sealed class DeliveryRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<long, HttpContext>> _byKid = new();
    private long _nextId;

    /// <summary>
    /// Gets how many deliveries are tracked.
    /// </summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var deliveries in _byKid.Values)
            {
                count += deliveries.Count;
            }

            return count;
        }
    }

    /// <summary>
    /// Tracks a delivery until the returned handle is disposed, when the response has ended.
    /// </summary>
    /// <param name="kid">The kid it is delivered to.</param>
    /// <param name="context">The request.</param>
    /// <returns>The handle that stops tracking it.</returns>
    public IDisposable Track(Guid kid, HttpContext context)
    {
        var id = Interlocked.Increment(ref _nextId);
        var deliveries = _byKid.GetOrAdd(kid, _ => new ConcurrentDictionary<long, HttpContext>());
        deliveries[id] = context;
        return new Handle(this, kid, id);
    }

    /// <summary>
    /// Cuts every delivery to a kid: the connection is reset, so the client sees the stream end with an error.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <returns>How many deliveries were cut.</returns>
    public int AbortAll(Guid kid)
    {
        if (!_byKid.TryGetValue(kid, out var deliveries))
        {
            return 0;
        }

        var count = 0;
        foreach (var (id, context) in deliveries)
        {
            if (deliveries.TryRemove(id, out _))
            {
                try
                {
                    context.Abort();
                    count++;
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    // The response ended between the lookup and the abort: nothing left to cut
                }
            }
        }

        return count;
    }

    private void Release(Guid kid, long id)
    {
        if (_byKid.TryGetValue(kid, out var deliveries))
        {
            deliveries.TryRemove(id, out _);
        }
    }

    private sealed class Handle(DeliveryRegistry registry, Guid kid, long id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                registry.Release(kid, id);
            }
        }
    }
}
