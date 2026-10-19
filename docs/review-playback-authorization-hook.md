# Review: a playback authorization hook for Jellyfin (12.2)

Date: 2026-10-19. Scope: an adversarial review of the proposed `IPlaybackAuthorizer` core extension point, and of what
the Jellyfin Kids plugin can do today with no core change. Nothing was posted or changed except this file.

## Conventions

- **[V]** is a fact verified in source, with `file:line`. **[I]** is an inference: reasoning from the code, or
  behaviour of client libraries that are not in `.reference/`. **[M]** is a measurement by somebody else, quoted.
- Path prefixes:
  - `S:` is the server, `.reference/jellyfin` (`SharedVersion.cs` says 12.2.0, commit `ca0f16e`).
  - `W:` is jellyfin-web 12.2, `.reference/jellyfin-web`.
  - `P:` is our plugin. **The `main` checkout at `/home/aldo/Dev/aldoborrero/jellyfin-kids` is still at the scaffold
    commit `64a42b6`.** The code that has `PlaybackTracker`, `UsageService` and `StatusCalculator` is in the worktree
    `.claude/worktrees/kids-native-ui` (commit `c8810f9`, which merges features 003 to 006). `P:` paths refer to it.
  - `Wh:` is Wholphin (`/home/aldo/Dev/aldoborrero/Wholphin`), our reference client.
  - `B:` is `.reference/benben42_jellyfin-kids-limit-plugin` (no licence file, studied only).

---

## Headline findings

1. **The proposed hook points are incomplete, and one omission defeats the stop flow.** Killing a transcode does not
   end HLS playback, because the next `hls1` segment request **starts ffmpeg again** (`S:Jellyfin.Api/Controllers/DynamicHlsController.cs:1472-1522`).
   If segments are not checked, "kill the transcode" lasts one segment.
2. **Several media routes are missing from the proposal:**
   - `Items/{id}/File` serves the raw file, with range support, to any authenticated user. It needs no download
     permission (`S:Jellyfin.Api/Controllers/LibraryController.cs:109-134`).
   - `Items/{id}/Download`.
   - `Videos/{id}/main.m3u8` and `Audio/{id}/main.m3u8`, which can be requested without the master playlist.
   - `Videos/{id}/live.m3u8`.
   - `LiveTv/LiveStreamFiles/{id}/stream.{container}` and `LiveTv/LiveRecordings/{id}/stream`, which are **anonymous**.
   - `LiveStreams/Open`.
3. **`PlaySessionId` cannot be the cache key.**
   - At PlaybackInfo time it does not exist yet: the server generates it after resolving media sources
     (`S:Jellyfin.Api/Helpers/MediaInfoHelper.cs:132`).
   - On stream routes it is an optional, unvalidated query parameter.
   - jellyfin-web leaves it out of direct-play URLs (`W:src/components/playback/playbackmanager.js:2884-2901`).
   - jellyfin-web makes it up from a client-side counter for music (`W:…/playbackmanager.js:308-324`).
4. **The cost the proposal tries to save does not matter.**
   - Every authenticated request already creates an EF `DbContext` and scans every device linearly
     (`S:Jellyfin.Server.Implementations/Security/AuthorizationContext.cs:129-133`,
     `S:Jellyfin.Server.Implementations/Devices/DeviceManager.cs:146-152`).
   - Every HLS segment already runs the full `GetStreamingState`.
   - An in-memory lookup per request is noise next to that. The real cost is inside the authorizer. Our
     `UsageService.GetStatusAsync` makes three SQLite reads (`P:src/Jellyfin.Plugin.Kids/Allowance/UsageService.cs:78, 293, 300`).
5. **Direct play *can* be cut on the server, which both the proposal and our plan assume is impossible [I, needs a
   spike].** A plugin `IAsyncResourceFilter` wraps the execution of the result. While a kid's `PhysicalFileResult`
   streams, the filter can keep a handle to the `HttpContext` and call `HttpContext.Abort()` when time runs out. The
   player then retries with a range request, and the gate refuses it. Playback ends when the client buffer runs dry.
6. **The exact-time timer is the wrong tool for allowances.**
   - Allowance time is used up only while playing, and twice as fast with two sessions, so a timer set to "now plus
     remaining" is wrong as soon as the kid pauses or a second device starts.
   - The server already raises a synthetic progress event every second for each playing session
     (`S:MediaBrowser.Controller/Session/SessionInfo.cs:397-447`), and `PlaybackTracker` already consumes it.
   - Checking "used ≥ limit" in memory on each event gives a cutoff within 1 s, with no timers. It is also robust to
     clock changes, DST, restarts and any number of kids.
7. **Our plugin can enforce today**, with an MVC application-model convention and a resource filter attached to
   about 24 actions. Every other request pays nothing. Details are in §F.

---

## A. Correctness of the hook points

### A.1 Every route that starts or continues media delivery (12.2)

All controllers derive from `BaseJellyfinApiController` (`[Route("[controller]")]`, `S:Jellyfin.Api/BaseJellyfinApiController.cs:13`).
**There is no fallback authorization policy**, so an action with no `[Authorize]` is anonymous
(`S:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:63-91` sets only `DefaultPolicy` and named policies).
Authentication still runs on every request, including anonymous ones:
- `AddAuthentication(CustomAuthentication)` makes our scheme the default (`ApiServiceCollectionExtensions.cs:100-101`);
- `UseAuthentication()` runs before routing (`S:Jellyfin.Server/Startup.cs:232-236`).

So a valid `?ApiKey=` on an anonymous route still fills `HttpContext.User` [V + I]. An *invalid* token gives
`AuthenticateResult.Fail` (`S:Jellyfin.Api/Auth/CustomAuthenticationHandler.cs:84-87`, through
`S:Emby.Server.Implementations/HttpServer/Security/AuthService.cs:30-33`). An anonymous action then runs anyway, as
anonymous [I].

Key to the "Default auth" column: **D** means `[Authorize]` with `DefaultAuthorizationRequirement`, which includes the
AccessSchedule check (`S:Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs:80-85`). **Anon**
means no authorization; the user is known only if a token is in the query.

