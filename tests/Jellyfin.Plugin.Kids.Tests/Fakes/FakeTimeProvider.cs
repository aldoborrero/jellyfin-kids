using System;

namespace Jellyfin.Plugin.Kids.Tests.Fakes;

/// <summary>
/// A clock the test moves by hand.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
