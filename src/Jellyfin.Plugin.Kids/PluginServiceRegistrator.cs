using System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Kids.Allowance;
using Jellyfin.Plugin.Kids.Auth;
using Jellyfin.Plugin.Kids.Enforcement;
using Jellyfin.Plugin.Kids.Events;
using Jellyfin.Plugin.Kids.Hosted;
using Jellyfin.Plugin.Kids.ParentLock;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.Kids;

/// <summary>
/// Registers the plugin's services.
/// </summary>
/// <remarks>
/// This runs before the plugin instance exists and an exception here marks the plugin Malfunctioned, so it only
/// declares types: nothing with dependencies is constructed (only the empty <see cref="GateCoverage"/>), and nothing
/// reads <see cref="Plugin.Instance"/>.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<KidsDatabase>();
        serviceCollection.AddSingleton<KidProfileStore>();
        serviceCollection.AddSingleton<KidRoster>();
        serviceCollection.AddSingleton<IKidsSettings, PluginKidsSettings>();
        serviceCollection.AddSingleton<UsageStore>();
        serviceCollection.AddSingleton<UsageService>();
        serviceCollection.AddSingleton<IKidsUserDirectory, JellyfinUserDirectory>();
        serviceCollection.AddSingleton<IPickCatalog, JellyfinPickCatalog>();
        serviceCollection.AddSingleton<KidProfileService>();
        serviceCollection.AddSingleton<ParentLockStore>();
        serviceCollection.AddSingleton<IDeviceDirectory, JellyfinDeviceDirectory>();
        serviceCollection.AddSingleton<GrantService>();
        serviceCollection.AddSingleton<ThrottleService>();
        serviceCollection.AddSingleton<ParentLockService>();
        serviceCollection.AddScoped<CallerResolver>();
        serviceCollection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedConsumer>();
        serviceCollection.AddHostedService<OrphanProfileSweeper>();
        serviceCollection.AddHostedService<PlaybackTracker>();

        // Enforcement: the filter is attached to playback actions only, by an MVC convention (research R2). The
        // coverage object is shared with the convention, which MVC builds before any service is resolved.
        var coverage = new GateCoverage();
        serviceCollection.AddSingleton(coverage);
        serviceCollection.AddSingleton<DeliveryRegistry>();
        serviceCollection.AddSingleton<IPlaybackControl, JellyfinPlaybackControl>();
        serviceCollection.AddSingleton<EnforcementState>();
        serviceCollection.AddSingleton<IPlaybackGate>(sp => sp.GetRequiredService<EnforcementState>());
        serviceCollection.AddSingleton<PlaybackEnforcer>();
        serviceCollection.AddSingleton<EnforcementService>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<EnforcementService>());
        serviceCollection.AddSingleton<KidsPlaybackFilter>();
        serviceCollection.Configure<MvcOptions>(options => options.Conventions.Add(new PlaybackGateConvention(coverage)));
    }
}