| # | Route (method) | Controller.Action | Auth | Path to bytes | Proposal covers |
|---|---|---|---|---|---|
| 1 | `GET Items/{id}/PlaybackInfo` | `MediaInfo.GetPlaybackInfo` (`MediaInfoController.cs:72-88`) | D (class, `:29`) | Negotiation; `MediaInfoHelper.GetPlaybackInfo` | Only POST is named. **Add GET** |
| 2 | `POST Items/{id}/PlaybackInfo` | `MediaInfo.GetPostedPlaybackInfo` (`:116-268`) | D | Negotiation; same helper (`:177`); returns early if `ErrorCode` is set (`:185-188`) | Yes |
| 3 | `POST LiveStreams/Open` | `MediaInfo.OpenLiveStream` (`:270-313`) | D | Opens a tuner or live source (`MediaInfoHelper.OpenMediaSource`, `MediaInfoHelper.cs:426-470`) | **No** |
| 4 | `GET/HEAD Audio/{id}/universal` | `UniversalAudio.GetUniversalAudioStream` (`UniversalAudioController.cs:92-99`) | D | Calls `MediaInfoHelper.GetPlaybackInfo` itself (`:133`), then an HLS master (`:228`), progressive or static audio (`:256`), or a redirect (`:177`) | Yes |
| 5 | `GET/HEAD Videos/{id}/master.m3u8` | `DynamicHls.GetMasterHlsVideoPlaylist` (`DynamicHlsController.cs:404-408`) | D (class, `:39`) | `DynamicHlsHelper` then `GetStreamingState` (`DynamicHlsHelper.cs:124`) | Yes |
| 6 | `GET/HEAD Audio/{id}/master.m3u8` | `DynamicHls.GetMasterHlsAudioPlaylist` (`:577-581`) | D | Same | Yes |
| 7 | `GET Videos/{id}/main.m3u8` | `DynamicHls.GetVariantHlsVideoPlaylist` (`:745-748`) | D | `GetStreamingState` (`:1389`). A static VOD playlist (`src/Jellyfin.MediaEncoding.Hls/Playlist/DynamicHlsPlaylistGenerator.cs:58, 104`) | **No**, and a client can skip the master |
| 8 | `GET Audio/{id}/main.m3u8` | `DynamicHls.GetVariantHlsAudioPlaylist` (`:914-917`) | D | Same | **No** |
| 9 | `GET Videos/{id}/live.m3u8` | `DynamicHls.GetLiveHlsStream` (`:164-167`) | D | `GetStreamingState` (`:283`), **starts ffmpeg** (`:309`) | **No** |
| 10 | `GET Videos/{id}/hls1/{pl}/{seg}.{c}` | `DynamicHls.GetHlsVideoSegment` (`:1086-1090`) | D | `GetDynamicSegment`, then `GetStreamingState` (`:1438`). **Restarts ffmpeg if no job or segment exists** (`:1472-1522`) | **Explicitly excluded** |
| 11 | `GET Audio/{id}/hls1/{pl}/{seg}.{c}` | `DynamicHls.GetHlsAudioSegment` (`:1268-1272`) | D | Same | **Excluded** |
| 12 | `GET/HEAD Videos/{id}/stream` and `stream.{container}` | `Videos.GetVideoStream` and `GetVideoStreamByContainer` (`VideosController.cs:314-318, 552-556`) | **Anon** (no class or action attribute, `:38-39`) | `GetStreamingState` (`:428`). Static means `PhysicalFileResult` with ranges (`FileStreamResponseHelpers.cs:105-110`); otherwise progressive ffmpeg | Yes ("direct stream") |
| 13 | `GET/HEAD Audio/{id}/stream` and `stream.{container}` | `Audio.GetAudioStream` and `GetAudioStreamByContainer` (`AudioController.cs:88-92, 252-256`) | **Anon** (`:19`) | `AudioHelper` then `GetStreamingState` (`AudioHelper.cs:88`) | Partly; only "direct stream" is named |
| 14 | `GET Videos/{id}/hls/{pl}/stream.m3u8` (legacy) | `HlsSegment.GetHlsPlaylistLegacy` (`HlsSegmentController.cs:79-96`) | D | Serves an existing file from the transcode folder | No; harmless once the transcode is gone |
| 15 | `GET Videos/{id}/hls/{pl}/{seg}.{c}`, `Audio/{id}/hls/{seg}/stream.mp3`/`.aac` (legacy) | `HlsSegment.GetHlsVideoSegmentLegacy` / `GetHlsAudioSegmentLegacy` (`:126-131, 55-60`) | **Anon** ("Can't require authentication just yet", `:52-53, 124-125`) | Existing transcode files only | No; dies with the transcode |
| 16 | `GET Items/{id}/File` | `Library.GetFile` (`LibraryController.cs:109-134`) | D, **with no download permission** | `PhysicalFile(..., enableRangeProcessing: true)` (`:133`): direct play of the original file | **No. A real bypass** |
| 17 | `GET Items/{id}/Download` | `Library.GetDownload` (`:669-704…`) | `Policies.Download` (`EnableContentDownloading`) | Full file | **No** |
| 18 | `GET LiveTv/LiveStreamFiles/{sid}/stream.{c}` | `LiveTv.GetLiveStreamFile` (`LiveTvController.cs:1145-1160`) | **Anon** (no class attribute, `:41`) | The tuner stream itself. The server publishes it as the source `Path` (`src/Jellyfin.LiveTv/TunerHosts/SharedHttpStream.cs:66`, `MediaInfoHelper.cs:550-610`) | **No** |
| 19 | `GET LiveTv/LiveRecordings/{rid}/stream` | `LiveTv.GetLiveRecordingFile` (`:1118-1131`) | **Anon** | An in-progress recording (`MediaSourceManager.cs:985`) | **No** |
| 20 | `POST Sessions/{id}/Playing` (remote Play) | `Session.Play` (`SessionController.cs:119-143`) | D | `SessionManager.SendPlayCommand` checks only `GetPlayAccess` (`SessionManager.cs:1354-1397`), which is `EnableMediaPlayback` only; the schedule check is commented out (`MediaBrowser.Controller/Entities/BaseItem.cs:1128-1141`). The target client then calls PlaybackInfo itself [I] | Indirectly (row 2) |
| 21 | `SyncPlay/*` | `SyncPlay.*` (`SyncPlayController.cs:25`, `Policies.SyncPlayHasAccess`) | D-based | No delivery. Members play locally through their own PlaybackInfo and streams [I]. `Unpause` resumes a stream that is already open | Indirectly |
| 22 | Subtitles (`Videos/{id}/{msid}/Subtitles/...`), `subtitles.m3u8`, Trickplay, Attachments, Lyrics | various | Mixed (`SubtitleController.cs:208-211` is anon) | Not media playback | Correctly out of scope; blocking them only breaks the UI |
| 23 | DLNA | — | — | **Not in 12.2 core.** Only `MediaBrowser.Model/Dlna` (profiles and `StreamInfo`) remains. DLNA is a separate plugin, and its renderers fetch row 12 or 13 URLs with no token [I] | — |

Other facts that matter for completeness:
- **Six entry points share one choke point.** `StreamingHelpers.GetStreamingState` is the only caller path for rows
  5 to 13 (`S:Jellyfin.Api/Helpers/StreamingHelpers.cs:45`; callers at `AudioHelper.cs:88`, `DynamicHlsHelper.cs:124`,
  `VideosController.cs:428`, `DynamicHlsController.cs:283, 1389, 1438`). It also takes the user from
  `httpContext.User` (`StreamingHelpers.cs:84-88`) [V].
- **Rows 1, 2 and 4 share `MediaInfoHelper.GetPlaybackInfo`** (`MediaInfoHelper.cs:93`; callers at
  `MediaInfoController.cs:87, 177` and `UniversalAudioController.cs:133`) [V].
- **UniversalAudio ignores `ErrorCode`.** It indexes `info.MediaSources[0]` without checking it
  (`UniversalAudioController.cs:174`). A deny injected into the shared helper therefore throws
  `IndexOutOfRangeException` there, unless UniversalAudio translates it to 403 [V].
