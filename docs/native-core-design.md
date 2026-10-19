# Native kids controls in Jellyfin core (12.2): fork or upstream?

Date: 2026-10-19. Status: study plus a working prototype. Nothing was posted, pushed or committed.

- The design starts from §G of [review-playback-authorization-hook.md](review-playback-authorization-hook.md) and uses
  the positions and LLM policy summarised in [research-jellyfin-kids-landscape.md](research-jellyfin-kids-landscape.md).
- The prototype is in `/home/aldo/Dev/aldoborrero/jellyfin-fork`, branch `kids-native` from tag `v12.2`, as an
  uncommitted working tree. The same diff is in [native-core-prototype.patch](native-core-prototype.patch)
  (26 files, +1679/−3). **It compiles and its tests pass** (§8).
- `S:` is the 12.2 server source, `.reference/jellyfin` at `v12.2` (`ca0f16e`). `W:` is jellyfin-web 12.2,
  `.reference/jellyfin-web`. Line numbers are those of 12.2 **before** the patch. `F:` is a file the prototype adds.
- **[V]** means verified in the source or by running it. **[I]** means inferred.

## Summary

| Item | What it is | Size (production code) | Upstream chance | Recommendation |
|---|---|---|---|---|
| (b) Stop playback when the AccessSchedule closes | A bug fix: one hosted service, no API change | 1 new file, ~145 lines, plus 1 line in `Startup.cs` | High | **Upstream first** |
| (a) Playback authorization extension point | An interface that plugins implement, a manager, a filter on 19 delivery actions, a check in PlaybackInfo, enforcement on the progress tick | ~1000 lines, 5 public types | Medium; needs a jellyfin-meta proposal | **Propose upstream; keep the plugin's own gate meanwhile** |
| (c) Watch-time limits in `UserPolicy` | Schema, migration, DTO, web UI and accounting | Large; spans server, web, SDK and clients | Low now; blocked on meta #136 | **Don't build now.** Pitch later as a consumer of #136 |
| (d) Kids profiles, parent lock, grants, client features | Product features | n/a | Very low | **Stay in the plugin** |

**Recommendation: don't maintain a fork.** Upstream (b), then propose (a). Keep everything else in the plugin, which
already works on stock 12.x. §5 explains why.

---

## 1. (a) The playback authorization extension point

### 1.1 The problem it solves

Today a plugin can refuse playback only by guessing controller and action names with an MVC convention (review §F).
Core has no notion of "this user may not play now", except AccessSchedules, which are checked per request by the
default policy (`S:Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs:80-85`). An extension
point makes the refusal a supported API, so it covers every client, and lets core own the stop path.

### 1.2 Public types (`MediaBrowser.Controller/Playback/`, new namespace)

The pattern follows `IMediaSegmentProvider` / `MediaSegmentManager`
(`S:MediaBrowser.Controller/MediaSegments/IMediaSegmentProvider.cs`,
`S:Jellyfin.Server.Implementations/MediaSegments/MediaSegmentManager.cs:28-51`). Plugins register an implementation
with `serviceCollection.AddSingleton<IPlaybackAuthorizer, MyAuthorizer>()` in their `IPluginServiceRegistrator`, and
the manager receives `IEnumerable<IPlaybackAuthorizer>`.

```csharp
public interface IPlaybackAuthorizer
{
    string Name { get; }
    ValueTask<PlaybackDecision> AuthorizeAsync(PlaybackAuthorizationContext context, CancellationToken cancellationToken);
}

public enum PlaybackAuthorizationStage { Negotiate, Open, Deliver, Playing }

public sealed class PlaybackAuthorizationContext   // User is never null; anonymous requests are not passed on
{
    public User User { get; }
    public PlaybackAuthorizationStage Stage { get; }
    public BaseItem? Item { get; init; }
    public string? MediaSourceId { get; init; }
    public string? DeviceId { get; init; }
    public string? Client { get; init; }
    public SessionInfo? Session { get; init; }       // set only at Playing
}

public sealed class PlaybackDecision
{
    public static PlaybackDecision Allowed { get; }
    public static PlaybackDecision Allow(DateTime validUntil);
    public static PlaybackDecision Deny(string reason, string? message = null, DateTime? validUntil = null);
    public bool IsAllowed { get; }
    public string? Reason { get; }       // machine-readable, e.g. "timeUp"
    public string? Message { get; }      // shown by clients that display server messages
    public DateTime? ValidUntil { get; } // UTC; core caches at most 5 minutes
}

public interface IPlaybackAuthorizationManager
{
    bool HasAuthorizers { get; }
    ValueTask<PlaybackDecision> AuthorizeAsync(PlaybackAuthorizationContext context, CancellationToken cancellationToken);
    Task RevalidateAsync(Guid userId, CancellationToken cancellationToken);
    Task EnforceAsync(SessionInfo session, User user, BaseItem? item, CancellationToken cancellationToken);
    IDisposable TrackDelivery(Guid userId, string? deviceId, Action abort);
}
```

