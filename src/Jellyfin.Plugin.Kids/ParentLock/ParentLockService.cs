using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// The household parent lock: status, code checks with grants and throttling, and changes.
/// </summary>
/// <remarks>
/// Changes follow the contract's order of checks: body shape and lock type, caller rights, import conflict, throttle,
/// current code, then the new code or verifier. Every change revokes every outstanding grant.
/// </remarks>
public sealed class ParentLockService
{
    /// <summary>
    /// Submitted codes longer than this are answered not valid without hashing them.
    /// </summary>
    public const int MaxSubmittedLength = 256;

    private readonly ParentLockStore _store;
    private readonly GrantService _grants;
    private readonly ThrottleService _throttle;
    private readonly TimeProvider _time;
    private readonly ILogger<ParentLockService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParentLockService"/> class.
    /// </summary>
    /// <param name="store">The lock store.</param>
    /// <param name="grants">The parent grants.</param>
    /// <param name="throttle">The wrong-code throttle.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public ParentLockService(ParentLockStore store, GrantService grants, ThrottleService throttle, TimeProvider time, ILogger<ParentLockService> logger)
    {
        _store = store;
        _grants = grants;
        _throttle = throttle;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets the household lock, for its status.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lock, or null if none is set. Throws if the store is unavailable, never reporting "no lock".</returns>
    public Task<StoredLock?> GetAsync(CancellationToken cancellationToken = default) => _store.GetAsync(cancellationToken);

    /// <summary>
    /// Checks a code and, if it is right, issues a grant for the caller's user and device.
    /// </summary>
    /// <param name="caller">The caller, with its device resolved.</param>
    /// <param name="code">The submitted code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer; never an error for a wrong code.</returns>
    public async Task<VerifyOutcome> VerifyAsync(CallerContext caller, string? code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var userKey = caller.ThrottleUserKey;
        if (caller.DeviceKey is null || userKey is null)
        {
            return VerifyOutcome.NotValid;
        }

        code ??= string.Empty;
        if (code.Length > MaxSubmittedLength)
        {
            return VerifyOutcome.NotValid;
        }

        if (!_throttle.TryBegin(userKey, caller.DeviceKey, out var retryAfter))
        {
            return new VerifyOutcome(false, RetryAfterSeconds: retryAfter);
        }

        StoredLock? stored;
        try
        {
            stored = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqliteException)
        {
            _logger.LogError(ex, "Cannot read the parent lock; codes cannot be checked");
            return VerifyOutcome.NotValid;
        }

        if (stored is null)
        {
            return VerifyOutcome.NotValid;
        }

        if (!Verifier.Verify(code, stored.Verifier))
        {
            _throttle.RecordFailure(userKey, caller.DeviceKey, caller.LogName);
            return VerifyOutcome.NotValid;
        }

        _throttle.RecordSuccess(userKey, caller.DeviceKey);
        var (grant, expiresAt) = _grants.Issue(caller.UserId, caller.DeviceKey);
        return new VerifyOutcome(true, grant, expiresAt);
    }

    /// <summary>
    /// Sets, replaces or imports the household lock.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <param name="input">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<LockChangeResult> SetAsync(CallerContext caller, LockUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(input);

        // 1. Body shape and lock type
        if (!ParentLockType.TryParse(input.LockType, out var lockType))
        {
            return LockChangeResult.Invalid("LockType", "Must be NUMERIC or REMOTE.");
        }

        var importing = input.Verifier is not null;
        if (importing == (input.Code is not null))
        {
            return LockChangeResult.Invalid("Code", "Send either a code or a verifier.");
        }

        // 2. Caller rights
        if (importing)
        {
            return LockAccess.CanImport(caller) ? await ImportAsync(caller, lockType, input.Verifier!, cancellationToken).ConfigureAwait(false) : LockChangeResult.Forbidden;
        }

        if (LockAccess.CanSetWithoutCurrent(caller))
        {
            if (ParentCode.Validate(lockType, input.Code) is { } adminError)
            {
                return LockChangeResult.Invalid("Code", adminError);
            }

            var value = new StoredLock(lockType, Verifier.Create(input.Code!), Now());
            await _store.UpsertAsync(value, cancellationToken).ConfigureAwait(false);
            return Changed(caller, "set", value);
        }

        return LockAccess.CanSetWithCurrent(caller)
            ? await ReplaceWithCurrentAsync(caller, lockType, input, cancellationToken).ConfigureAwait(false)
            : LockChangeResult.Forbidden;
    }

