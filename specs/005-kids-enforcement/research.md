# Research: Kids enforcement on every client

Sources: `S:` is `.reference/jellyfin` at `v12.2` (`ca0f16e`); `W:` is jellyfin-web 12.2; review is
[docs/review-playback-authorization-hook.md](../../docs/review-playback-authorization-hook.md); repro is
[docs/repro-schedule-playback.md](../../docs/repro-schedule-playback.md).

## R1. Spike: does cutting a direct stream stop real players, and how fast? (BLOCKING)

**Status**: measured 2026-10-19 on the dev server (Jellyfin 12.2 from the devshell, plugin 0.0.5.0 with the gate filter
and `DeliveryRegistry`, all on 127.0.0.1). A temporary admin-only endpoint denied the kid and called
`DeliveryRegistry.AbortAll`; it sent **no** Stop and no message, so every client behaved like one that ignores Stop.
Scripts and raw samples were kept outside the repo (job scratch directory).

**Media**: `Spike Direct (2026).mp4`, 12 min, 1280×720 H.264 at ~4 Mb/s plus AAC, `+faststart` (direct play in
Chromium); `Test Pattern (2026).mkv` (MKV, so jellyfin-web plays it through HLS).

| Client | What it did | Result |
|---|---|---|
| curl, no WebSocket (the repro's `curl-direct` shape): one long `Videos/{id}/stream?static=true&ApiKey=` download at 520 kB/s, a 1 MiB range request and a PlaybackInfo every 10 s | Denied 40 s in | `AbortAll` cut 1 delivery. The long download ended with a connection reset (curl exit 56) **4.0 s** after the deny, having read the bytes already in its socket buffer. Every later range request: **403** `PlaybackNotAllowed`; every PlaybackInfo: 200 `NotAllowed` with no sources |
| jellyfin-web 12.2, headless Chromium 155, direct play of the MP4 | Denied 45 s in | The browser kept playing from what it had already received: its buffer end still grew from 64 s to 104 s of media after the abort (kernel socket buffers on loopback plus Chromium's own media buffer). It read the reset at **+35.0 s**, retried with a range request (**403**), re-negotiated PlaybackInfo (`NotAllowed`) and showed the dialog "Playback Error — Playback of this media is not allowed." at **+36.4 s**, paused. No sign-out; the page stayed on `#/video` |
| jellyfin-web 12.2, headless Chromium, HLS (MKV remuxed to fMP4 segments) | Denied 40 s in | No delivery was in flight to cut (segments are short requests). The next segment got **403** at **+2.3 s**; hls.js reset the video element at +3.1 s and the picture froze with **no message** (as in the repro). |
| ExoPlayer (Wholphin, Android TV) | — | Not measured: no Android device or emulator in this environment. Its HTTP pattern (one open-ended range request, a new range request after an I/O error, then a player error) is the curl client's, so it is expected to stop within its buffer (ExoPlayer's default is up to 50 s) and show its playback error; to be confirmed on a device |

Incidental: when two static-stream requests for the same item arrive in the same millisecond, Jellyfin 12.2 sometimes
answers one with 500 (`.jellyfin-transcode` "being used by another process"). It happened once without any refusal
involved; it is a core race, not the plugin.

**Decision**: keep the cut in the chain. It is what ends playback on clients that ignore Stop or have no WebSocket,
and the 403 on the client's next request turns into a clear "not allowed" error in jellyfin-web. Its delay is the
client's buffer, not the server's decision: seconds for a paced client, about 35 s for Chromium at 4 Mb/s here,
roughly "the client's read-ahead". So the chain sends the message and Stop first (jellyfin-web obeys Stop in under 1 s,
repro §3.1), and the cut is the backstop. For HLS the segment refusal stops playback within one segment, but silently,
which is another reason the message goes first. Documented in the contract's limits.

## R2. Where to hook: a convention-attached resource filter

**Decision**: `IApplicationModelConvention` registered through `services.Configure<MvcOptions>(o =>
o.Conventions.Add(...))` in `PluginServiceRegistrator`, adding `ServiceFilterAttribute(typeof(KidsPlaybackFilter))
{ IsReusable = true }` to matched `ActionModel`s only. The filter is an `IAsyncResourceFilter`.

**Rationale**: plugins register before `AddMvc` builds its options (`S:Emby.Server.Implementations/ApplicationHost.cs:490-492`,
`S:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:111-163`), so the convention composes with the core's.
Unmatched actions have no filter, so they pay nothing (review §C.3). A resource filter runs before model binding (the
stream actions bind 40–60 parameters) and wraps the result execution, so it can keep the `HttpContext` of a streaming
response until it ends, which the abort needs.

**Alternatives**: a global action filter (runs on every action); `IStartupFilter` middleware (path regexes, every
request, breaks with `BaseUrl`); `IAuthorizationHandler` (cannot see anonymous stream routes, bare 403); wrapping
`IMediaSourceManager` (invasive, `user: null` in `GetStreamingState`). All rejected (review §C.3, §E).

## R3. Which actions are matched

**Decision**: match `(ControllerName, ActionName)` against this list, and also any action carrying Jellyfin's
`ProducesVideoFile`, `ProducesAudioFile` or `ProducesPlaylistFile` attribute, or `ProducesFile` with a `video/` or
`audio/` content type (matched by attribute type name through reflection, since `Jellyfin.Api` is not on NuGet),
excluding the `Subtitle`, `Trickplay` and `VideoAttachments` controllers and the plugin's own controllers.

| Kind | Controller | Actions |
|---|---|---|
| Negotiate | `MediaInfo` | `GetPlaybackInfo`, `GetPostedPlaybackInfo` |
| Open | `MediaInfo` | `OpenLiveStream` |
| Deliver | `UniversalAudio` | `GetUniversalAudioStream` |
| Deliver | `DynamicHls` | `GetLiveHlsStream`, `GetMasterHlsVideoPlaylist`, `GetMasterHlsAudioPlaylist`, `GetVariantHlsVideoPlaylist`, `GetVariantHlsAudioPlaylist`, `GetHlsVideoSegment`, `GetHlsAudioSegment` |
| Deliver | `Videos` | `GetVideoStream`, `GetVideoStreamByContainer` |
| Deliver | `Audio` | `GetAudioStream`, `GetAudioStreamByContainer` |
| Deliver | `HlsSegment` | `GetHlsPlaylistLegacy`, `GetHlsVideoSegmentLegacy`, `GetHlsAudioSegmentLegacy` |
| Deliver | `Library` | `GetFile`, `GetDownload` |
| Deliver | `LiveTv` | `GetLiveRecordingFile`, `GetLiveStreamFile` |

That is 22 action methods (GET and HEAD share a method). The attribute rule catches a renamed delivery action in a
later 12.x; the name list is what the self-check reports as missing (R10). Verified against
`S:Jellyfin.Api/Controllers/*` (`rg 'Produces(Video|Audio|Playlist)File|ProducesFile\('`).

**Never matched**: `Playstate.*` (blocking reports would blind the tracker), subtitles, trickplay, images, lyrics,
attachments, `MediaInfo.GetBitrateTestBytes`, `/Kids`.

## R4. Who the caller is

**Decision**: read `HttpContext.User`: `IsInRole("Administrator")` passes (admins and API keys both get that role,
`S:Jellyfin.Api/Auth/CustomAuthenticationHandler.cs:54-57`); the user id is the `Jellyfin-UserId` claim
(`S:Jellyfin.Api/Constants/InternalClaimTypes.cs`, "N" format), copied as a string constant because `Jellyfin.Api`
is not a NuGet package. When there is no user (anonymous stream route without `ApiKey`), the `deviceId` query value is
matched against the current sessions owned by kids (`ISessionManager.Sessions`, in memory). The filter never calls
`IAuthorizationContext`, which creates a `DbContext` (review §C.2).

**Rationale**: authentication runs on every request, anonymous routes included, so a valid `?ApiKey=` fills
`HttpContext.User` there too (review §A.1). Confirmed by the spike (R1): `Videos/{id}/stream?ApiKey=` from the kid was
refused by claim.

## R5. How to refuse

**Decision**:
- Negotiate: `200` with `PlaybackInfoResponse { ErrorCode = PlaybackErrorCode.NotAllowed }` (its `MediaSources` is
  already empty). jellyfin-web shows "Playback of this media is not allowed." (`W:playbackmanager.js:637-655`).
- Everything else: `403` with `X-Application-Error-Code: PlaybackNotAllowed`, `X-Kids-Reason: <reason>` and a
  ProblemDetails body (`title`, `reason`, `until`).
- Never `ParentalControl`, which makes jellyfin-web sign the user out (`W:src/components/router/appRouter.js:197-209`),
  and never 401.

## R6. Deciding: an in-memory deny set evaluated on the tick

**Decision**: `EnforcementState` keeps, per kid: a `KidBudget` (day, limit = allowance + bonus or unlimited, used =
the stored+pending total at load plus every interval the tracker counts since), a `Sitting`, and a cached `User` for
the schedule. `PlaybackTracker.Handle` passes each kid event and its counted intervals to it inside the tracker's lock;
`Evaluate(kid, now)` returns the denial, if any, in O(1) with no I/O. The filter reads the same state.

- Budgets load at startup (one `UsageService.GetStatusAsync` per kid, three SQLite reads) and reload in the background
  on a bonus, a report, a profile save, a day-start change, or the first event of a new day. Between a reload's read
  and its swap, up to one tick may be counted twice or missed; accepted (≤ 1 s).
- A new day is detected from `DayClock` on each evaluation: the budget then restarts at 0 used with the profile's
  allowance and no bonus, which is exactly the new day's state, and a reload is queued.
- No per-kid timers (review §D.3): pauses move the cutoff later and a second device moves it earlier, and the tick
  sees both.

**Alternatives**: timers armed at "now + remaining" (wrong under pause and concurrency, review §D.3); calling
`GetStatusAsync` per request (three reads per segment, review §C.2). Rejected.

## R7. The sitting, per kid across devices (clarifications Q1, Q2)

**Decision**: `Sitting` per kid: `UsedMs`, `LastPlayedAt` (end of the last counted interval), `ReachedAt`. On each
interval: if the gap since `LastPlayedAt` is at least the break, the sitting restarts at 0; then the interval is added.
The kid is denied (`sessionLimit`) while `UsedMs ≥ limit` and `now < LastPlayedAt + break`; when the break has passed
with nothing counted, the sitting resets lazily. Paused time produces no intervals, so a pause of at least the break
counts as a break. Playback the kid continues after the limit (a client ignoring Stop) moves `LastPlayedAt`, so the
break starts again, which matches "a break is time with nothing playing".

The sitting is counted whether or not enforcement is on, so Status can report it (FR-020a). It lives in memory only; a
restart is a break. `SittingBreakMinutes` is plugin XML, 1–240, default 15; out-of-range values read as 15.

## R8. Allowed hours

**Decision**: the denial uses Jellyfin's own rule: `user.IsParentalScheduleAllowed()` (`S:Jellyfin.Data/UserEntityExtensions.cs:148-152`,
public in the 12.0.0 `Jellyfin.Data` package), on a `User` entity cached per kid. The cache is filled at startup and on
first use, replaced on `IUserManager.OnUserUpdated` (raised by `UpdatePolicyAsync`, `S:…/UserManager.cs:925-927`), and
refreshed every 60 s by the sweep. `GetUserById` opens a `DbContext` (`S:…/UserManager.cs:123-133`), so it is never
called per request or per tick.

