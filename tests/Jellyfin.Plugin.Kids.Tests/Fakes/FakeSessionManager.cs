using System;
using System.Reflection;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

/// <summary>
/// An <see cref="ISessionManager"/> that only keeps the playback event handlers, so a test can raise the events the
/// way Jellyfin's session manager does. Anything else throws.
/// </summary>
public class FakeSessionManager : DispatchProxy
{
    private EventHandler<PlaybackProgressEventArgs>? _start;
    private EventHandler<PlaybackProgressEventArgs>? _progress;
    private EventHandler<PlaybackStopEventArgs>? _stopped;

    /// <summary>Gets the proxy to hand to the code under test.</summary>
    public ISessionManager Manager { get; private set; } = null!;

    public static FakeSessionManager Create()
    {
        var manager = Create<ISessionManager, FakeSessionManager>();
        var fake = (FakeSessionManager)(object)manager;
        fake.Manager = manager;
        return fake;
    }

    public void SendStart(PlaybackProgressEventArgs e) => _start?.Invoke(Manager, e);

    public void SendProgress(PlaybackProgressEventArgs e) => _progress?.Invoke(Manager, e);

    public void SendStopped(PlaybackStopEventArgs e) => _stopped?.Invoke(Manager, e);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var handler = args?.Length == 1 ? args[0] : null;
        switch (targetMethod.Name)
        {
            case "add_PlaybackStart": _start += (EventHandler<PlaybackProgressEventArgs>?)handler; return null;
            case "remove_PlaybackStart": _start -= (EventHandler<PlaybackProgressEventArgs>?)handler; return null;
            case "add_PlaybackProgress": _progress += (EventHandler<PlaybackProgressEventArgs>?)handler; return null;
            case "remove_PlaybackProgress": _progress -= (EventHandler<PlaybackProgressEventArgs>?)handler; return null;
            case "add_PlaybackStopped": _stopped += (EventHandler<PlaybackStopEventArgs>?)handler; return null;
            case "remove_PlaybackStopped": _stopped -= (EventHandler<PlaybackStopEventArgs>?)handler; return null;
            default: throw new NotSupportedException(targetMethod.Name);
        }
    }
}
