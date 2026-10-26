using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Kids.Profiles;

namespace Jellyfin.Plugin.Kids.Tests;

/// <summary>
/// Compares kid profiles by value, including the list properties that records compare by reference.
/// </summary>
public sealed class ProfileComparer : IEqualityComparer<KidProfile?>
{
    public static readonly ProfileComparer Instance = new();

    public bool Equals(KidProfile? x, KidProfile? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        return x.UserId == y.UserId
            && x.Revision == y.Revision
            && x.Settings.SearchAllowed == y.Settings.SearchAllowed
            && x.Settings.AutoplayEnabled == y.Settings.AutoplayEnabled
            && x.Settings.AutoplayCap == y.Settings.AutoplayCap
            && x.Settings.Sections.SequenceEqual(y.Settings.Sections)
            && x.Settings.PickedItemIds.SequenceEqual(y.Settings.PickedItemIds)
            && x.Settings.DailyAllowanceMinutes == y.Settings.DailyAllowanceMinutes
            && x.Settings.SessionLimitMinutes == y.Settings.SessionLimitMinutes
            && x.Settings.LiveTvChannelIds.SequenceEqual(y.Settings.LiveTvChannelIds)
            && x.Settings.Avatar == y.Settings.Avatar;
    }

    public int GetHashCode(KidProfile? obj) => obj is null ? 0 : HashCode.Combine(obj.UserId, obj.Revision);
}
