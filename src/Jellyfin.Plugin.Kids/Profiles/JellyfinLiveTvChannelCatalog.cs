using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;

namespace Jellyfin.Plugin.Kids.Profiles;

/// <summary>
/// <see cref="ILiveTvChannelCatalog"/> backed by Jellyfin's live TV. Queries run without a user, so every channel is
/// listed whatever any user may see.
/// </summary>
public sealed class JellyfinLiveTvChannelCatalog : ILiveTvChannelCatalog
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILiveTvManager _liveTvManager;
    private readonly IImageProcessor _imageProcessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLiveTvChannelCatalog"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="liveTvManager">The live TV manager.</param>
    /// <param name="imageProcessor">The image processor, for logo tags.</param>
    public JellyfinLiveTvChannelCatalog(ILibraryManager libraryManager, ILiveTvManager liveTvManager, IImageProcessor imageProcessor)
    {
        _libraryManager = libraryManager;
        _liveTvManager = liveTvManager;
        _imageProcessor = imageProcessor;
    }

    /// <inheritdoc />
    public IReadOnlyList<LiveTvChannelInfo> Resolve(IEnumerable<Guid> ids)
        // GetItemById throws on an empty id
        => [.. ids.Where(id => id != Guid.Empty).Select(id => _libraryManager.GetItemById(id)).OfType<LiveTvChannel>().Select(ToInfo)];

    /// <inheritdoc />
    public IReadOnlyList<LiveTvChannelInfo> List()
    {
        var channels = _liveTvManager.GetInternalChannels(new LiveTvChannelQuery(), new DtoOptions(false), CancellationToken.None);
        return [.. channels.Items.OfType<LiveTvChannel>().Select(ToInfo)];
    }

    private LiveTvChannelInfo ToInfo(LiveTvChannel channel)
    {
        var image = channel.HasImage(ImageType.Primary) ? channel.GetImageInfo(ImageType.Primary, 0) : null;
        return new LiveTvChannelInfo(
            channel.Id,
            channel.Name,
            string.IsNullOrEmpty(channel.Number) ? null : channel.Number,
            image is null ? null : _imageProcessor.GetImageCacheTag(channel, image),
            channel.ChannelType == ChannelType.Radio);
    }
}