**Warnings** need the time the window closes, which Jellyfin cannot tell (its per-schedule rule is private and uses
`UtcNow`). `ScheduleClock.ClosesWithin(schedules, now, zone, horizon)` evaluates the same formula (server local time,
`DynamicDayOfWeek` days, inclusive `StartHour`/`EndHour` in fractional hours) at `now + 300 s` and `now + 60 s`. It is
used only to send notices, never to deny (constitution I).

**Rationale for enforcing at all**: stock 12.2 keeps direct play running after the window and keeps HLS ffmpeg alive
~10 minutes (repro §3). Core refuses new `[Authorize]` requests after hours, but not the anonymous stream routes; the
filter refuses those too for a known kid.

## R9. Stopping: send once, end server-side, escalate only on real reports

**Decision**: `PlaybackEnforcer.Enforce(kid, denial)` runs, off the session-manager thread, for each session the kid
owns that has a `NowPlayingItem`:
1. `SendMessageCommand(null, id, { Header, Text })` with no `TimeoutMs` (modal on web, research §3.1);
2. wait 5 s (Q5: stop at once, after the message), then evaluate again: if a bonus, the switch or a new day lifted the
   denial meanwhile, stop nothing (found by dev-check: without it, a bonus given during the wait was followed by a
   stop that also cut the kid's new stream);
3. `SendPlaystateCommand(null, id, Stop)`;
4. `KillTranscodingJobs(session.DeviceId, null, _ => true)`;
5. `CloseLiveStream(session.PlayState.LiveStreamId)` when set;
6. `DeliveryRegistry.AbortAll(kid)`;
7. `OnPlaybackStopped(new PlaybackStopInfo { Item, ItemId, SessionId, MediaSourceId, PositionTicks =
   LastPlaybackCheckInPositionTicks })`, exactly what `SessionManager.CheckForIdlePlayback` does
   (`S:Emby.Server.Implementations/Session/SessionManager.cs:639-674`). This clears `NowPlayingItem`, so the 1 s
   synthetic tick, the transcode pings and repeat stops end. The core refuses the client's own `Stopped` report and
   `DELETE ActiveEncodings` after hours (repro §3.1 B), so the server has to do it.

Each session has a record keyed by denial (reason + until): `Stopped` once. A later **real** `PlaybackStart` or
`PlaybackProgress` from that session while denied triggers steps 3–7 again, at most once per 10 s per session. The
30 s tracker timer doubles as the sweep for denied kids whose sessions still show `NowPlayingItem` (same 10 s limit).

**Logging**: one information line per denial ("alice: time's up") and per session stopped; repeats, refusals and
aborts at debug (FR-013a). The repro's fork logged 118 lines in 300 s; this logs about 2 per kid per denial.

**Warnings**: `SendMessageCommand` with `TimeoutMs = 10000` (a toast on web) at 300 s and 60 s left, once per
(session, limit kind, threshold, limit instance), where the instance is the day for the allowance, the sitting's start
for the sitting, and the window's close for the schedule. Only thresholds still ahead when playback starts are sent.

## R10. Startup self-check

**Decision**: `EnforcementService` registers on `IHostApplicationLifetime.ApplicationStarted`, reads
`IActionDescriptorCollectionProvider.ActionDescriptors` (which forces the convention to have run), compares the
`GateCoverage` matches with R3's list, logs a warning per missing action and an information line with the count, and
exposes `Enforcement { Gate: ok | degraded | pending, Missing: [...] }` in `/Kids/Info`. `pending` means the check has
not run yet.

## R11. Contract

**Decision**: additive to API v1 (constitution V): Info gains `Enforcement` and the capabilities `enforcement` and
`sitting`; Status gains `Enforced`, `DeniedReason`, `DeniedUntil`, `SittingLimitSeconds`, `SittingUsedSeconds`,
`SittingRemainingSeconds`, `BreakRemainingSeconds`. Whether enforcement is on is household data, so it is in Status
(signed in), not Info (constitution IV). See [contracts/kids-api-v1-enforcement.md](contracts/kids-api-v1-enforcement.md).

## R12. Dev loop on a free port

**Decision**: the dev scripts read `JELLYFIN_KIDS_PORT` (default 8096): `dev-jellyfin.sh` writes it as
`InternalHttpPort` in `network.xml`; `dev-provision.sh` and `dev-check.sh` derive `JELLYFIN_URL` from it; the
process-compose readiness probe uses it. Run with `JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<free> dev -p
<free> -t=false` so it does not collide with another dev loop.

## Validation (2026-10-19, dev server, jellyfin-web 12.2 in headless Chromium, enforcement on, page obeying Stop)

| Scenario | Observed |
|---|---|
| Direct play, alice with 5 minutes left | Toast "Screen time — 5 minutes left" at once; "1 minute left" at 244 s; dialog "Time's up — Screen time is over for today." at 304 s; Playstate Stop at 309 s (the 5 s grace); the video element was gone 0.7 s later. Pressing play again: "Playback of this media is not allowed." |
| HLS (MKV remuxed), alice with 55 s left | Toast "1 minute left"; dialog at 59.7 s; the next segment answered 403 at 60.4 s (the picture froze behind the dialog); Stop at 64.7 s; no ffmpeg left running |
| Bedtime, window ending 150 s after the start | "Bedtime — 3 minutes left" toast at start, "1 minute left" at 89.7 s, dialog "Bedtime — It is past your allowed hours." at 149.7 s, Stop at 154.7 s. Pressing play again: "Unable to find a valid media source to play.", because Jellyfin's own schedule check answers PlaybackInfo 403 before the plugin's filter runs |
| `scripts/dev-check.sh` with `DEV_CHECK_SLOW=1` | All 167 checks pass, including the live cutoff of a socket-less client (cut, later ranges 403, playback ended on the server, 3 info log lines), bedtime at the boundary (cut 11 s after the window closed), the sitting across two devices with a 1-minute break, and the switch |

**jellyfin-web quirk**: on a `NotAllowed` PlaybackInfo, web 12.2 shows its "not allowed" alert and then a second,
generic "There was an error processing the request" alert: `validatePlaybackInfoResult` alerts and returns a rejected
promise, which `onPlaybackRejection` alerts again (`W:src/components/playback/playbackmanager.js:637-655, 2436-2449,
3014-3050`). It happens for any `NotAllowed`, not only the plugin's.

**SC-006 (latency)**: 200 requests each, median (p90), enforcement off → on: guarded stream as a kid 5.25 (6.14) →
4.94 (5.75) ms; guarded stream as admin 4.81 (5.54) → 4.74 (5.62) ms; unguarded item read as a kid 8.04 (9.77) →
8.12 (9.94) ms. The differences are within run-to-run noise (< 0.4 ms).

## Review pass (2026-10-19, .NET skills)

Run with `dotnet:csharp-expert`, `dotnet-aspnetcore:dotnet-webapi`, `dotnet-test:test-anti-patterns`,
`dotnet-test:test-smell-detection`, `dotnet-test:test-gap-analysis` and the `dotnet-test:test-quality-auditor` agent.
Each finding was checked against the code before acting on it.

- **Fixed (C#)**:
  - The fire-and-forget stop chain is wrapped as a whole (`StopSafeAsync`), not only its Jellyfin calls.
  - `DeliveryRegistry.AbortAll` skips a response that ended between the lookup and the abort.
  - The 5- and 1-minute warning check no longer copies `AccessSchedules` on every 1 s tick when the kid has no
    schedule.
  - `TryGetDenial` is annotated `[MaybeNullWhen(false)]`.
- **Fixed (ASP.NET Core)**: refusals are always `application/problem+json`. Jellyfin honours the browser's `Accept`
  header and also registers XML and CSS output formatters, so a media request's `Accept` value could change the body's
  format. `dev-check` sends `Accept: video/mp4` to cover it.
- **Fixed (tests)**:
  - A 200 ms real delay is replaced by a seam (`PlaybackEnforcer.Wait`) that the test releases.
  - Four new tests cover gaps, and each one fails against its mutation:
    - enforcement switched off during the message wait;
    - time-up reported before the sitting limit;
    - bedtime warnings;
    - an abort on a response that has already ended.
  - The test-quality auditor's points are applied:
    - the `EnforcementOff_DoesNothing` sweep now runs with a session present;
    - the stop-chain tests compare the exact list of calls;
    - the renamed-action coverage check compares the exact set of names;
    - two assertions that only checked presence now check exact values;
    - a weak `KidsInfo` assertion is replaced.
- **Skipped**:
  - A shutdown cancellation token for the 5 s chain: at worst one extra Stop goes out while the server stops.
  - A lock-removal stress test: data races are not deterministically observable here.
  - The low test smells: system time-zone data (same as `DayClockTests`), one test that walks pending, degraded and
    ok, and one test with eight field assertions.
- **Results**:
  - `dotnet build` (Debug and Release, no incremental build): 0 warnings.
  - `dotnet test`: 396 pass.
  - `scripts/dev-check.sh` with `DEV_CHECK_SLOW=1`: 168/168.
