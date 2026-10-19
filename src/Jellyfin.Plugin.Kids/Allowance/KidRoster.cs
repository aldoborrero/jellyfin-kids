using System;
using System.Collections.Generic;
using System.Threading;

namespace Jellyfin.Plugin.Kids.Allowance;

/// <summary>
/// The kid user ids, in memory, so playback events never query the database.
/// </summary>
public sealed class KidRoster
{
    private readonly Lock _lock = new();
    private HashSet<Guid> _kids = [];
    private bool _loaded;

    /// <summary>
    /// Gets a value indicating whether the roster has been loaded from the store.
    /// </summary>
    public bool IsLoaded
    {
        get
        {
            lock (_lock)
            {
                return _loaded;
            }
        }
    }

    /// <summary>
    /// Whether a user is a kid.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns><c>true</c> if the user is a kid.</returns>
    public bool Contains(Guid userId)
    {
        lock (_lock)
        {
            return _kids.Contains(userId);
        }
    }

    /// <summary>
    /// Adds a kid.
    /// </summary>
    /// <param name="userId">The user id.</param>
    public void Add(Guid userId)
    {
        lock (_lock)
        {
            _kids.Add(userId);
        }
    }

    /// <summary>
    /// Removes a kid.
    /// </summary>
    /// <param name="userId">The user id.</param>
    public void Remove(Guid userId)
    {
        lock (_lock)
        {
            _kids.Remove(userId);
        }
    }

    /// <summary>
    /// Replaces every kid and marks the roster loaded.
    /// </summary>
    /// <param name="userIds">The kid user ids.</param>
    public void Replace(IEnumerable<Guid> userIds)
    {
        var kids = new HashSet<Guid>(userIds);
        lock (_lock)
        {
            _kids = kids;
            _loaded = true;
        }
    }
}