The contract is in the XML docs (`F:MediaBrowser.Controller/Playback/IPlaybackAuthorizer.cs`):
- any refusal wins;
- an authorizer that throws counts as an allow (fail open);
- `AuthorizeAsync` runs on the streaming path, up to once per segment, so it must answer from memory;
- return `ValidUntil` when the answer is known to hold; call `RevalidateAsync` when it changes earlier.

Changes from the proposal reviewed in the review doc:
- the extra stages `Open`, `Deliver` and `Playing`;
- `User` is non-null;
- `ValidUntil`;
- **no `PlaySessionId`** anywhere (review §B).

**A DI cycle to document.** The manager depends on the authorizers. An authorizer must therefore not take a
constructor dependency on `IPlaybackAuthorizationManager`, either directly or through its own dependencies. A plugin
puts its `RevalidateAsync` calls in a different class, for example the hosted playback tracker. Our plugin's layout
already does this: `PlaybackTracker` and `KidBudgets` are separate.

### 1.3 The manager (`F:Jellyfin.Server.Implementations/Playback/PlaybackAuthorizationManager.cs`)

- **Empty fast path.** With no authorizers, `HasAuthorizers` is false and every call returns `Allowed` without
  touching the cache. That is the state of every server without such a plugin.
- **Cache key:** `(UserId, ItemId, Stage)`. Each entry stores the user's **generation** at the time it was computed,
  and its expiry: `min(ValidUntil of all answers, now + 5 min)`, or `now + 30 s` after an authorizer failure.
- **Revalidation.** `RevalidateAsync(userId)` increments the user's generation, which invalidates all of that user's
  entries in O(1), because a lookup ignores an entry with an old generation. It then checks each of the user's
  sessions that has a `NowPlayingItem`. This is the "per-user generation counter instead of a `PlaySessionId` cache"
  from review §B and §G.3. A computation that races with a revalidation stores an entry under the old generation,
  and that entry is never served.
- **Pruning.** When the cache passes 4096 entries, expired entries are dropped.
- **Fail open.** An exception from an authorizer is logged and treated as an allow. `OperationCanceledException`
  from the request's own token is rethrown.

### 1.4 Hook call sites

| Where | 12.2 location | Stage | Refusal |
|---|---|---|---|
| `MediaInfoHelper.GetPlaybackInfo`, which serves GET and POST `Items/{id}/PlaybackInfo` and UniversalAudio | `S:Jellyfin.Api/Helpers/MediaInfoHelper.cs:93-137`. The check is placed before `ResolvePlaybackMediaSources` (`:101`), so no source is probed and no tuner opened | Negotiate | 200 with `ErrorCode = NotAllowed` and no `MediaSources` or `PlaySessionId`. `GetPostedPlaybackInfo` already returns early on `ErrorCode` (`S:Jellyfin.Api/Controllers/MediaInfoController.cs:185-188`). Web shows "Playback of this media is not allowed." (`W:src/components/playback/playbackmanager.js:637-655`) |
| `UniversalAudio.GetUniversalAudioStream` | `S:Jellyfin.Api/Controllers/UniversalAudioController.cs:92-99` (attribute). It also stops indexing `MediaSources[0]` unchecked at `:174` | Open | 403 (below). An empty source list now gives 404 instead of an `IndexOutOfRangeException` |
| `MediaInfo.OpenLiveStream` (`LiveStreams/Open`) | `S:Jellyfin.Api/Controllers/MediaInfoController.cs:270-272` | Open | 403 |
| The 19 delivery actions: `DynamicHls` (`live.m3u8`, both masters, both variants, both `hls1` segment routes), `Videos` and `Audio` `stream[.{container}]`, the three legacy `HlsSegment` routes, `Library.GetFile` and `GetDownload`, `LiveTv.GetLiveRecordingFile` and `GetLiveStreamFile` | review §A.1 rows 5 to 19 | Deliver | 403 |
| The progress tick | `S:Emby.Server.Implementations/Session/SessionManager.cs:957` (`PlaybackProgress?.Invoke`), driven by `S:MediaBrowser.Controller/Session/SessionInfo.cs:397` | Playing | Stop path (§1.6) |

**The placement differs from review §G.2 on purpose.** The review suggested a check inside the static
`StreamingHelpers.GetStreamingState` (`S:Jellyfin.Api/Helpers/StreamingHelpers.cs:45-56`). That method takes all its
dependencies as parameters, so the check would mean a new parameter on six call sites and constructor changes in
`AudioHelper`, `DynamicHlsHelper`, `VideosController` and `DynamicHlsController`. It would still miss the four
file routes. The prototype uses the review's alternative: an attribute.
- `F:Jellyfin.Api/Attributes/PlaybackAuthorizationAttribute.cs` is a `TypeFilterAttribute` that carries a stage.
- One line, `[PlaybackAuthorization]`, goes on each of the 19 actions, and `[PlaybackAuthorization(Open)]` on two.
- The diff touches 8 controllers: 21 attribute lines, a few `using` lines and the UniversalAudio fix. `rg PlaybackAuthorization
  Jellyfin.Api/Controllers` audits it.
