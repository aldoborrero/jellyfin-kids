using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.Stats;
using Jellyfin.Plugin.Kids.Storage;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// Kids' statistics (feature 22): the client endpoint agreed with Peke, and the dashboard's own detail and watch log
/// management.
/// </summary>
/// <remarks>
/// Every endpoint uses <see cref="Policies.IgnoreParentalControl"/>, so it works outside a kid's allowed hours.
/// Refusals are 403, never 401. Checks run in this order: authorization, then validation, then kid.
/// </remarks>
[ApiController]
[Route("Kids")]
[Produces(MediaTypeNames.Application.Json, JsonDefaults.CamelCaseMediaType, JsonDefaults.PascalCaseMediaType)]
public class StatsController : ControllerBase
{
    private readonly CallerResolver _callers;
    private readonly StatsService _stats;
    private readonly WatchLog _watch;
    private readonly StatsStore _store;
    private readonly IKidsSettings _settings;
    private readonly ILogger<StatsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsController"/> class.
    /// </summary>
    /// <param name="callers">Resolves the caller and any parent grant.</param>
    /// <param name="stats">The statistics service.</param>
    /// <param name="watch">The watch log.</param>
    /// <param name="store">The statistics store, for the watch log's size.</param>
    /// <param name="settings">The household settings.</param>
    /// <param name="logger">The logger.</param>
    public StatsController(CallerResolver callers, StatsService stats, WatchLog watch, StatsStore store, IKidsSettings settings, ILogger<StatsController> logger)
    {
        _callers = callers;
        _stats = stats;
        _watch = watch;
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Gets a kid's statistics over the last days: minutes per day with bonus and allowance, and, when the household
    /// keeps the watch log, the most watched series and the breaks (contract stats-contract.md).
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="days">How many days, today included: 1–90, 7 when absent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The statistics.</response>
    /// <response code="400">Days out of range, keyed <c>Days</c>.</response>
    /// <response code="403">The caller is neither an administrator nor a parent grant holder.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The statistics, or a refusal.</returns>
    [HttpGet("Users/{userId}/Stats")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidStats>> GetStats([FromRoute] Guid userId, [FromQuery] int? days, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        var range = days ?? 7;
        if (range is < 1 or > StatsService.MaxClientDays)
        {
            return DaysOutOfRange(StatsService.MaxClientDays);
        }

        // A store failure propagates as a 500: never "0 used"
        var stats = await _stats.GetAsync(userId, range, cancellationToken).ConfigureAwait(false);
        return stats is null ? NotAKid() : stats;
    }

    /// <summary>
    /// Gets a kid's statistics for the dashboard's statistics page: per day by series, per series with episodes, per
    /// hour and per device. Not part of the client contract.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="days">How many days, today included: 1–365, 14 when absent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The statistics.</response>
    /// <response code="400">Days out of range, keyed <c>Days</c>.</response>
    /// <response code="403">The caller is neither an administrator nor a parent grant holder.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The statistics, or a refusal.</returns>
    [HttpGet("Users/{userId}/Stats/Detail")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidStatsDetail>> GetStatsDetail([FromRoute] Guid userId, [FromQuery] int? days, CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        var range = days ?? 14;
        if (range is < 1 or > StatsService.MaxDetailDays)
        {
            return DaysOutOfRange(StatsService.MaxDetailDays);
        }

        var stats = await _stats.GetDetailAsync(userId, range, cancellationToken).ConfigureAwait(false);
        return stats is null ? NotAKid() : stats;
    }

    /// <summary>
    /// Says whether the watch log is on and how much it holds, for the dashboard's settings card.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The watch log's state.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <returns>The state, or a refusal.</returns>
    [HttpGet("Admin/WatchLog")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WatchLogInfo>> GetWatchLog(CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!caller.IsAdmin)
        {
            return Forbid();
        }

        var (segments, breaks, oldest) = await _store.CountWatchLogAsync(cancellationToken).ConfigureAwait(false);
        return new WatchLogInfo(_settings.AdvancedStats, _settings.RetentionDays, segments, breaks, oldest);
    }

    /// <summary>
    /// Deletes the whole watch log of every kid. Daily screen time, bonuses and allowances stay. Logged.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">The watch log is empty.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <returns>Nothing, or a refusal.</returns>
    [HttpDelete("Admin/WatchLog")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteWatchLog(CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        if (!caller.IsAdmin)
        {
            return Forbid();
        }

        var removed = await _watch.DeleteAllAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Kids watch log deleted by {Caller}: {Count} row(s)", caller.ActorName, removed);
        return NoContent();
    }

    private BadRequestObjectResult DaysOutOfRange(int max)
        => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["Days"] = [$"Must be between 1 and {max}."] }));

    private NotFoundObjectResult NotAKid()
        => NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "This user is not a kid." });
}
