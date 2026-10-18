using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Kids.Events;
using Jellyfin.Plugin.Kids.Hosted;
using Jellyfin.Plugin.Kids.Profiles;
using Jellyfin.Plugin.Kids.Storage;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Kids;

/// <summary>
/// Registers the plugin's services.
/// </summary>
/// <remarks>
/// This runs before the plugin instance exists and an exception here marks the plugin Malfunctioned, so it only
/// declares types: nothing is constructed, and nothing reads <see cref="Plugin.Instance"/>.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<KidsDatabase>();
        serviceCollection.AddSingleton<KidProfileStore>();
        serviceCollection.AddSingleton<IKidsUserDirectory, JellyfinUserDirectory>();
        serviceCollection.AddSingleton<IPickCatalog, JellyfinPickCatalog>();
        serviceCollection.AddSingleton<KidProfileService>();
        serviceCollection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedConsumer>();
        serviceCollection.AddHostedService<OrphanProfileSweeper>();
    }
}