- **Coverage is enforced by a test.** `F:tests/Jellyfin.Api.Tests/Auth/PlaybackAuthorizationCoverageTests.cs` fails
  if any action with `[ProducesVideoFile]`, `[ProducesAudioFile]`, `[ProducesPlaylistFile]` or
  `ProducesFile("video/*"|"audio/*")` lacks the attribute. Subtitles and trickplay are excluded. So a future delivery
  route cannot be added without it, which answers the "a helper hook covers future routes" argument.

**The filter.** `F:Jellyfin.Api/Auth/PlaybackAuthorizationFilter.cs` is an `IAsyncResourceFilter`, so it runs before
model binding (the stream actions bind 40 to 60 parameters) and it wraps the execution of the result.
1. If there are no authorizers, call `next()`.
2. If there is no user claim (anonymous, or an API key without a user), call `next()`. That is a documented gap.
3. Read `itemId` and `mediaSourceId` from the route or the query, build the context and authorize.
4. On a refusal, set the 403 result.
5. On `Deliver`, wrap `next()` in `TrackDelivery(user.Id, deviceId, HttpContext.Abort)`. A `PhysicalFileResult`
   streams inside `next()`, so the registration lives exactly as long as the response.

### 1.5 Refusal code and headers

- **PlaybackInfo:** 200 with `PlaybackInfoResponse { ErrorCode = PlaybackErrorCode.NotAllowed }`
  (`S:MediaBrowser.Model/Dlna/PlaybackErrorCode.cs:11`). It already exists, clients already map it, and core never
  set it before.
- **Every other route:** 403 with `X-Application-Error-Code: PlaybackNotAllowed` and a `ProblemDetails` body:
  `{ status: 403, title: "Playback is not allowed.", detail: <Message>, reason: <Reason> }`
  (`PlaybackAuthorizationFilter.CreateRefusal`).
- **Never `ParentalControl`.** jellyfin-web treats that value as a forced logout
  (`W:src/components/router/appRouter.js:200`).

### 1.6 Revalidation and enforcement on session progress

`F:Jellyfin.Server.Implementations/Playback/PlaybackAuthorizationHost.cs` is an `IHostedService` registered next to
`DeviceAccessHost` (`S:Jellyfin.Server/Startup.cs:159`).
- It subscribes to `PlaybackStart` and `PlaybackProgress`, but only when authorizers exist.
- For the session owner, found by `session.UserId` in `e.Users`, it calls `EnforceAsync(session, user, e.Item)`.

Why the tick:
- The server raises a synthetic progress event every second for each playing, unpaused session
  (`S:MediaBrowser.Controller/Session/SessionInfo.cs:383-447`).
- The event goes through the same `OnPlaybackProgress` and `PlaybackProgress` event as real reports
  (`S:Emby.Server.Implementations/Session/SessionManager.cs:903-969`, `IsAutomated = true`).
- A refusal therefore takes effect within about 1 s, with no timers. That is robust to clock changes, DST and
  restarts, and handles pause and concurrent sessions correctly (review §D.3).
- `GetUsers(session)` already loads the users from the database on every tick (`:583-603`, and `GetUserById` has no
  cache, `S:Jellyfin.Server.Implementations/Users/UserManager.cs:123-133`). The check adds one cached dictionary
  lookup on top.

`EnforceAsync` does this for a refused session, at most once per session every 10 s (`RetryInterval`):
1. **Message.** On the first attempt only, if the decision has a `Message`: `SendMessageCommand(null, …)` with no
   `TimeoutMs`, which web shows as a modal.
2. **Stop.** `SendPlaystateCommand(null, session.Id, Stop)`. A `null` controlling session skips `AssertCanControl`
   (`S:…/SessionManager.cs:1542-1556`).
3. **Transcode kill.** `KillTranscodingJobs(session.DeviceId, null, _ => true)`
   (`S:MediaBrowser.MediaEncoding/Transcoding/TranscodeManager.cs:194`). HLS cannot restart ffmpeg, because the
   segment routes now refuse (`S:Jellyfin.Api/Controllers/DynamicHlsController.cs:1472-1522`).
4. **Abort.** Every tracked in-flight response of that user **and device** gets `HttpContext.Abort()`. The client's
   next range request is refused.

An allowed decision clears the session's retry entry. Each step is wrapped so that one failure doesn't skip the rest.

**Why the enforcement is not inside `SessionManager`.** `TranscodeManager` depends on `ISessionManager`
(`S:…/TranscodeManager.cs:76, 95`), so `SessionManager` cannot take `ITranscodeManager` without a cycle. A hosted
subscriber, the pattern of `DeviceAccessHost`, avoids that and keeps `SessionManager` (2285 lines, the busiest file
in §5.2) untouched.

