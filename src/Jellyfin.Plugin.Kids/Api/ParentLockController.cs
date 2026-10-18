using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.ParentLock;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The household parent lock: status, code checks that issue grants, changes and the offline verifier.
/// </summary>
/// <remarks>
/// Every endpoint uses <see cref="Policies.IgnoreParentalControl"/>, so a parent can unlock at bedtime. A wrong code is
/// a 200 answer and a refusal is 403, never 401, which clients treat as an expired session.
/// </remarks>
[ApiController]
[Route("Kids/Parent")]
[Produces(MediaTypeNames.Application.Json, JsonDefaults.CamelCaseMediaType, JsonDefaults.PascalCaseMediaType)]
public class ParentLockController : ControllerBase
{
    private readonly CallerResolver _callers;
    private readonly ParentLockService _lock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParentLockController"/> class.
    /// </summary>
    /// <param name="callers">Resolves the caller, device and grant.</param>
    /// <param name="parentLock">The parent lock service.</param>
    public ParentLockController(CallerResolver callers, ParentLockService parentLock)
    {
        _callers = callers;
        _lock = parentLock;
    }

    /// <summary>
    /// Gets the household lock type and when it last changed, so a client can show the right gate.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The lock status; empty when no lock is set.</response>
    /// <returns>The lock status.</returns>
    [HttpGet("Lock")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<LockStatusDto>> GetLock(CancellationToken cancellationToken)
    {
        var stored = await _lock.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LockStatusDto(stored?.LockType, stored?.ChangedAt);
    }

    /// <summary>
    /// Sets, replaces or imports the household lock.
    /// </summary>
    /// <param name="update">A lock type with a new code, or with a verifier to import.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The new lock status; every outstanding grant is revoked.</response>
    /// <response code="400">Invalid request, keyed by field (<c>LockType</c>, <c>Code</c>, <c>Verifier</c>, <c>Current</c>).</response>
    /// <response code="403">The caller is neither an administrator nor a grant holder, or a grant holder tried to import.</response>
    /// <response code="409">An import while a lock is set; the body is the current status.</response>
    /// <response code="429">Too many wrong codes; the current code was not checked.</response>
    /// <returns>The new lock status, or a refusal.</returns>
    [HttpPut("Lock")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LockStatusDto>> PutLock([FromBody] LockUpdate update, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(update);
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        var result = await _lock.SetAsync(caller, update.ToInput(), cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            LockChangeOutcome.Saved => Status(result.Lock),
            LockChangeOutcome.Conflict => Conflict(Status(result.Lock)),
            LockChangeOutcome.Invalid => BadRequest(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
            LockChangeOutcome.Throttled => TooManyRequests(result.RetryAfterSeconds ?? 1),
            _ => Forbid()
        };
    }

    /// <summary>
    /// Removes the household lock. Administrators only; a grant is not enough.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">The lock was removed and every outstanding grant revoked.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No lock is set.</response>
    /// <returns>No content.</returns>
    [HttpDelete("Lock")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteLock(CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        var result = await _lock.RemoveAsync(caller, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            LockChangeOutcome.Saved => NoContent(),
            LockChangeOutcome.NoLock => NoLock(),
            _ => Forbid()
        };
    }

    /// <summary>
    /// Checks a parent code from this device and, if it is right, issues a grant for this user and device.
    /// </summary>
    /// <param name="request">The code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Always, for signed-in callers: valid with a grant, or not valid (with a wait when throttled).</response>
    /// <returns>The answer.</returns>
    [HttpPost("Verify")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<VerifyResult>> Verify([FromBody] VerifyRequest request, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(request);
        var caller = await _callers.ResolveAsync(HttpContext, needDevice: true).ConfigureAwait(false);
        var outcome = await _lock.VerifyAsync(caller, request.Code, cancellationToken).ConfigureAwait(false);
        return VerifyResult.From(outcome);
    }

    /// <summary>
    /// Gets the lock type and verifier, for a device to cache so it can unlock offline.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The lock type and verifier.</response>
    /// <response code="403">The caller is neither an administrator nor a grant holder.</response>
    /// <response code="404">No lock is set.</response>
    /// <returns>The verifier, or a refusal.</returns>
    [HttpGet("Verifier")]
    [Authorize(Policy = Policies.IgnoreParentalControl)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VerifierDto>> GetVerifier(CancellationToken cancellationToken)
    {
        var caller = await _callers.ResolveAsync(HttpContext).ConfigureAwait(false);
        var result = await _lock.GetVerifierAsync(caller, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            LockChangeOutcome.Saved => new VerifierDto(result.Lock!.LockType, result.Lock.Verifier),
            LockChangeOutcome.NoLock => NoLock(),
            _ => Forbid()
        };
    }

    private static LockStatusDto Status(StoredLock? stored) => new(stored?.LockType, stored?.ChangedAt);

    private ObjectResult TooManyRequests(int retryAfterSeconds)
    {
        Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return StatusCode(StatusCodes.Status429TooManyRequests, new RetryAfterDto(retryAfterSeconds));
    }

    private NotFoundObjectResult NoLock()
        => NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "No parent lock is set." });
}
