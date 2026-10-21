# Research: Live TV for kids

All citations are against `.reference/jellyfin` at tag `v12.2` unless stated otherwise; the plugin compiles against the
12.0.0 packages, and every member used below is in `MediaBrowser.Controller.xml` 12.0.0.

## R1. How Jellyfin plays a live TV channel

- **Decision**: treat a request as live TV when it names a channel through any of: the item id (a `LiveTvChannel`, or a
  `LiveTvProgram` whose `ChannelId` is the channel), a live stream **open token**, or a live stream id.
- **Findings**:
  - A channel is a `BaseItem` (`MediaBrowser.Controller/LiveTv/LiveTvChannel.cs`); clients ask
    `Items/{channelId}/PlaybackInfo` (GET or POST, `AutoOpenLiveStream` usually true), or `LiveStreams/Open` with an
    `OpenToken` (query or body) and/or `ItemId`; then HLS `Videos/{channelId}/master.m3u8?LiveStreamId=…`, or the
    direct live stream file `LiveTv/LiveStreamFiles/{uniqueId}/stream.{container}` (shared HTTP tuner streams,
    `SharedHttpStream.cs:66`).
  - The open token is `md5(provider type)_` + `LiveTvChannel_<channelIdN>_<mediaSourceId>`
    (`LiveTvMediaSourceProvider.cs:105-116`, `MediaSourceManager.cs:356-369`, delimiter `_`). The channel id is in it.
  - The live stream id is `md5("Jellyfin.LiveTv.LiveTvMediaSourceProvider")_…` (`SetKeyProperties`), and says nothing
    about the channel. `IMediaSourceManager.GetLiveStreamInfo(id)` / `GetLiveStreamInfoByUniqueId(uniqueId)` return the
    open `ILiveStream`, whose `OriginalStreamId` is the tuner's media source id, not the Jellyfin channel id.
  - `GetMD5()` is `new Guid(MD5(UTF-16LE(str)))` (`MediaBrowser.Common/Extensions/BaseExtensions.cs:30`), callable
    from the plugin.
- **Alternatives**: map `OriginalStreamId` back to channels by asking every channel for its media sources (slow, tuner
  specific); rejected.

## R2. Attributing open live streams (FR-007)

- **Decision**: a `LiveStreamRegistry` in memory. When the plugin lets a kid's `PlaybackInfo` or `LiveStreams/Open`
  through for a channel, it reads the answer (`PlaybackInfoResponse.MediaSources[].LiveStreamId`,
  `LiveStreamResponse.MediaSource.LiveStreamId`) and records `liveStreamId → channel` and, through
  `GetLiveStreamInfo(id).UniqueId`, `uniqueId → channel`. Entries whose stream is no longer open are pruned on each
  record. A kid request naming a live TV live stream id (the provider prefix) or a live stream file id that is not
  recorded is refused as an unknown live stream.
- **Rationale**: the plugin sees every kid opening, since both routes are gated; no tuner knowledge needed. A restart
  loses the registry, but Jellyfin's open streams are in memory too and end with it.
- **Live stream ids of other providers** (internet channel plugins, `ChannelDynamicMediaSourceProvider`) do not carry
  the live TV prefix and are left alone.

## R3. Where the channel rule runs: an action filter

- **Decision**: a new `IAsyncActionFilter` (`KidsLiveTvFilter`), attached by the existing `PlaybackGateConvention` to
  the playback actions it already guards plus the live TV browse and recording actions (R6, R7).
- **Rationale**: the 005 filter is a resource filter that runs before model binding, so it cannot see a body-only
  `OpenToken` or `ItemId` (`OpenLiveStreamDto`, `PlaybackInfoDto.LiveStreamId`). An action filter sees the bound
  `ActionArguments` and, after `next()`, the action's `ObjectResult` (needed for R2 and the browse lists). The time
  rule (resource filter) still runs first and wins.
- **Argument reading**: by name, case-insensitive: `itemId`, `liveStreamId`, `openToken`, `streamId` (only on
  `LiveTv.GetLiveStreamFile`), plus the same names as properties of a bound object whose type is in a `Jellyfin.*` or
  `MediaBrowser.*` namespace (the DTOs live in `Jellyfin.Api`, which plugins cannot reference). Matching by name follows
  005's `ProducesFile` approach.