### 1.7 Known gaps in the prototype, for a real PR

- **Authorizer timeout.** There is no timeout yet (review §G.2 item 7: about 250 ms on async paths), and no
  rate-limited logging. A broken authorizer logs once per 30 s per cache key.
- **No disable list.** `ServerConfiguration.DisabledPlaybackAuthorizers`, mirroring
  `LibraryOptions.DisabledMediaSegmentProviders` (`S:…/MediaSegmentManager.cs:56-57`), is left for a follow-up.
- **Database load per gated request.** The filter loads the `User` with `GetUserById`, one database query per gated
  request, but only when authorizers exist. A cheaper version would let the cache key on the user id before it loads
  the user.
- **UniversalAudio loses the reason.** Its refusal carries a generic reason, because `GetPlaybackInfo` returns only
  an `ErrorCode`.
- **Abort needs a spike.** Its effect on real clients (web `<video>`, ExoPlayer) is unproven (review §D.1).
  Constitution VI requires a spike before anything depends on it.
- **Anonymous delivery is unchanged.** It still bypasses everything. An opt-in `RequireAuthenticatedStreams` option is
  a separate discussion (review §G.2 item 4).

---

## 2. (b) Native enforcement of AccessSchedules for running playback

### 2.1 The bug

- AccessSchedules are checked at login (`S:Jellyfin.Server.Implementations/Users/UserManager.cs:643-649`) and on
  every `[Authorize]` request (`S:…/DefaultAuthorizationHandler.cs:80-85`).
- **Nothing stops playback that is already running.** The schedule check in `GetPlayAccess` is commented out
  (`S:MediaBrowser.Controller/Entities/BaseItem.cs:1135`).
- Direct play keeps going: `Videos/{id}/stream?static=true` is anonymous (`S:Jellyfin.Api/Controllers/VideosController.cs:314-318`),
  so it is never re-authorized.
- Progressive transcodes keep going too.
- HLS stalls at the next segment, but its ffmpeg stays alive. The client's reports are now refused, yet the server's
  own 1 s tick keeps pinging the job: `TranscodeManager.OnPlaybackProgress` → `PingTranscodingJob`
  (`S:…/TranscodeManager.cs:95-96, 710-716`).
- **The client's own `Sessions/Playing/Stopped` report is refused after hours.** `PlaystateController` is
  `[Authorize]` (`S:Jellyfin.Api/Controllers/PlaystateController.cs:26`), so its transcode kill (`:254`) never runs.
  The server-side state lingers until the 5-minute idle check (`S:…/SessionManager.cs:641-674`).

So the only signal the server gets after the boundary is its own synthetic tick, and that is where the fix goes.

### 2.2 The patch (exactly what is in the prototype)

New file `Jellyfin.Server.Implementations/Users/ParentalScheduleHost.cs`, a sibling of `DeviceAccessHost.cs`
(abridged; the full file is in the patch):

```csharp
public sealed class ParentalScheduleHost : IHostedService
{
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, DateTime> _lastStopAttempts = new(StringComparer.Ordinal);
    // ctor: ISessionManager, ITranscodeManager, ILogger<ParentalScheduleHost>

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackProgress;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;   // includes the 1 s automated ticks
        _sessionManager.PlaybackStopped += OnPlaybackStopped;     // forget the session
        return Task.CompletedTask;
    }

    internal async Task CheckSessionAsync(PlaybackProgressEventArgs e)
    {
        var session = e.Session;
        if (session is null || session.UserId.IsEmpty()) { return; }

        // The owner's schedule applies, as it does for the owner's token on every request.
        var user = e.Users?.Find(u => u.Id.Equals(session.UserId));
        if (user is null || user.HasPermission(PermissionKind.IsAdministrator) || user.IsParentalScheduleAllowed())
        {
            _lastStopAttempts.TryRemove(session.Id, out _);
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastStopAttempts.TryGetValue(session.Id, out var last) && now - last < RetryInterval) { return; }
        _lastStopAttempts[session.Id] = now;

        await _sessionManager.SendPlaystateCommand(null, session.Id,
            new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(session.DeviceId))
        {
            await _transcodeManager.KillTranscodingJobs(session.DeviceId, null, _ => true).ConfigureAwait(false);
        }
    }

    // async void handler with try/catch around CheckSessionAsync, copied from DeviceAccessHost.OnUserUpdated
}
```

Plus one line in `Jellyfin.Server/Startup.cs` after `:159`:

```csharp
            services.AddHostedService<ParentalScheduleHost>();
```

Design choices a reviewer will ask about:
- **Why the tick, not boundary timers?** The tick already exists and runs only while something plays. It needs no
  time-zone or DST arithmetic: `IsParentalScheduleAllowed` (`S:Jellyfin.Data/UserEntityExtensions.cs:148-152, 227-236`)
  is reused as is, and that loop costs nothing next to the database query the tick already makes.
