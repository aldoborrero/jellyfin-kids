using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Enforcement;

/// <summary>
/// Keeps <see cref="EnforcementState"/> loaded (research R6, R8) and runs the startup self-check (R10): every kid's
/// budget, sitting limit and Jellyfin user at startup, and again after a bonus, a report, a profile change, a new day,
/// a settings change or a user update; the users once a minute as well.
/// </summary>
public sealed class EnforcementService : IHostedService, IDisposable
{
    /// <summary>How often the kids' Jellyfin users (their allowed hours) are read again.</summary>
    public static readonly TimeSpan UserRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly EnforcementState _state;
    private readonly PlaybackEnforcer _enforcer;
    private readonly KidsDatabase _database;
    private readonly KidProfileService _profiles;
    private readonly UsageService _usage;
    private readonly IUserManager _users;
    private readonly ISessionManager _sessions;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IActionDescriptorCollectionProvider _actions;
    private readonly GateCoverage _coverage;
    private readonly TimeProvider _time;
    private readonly ILogger<EnforcementService> _logger;
    private readonly Lock _lock = new();
    private readonly HashSet<Guid> _reloading = [];
    private readonly HashSet<Guid> _dirty = [];
    private ITimer? _timer;
    private CancellationTokenRegistration _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnforcementService"/> class.
    /// </summary>
    /// <param name="state">The kids' limits.</param>
    /// <param name="enforcer">The enforcer, told when sessions end.</param>
    /// <param name="database">The plugin database.</param>
    /// <param name="profiles">The kid profiles.</param>
    /// <param name="usage">The screen-time service.</param>
    /// <param name="users">Jellyfin's user manager.</param>
    /// <param name="sessions">Jellyfin's session manager.</param>
    /// <param name="lifetime">The host lifetime, for the self-check once the server has started.</param>
    /// <param name="actions">MVC's action list, for the self-check.</param>
    /// <param name="coverage">What the convention guarded.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public EnforcementService(
        EnforcementState state,
        PlaybackEnforcer enforcer,
        KidsDatabase database,
        KidProfileService profiles,
        UsageService usage,
        IUserManager users,
        ISessionManager sessions,
        IHostApplicationLifetime lifetime,
        IActionDescriptorCollectionProvider actions,
        GateCoverage coverage,
        TimeProvider time,
        ILogger<EnforcementService> logger)
    {
        _state = state;
        _enforcer = enforcer;
        _database = database;
        _profiles = profiles;
        _usage = usage;
        _users = users;
        _sessions = sessions;
        _lifetime = lifetime;
        _actions = actions;
        _coverage = coverage;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _state.ReloadRequested += OnReloadRequested;
        _usage.UsageChanged += OnReloadRequested;
        _profiles.ProfileChanged += OnReloadRequested;
        _users.OnUserUpdated += OnUserUpdated;
        _sessions.SessionEnded += OnSessionEnded;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
        }

        await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        _started = _lifetime.ApplicationStarted.Register(SelfCheck);
        _timer = _time.CreateTimer(_ => RefreshUsers(), null, UserRefreshInterval, UserRefreshInterval);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _state.ReloadRequested -= OnReloadRequested;
        _usage.UsageChanged -= OnReloadRequested;
        _profiles.ProfileChanged -= OnReloadRequested;
        _users.OnUserUpdated -= OnUserUpdated;
        _sessions.SessionEnded -= OnSessionEnded;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
        }

        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _started.Dispose();
    }

    /// <summary>
    /// Loads a kid now: budget, sitting limit and Jellyfin user; forgets a user who is no longer a kid.
    /// </summary>
    /// <param name="kid">The user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the kid is loaded.</returns>
    public async Task LoadAsync(Guid kid, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(kid, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            _state.Forget(kid);
            return;
        }

        var status = await _usage.GetStatusAsync(kid, cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            _state.Forget(kid);
            return;
        }

        var budget = new KidBudget(
            status.Date,
            status.UsedSeconds * 1000,
            status.AllowanceSeconds is long allowance ? (int)(allowance / 60) : null,
            (int)(status.BonusSeconds / 60));
        _state.SetKid(kid, budget, profile.Settings.SessionLimitMinutes, _users.GetUserById(kid));
    }

    /// <summary>
    /// Loads a kid now, logging instead of throwing: the change that asked for it is already stored.
    /// </summary>
    /// <param name="kid">The user.</param>
    /// <returns>A task that completes when the kid is loaded or the load failed.</returns>
    public async Task TryLoadAsync(Guid kid)
    {
        try
        {
            await LoadAsync(kid).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kids enforcement could not load kid {UserId}", kid);
        }
    }

    private async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_database.EnsureInitialized())
            {
                _logger.LogWarning("Kids enforcement cannot load kids until the kids database can be read; nothing is refused meanwhile");
                return;
            }

            var kids = await _profiles.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var kid in kids)
            {
                await LoadAsync(kid.UserId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kids load on their next playback event instead (fail open meanwhile)
            _logger.LogWarning(ex, "Kids enforcement could not load the kids at startup");
        }
    }

    private void OnReloadRequested(object? sender, GenericEventArgs<Guid> e) => RequestReload(e.Argument);

    private void RequestReload(Guid kid)
    {
        lock (_lock)
        {
            if (!_reloading.Add(kid))
            {
                _dirty.Add(kid);
                return;
            }
        }

        _ = Task.Run(() => ReloadLoopAsync(kid));
    }

    private async Task ReloadLoopAsync(Guid kid)
    {
        while (true)
        {
            try
            {
                await LoadAsync(kid).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kids enforcement could not load kid {UserId}", kid);
            }

            lock (_lock)
            {
                if (!_dirty.Remove(kid))
                {
                    _reloading.Remove(kid);
                    return;
                }
            }
        }
    }

    private void OnUserUpdated(object? sender, GenericEventArgs<User> e)
    {
        if (e?.Argument is { } user && _state.IsKid(user.Id))
        {
            _state.SetUser(user.Id, user);
        }
    }

    private void OnSessionEnded(object? sender, SessionEventArgs e)
    {
        if (e?.SessionInfo is { } session)
        {
            _enforcer.Forget(session.Id);
        }
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        // The day start may have moved: every budget's day is read again
        foreach (var kid in _state.LoadedKids())
        {
            RequestReload(kid);
        }
    }

    private void RefreshUsers()
    {
        try
        {
            foreach (var kid in _state.LoadedKids())
            {
                if (_users.GetUserById(kid) is { } user)
                {
                    _state.SetUser(kid, user);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Kids enforcement could not refresh the kids' users");
        }
    }

    private void SelfCheck()
    {
        try
        {
            // Reading the action list makes MVC build it, which runs the convention
            var actionCount = _actions.ActionDescriptors.Items.Count;
            _coverage.MarkChecked();
            foreach (var missing in _coverage.Missing)
            {
                _logger.LogWarning("Kids enforcement cannot guard {Action}: this Jellyfin version does not have it", missing);
            }

            foreach (var extra in _coverage.Matched.Where(m => !m.ByName))
            {
                _logger.LogInformation("Kids enforcement also guards {Controller}.{Action}, found by its media type", extra.Controller, extra.Action);
            }

            _logger.LogInformation(
                "Kids enforcement guards {Count} playback actions out of {Total} ({State})",
                _coverage.Matched.Count,
                actionCount,
                _coverage.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kids enforcement self-check failed");
        }
    }
}
