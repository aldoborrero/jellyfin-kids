using System;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Kids.ParentLock;

/// <summary>
/// Issues and checks parent grants: random, short-lived, bound to a user and device, and kept only in memory.
/// </summary>
/// <remarks>
/// Only a SHA-256 of each grant is kept, so neither memory nor a log line ever holds a usable grant. A restart drops
/// every grant.
/// </remarks>
public sealed class GrantService
{
    /// <summary>
    /// How long a grant is valid after it is issued. Use does not extend it.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const int GrantBytes = 32;

    private readonly ConcurrentDictionary<string, Entry> _grants = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrantService"/> class.
    /// </summary>
    /// <param name="time">The clock.</param>
    public GrantService(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Gets how many grants are held, expired ones included until they are pruned.
    /// </summary>
    internal int Count => _grants.Count;

    /// <summary>
    /// Issues a grant.
    /// </summary>
    /// <param name="userId">The user who verified the code.</param>
    /// <param name="deviceKey">The device they verified from.</param>
    /// <returns>The grant, as sent to the client, and when it expires.</returns>
    public (string Grant, DateTime ExpiresAt) Issue(Guid userId, string deviceKey)
    {
        Prune();
        var grant = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(GrantBytes));
        var expiresAt = _time.GetUtcNow().UtcDateTime + Lifetime;
        _grants[Key(grant)] = new Entry(userId, deviceKey, expiresAt);
        return (grant, expiresAt);
    }

    /// <summary>
    /// Checks a grant for a user and device.
    /// </summary>
    /// <param name="grant">The grant the client sent, or null.</param>
    /// <param name="userId">The calling user.</param>
    /// <param name="deviceKey">The calling device.</param>
    /// <returns><c>true</c> if the grant was issued to this user on this device and has not expired or been revoked.</returns>
    public bool IsValid(string? grant, Guid userId, string deviceKey)
    {
        if (string.IsNullOrEmpty(grant) || grant.Length > 64 || !_grants.TryGetValue(Key(grant), out var entry))
        {
            return false;
        }

        if (_time.GetUtcNow().UtcDateTime >= entry.ExpiresAt)
        {
            _grants.TryRemove(Key(grant), out _);
            return false;
        }

        return entry.UserId == userId && string.Equals(entry.DeviceKey, deviceKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Revokes every grant, for example when the lock changes.
    /// </summary>
    public void RevokeAll() => _grants.Clear();

    /// <summary>
    /// Revokes the grants of one user, for example when the user is deleted.
    /// </summary>
    /// <param name="userId">The user.</param>
    public void RevokeUser(Guid userId)
    {
        foreach (var (key, entry) in _grants)
        {
            if (entry.UserId == userId)
            {
                _grants.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Whether the raw grant value is used as a key, which it never should be.
    /// </summary>
    /// <param name="grant">A grant.</param>
    /// <returns><c>true</c> if the raw value is stored.</returns>
    internal bool HoldsRawValue(string grant) => _grants.ContainsKey(grant);

    private static string Key(string grant) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(grant)));

    private void Prune()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var (key, entry) in _grants)
        {
            if (now >= entry.ExpiresAt)
            {
                _grants.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(Guid UserId, string DeviceKey, DateTime ExpiresAt);
}