- **What the core already enforces per request.** The default policy refuses the request outside AccessSchedules on
  every D route, HLS segments included (`DefaultAuthorizationHandler.cs:80-85`). HLS playback after the allowed hours
  therefore stalls at the next segment, and anonymous `Videos/{id}/stream` keeps going [V + I].

### A.2 Which routes bypass PlaybackInfo

- **By design:** UniversalAudio (row 4) runs its own PlaybackInfo internally.
  - jellyfin-web builds the music URL directly (`W:…/playbackmanager.js:308-326`).
  - So does Wholphin (`Wh:app/.../services/MusicService.kt:308-322`, `services/ThemeSongPlayer.kt:95`).
- **By choice of client** (nothing ties a stream request to an earlier PlaybackInfo):
  - rows 7 to 13 accept any `itemId` and `mediaSourceId`;
  - `GetStreamingState` resolves the item **without the user** (`StreamingHelpers.cs:110-111, 129`), so it applies no
    library or rating check either;
  - rows 16 to 19.
  - Wholphin's external-player path builds `Videos/{id}/stream?static=true` straight from the item
    (`Wh:app/.../ui/playback/PlayExternalPage.kt:201-207`).
- **Remote Play (row 20)** is only a message. The receiving client still negotiates.

### A.3 Anonymous routes that carry `ApiKey`

- **Anonymous:** rows 12, 13, 15, 18 and 19, plus the subtitle stream.
- **Who sends `ApiKey`:**
  - Web direct-play URLs: `Static`, `mediaSourceId`, `deviceId`, `ApiKey`, `Tag` and `LiveStreamId`, with **no
    `PlaySessionId`** (`W:…/playbackmanager.js:2884-2901`).
  - Server-built `TranscodingUrl`s: `PlaySessionId`, then `ApiKey` (`MediaBrowser.Model/Dlna/StreamInfo.cs:1026-1036`;
    `MediaInfoHelper.cs:312, 325`).
- **Token carriers.** `?ApiKey=` is always read. `?api_key=` and the `X-Emby-*` headers are read only with
  `EnableLegacyAuthorization` (`AuthorizationContext.cs:93-111`).
- **Consequence [I].** On rows 12 and 13 the kid is identifiable *only* while the client sends a token. Remove
  `ApiKey` and the request is anonymous and still served. Any authorizer keyed on "the user" is advisory on those
  rows. The proposal's context ("User (the session owner)") has no defined value there.

### A.4 Are the proposed hook points complete?

**No.** These must be added:
1. **HLS segments and variant playlists (rows 7, 8, 10 and 11).** This is the most important one: segments are where
   a killed transcode comes back to life (`DynamicHlsController.cs:1472-1522`). With a per-user cache the cost is a
   dictionary hit.
2. **`live.m3u8` (row 9).**
3. **`GET` PlaybackInfo (row 1)** and **`LiveStreams/Open` (row 3)**, so a tuner is not opened for a denied user.
4. **`Items/{id}/File` (row 16)** and **`Items/{id}/Download` (row 17).**
5. **`LiveStreamFiles` and `LiveRecordings` (rows 18 and 19).** They are anonymous, so this works only with a token
   or a binding.
6. **Progressive (non-static) audio and video streams.** They are covered if the hook sits in `GetStreamingState`;
   they are not if only "direct stream" is hooked.

**Placement fix.** Hook the two shared helpers, `MediaInfoHelper.GetPlaybackInfo` and `OpenMediaSource` for
negotiation and `StreamingHelpers.GetStreamingState` for delivery, plus four explicit calls (rows 16 to 19). This is
more complete and cheaper to review than five controller actions.

**Alternative placement.** One core resource filter that keys on endpoint metadata. The existing
`[ProducesVideoFile]`, `[ProducesAudioFile]`, `[ProducesPlaylistFile]` and `[ProducesFile("video/*","audio/*")]`
attributes already mark exactly rows 5 to 19, plus subtitle and trickplay playlists, which are easy to exclude
(`S:Jellyfin.Api/Attributes/Produces*.cs`; a grep of the controllers is listed in §F.2).

---

## B. Is `PlaySessionId` a usable cache key?

| Hook point | Is `PlaySessionId` there? | Trustworthy? |
|---|---|---|
| PlaybackInfo, before media sources are resolved | **No.** Generated afterwards, as a fresh GUID on every call (`MediaInfoHelper.cs:132`) | — |
| UniversalAudio | The internal `info.PlaySessionId` is fresh on each request (`UniversalAudioController.cs:133, 203, 237`). The client's own `PlaySessionId` query (web: a timestamp counter, `playbackmanager.js:308-324`) is overwritten | Server value is fresh per request, so the cache always misses |
| HLS master, variant, segments | In the `TranscodingUrl` query (`StreamInfo.cs:1026-1030`) | A plain query string; the server only uses it to find a transcode job (`StreamingHelpers.cs:117-120`) |
| `Videos`/`Audio` `stream` (static) | **Optional** (`VideosController.cs:325`). jellyfin-web leaves it out (`playbackmanager.js:2884-2901`). Wholphin sends it for video (`Wh:…/PlaybackViewModel.kt:708-714`) but not for music or the external player | Never validated |
| `Items/{id}/File`, Download, LiveTv files | Not a parameter at all | — |

**Can a client omit or forge it? [V + I]**
- **Omit it.** Then the key is null. The core either calls the authorizer on every request (every range request,
  every segment) or skips caching. That is a cost problem, not a bypass.
- **Randomise it.** The cache misses every time. Same result.
- **Replay an old *allowed* id.** If the cache keeps "allow" entries keyed by `PlaySessionId`, and revalidation only
  stops sessions without evicting **every** entry of that user, a hand-made URL that reuses an old id gets a cached
  allow after time is up. **That is a bypass.**
- **Map sessions to entries.** `SessionInfo` and `PlayerStateInfo` don't store the `PlaySessionId`
  (`MediaBrowser.Model/Session/PlayerStateInfo.cs`, which has `PlayMethod` and `LiveStreamId` only). So
  `RevalidateAsync(userId)` can't even find the entries it should drop without new bookkeeping [V].

**A better key [I].**
- **For time and schedule rules:** `UserId`, plus a per-user **epoch** counter. `RevalidateAsync(userId)` increments
  the epoch, which drops every entry for that user in O(1).
- **For content-dependent authorizers:** `(UserId, ItemId, epoch)`.
- **Validity:** the decision carries a `ValidUntil`, for example the next day start or the next schedule boundary.
  The core caps it at a few minutes.
- **No user:** don't key at all. That is the anonymous case in §A.3.

---

## C. Efficiency

### C.1 Actual request and event rates

- **Direct play [I, from client behaviour].** The browser `<video>` element and ExoPlayer start with one open-ended
  range request (`Range: bytes=0-`). The server answers with a range-capable `PhysicalFileResult`
  (`FileStreamResponseHelpers.cs:109`) and TCP backpressure paces it. They add one request per seek, and sometimes one
  for an MP4 `moov` atom at the end of the file. A typical session is **2 to 10 requests**, not hundreds.
  Per-`PlaySessionId` caching solves a problem that is small to begin with.
