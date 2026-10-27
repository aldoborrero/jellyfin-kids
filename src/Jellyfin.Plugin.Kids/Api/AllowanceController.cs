using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.Enforcement;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// A kid's screen time: status for clients, bonus minutes from parents and offline reports from the kid's device.
/// </summary>
/// <remarks>
/// Every endpoint uses <see cref="Policies.IgnoreParentalControl"/>, so it works outside a kid's allowed hours.
/// Refusals are 403, never 401. Checks run in this order: authorization, then kid, then validation.
/// </remarks>
[ApiController]
[Route("Kids/Users/{userId}")]
[Produces(MediaTypeNames.Application.Json, JsonDefaults.CamelCaseMediaType, JsonDefaults.PascalCaseMediaType)]
public class AllowanceController : ControllerBase
{
    private readonly CallerResolver _callers;
    private readonly UsageService _usage;
    private readonly EnforcementState _enforcement;
    private readonly EnforcementService _enforcementLoader;
    private readonly TimeProvider _time;
    private readonly ILogger<AllowanceController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllowanceController"/> class.
    /// </summary>
    /// <param name="callers">Resolves the caller and any parent grant.</param>
    /// <param name="usage">The screen-time service.</param>
    /// <param name="enforcement">The kids' enforcement state, for the status.</param>
    /// <param name="enforcementLoader">Reloads a kid after a change, so the answer already reflects it.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public AllowanceController(
        CallerResolver callers,
        UsageService usage,
        EnforcementState enforcement,
        EnforcementService enforcementLoader,
        TimeProvider time,
        ILogger<AllowanceController> logger)
    {
        _callers = callers;
        _usage = usage;
        _enforcement = enforcement;
        _enforcementLoader = enforcementLoader;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets how a kid is doing today: used, allowance, bonus, remaining and whether time is up.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The status.</response>
    /// <response code="403">The caller is neither the kid nor an administrator.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The status, or a refusal.</returns>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidStatusDto>> GetStatus([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanRead(caller, userId))
        {
            return Forbid();
        }

        // A store failure propagates as a 500: never "0 used"
        var status = await _usage.GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);
        return status is null ? NotAKid() : KidStatusDto.From(status, _enforcement.Describe(userId, _time.GetUtcNow()));
    }

    /// <summary>
    /// Adds bonus minutes to a kid's current day.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="request">The minutes, 1–240.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The kid's new status.</response>
    /// <response code="400">Invalid minutes, keyed <c>Minutes</c>.</response>
    /// <response code="403">The caller is neither an administrator nor a parent grant holder.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The new status, or a refusal.</returns>
    [HttpPost("WatchTime/Bonus")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidStatusDto>> PostBonus([FromRoute] Guid userId, [FromBody] BonusRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        var result = await _usage.AddBonusAsync(caller, userId, request.Minutes, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != UsageOutcome.Saved)
        {
            return await ToResponseAsync(userId, result).ConfigureAwait(false);
        }

        _logger.LogInformation("Bonus {BonusId} of {Minutes} min for {UserId} by {Caller}", result.Bonus!.Id, request.Minutes, userId, caller.ActorName);
        var answer = await ToResponseAsync(userId, result).ConfigureAwait(false);
        return answer.Value! with { BonusId = result.Bonus.Id, BonusUndoUntil = result.BonusUndoUntil?.UtcDateTime };
    }

    /// <summary>
    /// Lists a kid's bonuses of the current day, newest first, with whether each can still be undone (feature 13).
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Today's bonuses.</response>
    /// <response code="403">The caller is neither the kid nor an administrator.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The bonuses, or a refusal.</returns>
    [HttpGet("WatchTime/Bonus")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BonusListDto>> GetBonuses([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanRead(caller, userId))
        {
            return Forbid();
        }

        var today = await _usage.ListBonusesAsync(userId, cancellationToken).ConfigureAwait(false);
        return today is null ? NotAKid() : BonusListDto.From(today);
    }

    /// <summary>
    /// Undoes one of today's bonuses, at most 10 minutes after it was given (feature 13). Logged.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="bonusId">The bonus id, from the answer to the bonus or the list.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The kid's new status; time may be up again.</response>
    /// <response code="403">The caller is neither an administrator nor a parent grant holder.</response>
    /// <response code="404">The user is not a kid, or has no bonus with that id (never given, or undone already).</response>
    /// <response code="409">The bonus is from an earlier day or older than the undo window.</response>
    /// <returns>The new status, or a refusal.</returns>
    [HttpDelete("WatchTime/Bonus/{bonusId}")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<KidStatusDto>> DeleteBonus([FromRoute] Guid userId, [FromRoute] long bonusId, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        var result = await _usage.UndoBonusAsync(userId, bonusId, cancellationToken).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case UsageOutcome.NotFound:
                return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "No such bonus." });
            case UsageOutcome.TooLate:
                return Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "This bonus can no longer be undone." });
            case UsageOutcome.Saved:
                var bonus = result.Bonus!;
                _logger.LogInformation(
                    "Bonus {BonusId} of {Minutes} min for {UserId} (given by {GrantedBy} at {CreatedAt:O}) undone by {Caller}",
                    bonus.Id,
                    bonus.Minutes,
                    userId,
                    bonus.GrantedBy,
                    bonus.CreatedAt,
                    caller.ActorName);
                break;
        }

        return await ToResponseAsync(userId, result).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends a kid's break after the sitting limit, so they may play again at once (feature 13). Used time and bonus
    /// stay; the next sitting starts from zero at the next playback. When the kid is not in a break nothing changes.
    /// Logged when a break was ended.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The kid's new status.</response>
    /// <response code="403">The caller is neither an administrator nor a parent grant holder.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The new status, or a refusal.</returns>
    [HttpPost("WatchTime/Break/End")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidStatusDto>> PostEndBreak([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        if (await _usage.GetStatusAsync(userId, cancellationToken).ConfigureAwait(false) is null)
        {
            return NotAKid();
        }

        if (_enforcement.EndBreak(userId, _time.GetUtcNow()) is { } left)
        {
            _logger.LogInformation("Break of {UserId} ended by {Caller} with {Seconds} s left", userId, caller.ActorName, (long)Math.Ceiling(left.TotalSeconds));
        }

        // Read after the change, so the answer already follows it
        var status = await _usage.GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);
        return status is null ? NotAKid() : KidStatusDto.From(status, _enforcement.Describe(userId, _time.GetUtcNow()));
    }

    /// <summary>
    /// Sets a kid's used time and bonus for the current day back to 0 and ends the current sitting and its break
    /// (feature 7), so a stop for time or the sitting limit is lifted at once. Logged.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The kid's new status.</response>
    /// <response code="403">The caller is neither an administrator nor a parent grant holder.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The new status, or a refusal.</returns>
    [HttpPost("WatchTime/Reset")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidStatusDto>> PostReset([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        var reset = await _usage.ResetDayAsync(userId, cancellationToken).ConfigureAwait(false);
        if (reset is null)
        {
            return NotAKid();
        }

        _enforcement.ResetSitting(userId);
        _logger.LogInformation(
            "Screen time of {UserId} reset for {Day} by {Caller}: {UsedMinutes} min used and {BonusMinutes} min bonus cleared",
            userId,
            reset.Day,
            caller.ActorName,
            reset.UsedMs / 60_000,
            reset.BonusMinutes);

        // Reload before answering, so the next request already follows the reset
        await _enforcementLoader.TryLoadAsync(userId).ConfigureAwait(false);
        return KidStatusDto.From(reset.Status, _enforcement.Describe(userId, _time.GetUtcNow()));
    }

    /// <summary>
    /// Reports playback the server did not see (offline downloads). Only adds time; a repeated segment id is ignored.
    /// </summary>
    /// <param name="userId">The kid's user id; must be the caller.</param>
    /// <param name="request">The segments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The kid's status for today.</response>
    /// <response code="400">An invalid segment, keyed <c>Segments[i].Field</c> or <c>Segments</c>; nothing was added.</response>
    /// <response code="403">The caller is not the kid (administrators and API keys included).</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The status, or a refusal.</returns>
    [HttpPost("WatchTime/Report")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidStatusDto>> PostReport([FromRoute] Guid userId, [FromBody] ReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (caller.IsApiKey || caller.UserId == Guid.Empty || caller.UserId != userId)
        {
            return Forbid();
        }

        var result = await _usage.ReportAsync(userId, request.ToInput(), cancellationToken).ConfigureAwait(false);
        return await ToResponseAsync(userId, result).ConfigureAwait(false);
    }

    private async Task<ActionResult<KidStatusDto>> ToResponseAsync(Guid userId, UsageResult result)
    {
        if (result.Outcome == UsageOutcome.Saved)
        {
            // Reload before answering, so a bonus that lifts time's up is already in the answer and the next request
            await _enforcementLoader.TryLoadAsync(userId).ConfigureAwait(false);
            return KidStatusDto.From(result.Status!, _enforcement.Describe(userId, _time.GetUtcNow()));
        }

        return result.Outcome == UsageOutcome.Invalid
            ? BadRequest(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value)))
            : NotAKid();
    }

    private NotFoundObjectResult NotAKid()
        => NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "This user is not a kid." });
}
