using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Refuses playback to kids who may not play now, on the actions <see cref="PlaybackGateConvention"/> attached it to
/// (research R4, R5), and tracks the media it lets through to kids so a stop can cut it (R1).
/// </summary>
/// <remarks>
/// A resource filter runs before model binding, and its <c>next()</c> covers the whole response, file streaming
/// included. Answers come from memory only. Any failure in this filter's own logic lets the request through (fail
/// open, FR-017); exceptions from the action itself are never caught here.
/// </remarks>
public sealed class KidsPlaybackFilter : IAsyncResourceFilter
{
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly IPlaybackGate _gate;
    private readonly DeliveryRegistry _deliveries;
    private readonly TimeProvider _time;
    private readonly ILogger<KidsPlaybackFilter> _logger;
    private long _lastFailureLog = long.MinValue;
    private int _claimWarningLogged;

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsPlaybackFilter"/> class.
    /// </summary>
    /// <param name="gate">The kids' denials.</param>
    /// <param name="deliveries">The kids' in-flight deliveries.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public KidsPlaybackFilter(IPlaybackGate gate, DeliveryRegistry deliveries, TimeProvider time, ILogger<KidsPlaybackFilter> logger)
    {
        _gate = gate;
        _deliveries = deliveries;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        Guid kid;
        GateKind kind;
        try
        {
            kid = KidOf(context.HttpContext);
            kind = context.ActionDescriptor.Properties.TryGetValue(typeof(GateKind), out var value) && value is GateKind k ? k : GateKind.Deliver;
            if (kid != Guid.Empty && _gate.TryGetDenial(kid, _time.GetUtcNow(), out var denial))
            {
                _logger.LogDebug("Refused {Action} to kid {UserId}: {Reason}", context.ActionDescriptor.DisplayName, kid, denial.Code);
                context.Result = PlaybackRefusal.Create(context.HttpContext, kind, denial.Code, "Playback is not allowed now.", denial.Until);
                return;
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            await next().ConfigureAwait(false);
            return;
        }

        if (kid == Guid.Empty || kind != GateKind.Deliver)
        {
            await next().ConfigureAwait(false);
            return;
        }

        // The response streams inside next(), so the handle covers the whole delivery
        using (_deliveries.Track(kid, context.HttpContext))
        {
            await next().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The kid a request is for (<see cref="KidRequest"/>), or empty while enforcement is off.
    /// </summary>
    private Guid KidOf(HttpContext httpContext)
    {
        if (!_gate.IsEnabled)
        {
            return Guid.Empty;
        }

        var kid = KidRequest.Resolve(httpContext, _gate, out var claimMissing);
        if (claimMissing && Interlocked.Exchange(ref _claimWarningLogged, 1) == 0)
        {
            _logger.LogWarning("Signed-in requests carry no {Claim} claim; kids are only recognised by device", KidRequest.UserIdClaim);
        }

        return kid;
    }

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
            _logger.LogError(ex, "Kids playback gate failed; letting the request through");
        }
    }
}
