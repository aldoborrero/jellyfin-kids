using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kids.Allowance;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Tasks;

/// <summary>
/// Removes screen time and the watch log older than the retention setting, 90 days by default (research R9, feature
/// 22). Listed under "Kids" in the dashboard's scheduled tasks, where administrators can also run it on demand. A new
/// setting applies from the next run.
/// </summary>
/// <remarks>
/// Jellyfin finds scheduled tasks by scanning plugin assemblies (<c>ApplicationHost.GetExports</c>) and builds them
/// from the server's services, so the task is not registered in <see cref="PluginServiceRegistrator"/>.
/// </remarks>
public sealed class PruneUsageTask : IScheduledTask
{
    private readonly UsageService _usage;
    private readonly IKidsSettings _settings;
    private readonly ILogger<PruneUsageTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PruneUsageTask"/> class.
    /// </summary>
    /// <param name="usage">The screen-time service.</param>
    /// <param name="settings">The household settings, for the retention period.</param>
    /// <param name="logger">The logger.</param>
    public PruneUsageTask(UsageService usage, IKidsSettings settings, ILogger<PruneUsageTask> logger)
    {
        _usage = usage;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Prune kids screen time";

    /// <inheritdoc />
    public string Key => "KidsPruneUsage";

    /// <inheritdoc />
    public string Description => "Removes kids screen time and watch log older than the days set on the Kids statistics page (90 by default).";

    /// <inheritdoc />
    public string Category => "Kids";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var removed = await _usage.PruneAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Pruned {Count} kids screen-time row(s) older than {Days} days", removed, _settings.RetentionDays);
        progress?.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(3.5).Ticks }];
}
