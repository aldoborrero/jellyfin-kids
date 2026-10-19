namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The body of <c>POST /Kids/Users/{userId}/WatchTime/Bonus</c>.
/// </summary>
public class BonusRequest
{
    /// <summary>
    /// Gets the minutes to add to the kid's current day, 1–240.
    /// </summary>
    public int? Minutes { get; init; }
}