- **Why are admins skipped?** To match `DefaultAuthorizationHandler.cs:73-78`, which lets admins past the schedule.
  Login does not skip them (`UserManager.cs:643`). Reviewers may prefer "no skip"; it is one condition either way.
- **Why only the owner?** Requests are authorized with the owner's token. A guest in `AdditionalUsers` outside
  their window doesn't stop the owner's playback. A test pins this behaviour.
- **Why retry every 10 s?** A client that ignores Stop would otherwise get one command per second. Repeating is still
  needed, because the client may restart playback from its buffer or from a cached URL.
- **Why the transcode kill?** For the reasons in §2.1: after hours nothing else kills the job. HLS cannot restart it,
  because segments are `[Authorize]` and refused outside the schedule.
- **What it does not fix:** a client that ignores Stop *and* direct-plays still plays out its buffer, or to the end
  of the item. Fixing that needs the abort from (a).

Tests: `F:tests/Jellyfin.Server.Implementations.Tests/Users/ParentalScheduleHostTests.cs` (6 tests, Moq, the same
style as `DeviceAccessHostTests`):
- outside the schedule: Stop and kill;
- no schedule: nothing;
- administrator: nothing;
- additional user outside the schedule: nothing;
- five ticks: one Stop;
- two sessions: two Stops.

The tests build the "outside" schedule as a single weekday three days from today, from 0 to 24 h, so they don't
depend on the clock.

