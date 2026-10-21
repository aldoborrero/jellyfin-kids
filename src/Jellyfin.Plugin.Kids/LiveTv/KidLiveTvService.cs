using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Kids.LiveTv;

/// <summary>
/// A kid's own channels and guide (research R10), read through Jellyfin's live TV with the kid as user, so Jellyfin's
/// content restrictions still apply, and cut to the kid's effective channels.
/// </summary>
public sealed class KidLiveTvService
{
    private readonly ILiveTvManager _liveTvManager;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly ILiveTvGate _gate;

    /// <summary>
    /// Initializes a new instance of the <see cref="KidLiveTvService"/> class.
    /// </summary>
    /// <param name="liveTvManager">Jellyfin's live TV.</param>
    /// <param name="userManager">Jellyfin's users.</param>
    /// <param name="dtoService">Jellyfin's item mapping.</param>
    /// <param name="gate">The kids' channels.</param>
    public KidLiveTvService(ILiveTvManager liveTvManager, IUserManager userManager, IDtoService dtoService, ILiveTvGate gate)
    {
        _liveTvManager = liveTvManager;
        _userManager = userManager;
        _dtoService = dtoService;
        _gate = gate;
    }

    /// <summary>
    /// Gets the kid's channels, in Jellyfin's channel order, each with the program on now.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The channels; empty when the kid has no live TV.</returns>
    public QueryResult<BaseItemDto> Channels(Guid kid, CancellationToken cancellationToken)
    {
        var allowed = _gate.AllowedChannels(kid);
        if (allowed.Count == 0 || _userManager.GetUserById(kid) is not { } user)
        {
            return new QueryResult<BaseItemDto>();
        }

        var options = Options();
        options.AddCurrentProgram = true;
        var channels = _liveTvManager.GetInternalChannels(
            new LiveTvChannelQuery { UserId = kid, AddCurrentProgram = true },
            options,
            cancellationToken);
        var items = channels.Items.Where(i => allowed.Contains(i.Id)).ToList();
        var dtos = _dtoService.GetBaseItemDtos(items, options, user);
        return new QueryResult<BaseItemDto>(0, dtos.Count, dtos);
    }

    /// <summary>
    /// Gets the programs of the kid's channels that overlap a window, ordered by start.
    /// </summary>
    /// <param name="kid">The kid.</param>
    /// <param name="window">The window.</param>
    /// <param name="channelIds">Channels to narrow to, or null for all of the kid's; others are ignored.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The programs; empty when the kid has no live TV.</returns>
    public async Task<QueryResult<BaseItemDto>> ProgramsAsync(Guid kid, GuideWindow window, IReadOnlyCollection<Guid>? channelIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        var allowed = _gate.AllowedChannels(kid);
        Guid[] channels = channelIds is { Count: > 0 } asked ? [.. asked.Where(allowed.Contains).Distinct()] : [.. allowed];
        if (channels.Length == 0 || _userManager.GetUserById(kid) is not { } user)
        {
            return new QueryResult<BaseItemDto>();
        }

        var query = new InternalItemsQuery(user)
        {
            ChannelIds = channels,
            MinEndDate = window.Start,
            MaxStartDate = window.End,
            OrderBy = [(ItemSortBy.StartDate, SortOrder.Ascending)],
            EnableTotalRecordCount = true
        };
        var options = Options();
        options.Fields = [ItemFields.Overview];
        return await _liveTvManager.GetPrograms(query, options, cancellationToken).ConfigureAwait(false);
    }

    private static DtoOptions Options() => new(false)
    {
        EnableImages = true,
        ImageTypes = [ImageType.Primary, ImageType.Thumb, ImageType.Logo],
        ImageTypeLimit = 1,
        EnableUserData = false
    };
}