- **Alternatives**: buffering and parsing the body in the resource filter (fragile, double work); a global filter
  (cost on every request).

## R4. Always on, and who is a kid

- **Decision**: the live TV rules ignore `EnforcementEnabled` (clarification Q1). The kid lookup that 005's filter
  does (claims, then `deviceId` for token-less requests) moves to a shared `KidRequest.Resolve`, called with or without
  the enforcement check. Administrators (role `Administrator`, which includes API keys) and non-kids return no kid.
- **Token-less requests** (server-side ffmpeg reading `LiveStreamFiles`, anonymous URLs) are judged only if their
  `deviceId` names a kid's session, as in 005.

## R5. The effective channel set, in memory

- **Decision**: `EnforcementState` already keeps, per kid, the profile-derived sitting limit and a cached Jellyfin
  `User` (refreshed on `OnUserUpdated` and every 60 s). It also keeps the profile's channel ids; the effective set is
  empty when `user.HasPermission(PermissionKind.EnableLiveTvAccess)` is false (FR-006). A kid in the roster but not yet
  loaded has an empty set (refused) and asks for a load.
- **Item → channel**: `ILibraryManager.GetItemById(Guid)` (Jellyfin's item cache) only for kid requests; a small
  bounded memo `itemId → channelId | none` avoids repeated lookups for HLS segments. Items never change type.

## R6. Jellyfin's own browse routes (clarification Q2)

| Action | Kid treatment |
|---|---|
| `LiveTv.GetLiveTvChannels` | before: drop `startIndex`/`limit`; after: keep only effective channels, re-apply paging, `TotalRecordCount` = kept count |
| `LiveTv.GetLiveTvPrograms` (GET) | before: `channelIds` = requested ∩ effective, or effective if none requested; nothing left → empty `QueryResult` without running the action |
| `LiveTv.GetPrograms` (POST) | same on `body.ChannelIds` |
| `LiveTv.GetRecommendedPrograms` | before: drop `startIndex`/`limit`; after: keep programs of effective channels, page, count |
| `LiveTv.GetChannel` | channel item not effective → 404 (non-channel ids pass) |
| `LiveTv.GetProgram` | program whose channel is not effective → 404 |

- `ActionResult<T>` is converted to an `ObjectResult` before `OnActionExecuted`, so the filter reads and replaces
  `ObjectResult.Value` (`QueryResult<BaseItemDto>`).
- An empty `channelIds` means "all channels" to Jellyfin (`InternalItemsQuery.ChannelIds`), hence the short-circuit.
- Not covered (documented limit): generic `/Items` queries that name channel or program ids directly, and images.

## R7. Recordings and timers

- **Decision**: for a kid, every `LiveTv` action whose name contains `Recording` or `Timer` (recordings, recording
  groups/folders/series, timers, series timers, timer defaults, create/update/cancel/delete) and
  `LiveTv.GetLiveRecordingFile` answer 403 with `X-Kids-Reason: liveTvRecording`. Matching by name catches actions
  added in later 12.x.
- Recordings already in the library are ordinary videos under the kid's folder access (constitution I).

## R8. Refusal shapes

- Same as 005 (`KidsPlaybackFilter.Refusal`, made shared): PlaybackInfo → 200 `ErrorCode: NotAllowed`, header
  `X-Kids-Reason`; other playback → 403, `X-Application-Error-Code: PlaybackNotAllowed`, ProblemDetails
  `{"status":403,"title":"This channel is not available.","reason":"liveTvChannel"}`. Recordings → 403 with
  `reason: liveTvRecording` and title "Recordings are not available.".

## R9. Counting

- `PlaybackTracker` counts any playback event whose session owner is a kid, whatever the item; live TV reports
  `ItemId` = the channel and gets the same automatic 1 s progress ticks. The plan is to verify it in the dev loop
  (quickstart §3) and fix only if a gap shows. The stop chain already closes `PlayState.LiveStreamId`
  (`JellyfinPlaybackControl`).
- **Verified 2026-10-21, no gap, no change to `PlaybackTracker`.** dev-check `cm`: 60 s of PekeTV playstate counts
  60 s. jellyfin-web 12.2 in headless Chromium, enforcement on, alice with 98 s left, playing PekeTV (HLS
  `live.m3u8` over the shared M3U stream): toasts "2 minutes left" and "1 minute left", the "Screen time is over for
  today." dialog at 98 s, the next `live.m3u8` 403 `timeUp`, the player gone 4 s later; the server logged the stop and
  "Closing live stream …" / `SharedHttpStream` closed 5 s after time's up (the grace). `UsedSeconds` 82 → 182.

## R10. Kid endpoints and the admin channel list

- `GET /Kids/Users/{userId}/LiveTv/Channels`: `ILiveTvManager.GetInternalChannels` with the kid as `UserId` (Jellyfin's
  content restrictions apply), kept to the effective set, mapped by `IDtoService.GetBaseItemDtos` with
  `AddCurrentProgram` and primary images: Jellyfin's `BaseItemDto` shape, so clients reuse their live TV models.
- `GET /Kids/Users/{userId}/LiveTv/Programs?startDate&endDate&channelIds`: `ILiveTvManager.GetPrograms` with the kid's
  `InternalItemsQuery`, `ChannelIds` = effective ∩ requested, `MinEndDate = start`, `MaxStartDate = end`, ordered by
  start date. Defaults now → +24 h; longer than 7 days is cut; end ≤ start → 400.
- `GET /Kids/Admin/LiveTvChannels` (admin): every channel with `Id`, `Name`, `Number`, `ImageTag`, `Type`
  (`TV`/`Radio`), in Jellyfin's channel order, for the editor.
- Validation: `ILiveTvChannelCatalog.Existing(ids)` = ids whose item is a `LiveTvChannel`, mirroring `IPickCatalog`.

## R11. Storage

- Profiles are a JSON column (`kid_profile.settings`); unknown and missing properties are tolerated
  (`KidProfileStore._jsonOptions`). A new `LiveTvChannelIds` property needs **no schema migration**; old rows read as
  empty.

## R12. Dev loop live TV

- **Finding**: Jellyfin 12.2 skips M3U entries whose path is not HTTP ("Skipping M3U channel entry with non-HTTP path"),
  so local files cannot stand in for a tuner.
- **Decision**: a `livetv` dev-loop process (`scripts/dev-livetv.sh`) serves two live MPEG-TS channels generated by
  jellyfin-ffmpeg over HTTP (`testsrc2` with a clock and distinct tones, `-re`, `-listen 1`, restarted after each
  connection), on ports `JELLYFIN_KIDS_PORT` + 10 and + 11. Provisioning writes an M3U ("PekeTV" 1, "Grown-ups News" 2,
  `tvg-id`) and an XMLTV guide with 30-minute programs for the next 48 h, saves an M3U tuner host with stream sharing
  (no looping, as ErsatzTV channels are set up) and an XMLTV listing provider, refreshes the guide
  (`/ScheduledTasks/Running/{RefreshGuide}`) and waits for both channels and their programs.
- **Rationale**: no new package; the shared HTTP stream exercises `LiveTv/LiveStreamFiles` like the household's
  ErsatzTV channels; encoding runs only while Jellyfin reads a channel.

## R13. Refusals from an action filter (found in the dev loop)

- **Finding**: when an action filter sets `context.Result`, MVC still runs the result filters, and Jellyfin's
  controllers carry `[Produces("application/json", …)]`, whose `OnResultExecuting` replaces an `ObjectResult`'s content
  types: the live TV 403s went out as `application/json` instead of `application/problem+json`. Feature 5's refusals come
  from a resource filter, which skips result filters, so they were not affected.
- **Decision**: `PlaybackRefusal.Forbidden` returns a `JsonResult` with `ContentType = "application/problem+json"` for
  every rule (Jellyfin's `[Produces]` does not touch it). PlaybackInfo's `NotAllowed` stays an `OkObjectResult`, so it
  keeps Jellyfin's JSON profiles.
