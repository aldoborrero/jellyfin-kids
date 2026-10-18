namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The body of a 429 answer: too many wrong codes.
/// </summary>
/// <param name="RetryAfterSeconds">The seconds to wait.</param>
public record RetryAfterDto(int RetryAfterSeconds);