**A related follow-up, not in the patch:** web shows nothing useful when the schedule refuses a request (jf#17740,
web#4447; two competing fixes are open, jf#17741 and jf#17772). Review and test those; don't send a third.

---

## 3. (c) Native watch-time limits in `UserPolicy` (optional, later)

### 3.1 Schema

Add nullable columns to the `User` entity (`S:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs`),
next to `MaxActiveSessions` (`:160`) and `RemoteClientBitrateLimit` (`:273`), which are the precedent for plain policy
columns:

| Column | Type | Meaning |
|---|---|---|
| `MaxDailyPlaybackMinutes` | `int?` | `null` means unlimited. Playback time per day, summed across sessions |
| `PlaybackDayStartHour` | `int?` | 0 to 23, server local time, the same clock as AccessSchedules. `null` means midnight |

**Bonus time is not a policy.** It is a dated grant, so it belongs in a small table
`UserPlaybackAllowanceAdjustments (Id, UserId, Day (date), Minutes, CreatedAt, CreatedByUserId)`, with a foreign key
to `Users` and cascade delete. It gets an endpoint, not a `UserPolicy` field.

**Migration.** Use `dotnet ef migrations add AddPlaybackLimits --project src/Jellyfin.Database/Jellyfin.Database.Providers.Sqlite --output-dir Migrations -- --migration-provider Jellyfin-SQLite`
(`S:src/Jellyfin.Database/readme.md`).
- The newest 12.2 migration is `20261002210711_MakeOwnerIdIndexesPartial`.
- SQLite is the only provider in 12.2, but the readme requires a migration for every provider that exists when the
  PR lands.
- The model configuration is `S:src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/UserConfiguration.cs`.
- Plain nullable columns need no data migration. Full system backups include them automatically, because they back
  up the `Users` table.

### 3.2 API

- `S:MediaBrowser.Model/Users/UserPolicy.cs` gets `int? MaxDailyPlaybackMinutes` and `int? PlaybackDayStartHour`.
- `UserManager.GetUserDto` (`S:Jellyfin.Server.Implementations/Users/UserManager.cs:452-525`, policy block from
  `:487`) maps them, and so does `UpdatePolicyAsync` (`:848-927`, next to `MaxActiveSessions` at `:878`).
  `UpdateUserAsync` copies scalar columns through `SetValues` (`:227`).
- **Partial updates are destructive.** `UpdatePolicyAsync` overwrites every field, so a client that does not know the
  new fields resets them to `null`. An older admin UI saving a user would silently remove the limit. This is the
  existing behaviour for every policy field, but reviewers will raise it for a field that matters to parents. Either
  document it, or keep the limits in their own endpoint (`GET/PUT /Users/{id}/PlaybackLimits`), which avoids both the
  problem and the `UserPolicy` churn.
- New endpoints:
  - `GET /Users/{id}/PlaybackAllowance`, which returns `{ day, usedSeconds, limitSeconds, bonusSeconds,
    remainingSeconds, nextResetAt }`, readable by the user under `Policies.IgnoreParentalControl`;
  - `POST /Users/{id}/PlaybackAllowance/Bonus`, admin only.
- Enforcement: a built-in `IPlaybackAuthorizer` from (a), named for example `PlaybackAllowanceAuthorizer`, with reason
  `allowanceExceeded`, `ValidUntil = now + remaining / playing sessions`, and `RevalidateAsync` on bonus or policy
  changes. That is the reason to land (a) first: (c) then needs no new hook points.
- After `Jellyfin.Api` changes, the OpenAPI document (generated at build time) and `@jellyfin/sdk`
  (web 12.2 pins `1.0.0`, `W:package.json:89`) must be regenerated before web can use typed fields.

### 3.3 Dashboard (jellyfin-web)

- `W:src/apps/dashboard/features/users/components/ParentalControl.tsx` (508 lines) already edits AccessSchedules.
  - It loads `user.Policy.AccessSchedules` at `:199`.
  - It saves with `updateUserPolicy` at `:56-60`.
  - It renders the "Access Schedule" section at `:475-490`.
- A "Daily playback limit" section (minutes plus day-start hour) fits under it, with strings next to
  `HeaderAccessSchedule` in `W:src/strings/en-us.json:393-394`.
- The bonus button and today's usage belong on the user list or on the user's page.
- **UI/UX approval is required** before any web PR (web discussion #7059), and the admin pages are being reshuffled
  now (web#8636, web#8645).

### 3.4 The playback-history work (jellyfin-meta discussion #136)

- Shadowghost's #136 proposes playback history and watch statistics in core, with an implementation on his
  `playback-history` branch.
- #121 discusses moving the Playback Reporting plugin into core.
- I did not re-read either discussion for this study; the facts come from the research doc §1.3.

**How they relate.** A daily limit needs "played seconds per user per day", which is exactly what a history table
records.
- If (c) builds its own accumulator, it duplicates #136, and maintainers will say so.
- The right order is: #136 lands, then the allowance authorizer sums today's history rows at startup and adds live
  ticks in memory, the way our plugin's `PlaybackTracker` does.

So (c) should be pitched as a consumer of #136: a jellyfin-meta comment on #136 plus Fider #699 and #2266, citing the
plugin as field evidence. It should not be a standalone PR.

---

## 4. (d) What stays in a plugin even with a fork

| Feature | Why it stays out of core |
|---|---|
| **Kid profile UI settings**: home sections, picks, search and autoplay toggles, the sitting limit's UX | They are product and UX policy for one client family (Wholphin kids mode). Core has no kids concept, and joshuaboniface opposes Netflix-style sub-profiles (research §1.2). A per-user preference blob in core would be a new client-settings API that only our clients read. `DisplayPreferences` already exists per client for that purpose |
| **Household parent lock and grants** | The lock is a secret that authorises adult actions on a device that is already signed in. Core removed Easy PIN for security (jf#9820). A lock that is not an authentication factor is plausible upstream only after the UI/UX "Who's Watching" design (web discussion #7059, meta #90). Our PBKDF2 format comes from Wholphin (`P:…/ParentLock/Verifier.cs`), and grants are bound to a device and kept in memory. All of that is our contract, not Jellyfin's |
| **The allowance policy itself**: limits, bonuses, sitting limits, warnings at 5 and 1 minutes | Until (c) lands on #136, this is plugin logic. With (a) in core it becomes an `IPlaybackAuthorizer` in the plugin, and the plugin's MVC convention gate and self-check (review §F.2, §F.7) can be deleted |
| **Client-specific features**: the `/Kids` contract, `GET /Kids/Info` detection, the device lock and offline outbox, the web `PreplayIntercept` layer | They are versioned for our clients (constitution V). Core only supports "the official Jellyfin API"; the Android TV maintainers said so (research §1.1) |

**What moves into core:** only the mechanism, meaning "may this user play now" plus the stop path, and the schedule
bug fix. The plugin keeps the policy and the UX. That is the "core adds interfaces, plugins implement" pattern
(research §1.1), and it keeps a fork, if one ever exists, small.

---

## 5. (e) The cost of a fork

### 5.1 Release cadence [V, `gh api repos/jellyfin/jellyfin/releases`]

- Majors land about once a year: 10.10.0 on 2024-10-26, 10.11.0 on 2025-10-20, 12.0 on 2026-09-08. Each follows
  three to four months of release candidates (12.0 had rc1 to rc7, from 2026-06-21 to 2026-08-31).
- Minor and patch releases come every one to three weeks after a major:
  - 10.11.x: 12 releases in 7.5 months (10.11.1 on 2025-10-27 to 10.11.11 on 2026-06-06);
  - 12.x: 12.1 one week after 12.0, and 12.2 three weeks later.
- **A fork must therefore be rebased about twice a month**, and rebuilt for every distribution it ships to. The
  household servers update themselves (Docker images, distribution packages), so a fork also needs its own image and
  repository, and that cuts users off from the official images.

### 5.2 Conflict hotspots [V, commits on `master` touching each file since 2025-10-19]

| File the patch touches | Commits in the last year | Risk for our patch |
|---|---|---|
| `Emby.Server.Implementations/Session/SessionManager.cs` | 32 | **Avoided.** The prototype doesn't touch it, and enforcement lives in hosted services |
| `Jellyfin.Server.Implementations/Users/UserManager.cs` | 19 | Only (c) touches it, in the DTO and policy mapping: a frequent conflict |
| `Emby.Server.Implementations/ApplicationHost.cs` | 16 | One line, in a registration list that changes often: a trivial conflict |
| `Jellyfin.Api/Controllers/DynamicHlsController.cs` | 14 | One attribute line on each of 7 actions; conflicts only if those signatures change |
| `Jellyfin.Api/Controllers/VideosController.cs` | 13 | 2 attribute lines |
| `Jellyfin.Api/Controllers/LibraryController.cs` | 11 | 2 attribute lines |
| `Jellyfin.Api/Controllers/UniversalAudioController.cs` | 6 | One 10-line block next to `GetPlaybackInfo` |
| `Jellyfin.Server/Startup.cs` | 6 | 2 lines in the hosted-service list |
| `Jellyfin.Api/Helpers/MediaInfoHelper.cs` | 4 | One constructor parameter and a 20-line block |
| `MediaBrowser.Model/Users/UserPolicy.cs` | 1 | Only (c) |

Other fork costs:
- **The EF migrations in (c) are the worst fork cost.** Every upstream migration after ours changes the model
  snapshot (`JellyfinDbModelSnapshot.cs`), so the fork has to regenerate its migration on every rebase.
- **Migrating back is hard.** A database migrated by the fork cannot go back to the official images unless upstream
  ships an identical migration under the same id. That is a one-way door, and a strong reason **never to fork for (c)**.

### 5.3 Keeping a patch small

- Add one extension point (a), with new files in new folders (`MediaBrowser.Controller/Playback`,
  `Jellyfin.Server.Implementations/Playback`) and **one-line edits** everywhere else: attributes, two registrations,
  one helper block.
- Keep `SessionManager` untouched. Subscribe from hosted services instead (the `DeviceAccessHost` pattern).
- Make no schema changes. All policy and state stay in the plugin's own SQLite database.
- Keep the coverage test, so that a rebase that adds a delivery route fails loudly instead of leaking.
- With all that, the prototype's non-test diff to existing files is **66 added lines in 11 files**. The rest is new
  files. A rebase would mostly be mechanical.

### 5.4 Recommendation

**Don't fork. Upstream (b), propose (a), and keep everything else in the plugin.**

1. The plugin already enforces on stock 12.x: tick-driven cutoff, Stop and kill, and the gate convention from review
   §F. A fork adds the abort and a supported API, but that is not worth a rebase twice a month, a separate image, and
   households on a non-official server.
2. (b) is a real bug with no issue filed, and the patch is small and self-contained. It helps every Jellyfin user,
   and our plugin gets it for free.
3. (a) needs maintainer buy-in on its API surface, which makes it a jellyfin-meta discussion first. If it lands, the
   plugin replaces its MVC convention with an `IPlaybackAuthorizer` and deletes the self-check. If it doesn't land,
   nothing is lost.
4. A private fork is justified only for a measured need that the plugin cannot meet. The only candidate is the
   direct-play abort, and that should first be spiked inside the plugin, where a resource filter can do the same
   thing (review §F.2).

---

## 6. (f) A sequence of small upstream PRs

The LLM policy (<https://jellyfin.org/docs/general/contributing/llm-policies/>) applies to every step:
- the author must understand and be able to explain every change;
- the author writes the issue, PR and discussion text themselves, and no LLM writes it;
- no `.claude` files or other tooling configuration go into commits;
- commits are authored by the human, with no AI trailers.

The prototype here was written with an LLM. Treat it as a study aid: read it line by line, re-derive each choice from
this document, and preferably retype the small PRs. Three parental-control PRs were closed under this policy in 2026
(research §1.1).

| # | Where | What | Size | Gate |
|---|---|---|---|---|
| 0 | jellyfin/jellyfin **issue** | "Playback that is already running continues after the user's access schedule ends". Give reproduction steps (direct play, then HLS) and the 12.2 references from §2.1 | text | none |
| 1 | jellyfin/jellyfin **PR** | `ParentalScheduleHost` plus its registration plus 6 tests (§2.2). The PR text explains the tick, the admin skip, the owner-only rule, the 10 s retry and the transcode kill | ~145 + 150 lines | 2 team reviews (`master`) |
| 2 | jellyfin/jellyfin issue threads jf#17740 / web#4447 | Test and review jf#17741 and jf#17772, which make schedule refusals visible to clients. Don't open a third PR | comments | none |
| 3 | jellyfin-meta **discussion** | "A playback authorization extension point". Cover the interface (§1.2), the stages, the refusal signalling (§1.5), the cost model (review §C), the anonymous-stream limit, and our plugin and the session-limit plugins as consumers. Ask the server team about placement (attribute versus helper) | text | go-ahead from the server team |
| 4 | PR | Types plus manager plus `MediaInfoHelper` Negotiate check plus the UniversalAudio fix. No delivery filter yet. PlaybackInfo refusals already cover web and most clients | ~450 lines | after #3 |
| 5 | PR | The `[PlaybackAuthorization]` filter on the 21 actions plus the coverage test | ~200 lines | after #4 |
| 6 | PR | `PlaybackAuthorizationHost`, the `EnforceAsync` stop path and `RevalidateAsync`. Optionally fold #1 into a built-in schedule authorizer so there is a single stop path | ~250 lines | after #5 |
| 7 | PR | The in-flight abort (`TrackDelivery`), with a spike report on web and ExoPlayer behaviour attached | ~80 lines | after #6 and the spike |
| 8 | PR | Authorizer timeout plus `DisabledPlaybackAuthorizers` in server configuration; dashboard UI later, through UI/UX | small | after #6 |
| 9 | jellyfin-meta #136 comment plus Fider #699 / #2266 | Daily limits as a consumer of playback history (§3) | text | after #136 lands |

Each PR stands alone: it builds, it has tests, and it can be reverted on its own. #1 doesn't depend on any of the
later ones.

---

## 7. How the plugin changes if (a) and (b) land

- Add `KidsPlaybackAuthorizer : IPlaybackAuthorizer`. It reads the in-memory `KidBudgets` (review §F.3):
  - non-kids get `Allowed`;
  - kids with time left get `Allow(now + remaining / playingSessions)`;
  - otherwise `Deny("timeUp", "Time's up", nextDayStart)`.
- `PlaybackTracker` and the bonus or profile save call `RevalidateAsync(kidId)`. The DI cycle rule from §1.2 means
  `KidBudgets` must not depend on the manager.
- Delete the plugin's MVC convention, the deny set, the abort registry and the gate self-check. Delete its
  schedule-close stop, which (b) replaces.
- Keep the `/Kids` contract, profiles, the parent lock, grants, the dashboard pages, the warnings and the sitting
  limit.
- Version gating: the authorizer can only load on servers that have the interface. Ship it as a separate assembly or
  behind a reflection check, so the plugin still loads on 12.0 to 12.2 (constitution V).

---

## 8. Prototype: what was built and proven

**Disk.** `df -h /` showed 307 GB free at the start (the "about 1.4 GB" figure was stale), so the full build was
possible. The build and the NuGet restore used about 4 GB. The restore downloaded packages from nuget.org: the cache
under `~/.nuget/packages` grew from 2.7 GB to 4.6 GB. The source itself was cloned from the local `.reference/jellyfin`.
Nothing was deleted.

**Toolchain.** .NET SDK 10.0.401 from the plugin's Nix devshell (`nix develop`).

| Check | Result |
|---|---|
| `dotnet build -c Debug` (`AnalysisMode=AllEnabledByDefault` plus the Jellyfin analyzers, warnings as errors) | Builds. No warnings in any touched or new file (the 215 warnings in the log are pre-existing ones in `MediaBrowser.Controller`, `MediaBrowser.Model`, `Emby.Naming` and `Emby.Server.Implementations`) |
| `dotnet build -c Release` | 0 warnings, 0 errors |
| `Jellyfin.Api.Tests` | 166/166 passed (4 filter tests, 5 coverage tests and 1 `MediaInfoHelper` refusal test are new) |
| `Jellyfin.Server.Implementations.Tests` | 1092 passed, 12 skipped (Windows-only), 0 failed. Includes the 12 new manager tests and the 6 new `ParentalScheduleHost` tests |
| `Jellyfin.Server.Integration.Tests` (boots the real server with the new DI wiring) | 129 passed, 3 skipped, 0 failed. It needed `LD_LIBRARY_PATH` pointing at Nix `fontconfig` for SkiaSharp. That is an environment issue: without it, 124 tests fail in server start-up, unrelated to the patch |

**What this proves:**
- the design compiles against 12.2 and passes the house analyzers;
- the server starts with the new singleton, the two hosted services and the `TypeFilter` resolution;
- the existing tests are unaffected;
- the decision logic works: any refusal wins, fail open, caching, generation revalidation, the retry interval, and
  device-scoped abort;
- the schedule stop works on synthetic ticks for the right users;
- every media-producing action carries the attribute.

**What it does not prove:**
- no end-to-end run with a real client against a running server;
- no real HLS or range-request stream was refused mid-play;
- the effect of `HttpContext.Abort()` on players is unmeasured;
- there is no load measurement of the filter on segment traffic.

These need the dev server loop, which is a natural next spike.

**Working tree.** `/home/aldo/Dev/aldoborrero/jellyfin-fork`, branch `kids-native`, nothing committed. New files are
marked intent-to-add (`git add -N`) so that `git diff` includes them. The push URL of `origin` is set to `no-push`.
The patch file is that `git diff`.
