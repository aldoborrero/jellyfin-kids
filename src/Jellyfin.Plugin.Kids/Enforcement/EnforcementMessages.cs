using System;
using System.Globalization;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// What kids read on screen (contract "Server-sent messages"). English only in this version.
/// </summary>
public static class EnforcementMessages
{
    /// <summary>How long a warning toast shows.</summary>
    public const long WarningTimeoutMs = 10_000;

    /// <summary>
    /// The warning before a limit ends.
    /// </summary>
    /// <param name="limit">Which limit ends.</param>
    /// <param name="remainingMs">How long is left.</param>
    /// <returns>The header and text.</returns>
    public static (string Header, string Text) Warning(DenialReason limit, long remainingMs)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remainingMs / 60_000.0));
        var text = minutes == 1 ? "1 minute left" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes left");
        return (Header(limit), text);
    }

    /// <summary>
    /// The message when a kid may not play any more, shown until dismissed.
    /// </summary>
    /// <param name="denial">The denial.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The header and text.</returns>
    public static (string Header, string Text) Denied(Denial denial, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(denial);
        return denial.Reason switch
        {
            DenialReason.TimeUp => ("Time's up", "Screen time is over for today."),
            DenialReason.OutsideSchedule => ("Bedtime", "It is past your allowed hours."),
            _ => ("Time for a break", BreakText(denial.Until, now))
        };
    }

    private static string Header(DenialReason limit) => limit switch
    {
        DenialReason.TimeUp => "Screen time",
        DenialReason.OutsideSchedule => "Bedtime",
        _ => "Break time"
    };

    private static string BreakText(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is not { } end)
        {
            return "Take a break before watching again.";
        }

        var minutes = Math.Max(1, (int)Math.Ceiling((end - now).TotalMinutes));
        return minutes == 1
            ? "You can watch again in 1 minute."
            : string.Create(CultureInfo.InvariantCulture, $"You can watch again in {minutes} minutes.");
    }
}
