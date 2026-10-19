using System;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// An offline playback segment as a client reported it, before validation.
/// </summary>
/// <param name="Id">The client's id for the segment.</param>
/// <param name="StartedAt">When the playback started.</param>
/// <param name="Seconds">How long it played.</param>
public sealed record SegmentInput(string? Id, DateTimeOffset? StartedAt, int? Seconds);
