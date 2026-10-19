using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Enforcement;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

/// <summary>
/// Records what enforcement does to sessions, in order.
/// </summary>
public sealed class FakePlaybackControl : IPlaybackControl
{
    private readonly Lock _lock = new();

    public List<KidSession> Current { get; } = [];

    public List<string> Calls { get; } = [];

    public IReadOnlyList<KidSession> Sessions() => Current;

    public Task SendMessageAsync(string sessionId, string header, string text, long? timeoutMs, CancellationToken cancellationToken)
        => Record(timeoutMs is null ? $"dialog {sessionId} {header}: {text}" : $"toast {sessionId} {header}: {text}");

    public Task SendStopAsync(string sessionId, CancellationToken cancellationToken) => Record($"stop {sessionId}");

    public Task KillTranscodesAsync(string deviceId) => Record($"kill {deviceId}");

    public Task CloseLiveStreamAsync(string liveStreamId) => Record($"close {liveStreamId}");

    public Task<bool> EndPlaybackAsync(string sessionId)
    {
        Record($"end {sessionId}");
        return Task.FromResult(true);
    }

    public List<string> Snapshot()
    {
        lock (_lock)
        {
            return [.. Calls];
        }
    }

    private Task Record(string call)
    {
        lock (_lock)
        {
            Calls.Add(call);
        }

        return Task.CompletedTask;
    }
}
