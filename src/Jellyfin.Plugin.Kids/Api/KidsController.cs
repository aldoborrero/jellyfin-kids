using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.Profiles;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The Kids plugin API.
/// </summary>
/// <remarks>
/// Authenticated endpoints use <see cref="Policies.IgnoreParentalControl"/> so they keep working outside a kid's
/// allowed hours, and decide self, admin or forbidden in <see cref="ProfileAccess"/>. Refusals are 403, never 401.
/// </remarks>
[ApiController]
[Route("Kids")]
// Same as Jellyfin's own controllers, so clients can ask for camelCase with the profile="CamelCase" media type
[Produces(MediaTypeNames.Application.Json, JsonDefaults.CamelCaseMediaType, JsonDefaults.PascalCaseMediaType)]
public class KidsController : ControllerBase
{
    private readonly IAuthorizationContext _authorizationContext;
    private readonly KidProfileService _profiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsController"/> class.
    /// </summary>
    /// <param name="authorizationContext">Jellyfin's authorization context.</param>
    /// <param name="profiles">The kid profile service.</param>
    public KidsController(IAuthorizationContext authorizationContext, KidProfileService profiles)
    {
        _authorizationContext = authorizationContext;
        _profiles = profiles;
    }

    /// <summary>
    /// Lets clients detect the plugin, even before signing in.
    /// </summary>
    /// <response code="200">The plugin is installed.</response>
    /// <returns>The plugin version, API version and features.</returns>
    [HttpGet("Info")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<KidsInfo> GetInfo()
        => new KidsInfo(Plugin.Instance?.Version.ToString() ?? string.Empty, ApiVersion: 1, Features: ["profiles"], LockType: null);

    /// <summary>
    /// Lists the kids, so a user switcher can tell who they are.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The kids' user ids and profile revisions.</response>
    /// <returns>The kids.</returns>
    [HttpGet("Profiles")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<KidSummaryDto>>> GetProfiles(CancellationToken cancellationToken)
    {
        var kids = await _profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        return Ok(kids.Select(k => new KidSummaryDto(k.UserId, k.Revision)));
    }

    /// <summary>
    /// Gets a kid's profile.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The profile.</response>
    /// <response code="403">The caller is neither the kid nor an administrator.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>The kid's profile, or a refusal.</returns>
    [HttpGet("Users/{userId}/Profile")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KidProfileDto>> GetProfile([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        var caller = await CallerContext.FromAsync(_authorizationContext, HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanRead(caller, userId))
        {
            return Forbid();
        }

        var profile = await _profiles.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        return profile is null ? NotAKid() : KidProfileDto.From(profile);
    }

    /// <summary>
    /// Marks a user as a kid (<c>Revision</c> 0) or replaces a kid's settings (the revision they are based on).
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="update">The settings; omitted ones take their defaults.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The stored profile.</response>
    /// <response code="400">Invalid settings, keyed by field.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">The user does not exist (create) or is not a kid (update).</response>
    /// <response code="409">Already a kid, or a stale revision; the body is the current profile.</response>
    /// <returns>The stored profile, or the current one on a conflict.</returns>
    [HttpPut("Users/{userId}/Profile")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<KidProfileDto>> PutProfile([FromRoute] Guid userId, [FromBody] KidProfileUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var caller = await CallerContext.FromAsync(_authorizationContext, HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        // [Required] has already rejected a missing revision
        var result = await _profiles.SaveAsync(userId, update.Revision!.Value, update.ToInput(), cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            SaveOutcome.Saved => KidProfileDto.From(result.Profile!),
            SaveOutcome.Conflict => Conflict(KidProfileDto.From(result.Profile!)),
            SaveOutcome.Invalid => BadRequest(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
            _ => NotAKid()
        };
    }

    /// <summary>
    /// Unmarks a kid and deletes their settings.
    /// </summary>
    /// <param name="userId">The kid's user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">The kid was unmarked.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">The user is not a kid.</response>
    /// <returns>No content.</returns>
    [HttpDelete("Users/{userId}/Profile")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteProfile([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        var caller = await CallerContext.FromAsync(_authorizationContext, HttpContext).ConfigureAwait(false);
        if (!ProfileAccess.CanMutate(caller))
        {
            return Forbid();
        }

        return await _profiles.DeleteAsync(userId, cancellationToken).ConfigureAwait(false) ? NoContent() : NotAKid();
    }

    private NotFoundObjectResult NotAKid()
        => NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "This user is not a kid." });
}
