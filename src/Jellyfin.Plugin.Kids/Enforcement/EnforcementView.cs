using System;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// A kid's enforcement state, for their Status (contract "GET /Kids/Users/{userId}/Status").
/// </summary>
/// <param name="Enforced">Whether the household turned enforcement on.</param>
/// <param name="Denial">The denial, only while enforced.</param>
/// <param name="Sitting">The current sitting, when the kid has a sitting limit.</param>
/// <param name="SittingLimit">The kid's sitting limit, or null.</param>
/// <param name="Break">The household break.</param>
public sealed record EnforcementView(bool Enforced, Denial? Denial, SittingState? Sitting, TimeSpan? SittingLimit, TimeSpan Break);
