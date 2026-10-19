using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Storage;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Tasks;

/// <summary>
/// Removes screen time older than <see cref="UsageStore.RetentionDays"/> days (research R9). Listed under "Kids" in
/// the dashboard's scheduled tasks, where administrators can also run it on demand.
/// </summary>
/// <remarks>
/// Jellyfin finds scheduled tasks by scanning plugin assemblies (<c>ApplicationHost.GetExports</c>) and builds them
/// from the server's services, so the task is not registered in <see cref="PluginServiceRegistrator"/>.
/// </remarks>
public sealed class PruneUsageTask : IScheduledTask
{
    private readonly UsageService _usage;
    private readonly ILogger<PruneUsageTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PruneUsageTask"/> class.
    /// </summary>
    /// <param name="usage">The screen-time service.</param>
    /// <param name="logger">The logger.</param>
    public PruneUsageTask(UsageService usage, ILogger<PruneUsageTask> logger)
    {
        _usage = usage;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Prune kids screen time";

    /// <inheritdoc />
    public string Key => "KidsPruneUsage";

    /// <inheritdoc />
    public string Description => "Removes kids screen time older than 90 days.";

    /// <inheritdoc />
    public string Category => "Kids";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var removed = await _usage.PruneAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Pruned {Count} kids screen-time row(s) older than {Days} days", removed, UsageStore.RetentionDays);
        progress?.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(3.5).Ticks }];
}
