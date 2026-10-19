using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    /// <summary>The header Jellyfin clients read an error code from.</summary>
    public const string ErrorCodeHeader = "X-Application-Error-Code";

    /// <summary>The error code for a refused playback request. Never <c>ParentalControl</c>, which signs web out.</summary>
    public const string ErrorCode = "PlaybackNotAllowed";

    /// <summary>The header that carries the denial reason.</summary>
    public const string ReasonHeader = "X-Kids-Reason";

    // Jellyfin.Api's InternalClaimTypes.UserId; that assembly is not a package plugins can reference
    private const string UserIdClaim = "Jellyfin-UserId";

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
                context.Result = Refusal(context.HttpContext, kind, denial);
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
    /// The kid a request is for: its signed-in user, or for a request without one, the kid who owns a session on the
    /// device it names. Empty when enforcement is off, for administrators and API keys, and for anyone else.
    /// </summary>
    private Guid KidOf(HttpContext httpContext)
    {
        if (!_gate.IsEnabled)
        {
            return Guid.Empty;
        }

        var user = httpContext.User;
        if (user.IsInRole("Administrator"))
        {
            return Guid.Empty;
        }

        var claim = user.FindFirst(UserIdClaim)?.Value;
        if (Guid.TryParse(claim, out var userId) && userId != Guid.Empty)
        {
            return _gate.IsKid(userId) ? userId : Guid.Empty;
        }

        if (user.Identity?.IsAuthenticated == true && claim is null && Interlocked.Exchange(ref _claimWarningLogged, 1) == 0)
        {
            _logger.LogWarning("Signed-in requests carry no {Claim} claim; kids are only recognised by device", UserIdClaim);
        }

        var deviceId = httpContext.Request.Query["deviceId"].ToString();
        return deviceId.Length == 0 ? Guid.Empty : _gate.KidForDevice(deviceId);
    }

    private static IActionResult Refusal(HttpContext httpContext, GateKind kind, Denial denial)
    {
        httpContext.Response.Headers[ReasonHeader] = denial.Code;
        if (kind == GateKind.Negotiate)
        {
            // Web shows "Playback of this media is not allowed." for this answer, and keeps the user signed in
            return new OkObjectResult(new PlaybackInfoResponse { ErrorCode = PlaybackErrorCode.NotAllowed });
        }

        httpContext.Response.Headers[ErrorCodeHeader] = ErrorCode;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Playback is not allowed now.",
        };
        problem.Extensions["reason"] = denial.Code;
        if (denial.Until is { } until)
        {
            problem.Extensions["until"] = until.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        // Media requests accept video or audio; the problem is always JSON, whatever they asked for
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
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