- **HLS VOD [V].**
  - The master playlist is fetched once, and the variant (`main.m3u8`) once, because it is a complete VOD playlist
    with `ENDLIST` (`DynamicHlsPlaylistGenerator.cs:58, 104`).
  - After that the client fetches **one segment per segment length**: 3 s when transcoding, 6 s when copying
    (`S:MediaBrowser.Controller/Streaming/StreamState.cs:74-103`).
  - That is **600 to 1200 segment requests per hour**. Each runs authentication, the default authorization handler
    (`GetUserById`, the schedule check) and `GetStreamingState`.
- **HLS live (`live.m3u8`) [I].** The ffmpeg-written playlist grows, so it is refetched about once per target duration
  (3 s for segmented live, `StreamState.cs:95-98`), plus segments.
- **Progress events [V].**
  - jellyfin-web reports every **10 s** (`W:…/playbackmanager.js:3343-3351`). B measured about 10 s on Android TV
    (`B:REQUIREMENTS.md:29`) [M].
  - The server adds a **synthetic tick every 1 s** for each playing, unpaused session (`SessionInfo.cs:383-447`,
    timer at `:397`). The tick stops at the end of the runtime (`:434-438`) and is restarted by every real report
    (`SessionManager.cs:965`).
  - Idle playing sessions are stopped after 5 minutes without a check-in (`SessionManager.cs:609, 639-674`).
  - **So about 1.1 events per second per playing session.** `PlaybackTracker` already handles them
    (`P:src/Jellyfin.Plugin.Kids/Hosted/PlaybackTracker.cs:74-76, 117-147`).

### C.2 Is per-play-session caching enough, and is the empty fast path free?

- **The empty fast path is free [I].** `if (_authorizers.Length == 0) return Allowed` is one predictable branch. The
  claim holds only if the cache, the response registry and the header logic are skipped too.
- **Per-play-session caching is not the right design** (§B). With a per-user epoch cache, checking **every** delivery
  request, segments included, costs a `ConcurrentDictionary` read, roughly tens of nanoseconds.
- **The baseline those requests already pay [V]:**
  - `AuthorizationContext.GetAuthorization` creates a `DbContext` per authenticated request
    (`AuthorizationContext.cs:129-130`);
  - it calls `DeviceManager.GetDevices`, which runs LINQ over **all** devices, sorts and makes two `ToList()` calls
    (`DeviceManager.cs:146-166`);
  - it may write the device (`AuthorizationContext.cs:188-191`);
  - `DefaultAuthorizationHandler` calls `GetUserById` (`:62`);
  - `GetStreamingState` resolves the item twice and may call `GetPlaybackMediaSources` (`StreamingHelpers.cs:110-136`).
- **The real cost is in the authorizer, not the dispatch.** Our `GetStatusAsync` is three SQLite reads: the profile
  (`UsageService.cs:78`), the bonus (`:293`) and the stored usage (`:300`), under a semaphore (`:298`). Called per
  request, that would dominate. Called per playback start, it is fine. **The design must push decisions into memory**
  (§F.3), and the core contract should say "must be O(1) and non-blocking".

### C.3 Comparison of interception mechanisms (cost per request)

| Mechanism | Runs on | Sees anonymous routes | Can answer PlaybackInfo with 200 `NotAllowed` | Cost on unrelated requests |
|---|---|---|---|---|
| Global MVC `IAsyncActionFilter` (`Configure<MvcOptions>(o => o.Filters.Add(...))`) | Every MVC action | Yes | Yes | One virtual call plus a descriptor check per action (images and lists are most of the traffic) [I] |
| **`IApplicationModelConvention` that attaches a filter only to matched actions** | About 24 actions | Yes | Yes | **Zero.** Unmatched actions have no filter [I] |
| `IAsyncResourceFilter` rather than an action filter | Before model binding (stream actions bind 40 to 60 parameters) | Yes | Yes | Cheaper deny. And **it wraps result execution**, so it knows when a streamed response ends [I] |
| `IStartupFilter` middleware | Every HTTP request, static `/web` files included, **before** `app.Map(BaseUrl)` and routing (`Startup.cs:177, 235`) | Yes | Awkward | Path regex on every request; breaks with `BaseUrl` and route changes |
| `IAuthorizationHandler` | Every `[Authorize]` request (handlers run for all requirements) | **No** (rows 12, 13, 15, 18, 19) | No; bare 403 | One call per authorized request; must filter by endpoint metadata |
| Wrapping or replacing `IMediaSourceManager` | `GetPlaybackMediaSources` | Partly | No | Very invasive. `GetStreamingState` passes `user: null` (`StreamingHelpers.cs:129`) and skips the call when a transcode job exists (`:117-125`). Rows 16 to 19 never call it |

**Where the real cost is:** the authorizer's own I/O, then the existing authentication per request. Dispatch is not
the cost.

---

## D. Push-based revalidation

### D.1 Can the core stop a session reliably?

- **`SendPlaystateCommand(Stop)` [V].** It sends a WebSocket message to the session's controllers
  (`SessionManager.cs:1541-1556, 1325-1334`). It does not check `SupportedCommands`. With a null
  `controllingSessionId` the control check is skipped (`:1547-1555`). **A session with no open socket never gets it.**
- **How clients react:**

  | Client | Reaction to Stop | Source |
  |---|---|---|
  | jellyfin-web | Obeys: `onPlaystate` → `inputManager 'stop'` → `playbackManager.stop` | [V] `W:src/scripts/serverNotifications.js:164-166`, `playbackmanager.js:4066-4077` |
  | Wholphin | Handles Playstate, including STOP for music | [V] `Wh:…/services/MusicService.kt:440`; video per `docs/plan.md` §2.3 |
  | Android TV 12-beta2 | Obeys Stop and Pause. Ignores GoHome and Back. Logout, `ReportSessionEnded` and a policy change do **not** stop playback | [M] `B:REQUIREMENTS.md:56-71` |
  | Other clients (Swiftfin, Infuse, Kodi add-ons, Findroid, Streamyfin, DLNA renderers) | Not verified. Clients without a socket or without media control never see Stop | [I] |

- **Killing transcodes [V].** `KillTranscodingJobs(deviceId, playSessionId, deleteFiles)` matches by
  `PlaySessionId`, or by `DeviceId` when the id is empty (`S:MediaBrowser.MediaEncoding/Transcoding/TranscodeManager.cs:194-215`).
  It cancels ffmpeg and can delete files (`:218-247`).
  - **Progressive transcodes** end the response stream.
  - **HLS: the next segment request restarts ffmpeg** (`DynamicHlsController.cs:1472-1522`). It even calls
    `KillTranscodingJobs` itself first (`:1504`). **Without a delivery check on segments, a server-side kill of HLS
    is undone at once.**
