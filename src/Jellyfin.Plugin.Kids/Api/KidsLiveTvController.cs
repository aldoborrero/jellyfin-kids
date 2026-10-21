using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.LiveTv;
using Jellyfin.Plugin.Kids.Profiles;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A kid's live TV channels and guide, and the server's channels for the dashboard (feature 6).
/// </summary>
/// <remarks>
/// Kid reads answer only the kid's effective channels, in Jellyfin's own item shape, so clients reuse their live TV
/// models. They work outside allowed hours, like the other kid reads.
/// </remarks>
[ApiController]
[Route("Kids")]
[Produces(MediaTypeNames.Application.Json, JsonDefaults.CamelCaseMediaType, JsonDefaults.PascalCaseMediaType)]
public class KidsLiveTvController : ControllerBase
{
    private readonly CallerResolver _callers;
    private readonly KidProfileService _profiles;
    private readonly KidLiveTvService _liveTv;
    private readonly ILiveTvChannelCatalog _channels;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsLiveTvController"/> class.
    /// </summary>
    /// <param name="callers">Resolves the caller.</param>
    /// <param name="profiles">The kid profiles, to tell kids from other users.</param>
    /// <param name="liveTv">The kids' channels and guide.</param>
    /// <param name="channels">The server's live TV channels.</param>
    /// <param name="time">The clock, for the default guide window.</param>
    public KidsLiveTvController(CallerResolver callers, KidProfileService profiles, KidLiveTvService liveTv, ILiveTvChannelCatalog channels, TimeProvider time)
    {
        _callers = callers;
        _profiles = profiles;
        _liveTv = liveTv;
        _channels = channels;
        _time = time;
    }

    /// <summary>
    /// Gets the live TV channels a kid may watch, each with the program on now.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The kid's channels; empty when the kid has no live TV.</response>
    /// <response code="403">The caller is neither the kid nor an administrator.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The channels, or a refusal.</returns>
    [HttpGet("Users/{userId}/LiveTv/Channels")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QueryResult<BaseItemDto>>> GetChannels([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        var refusal = await RefuseReadAsync(userId, cancellationToken).ConfigureAwait(false);
        return refusal is null ? _liveTv.Channels(userId, cancellationToken) : refusal;
    }

    /// <summary>
    /// Gets the guide of a kid's channels: the programs that overlap a time window.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="startDate">Programs must end after this (UTC); default now.</param>
    /// <param name="endDate">Programs must start before this (UTC); default the start plus 24 hours, at most 7 days after it.</param>
    /// <param name="channelIds">Comma-separated channels to narrow to; channels the kid may not watch are ignored.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The programs, ordered by start; empty when the kid has no live TV.</response>
    /// <response code="400">The end is not after the start, or a channel id is not an id.</response>
    /// <response code="403">The caller is neither the kid nor an administrator.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The programs, or a refusal.</returns>
    [HttpGet("Users/{userId}/LiveTv/Programs")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QueryResult<BaseItemDto>>> GetPrograms(
        [FromRoute] Guid userId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? channelIds,
        CancellationToken cancellationToken)
    {
        var refusal = await RefuseReadAsync(userId, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        if (GuideWindow.Resolve(startDate, endDate, _time.GetUtcNow()) is not { } window)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["endDate"] = ["Must be after startDate."] }));
        }

        if (!TryParseIds(channelIds, out var channels, out var bad))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["channelIds"] = [$"'{bad}' is not an id."] }));
        }

        return await _liveTv.ProgramsAsync(userId, window, channels, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the server's live TV channels for the kid editor, whoever may watch them.
    /// </summary>
    /// <param name="ids">Comma-separated channel ids to resolve instead of listing all; unknown ids are left out.</param>
    /// <response code="200">The channels, in Jellyfin's channel order.</response>
    /// <response code="400">An id is not a GUID.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <returns>The channels, or a refusal.</returns>
    [HttpGet("Admin/LiveTvChannels")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<LiveTvChannelDto>>> GetAdminChannels([FromQuery] string? ids)
    {
        // Administrators only: a parent grant unlocks profile changes, not this dashboard data
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!caller.IsAdmin)
        {
            return Forbid();
        }

        if (!TryParseIds(ids, out var parsed, out var bad))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["ids"] = [$"'{bad}' is not an id."] }));
        }

        var channels = parsed is null ? _channels.List() : _channels.Resolve(parsed);
        return Ok(channels.Select(LiveTvChannelDto.From));
    }

    private static bool TryParseIds(string? text, out List<Guid>? ids, out string? bad)
    {
        ids = null;
        bad = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        ids = [];
        foreach (var id in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(id, out var guid))
            {
                bad = id;
                return false;
            }

            ids.Add(guid);
        }

        return true;
    }

    /// <summary>
    /// Refuses a read of a kid's live TV to anyone but the kid and administrators, and for users who are not kids.
    /// </summary>
    private async Task<ActionResult?> RefuseReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanRead(caller, userId))
        {
            return Forbid();
        }

        return await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false) is null
            ? NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "This user is not a kid." })
            : null;
    }
}
