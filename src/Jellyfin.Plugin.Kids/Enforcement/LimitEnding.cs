namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// A limit that ends soon, for the warnings (research R9).
/// </summary>
/// <param name="Limit">Which limit: the allowance (<see cref="DenialReason.TimeUp"/>), the sitting or the allowed hours.</param>
/// <param name="Instance">Which occurrence of it (the day, the sitting, the window), so each is warned about once.</param>
/// <param name="RemainingMs">How long is left.</param>
public sealed record LimitEnding(DenialReason Limit, string Instance, long RemainingMs);