- **Direct streams [V + I].** `PhysicalFileResult` (`FileStreamResponseHelpers.cs:109`) has no server-side handle in
  the core today, and what the client has buffered plays out. **But a request that is still streaming can be aborted.**
  - A resource filter (plugin) or the core keeps the `HttpContext` of each in-flight kid delivery and calls
    `HttpContext.Abort()`.
  - The client's next range request is denied.
  - The cutoff is then **the client's buffer**: tens of seconds for ExoPlayer defaults, possibly minutes for a
    browser on a low-bitrate file [I]. This needs a spike (constitution VI). It is not a proven fact.

### D.2 What happens with clients that ignore Stop

They keep playing until one of these happens:
1. their buffer drains after the transcode is killed (HLS and progressive, once segments are gated);
2. the direct-play response is aborted and the next range request is denied (direct play, if the abort spike works);
3. they finish the item and the next PlaybackInfo is denied.

Without (1) and (2), a client that ignores Stop and direct-plays plays to the end of the item [I]. A plugin also gets
nothing back from Stop: delivery is fire-and-forget (`SendMessageToSession` returns once the socket write completes).
So "re-check on the next progress event and escalate" is required, whatever design is chosen.

### D.3 Is a timer robust to clock changes, DST, restarts and many kids?

- **Clock changes [I].** `System.Threading.Timer` and `TimeProvider.CreateTimer` use relative, monotonic due times.
  When NTP or an admin moves the wall clock, a timer armed for "21:00 local" fires at the wrong wall time. It has to be
  re-armed from the wall clock every time it fires.
- **DST [I].** It is safe only if boundaries are computed as UTC instants through `TimeZoneInfo`. Ambiguous and
  invalid local times at the change must be handled. AccessSchedules use `DateTime.ToLocalTime()` hours
  (`S:Jellyfin.Data/UserEntityExtensions.cs:227-236`).
- **Restarts [I].** Timers are lost and must be rebuilt at startup from stored state, before the first request is
  served.
- **Many kids [I].** One timer per kid is cheap. It is not a scaling problem.
- **The real defect is semantic [I].** For an *allowance* the time runs out "when the remaining time has been
  *played*", not at a wall-clock instant:
  - a pause moves it later;
  - two concurrent sessions move it earlier (the tracker adds per session: `PlaybackClock` →
    `UsageService.AddPlayed`, `PlaybackTracker.cs:133-136`);
  - so a timer must be re-armed on every start, stop, pause and session-count change, which means re-deriving the
    tracker's own accounting.
- **Timers are right for:** schedule boundaries (wall-clock rules) and day rollover. And day rollover can be lazy:
  a denial carries `Until = NextDayStartsAt`.

### D.4 Is "no polling" true? Is a safety net still worth it?

- **In core:** no new polling is added. But the server already "polls" every playing session at 1 Hz through the
  synthetic tick (`SessionInfo.cs:397`). Hooking enforcement onto `OnPlaybackProgress` is free, precise to the
  second, and needs no timers. For AccessSchedules, core could evaluate `user.IsParentalScheduleAllowed()` (a loop over
  a handful of schedules) in `OnPlaybackProgress` for the session's users, then call the stop path [I]. That is more
  robust than boundary timers (§D.3).
- **In the plugin:** a safety-net sweep still makes sense. It is free because `PlaybackTracker` already runs a 30 s
  timer (`PlaybackTracker.cs:27, 77`). It catches:
  - kids made "out of time" by an admin edit while no event arrives (for example a paused session that resumes
    through SyncPlay `Unpause`);
  - lost events and exceptions;
  - denied kids whose sessions still report `NowPlayingItem`.

---

## E. Alternatives, ranked

Scores: ★★★ is best. "Upstream" means the likelihood of acceptance, based on research §1.1 and §4.2.

