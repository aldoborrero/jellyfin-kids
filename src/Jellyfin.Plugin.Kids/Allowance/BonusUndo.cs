using System;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// How long a bonus can be undone (feature 13, research R2): a short window, so undo corrects a wrong tap and cannot
/// become a hidden way to take time away.
/// </summary>
public static class BonusUndo
{
    /// <summary>How long after it was given a bonus can be undone.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When a bonus stops being undoable: the window after it was given, or the start of the next day if that comes
    /// first.
    /// </summary>
    /// <param name="createdAt">When the bonus was given.</param>
    /// <param name="nextDayStartsAt">When the day after the bonus's day starts.</param>
    /// <returns>The last moment it can be undone.</returns>
    public static DateTimeOffset UndoUntil(DateTimeOffset createdAt, DateTimeOffset nextDayStartsAt)
    {
        var end = createdAt + Window;
        return end < nextDayStartsAt ? end : nextDayStartsAt;
    }

    /// <summary>
    /// Whether a bonus can be undone now: it is today's, and the window has not passed.
    /// </summary>
    /// <param name="bonusDay">The bonus's day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="today">Today, <c>yyyy-MM-dd</c>.</param>
    /// <param name="undoUntil">From <see cref="UndoUntil"/>.</param>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> if it can be undone.</returns>
    public static bool CanUndo(string bonusDay, string today, DateTimeOffset undoUntil, DateTimeOffset now)
        => string.Equals(bonusDay, today, StringComparison.Ordinal) && now <= undoUntil;
}
