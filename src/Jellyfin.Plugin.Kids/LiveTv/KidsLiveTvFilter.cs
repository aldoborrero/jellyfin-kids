using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Enforcement;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// Holds each kid to their live TV channels on every client (feature 6): refuses playback of other channels and of
/// recordings, and cuts Jellyfin's own channel and guide lists to the kid's channels (research R3, R6, R7).
/// </summary>
/// <remarks>
/// Always on, whatever the household's enforcement switch says (clarification Q1). An action filter, so it sees the
/// bound arguments (an open token or item id may be in the body) and the action's answer. Administrators, API keys
/// and users who are not kids pass untouched. Any failure in this filter's own logic lets the request through (fail
/// open, FR-011); exceptions from the action itself are never caught here.
/// </remarks>
public sealed class KidsLiveTvFilter : IAsyncActionFilter
{
    /// <summary>The reason for a channel outside the kid's set.</summary>
    public const string ChannelReason = "liveTvChannel";

    /// <summary>The reason for a recording or timer route.</summary>
    public const string RecordingReason = "liveTvRecording";

    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly ILiveTvGate _gate;
    private readonly ILiveTvItems _items;
    private readonly LiveStreamRegistry _streams;
    private readonly TimeProvider _time;
    private readonly ILogger<KidsLiveTvFilter> _logger;
    private long _lastFailureLog = long.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsLiveTvFilter"/> class.
    /// </summary>
    /// <param name="gate">The kids' channels.</param>
    /// <param name="items">Jellyfin's live TV items and open streams.</param>
    /// <param name="streams">The live streams kids opened, by channel.</param>
    /// <param name="time">The clock, for rate-limiting failure logs.</param>
    /// <param name="logger">The logger.</param>
    public KidsLiveTvFilter(ILiveTvGate gate, ILiveTvItems items, LiveStreamRegistry streams, TimeProvider time, ILogger<KidsLiveTvFilter> logger)
    {
        _gate = gate;
        _items = items;
        _streams = streams;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // Decided first, outside the action, so the action runs exactly once whatever happens here
        LiveTvRule? rule = null;
        IReadOnlySet<Guid>? allowed = null;
        Paging? paging = null;
        Guid? opened = null;
        try
        {
            var kid = KidRequest.Resolve(context.HttpContext, _gate, out _);
            if (kid != Guid.Empty && context.ActionDescriptor.Properties.TryGetValue(typeof(LiveTvRule), out var value) && value is LiveTvRule found)
            {
                var kidsChannels = _gate.AllowedChannels(kid);
                var answer = Before(context, found, kidsChannels, out paging, out opened);
                if (answer is not null)
                {
                    _logger.LogDebug("Kids live TV: refused {Action} to kid {UserId} ({Rule})", context.ActionDescriptor.DisplayName, kid, found);
                    context.Result = answer;
                    return;
                }

                rule = found;
                allowed = kidsChannels;
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            rule = null;
        }

        var executed = await next().ConfigureAwait(false);
        if (rule is null || allowed is null)
        {
            return;
        }

        try
        {
            After(executed, rule.Value, allowed, paging, opened);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }
    }

    /// <summary>
    /// Decides before the action runs: an answer that replaces it, or null to run it (possibly with narrowed
    /// arguments).
    /// </summary>
    private IActionResult? Before(ActionExecutingContext context, LiveTvRule rule, IReadOnlySet<Guid> allowed, out Paging? paging, out Guid? opened)
    {
        paging = null;
        opened = null;
        var arguments = context.ActionArguments;
        switch (rule)
        {
            case LiveTvRule.Recording:
                return PlaybackRefusal.Forbidden(context.HttpContext, RecordingReason, "Recordings are not available.");

            case LiveTvRule.Playback:
                var channels = new List<Guid>();
                if (!NamedChannels(context, channels) || channels.Any(c => !allowed.Contains(c)))
                {
                    var kind = context.ActionDescriptor.Properties.TryGetValue(typeof(GateKind), out var value) && value is GateKind k ? k : GateKind.Deliver;
                    return PlaybackRefusal.Create(context.HttpContext, kind, ChannelReason, "This channel is not available.");
                }

                opened = channels.Count > 0 ? channels[0] : null;
                return null;

            case LiveTvRule.ChannelList:
            case LiveTvRule.RecommendedPrograms:
                // Run unpaged, then page the kid's part, so pages and counts match what the kid sees
                paging = new Paging(IntArgument(arguments, "startIndex"), IntArgument(arguments, "limit"));
                SetArgument(arguments, "startIndex", null);
                SetArgument(arguments, "limit", null);
                return null;

            case LiveTvRule.ProgramList:
                var requested = Argument(arguments, "channelIds") as IEnumerable<Guid>;
                var narrowed = Narrow(requested, allowed);
                if (narrowed.Length == 0)
                {
                    // Jellyfin reads no channel ids as every channel
                    return new OkObjectResult(new QueryResult<BaseItemDto>());
                }

                SetArgument(arguments, "channelIds", narrowed);
                return null;

            case LiveTvRule.ProgramListPosted:
                var body = Argument(arguments, "body");
                var property = body?.GetType().GetProperty("ChannelIds", BindingFlags.Public | BindingFlags.Instance);
                var narrowedPosted = Narrow(property?.GetValue(body) as IEnumerable<Guid>, allowed);
                if (narrowedPosted.Length == 0 || property is null || !property.CanWrite)
                {
                    return new OkObjectResult(new QueryResult<BaseItemDto>());
                }

                property.SetValue(body, narrowedPosted);
                return null;

            case LiveTvRule.Channel:
                return GuidArgument(Argument(arguments, "channelId")) is { } channelItem && _items.ChannelOf(channelItem) is { } channel && !allowed.Contains(channel)
                    ? new NotFoundResult()
                    : null;

            case LiveTvRule.Program:
                return GuidArgument(Argument(arguments, "programId")) is { } program && _items.ChannelOf(program) is { } programChannel && !allowed.Contains(programChannel)
                    ? new NotFoundResult()
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Works on the action's answer: records the live streams a kid opened, and cuts lists to the kid's channels.
    /// </summary>
    private void After(ActionExecutedContext executed, LiveTvRule rule, IReadOnlySet<Guid> allowed, Paging? paging, Guid? opened)
    {
        if (executed.Result is not ObjectResult { Value: { } value } result)
        {
            return;
        }

        switch (rule)
        {
            case LiveTvRule.Playback when opened is { } channel:
                IEnumerable<string?> liveStreamIds = value switch
                {
                    PlaybackInfoResponse info => info.MediaSources.Select(s => (string?)s.LiveStreamId),
                    LiveStreamResponse response => [response.MediaSource?.LiveStreamId],
                    _ => []
                };
                foreach (var liveStreamId in liveStreamIds.OfType<string>().Where(id => id.Length > 0))
                {
                    _streams.Record(liveStreamId, _items.UniqueIdOf(liveStreamId), channel, _items.IsOpen);
                }

                break;

            case LiveTvRule.ChannelList when value is QueryResult<BaseItemDto> channels:
                result.Value = Page(channels.Items.Where(i => allowed.Contains(i.Id)), paging);
                break;

            case LiveTvRule.RecommendedPrograms when value is QueryResult<BaseItemDto> programs:
                result.Value = Page(programs.Items.Where(i => i.ChannelId is { } c && allowed.Contains(c)), paging);
                break;
        }
    }

    /// <summary>
    /// Collects the channels a playback request names: its item (a channel, or a program's channel), a live TV open
    /// token, and live stream ids recorded when a kid opened them.
    /// </summary>
    /// <returns><c>false</c> when it names a live TV stream the plugin cannot attribute (never allowed).</returns>
    private bool NamedChannels(ActionExecutingContext context, List<Guid> channels)
    {
        var liveTvController = context.ActionDescriptor is ControllerActionDescriptor { ControllerName: LiveTvActions.Controller };
        foreach (var (name, value) in context.ActionArguments)
        {
            if (!Named(name, value, liveTvController, channels))
            {
                return false;
            }

            // DTOs from Jellyfin.Api (PlaybackInfoDto, OpenLiveStreamDto) carry the same names as properties
            if (value is not null && IsJellyfinObject(value.GetType()))
            {
                foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length == 0 && IsIdName(property.Name)
                        && !Named(property.Name, property.GetValue(value), false, channels))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool Named(string name, object? value, bool liveTvController, List<Guid> channels)
    {
        if (name.Equals("itemId", StringComparison.OrdinalIgnoreCase))
        {
            if (GuidArgument(value) is { } item && _items.ChannelOf(item) is { } channel)
            {
                channels.Add(channel);
            }
        }
        else if (name.Equals("openToken", StringComparison.OrdinalIgnoreCase) && value is string { Length: > 0 } openToken)
        {
            if (LiveTvIds.ItemFromOpenToken(openToken) is { } item && _items.ChannelOf(item) is { } channel)
            {
                channels.Add(channel);
            }
            else if (LiveTvIds.IsLiveTvOpenToken(openToken))
            {
                // Jellyfin would open whatever the token names: a live TV token the plugin cannot read is never allowed
                return false;
            }
        }
        else if (name.Equals("liveStreamId", StringComparison.OrdinalIgnoreCase) && value is string { Length: > 0 } liveStreamId)
        {
            if (_streams.TryGetChannel(liveStreamId, out var channel))
            {
                channels.Add(channel);
            }
            else if (LiveTvIds.IsLiveTvStreamId(liveStreamId))
            {
                return false;
            }
        }
        else if (liveTvController && name.Equals("streamId", StringComparison.OrdinalIgnoreCase) && value is string { Length: > 0 } uniqueId)
        {
            // LiveTv/LiveStreamFiles/{streamId}: always a live TV stream
            if (!_streams.TryGetChannel(uniqueId, out var channel))
            {
                return false;
            }

            channels.Add(channel);
        }

        return true;
    }

    private static bool IsIdName(string name)
        => name.Equals("ItemId", StringComparison.OrdinalIgnoreCase)
            || name.Equals("OpenToken", StringComparison.OrdinalIgnoreCase)
            || name.Equals("LiveStreamId", StringComparison.OrdinalIgnoreCase);

    private static bool IsJellyfinObject(Type type)
        => !type.IsPrimitive && !type.IsEnum && type.IsClass && type != typeof(string)
            && type.Namespace is { } ns
            && (ns.StartsWith("Jellyfin.", StringComparison.Ordinal) || ns.StartsWith("MediaBrowser.", StringComparison.Ordinal));

    private static Guid[] Narrow(IEnumerable<Guid>? requested, IReadOnlySet<Guid> allowed)
    {
        var asked = requested?.ToArray() ?? [];
        return asked.Length == 0 ? [.. allowed] : [.. asked.Where(allowed.Contains).Distinct()];
    }

    private static QueryResult<BaseItemDto> Page(IEnumerable<BaseItemDto> items, Paging? paging)
    {
        var kept = items.ToList();
        var start = Math.Max(paging?.StartIndex ?? 0, 0);
        IEnumerable<BaseItemDto> page = kept.Skip(start);
        if (paging?.Limit is int limit and >= 0)
        {
            page = page.Take(limit);
        }

        return new QueryResult<BaseItemDto>(paging?.StartIndex, kept.Count, [.. page]);
    }

    private static object? Argument(IDictionary<string, object?> arguments, string name)
        => arguments.FirstOrDefault(a => a.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static int? IntArgument(IDictionary<string, object?> arguments, string name) => Argument(arguments, name) as int?;

    private static void SetArgument(IDictionary<string, object?> arguments, string name, object? value)
    {
        var key = arguments.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
        arguments[key] = value;
    }

    private static Guid? GuidArgument(object? value) => value switch
    {
        Guid guid when guid != Guid.Empty => guid,
        string text when Guid.TryParse(text, out var parsed) && parsed != Guid.Empty => parsed,
        _ => null
    };

    private void LogFailure(Exception ex)
    {
        var now = _time.GetTimestamp();
        var last = Interlocked.Read(ref _lastFailureLog);
        if (last != long.MinValue && _time.GetElapsedTime(last, now) < FailureLogInterval)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastFailureLog, now, last) == last)
        {
            _logger.LogError(ex, "Kids live TV rules failed; letting the request through");
        }
    }

    private sealed record Paging(int? StartIndex, int? Limit);
}