| # | Option | Efficiency | Completeness | Upstream | Maintenance | Notes |
|---|---|---|---|---|---|---|
| 1 | **Plugin now: an MVC convention plus a resource filter on matched actions, an in-memory deny set, tick-driven cutoff, and Stop + kill + abort** | ★★★ | ★★☆ (all authenticated delivery; anonymous only through a token or `deviceId`) | n/a | ★★☆ (action names and attributes; self-checked) | Best available today. §F |
| 2 | **Core: authorizer at the two helper choke points plus four routes, a per-user epoch cache, segments checked, an abort registry, `RevalidateAsync`** | ★★★ | ★★★ for authenticated delivery | ★★☆ (fits "core adds interfaces"; hot-path worries) | ★★★ (a supported API) | The corrected proposal. §G |
| 3 | Core: a schedule stop on the progress tick (a bug fix, no API) | ★★★ | ★★☆ (schedules only) | ★★★ (a bug: playback continues after hours) | ★★★ | Land **first**, separately |
| 4 | Core: an `IAuthorizationHandler` requirement on a new "Playback" policy | ★★☆ | ★☆☆ (cannot cover anonymous rows; bare 403; no `NotAllowed` body) | ★★☆ | ★★☆ | Inferior to 2 |
| 5 | Core: let `IsParentalScheduleAllowed` consult providers ("time's up behaves like after hours") | ★★★ (reuses the per-request check) | ★★☆ (D routes only; locks the **whole app** and login) | ★☆☆ (changes the meaning of the schedule) | ★★☆ | Consistent UX, but web shows a forced logout (`W:src/components/router/appRouter.js:197-209`) once the server emits `ParentalControl` |
| 6 | `UserPolicy` watch-time field enforced in core | ★★★ | ★★★ once built | ★☆☆ now (blocked on playback accounting, meta #136 and #121; UI/UX gate) | ★★★ | Long term, as a consumer of #136 |
| 7 | Plugin "strict lock": rewrite `AccessSchedules` (uses `DefaultAuthorizationHandler` with `ValidateParentalSchedule`) | ★★★ | ★★☆ (D routes; not anonymous streams) | n/a | ★☆☆ (mutates admin state; lockout risk if the plugin dies; constitution III) | Opt-in fallback only |
| 8 | Session-level "playback allowed" flag | ★★★ | ★☆☆ (no such field; streams are not bound to sessions, §A.3) | ★☆☆ | ★☆☆ | Needs the same binding work as 2, and gives less |
| 9 | Hooks on `IMediaSourceManager` / `IMediaSourceProvider` | ★★☆ | ★☆☆ | ★☆☆ | ★☆☆ | The provider has no user and cannot deny (`MediaBrowser.Controller/Library/IMediaSourceProvider.cs`). `GetStreamingState` passes `user: null`. Reject |
| 10 | Revoke or expire the kid's tokens | ★★★ | ★☆☆ (anonymous streams continue; B: logout does not stop ATV playback) | n/a | ★☆☆ | Every TV needs the kid's password again. Reject |

---

## F. What our plugin can do now (12.2, no core changes)

### F.1 Architecture

```
PlaybackTracker (existing, 1 Hz events) ──► KidBudgets (in memory) ──► PlaybackGate (deny set)
        ▲                                     ▲   ▲                         │       │
        │ bonus / profile / day change ───────┘   └── warm-up at start      │       │
        │                                                                  ▼       ▼
ISessionManager events               Enforcer: message → Stop → kill → abort    GateFilter (resource filter)
                                                                        on ~24 matched actions
```

### F.2 Registration and matching

In `P:src/Jellyfin.Plugin.Kids/PluginServiceRegistrator.cs`, still declarative only:

```csharp
serviceCollection.AddSingleton<PlaybackGate>();          // deny set, O(1) reads
serviceCollection.AddSingleton<DeliveryRegistry>();      // in-flight kid responses, for Abort()
serviceCollection.AddSingleton<PlaybackEnforcer>();      // message, Stop, kill, abort
serviceCollection.AddSingleton<KidsPlaybackFilter>();    // IAsyncResourceFilter
serviceCollection.AddSingleton<GateCoverage>();          // what the convention matched, for the self-check
serviceCollection.Configure<MvcOptions>(o => o.Conventions.Add(new PlaybackGateConvention()));
serviceCollection.AddHostedService<GateSelfCheck>();
```

- Plugins register after the core (`S:Emby.Server.Implementations/ApplicationHost.cs:490-492`).
- `MvcOptions` is materialised when the action descriptors are first built, so a plugin `Configure<MvcOptions>`
  composes with `AddMvc` (`ApiServiceCollectionExtensions.cs:120-163`). Jellyfin-Enhanced already relies on this
  (research §3.2).
- The convention adds `new ServiceFilterAttribute(typeof(KidsPlaybackFilter)) { IsReusable = true }` **only** to the
  matched `ActionModel`s, and records each match in a static `GateCoverage`.

**Matching rule.** Match on metadata, with names as the self-check list:
1. **Negotiation:** `MediaInfo.GetPlaybackInfo` and `MediaInfo.GetPostedPlaybackInfo`. A deny answers **200** with
   `PlaybackInfoResponse { ErrorCode = PlaybackErrorCode.NotAllowed, MediaSources = [] }`. Web shows "Playback of this
   media is not allowed." (`W:src/strings/en-us.json:1429`, `playbackmanager.js:637-645`). Wholphin shows the error
   (`Wh:…/PlaybackViewModel.kt:695-697`).
2. **Opening:** `MediaInfo.OpenLiveStream` and `UniversalAudio.GetUniversalAudioStream`. A deny answers **403**.
3. **Delivery:** every action whose `Jellyfin.Api.Attributes.ProducesFileAttribute`, or a subclass matched by type
   name through reflection on `ContentTypes`, has a content type starting with `video/`, `audio/` or
   `application/x-mpegURL`, excluding the `Subtitle` and `Trickplay` controllers. In 12.2 that is:
   - `Videos.GetVideoStream` and `GetVideoStreamByContainer`;
   - `Audio.GetAudioStream` and `GetAudioStreamByContainer`;
   - `DynamicHls.GetLiveHlsStream`, `GetMasterHlsVideoPlaylist`, `GetMasterHlsAudioPlaylist`,
     `GetVariantHlsVideoPlaylist`, `GetVariantHlsAudioPlaylist`, `GetHlsVideoSegment` and `GetHlsAudioSegment`;
   - `HlsSegment.GetHlsPlaylistLegacy`, `GetHlsVideoSegmentLegacy` and `GetHlsAudioSegmentLegacy`;
   - `Library.GetFile` and `GetDownload`;
   - `LiveTv.GetLiveRecordingFile` and `GetLiveStreamFile`;
   - `UniversalAudio.GetUniversalAudioStream` (already counted in step 2).

   That is 21 delivery actions and 24 in total (§A.1; the attribute grep is at
   `S:Jellyfin.Api/Controllers/*` under `[ProducesVideoFile]`, `[ProducesAudioFile]`, `[ProducesPlaylistFile]` and
   `ProducesFile("video/*","audio/*")`). A deny answers 403 with the JSON body `{"kids":"timeUp"}` and the header
   `X-Kids-Reason: timeUp`.
   - Don't reuse `X-Application-Error-Code: ParentalControl`. Web treats that value as a forced logout
     (`appRouter.js:197-206`).
4. **Never gate:**
   - `Playstate.*` (`Sessions/Playing*`): blocking reports would blind the tracker;
   - subtitles, trickplay, images, lyrics and attachments;
   - our own `/Kids` endpoints.

**Filter logic** (`IAsyncResourceFilter`, so it runs before model binding and wraps the result):

```csharp
public async Task OnResourceExecutionAsync(ResourceExecutingContext ctx, ResourceExecutionDelegate next)
{
    var user = ctx.HttpContext.User;
    if (user.IsInRole("Administrator")) { await next(); return; }      // admins and API keys (constitution III)
    var userId = ReadUserIdClaim(user);                                  // "Jellyfin-UserId", "N" format
    if (userId == Guid.Empty) userId = _gate.KidForDevice(ctx.HttpContext.Request.Query["deviceId"]); // best effort
    if (userId == Guid.Empty || !_gate.IsKid(userId)) { await next(); return; }
    if (_gate.TryGetDenial(userId, _time.GetUtcNow(), out var reason)) { ctx.Result = Deny(ctx, reason); return; }
    if (!IsDelivery(ctx.ActionDescriptor)) { await next(); return; }
    using var _ = _registry.Track(userId, ctx.HttpContext);             // removed when the response ends
    await next();                                                        // the file streams inside this await
}
```

- The claim names are strings copied from `Jellyfin.Api/Constants` (`Jellyfin.Api` is not on NuGet); `docs/plan.md`
  §2.2 lists them.
- `KidForDevice` is filled from `ISessionManager.Sessions` for kid sessions. It is spoofable, but web puts `deviceId`
  in direct URLs (`playbackmanager.js:2887`).

### F.3 Caching: an in-memory deny set, not a decision cache

- **`KidBudgets`:** for each kid and day, `{ LimitMs (allowance plus bonus; null means unlimited), UsedMs,
  SittingLimitMs, SittingUsedMs, Version }`.
  - It is loaded once from `UsageService.GetStatusAsync` (three reads) at startup for every kid, and again when
    something changes: a bonus (`UsageService.AddBonusAsync`, `:232`), a profile save, the day start or a report.
  - From then on `PlaybackTracker` adds each interval in memory, in the same lock where it calls
    `_usage.AddPlayed` (`PlaybackTracker.cs:133-136`).
- **`PlaybackGate`:** a `ConcurrentDictionary<Guid, Denial(reason, until)>`, plus an `IsKid` roster read (an existing
  in-memory `KidRoster`).
  - `TryGetDenial` drops expired entries lazily (`until = NextDayStartsAt`), so the day rollover needs no timer.
- **When the tracker applies an event and `UsedMs >= LimitMs`** (or the sitting limit is reached), it calls
  `Gate.Deny(userId, "timeUp", nextDayStart)` and queues `Enforcer.Stop(userId)` off the session-manager thread
  (`Task.Run`). This is the "exact-time" cutoff, within the 1 s tick.
- **When a bonus or limit changes,** the budget is reloaded. If time is left, `Gate.Allow(userId)`; otherwise
  `Deny` and `Stop`.

### F.4 The Stop, kill and abort flow

`PlaybackEnforcer.StopAsync(userId)` does this for each session in `ISessionManager.Sessions` that the kid owns and
that has a `NowPlayingItem` (the owner only, like the tracker; `PlaybackTracker.cs:117-121`):
1. **Message.** `SendMessageCommand(null, session.Id, { Header, Text = "Time's up" }, ct)` with **no** `TimeoutMs`,
   so web shows a modal (research §3.1).
2. **Wait about 5 s** so the message can be read (B uses 6 s, `B:REQUIREMENTS.md:283-284`).
3. **Stop.** `SendPlaystateCommand(null, session.Id, new PlaystateRequest { Command = Stop }, ct)`.
4. **Kill.** `KillTranscodingJobs(session.DeviceId, null, _ => true)`. Because the gate already denies segments, HLS
   cannot restart.
5. **Abort.** `DeliveryRegistry.AbortAll(userId)` calls `HttpContext.Abort()` on in-flight static responses (needs a
   spike).
6. **Live streams.** `CloseLiveStreamIfNeededAsync(session.PlayState.LiveStreamId, session.Id)` when a live stream is
   set (`SessionManager.cs:325`; B's pattern at `B:Services/PlaybackTerminator.cs:105-118`).
7. **Escalate.** On every later **non-automated** `PlaybackStart` or `PlaybackProgress` from a denied kid, repeat
   steps 3 to 5, at most once every 10 s.

### F.5 Timers

- **None for allowance or sitting cutoffs.** The tick does it (§D.3).
- **Day rollover is lazy** (`Denial.until`). A warm-up at startup re-creates the denials.
- **AccessSchedule close (plan §3.4):** on each tick, call `user.IsParentalScheduleAllowed()` for the kid. It is
  public in `Jellyfin.Data`; check that the 12.0 NuGet exposes it. If it returns false, call `Enforcer.Stop`.
  Blocking new playback is already native (`DefaultAuthorizationHandler.cs:80-85`, except anonymous streams). This
  stops a running stream without reimplementing schedules (constitution I).
- **Safety net:** the existing 30 s tracker timer sweeps denied kids whose sessions still have a `NowPlayingItem`,
  and re-sends Stop.

### F.6 Expected overhead

- Unmatched requests, which are nearly all traffic: **zero**, because no filter is attached [I].
- Matched requests from non-kids: one claims read plus a `HashSet` lookup, well under 1 µs. That is against an
  authentication cost of a `DbContext` plus a device scan (§C.2) [I].
- Kid delivery requests: the same, plus one registry add and remove per response. HLS segments add about 1 per 3 to
  6 s [I].
- Tracker: one compare per event, already at 1 Hz per session. No I/O on the event path.
- SQLite: one status load per kid at startup and per change, against today's three reads per `GET /Kids/Status`.

### F.7 Startup self-check (constitution V)

`GateSelfCheck : IHostedService` registers on `IHostApplicationLifetime.ApplicationStarted`, then:
1. it reads `IActionDescriptorCollectionProvider.ActionDescriptors.Items`, which forces the convention to run;
2. it takes the `ControllerActionDescriptor`s and compares `(ControllerName, ActionName)` with the expected list of
   24 actions in §F.2;
3. it logs a **warning** for each expected action that is missing, and at information level the actions matched only
   by attribute (new delivery routes in a newer 12.x);
4. it exposes `enforcement: { gate: "ok" | "degraded", missing: [...] }` in `GET /Kids/Info`, so Wholphin and the
   dashboard can tell a parent.

On 12.0 and 12.1 some names may differ. The attribute rule still catches renamed delivery actions, and the warning
names the gap.

### F.8 Documented limits (constitution II)

- **Anonymous streams with no token and a spoofed or missing `deviceId`** are not attributable. A kid who strips
  `ApiKey` from a URL bypasses the gate. Mitigations are Stop and the abort of earlier tracked responses.
- **DLNA renderers** stream anonymously.
- **Offline downloads.** Recommend `EnableContentDownloading = false` for kids.
- **Clients that ignore Stop *and* direct-play** run until their buffer drains, if the abort works, or until the
  item ends.

---

## G. Verdict

### G.1 Is the proposal the best and most efficient design?

**No.** Its direction is right: a core interface, plugins implement it, any deny wins, it fails open, and it uses the
existing `NotAllowed`. Its specifics have four defects:
1. **The hook points are incomplete**, and leaving out segments undoes the transcode kill (§A.4, §D.1).
2. **The cache key is not available, not trustworthy and replayable** (§B).
3. **The efficiency it buys is negligible** next to the per-request cost that already exists, and the real cost (the
   authorizer's I/O) is not addressed (§C).
4. **The exact-time timers are wrong** for usage-based limits, and fragile for wall-clock limits. The 1 Hz progress
   tick already gives an exact, robust trigger (§D.3, §D.4).

### G.2 Corrected design

1. **Interface.**
   - Keep `IPlaybackAuthorizer { string Name; ValueTask<PlaybackDecision> AuthorizeAsync(ctx, ct); }`.
   - `PlaybackAuthorizationContext { User (non-null), Item, MediaSourceId?, DeviceId, Client, Session?, Stage }`, where
     `Stage` is `Negotiate`, `Open`, `Deliver` or `Revalidate`.
   - `PlaybackDecision { Allowed, Reason, Message, ValidUntil? }`.
   - The documented contract: "must not block; must be O(1) in the common case".
2. **Placement.**
   - `MediaInfoHelper.GetPlaybackInfo` (covers GET and POST PlaybackInfo and UniversalAudio; UniversalAudio maps a deny
     to 403 instead of indexing `MediaSources[0]`).
   - `MediaInfoHelper.OpenMediaSource` (covers `LiveStreams/Open` and `autoOpenLiveStream`).
   - `StreamingHelpers.GetStreamingState` (rows 5 to 13, **segments included**).
   - Explicit calls in `Library.GetFile`, `Library.GetDownload`, `LiveTv.GetLiveStreamFile` and
     `LiveTv.GetLiveRecordingFile`.
   - Or one core resource filter keyed on a new `[MediaDelivery]` attribute, which reviewers can audit by grep.
3. **Cache.**
   - Keyed by `(UserId, epoch)`, plus `ItemId` when an authorizer declares itself content-dependent.
   - TTL = `min(ValidUntil, 5 min)`.
   - `RevalidateAsync(userId)` increments the epoch. **No `PlaySessionId`.**
4. **Anonymous delivery.**
   - No authorizer call without a user, and say so in the docs.
   - Open a separate issue: an opt-in `RequireAuthenticatedStreams` server option that resolves the TODO at
     `HlsSegmentController.cs:52-53, 124-125`. Clients already send `ApiKey`.
5. **Revalidation.**
   - `IPlaybackAuthorizationManager.RevalidateAsync(userId, ct)` evicts the cache, then for each of the user's sessions
     with a `NowPlayingItem` calls `AuthorizeAsync(Stage.Revalidate)`.
   - On a deny, the core sends `DisplayMessage` with `Message`, sends Stop, calls `KillTranscodingJobs(deviceId, null,
     _ => true)`, and **aborts tracked in-flight deliveries** for that user. The tracking is a registry kept only while
     authorizers exist.
6. **Schedules, with no timers.** Evaluate `IsParentalScheduleAllowed` in `SessionManager.OnPlaybackProgress`, real
   and automated, for the session's users, and call the same stop path. **Send this first as a standalone bug fix.**
7. **Failure policy.**
   - Fail open, log rate-limited (segment-rate calls would flood the logs), and use a short timeout (about 250 ms) on
     async paths only.
   - Disable list in `ServerConfiguration.DisabledPlaybackAuthorizers`, the same pattern as
     `LibraryOptions.DisabledMediaSegmentProviders` (`MediaSegmentManager.cs:56-57`). The dashboard UI comes in a
     later PR.
8. **Signalling.**
   - PlaybackInfo: `ErrorCode = NotAllowed`, which already exists and web already maps.
   - Delivery: 403 with `X-Application-Error-Code: PlaybackNotAllowed` and a reason in a ProblemDetails body.
   - **Never** `ParentalControl` for non-schedule reasons, because web logs the user out on it.

### G.3 Concrete changes to the proposal

1. Add the routes in §A.4: segments, variants, `live.m3u8`, GET PlaybackInfo, `LiveStreams/Open`, `Items/File`,
   `Items/Download` and the two LiveTv file routes.
2. Move the hooks from controller actions into `MediaInfoHelper` and `StreamingHelpers`.
3. Replace the per-`PlaySessionId` cache with a per-user epoch cache with `ValidUntil`.
4. Remove "HLS segments are not checked".
5. Add the stages `Open`, `Deliver` and `Revalidate`. Make `User` non-null and skip anonymous requests explicitly.
6. Add `ValidUntil` to `PlaybackDecision`.
7. Add an in-flight response abort to the revalidation flow.
8. Replace "a schedule authorizer with boundary timers" with a check on the progress tick, as a separate bug-fix PR.
9. Change the header value: `PlaybackNotAllowed`, not a free-form reason, and not `ParentalControl`.
10. Restate the cost claims:
    - "Zero cost without authorizers" holds.
    - "One call per start per authorizer" is wrong. It is a few calls per playback, plus cheap cached checks per
      segment.
    - "No polling" holds, piggybacking on the existing 1 Hz tick.
    - "Exact cutoff" is "within 1 s to decide; within the client buffer to take effect, or never for clients that
      ignore Stop and direct-play anonymously".
11. Fix UniversalAudio's unchecked `MediaSources[0]`.
12. Drop the admin UI from the first PR, and keep a config list.

### G.4 Risks maintainers would raise

- **Plugin code on the streaming hot path.** A slow or throwing authorizer degrades every segment for every user.
  Mitigations: the contract, the timeout, the cache, and per-authorizer disabling.
- **Security semantics.** Fail-open means "a broken plugin equals no limits". Reviewers will ask whether this is an
  authorization feature or a UX one. A partial guarantee (anonymous streams) may be judged worse than none unless
  documented.
- **Anonymous stream endpoints** are a long-standing, deliberate compromise (`HlsSegmentController.cs:52`). Any
  proposal that depends on identifying the user there reopens it.
- **API surface and ABI.** A new public interface in `MediaBrowser.Controller` is a plugin contract the team must
  keep across 12.x. Stage enums and context fields are hard to change later.
- **Client UX.** Only web maps `NotAllowed` with a clear message. Other clients may show generic errors. The UI/UX
  team would want consistent copy.
- **Overlap with planned core work:** playback history (meta #136) and moving playback reporting into core (#121).
  Reviewers may prefer a watch-time policy built on those.
- **Scope.** "Parental limits belong in plugins" cuts both ways. Expect a request for a jellyfin-meta proposal first.
  Two team reviews are needed on `master` (research §1.1).
- **Process.** The LLM policy has closed three parental-control PRs in 2026 (research §1.1). The PR and issue text
  must be written by a human who can defend every line.
- **Testing burden.** The streaming paths are complex (`GetDynamicSegment` locks and restarts). Maintainers will want
  integration tests for HLS restart, range requests and live TV.

---

## Executive summary

- **No, not as written.** The direction is right (a core interface plugins implement, any deny wins, fail-open,
  reuse of the existing `NotAllowed`). The hook points, cache key, cost model and timers are wrong.
- **Skipping HLS segments undoes the transcode kill.** The next segment request restarts ffmpeg
  (`DynamicHlsController.cs:1472-1522`). Segments must be checked, which costs one dictionary hit per request.
- **The hook list misses routes:**
  - GET PlaybackInfo and `LiveStreams/Open`;
  - variant playlists (a client can skip the master) and `live.m3u8`;
  - `Items/{id}/File`, which serves the raw file with range support and no download permission;
  - `Items/{id}/Download`;
  - the anonymous LiveTv `LiveStreamFiles` and `LiveRecordings` streams.
- **Put the hooks in two shared helpers, not five controller actions:**
  - `MediaInfoHelper.GetPlaybackInfo` covers PlaybackInfo and UniversalAudio. UniversalAudio must also stop indexing
    `MediaSources[0]` unchecked.
  - `StreamingHelpers.GetStreamingState` covers every stream and HLS route.
  - Four file routes need explicit calls.
- **`PlaySessionId` is a broken cache key:**
  - it doesn't exist yet when PlaybackInfo is checked;
  - it is optional and never validated, and jellyfin-web leaves it out of direct play;
  - a replayed old id can get a cached "allow".
  Use a per-user epoch with `ValidUntil` instead.
- **The cost being saved is noise.** Every request already creates a `DbContext` and scans all devices. The real cost
  is the authorizer's own I/O (ours is three SQLite reads), so decisions have to be kept in memory.
- **Drop the exact-time timers for allowances.** A pause moves the cutoff later and a second device moves it earlier.
  Checking in memory on the server's 1 Hz progress ticks is exact within a second, needs no timers, and survives
  clock changes, DST and restarts. The same approach fixes "playback continues after allowed hours" in core.
- **Stop is best-effort.** It goes only to clients with an open WebSocket. web, Wholphin and Android TV 12 obey it;
  other clients are unverified. So enforcement needs Stop, then a transcode kill, then a delivery deny, then an
  escalating repeat.
- **Direct play can probably be cut server-side.** A resource filter can keep in-flight kid responses and call
  `HttpContext.Abort()`; the client's next range request is then denied. This is unproven and needs a spike.
- **Anonymous streams are the hard limit for any design.** A kid who strips `ApiKey` from a stream URL is
  unidentifiable. Document it, and propose an opt-in "require authenticated streams" setting separately.
- **What we can do now:**
  - attach a resource filter to about 24 matched actions through an `IApplicationModelConvention`, so other requests
    pay nothing;
  - keep an in-memory deny set driven by `PlaybackTracker`;
  - stop with message, Stop, kill, abort and repeat;
  - add a startup self-check against `IActionDescriptorCollectionProvider`, reported in `/Kids/Info`.
- **Upstream order:**
  1. the schedule-stop bug fix on the progress tick;
  2. a jellyfin-meta proposal for the corrected authorizer, with this plugin as field evidence;
  3. later, a watch-time policy built on the playback-history work (meta #136).