    /// <summary>
    /// Removes the household lock.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see cref="LockChangeOutcome.Saved"/>, <see cref="LockChangeOutcome.NoLock"/> or <see cref="LockChangeOutcome.Forbidden"/>.</returns>
    public async Task<LockChangeResult> RemoveAsync(CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!LockAccess.CanRemove(caller))
        {
            return LockChangeResult.Forbidden;
        }

        if (!await _store.DeleteAsync(cancellationToken).ConfigureAwait(false))
        {
            return LockChangeResult.NoLock;
        }

        return Changed(caller, "removed", null);
    }

    /// <summary>
    /// Gets the lock type and verifier, for a device to cache for offline unlock.
    /// </summary>
    /// <param name="caller">The caller.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see cref="LockChangeOutcome.Saved"/> with the lock, <see cref="LockChangeOutcome.NoLock"/> or <see cref="LockChangeOutcome.Forbidden"/>.</returns>
    public async Task<LockChangeResult> GetVerifierAsync(CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!LockAccess.CanReadVerifier(caller))
        {
            return LockChangeResult.Forbidden;
        }

        var stored = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        return stored is null ? LockChangeResult.NoLock : new LockChangeResult(LockChangeOutcome.Saved, stored);
    }

    private async Task<LockChangeResult> ImportAsync(CallerContext caller, string lockType, string verifier, CancellationToken cancellationToken)
    {
        // 3. Import conflict, before the verifier format
        var current = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            return new LockChangeResult(LockChangeOutcome.Conflict, current);
        }

        if (Verifier.ValidateForImport(verifier) is { } error)
        {
            return LockChangeResult.Invalid("Verifier", error);
        }

        var value = new StoredLock(lockType, verifier, Now());
        if (!await _store.InsertIfAbsentAsync(value, cancellationToken).ConfigureAwait(false))
        {
            // Another import won the race
            return new LockChangeResult(LockChangeOutcome.Conflict, await _store.GetAsync(cancellationToken).ConfigureAwait(false));
        }

        return Changed(caller, "imported", value);
    }

    private async Task<LockChangeResult> ReplaceWithCurrentAsync(CallerContext caller, string lockType, LockUpdateInput input, CancellationToken cancellationToken)
    {
        var current = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        var userKey = caller.ThrottleUserKey;
        if (current is null || caller.DeviceKey is null || userKey is null)
        {
            return LockChangeResult.Forbidden;
        }

        // 4. Throttle
        if (!_throttle.TryBegin(userKey, caller.DeviceKey, out var retryAfter))
        {
            return new LockChangeResult(LockChangeOutcome.Throttled, RetryAfterSeconds: retryAfter);
        }

        // 5. Current code
        if (string.IsNullOrEmpty(input.Current))
        {
            return LockChangeResult.Invalid("Current", "The current code is required.");
        }

        if (input.Current.Length > MaxSubmittedLength || !Verifier.Verify(input.Current, current.Verifier))
        {
            _throttle.RecordFailure(userKey, caller.DeviceKey, caller.LogName);
            return LockChangeResult.Invalid("Current", "Wrong code.");
        }

        _throttle.RecordSuccess(userKey, caller.DeviceKey);

        // 6. New code
        if (ParentCode.Validate(lockType, input.Code) is { } error)
        {
            return LockChangeResult.Invalid("Code", error);
        }

        var value = new StoredLock(lockType, Verifier.Create(input.Code!), Now());
        if (!await _store.ReplaceIfVerifierMatchesAsync(current.Verifier, value, cancellationToken).ConfigureAwait(false))
        {
            return LockChangeResult.Invalid("Current", "The code was changed meanwhile.");
        }

        return Changed(caller, "replaced", value);
    }

    private LockChangeResult Changed(CallerContext caller, string action, StoredLock? value)
    {
        _grants.RevokeAll();
        _logger.LogInformation("Parent lock {Action} by {Caller}", action, caller.LogName);
        return new LockChangeResult(LockChangeOutcome.Saved, value);
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
