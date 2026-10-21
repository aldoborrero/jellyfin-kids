using System;
using System.Globalization;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// The answers a kid gets when a playback request is refused (feature 5 research R5), shared by every rule.
/// </summary>
public static class PlaybackRefusal
{
    /// <summary>The header Jellyfin clients read an error code from.</summary>
    public const string ErrorCodeHeader = "X-Application-Error-Code";

    /// <summary>The error code for a refused playback request. Never <c>ParentalControl</c>, which signs web out.</summary>
    public const string ErrorCode = "PlaybackNotAllowed";

    /// <summary>The header that carries the refusal reason.</summary>
    public const string ReasonHeader = "X-Kids-Reason";

    /// <summary>
    /// Builds the answer for a refused request and sets its headers.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="kind">What the refused action does: a PlaybackInfo is answered 200 <c>NotAllowed</c>, the rest 403.</param>
    /// <param name="reason">The reason clients see, such as <c>timeUp</c> or <c>liveTvChannel</c>.</param>
    /// <param name="title">The problem title for 403 answers.</param>
    /// <param name="until">When the refusal ends, if known.</param>
    /// <returns>The answer.</returns>
    public static IActionResult Create(HttpContext httpContext, GateKind kind, string reason, string title, DateTimeOffset? until = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Headers[ReasonHeader] = reason;
        if (kind == GateKind.Negotiate)
        {
            // Web shows "Playback of this media is not allowed." for this answer, and keeps the user signed in
            return new OkObjectResult(new PlaybackInfoResponse { ErrorCode = PlaybackErrorCode.NotAllowed });
        }

        return Forbidden(httpContext, reason, title, until);
    }

    /// <summary>
    /// Builds a 403 problem answer with the playback error code and the reason, and sets its headers.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="reason">The reason clients see.</param>
    /// <param name="title">The problem title.</param>
    /// <param name="until">When the refusal ends, if known.</param>
    /// <returns>The answer.</returns>
    public static IActionResult Forbidden(HttpContext httpContext, string reason, string title, DateTimeOffset? until = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Headers[ReasonHeader] = reason;
        httpContext.Response.Headers[ErrorCodeHeader] = ErrorCode;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = title,
        };
        problem.Extensions["reason"] = reason;
        if (until is { } end)
        {
            problem.Extensions["until"] = end.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        // Media requests accept video or audio; the problem is always JSON, whatever they asked for. A JsonResult, not an
        // ObjectResult: when an action filter answers, the controller's [Produces] result filter would reset the content
        // type of an ObjectResult to application/json
        return new JsonResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentType = "application/problem+json",
        };
    }
}
