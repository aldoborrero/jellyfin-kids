# Jellyfin Kids: plugin plan

Status: planning input for spec-kit (`/speckit.specify`, `/speckit.plan`). Nothing here is implemented.

- Target: Jellyfin server **12.x** (12.0 to 12.2; 12.2 released 2026-10-05), `net10.0`, `targetAbi 12.0.0.0`.
- Client: Wholphin kids mode (`Wholphin/.worktree/kids-mode`, specs `001-kids-mode`). This plan supersedes the
  prior study in `specs/002-kids-server-plugin/` where they disagree (see [Corrections](#corrections-to-the-prior-study)).
- All citations are against `.reference/jellyfin` at tag `v12.2` (commit `ca0f16e`) and `.reference/jellyfin-web` at
  tag `v12.2`, unless stated otherwise. Paths are relative to those checkouts.

## Decisions (2026-10-17)

- **Licence: MIT.** Code may be copied from MIT plugins (Trakt, Reports, session-provisioning), with their notices.
  Code from GPL-3.0 plugins (Playback Reporting, Jellyfin-Enhanced, intro-skipper) can be studied for patterns but
  not copied.
- **Route prefix: `/Kids`**, matching the plugin name; the grant header is `X-Kids-Grant`.
- **Parent lock: one per household.** No per-kid override in the MVP.
- **Day boundary: configurable** "day starts at" hour in server local time (default midnight).

These settle open questions 2, 3 (the hour; per-kid time zones stay out of scope), 7 and 8 below.

## 1. Goals and non-goals

**Goals**

1. Kids are configured **once per household** on the server, and every Wholphin TV picks the configuration up.
2. **Enforcement happens on the server** wherever the server can do it, so that it applies to every client,
   not only Wholphin:
   - content restrictions (rating, unrated, libraries, tags) and allowed hours, through Jellyfin's own `UserPolicy`;
   - a **daily screen-time allowance** counted from server playback events across all clients;
   - **stopping playback** when the allowance runs out, and **refusing new playback** until a grown-up adds time
     or the next day starts.
3. A household **parent lock** that the server verifies. The server issues short-lived grants, and a kid's token
   alone can never change rules or add time.
4. An admin **dashboard page** for setup: mark kids, set allowances, set allowed hours, and apply a recommended
   content policy in one click.
5. Reuse Jellyfin APIs instead of duplicating them. The plugin adds only what Jellyfin lacks: the kid flag and UX
   settings, the parent lock, the allowance, and the enforcement glue.

**Non-goals**

- Reimplementing rating, tag, library or allowed-hours filtering. Jellyfin already enforces these for every client.
- A separate kids web client, or injecting JavaScript into jellyfin-web for kid sessions.
- Supporting Jellyfin 10.11 in v1. Wholphin offers kids mode only with the plugin; on a server without it, Wholphin
  works as a normal Jellyfin client with no kids features (constitution V, amended 2026-10-19).
- Guaranteeing enforcement against a technically capable attacker who holds a kid token. Some stream endpoints are
  anonymous by design (§3.3), so the goal is "every normal client is stopped".

**Server first, and what stays on the device**

| Concern | Where | Why |
|---|---|---|
| Kid flag, home sections, picks, search and autoplay options | Server (plugin), with a cache on the device | Household-wide |
| Parent lock verifier and lock type | Server, with an offline copy cached on the device | Household-wide, rate-limited on the server |
| Daily allowance, usage, bonus time | Server | Must count every client |
| Allowed hours (bedtime) | Server, native `AccessSchedules` | Already enforced on login and every `[Authorize]` request |
| Content restrictions | Server, native `UserPolicy` | Already enforced |
| **Device lock** ("this TV is in kids mode, leaving needs the parent gate") | **Device** | Must work offline. Only the device knows its own UI state |
| **Offline cache and outbox** | **Device** | Kids mode must keep working while the server is unreachable |
| **Per-session timer** ("30 minutes, then stop") | **Device** counts it; the **server** stores the per-kid limit (`SessionLimitMinutes`, spec `004-kids-session-limit`) | It is a per-sitting UX feature, and the daily allowance covers the cross-device need. Each TV counts its own sitting. The server may later offer a "session cap" per kid that it enforces like the allowance |
| CHALLENGE lock type (arithmetic) | Device only | It is not a secret, so it cannot back a server grant (§5.3) |

## 2. Leverage map: what Jellyfin already gives a plugin

### 2.1 Plugin base, loading and DI

| Need | Mechanism | Source |
|---|---|---|
| Plugin class and XML config | `BasePlugin<TConfiguration>`: `Configuration`, `SaveConfiguration()`, `ConfigurationChanged`, `DataFolderPath` | `MediaBrowser.Common/Plugins/BasePluginOfT.cs:41-57, 90, 125-131` |
| Data folder | `DataFolderPath = <datadir>/plugins/<AssemblyName>`. `Version` is still null when the constructor runs, so the `_<Version>` suffix branch never applies to the base constructor. It sits beside the install folder and looks like a pre-manifest plugin install, so **the plugin no longer uses it** (spec 014): `kids.db` lives in `<DataPath>/kids/` | `BasePluginOfT.cs:50-57`; spec `014-kids-db-location` research R1 |
| Config file location | `<datadir>/plugins/configurations/<Assembly>.xml`, under the **data** dir, not the config dir | `Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs:58,61` |
| DI registration | `IPluginServiceRegistrator.RegisterServices(IServiceCollection, IServerApplicationHost)`. It is created with `Activator.CreateInstance` before DI is built, and skipped unless the plugin is enabled and supported. An exception marks the plugin Malfunctioned | `Emby.Server.Implementations/Plugins/PluginManager.cs:206-236` |
| Same container as ASP.NET | Plugin services go into the **same** `IServiceCollection` that `Startup.ConfigureServices` fills (generic host `ConfigureServices(appHost.Init)`). So a plugin can register `IHostedService`, `IAuthorizationHandler`, `IStartupFilter` and `Configure<MvcOptions>` | `Emby.Server.Implementations/ApplicationHost.cs:490-492`; `Jellyfin.Server/Program.cs:180-181`; `Jellyfin.Server/Startup.cs:64-80` |
| Controllers | Any `ControllerBase` in a plugin assembly is added as an MVC application part automatically | `ApplicationHost.cs:1010-1022`; `Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:135-163` |
| Scheduled tasks | `IScheduledTask` is discovered automatically through `GetExports` (no registration needed) | `ApplicationHost.cs:424`; `MediaBrowser.Model/Tasks/IScheduledTask.cs:17-48` |
| Hosted services | Not auto-discovered. Register them with `services.AddHostedService<T>()` in the registrator. `IServerEntryPoint` no longer exists | `PluginManager.cs:206-236` |
| Event consumers | `IEventManager.PublishAsync` resolves `IEventConsumer<T>` from a **new DI scope** per event. Register them, for example `services.AddScoped<IEventConsumer<UserDeletedEventArgs>, X>()` | `Jellyfin.Server.Implementations/Events/EventManager.cs:46-52`; examples in `Events/EventingServiceCollectionExtensions.cs:34-42` |
| Dependency isolation | Each plugin has its own `PluginLoadContext`. Assemblies found in the plugin's `deps.json` load privately, and anything else falls back to the server's copy. **Never ship `Jellyfin.*`, `MediaBrowser.*`, EF Core or `Microsoft.Extensions.*` DLLs** | `Emby.Server.Implementations/Plugins/PluginLoadContext.cs:23-31` |

### 2.2 Authentication, authorization and caller identity

- **Claims** set by `CustomAuthenticationHandler` (`Jellyfin.Api/Auth/CustomAuthenticationHandler.cs:53-71`):
  - `ClaimTypes.Role`: `"Administrator"` or `"User"`. **API keys get `Administrator`** (lines 54-57). This resolves the
    open question in the prior study.
  - `Jellyfin-UserId`: `Guid.ToString("N")`, which is `Guid.Empty` for API keys.
  - `Jellyfin-DeviceId`, `Jellyfin-Device`, `Jellyfin-Client`, `Jellyfin-Version`, `Jellyfin-Token`.
  - `Jellyfin-IsApiKey`: `bool.ToString(InvariantCulture)`, so the value is `"True"` or `"False"`.
  - The names are constants in `Jellyfin.Api/Constants/InternalClaimTypes.cs` and `UserRoles.cs`. `Jellyfin.Api` is
    not on NuGet, so copy the strings.
- **A better source of identity for plugins**: `MediaBrowser.Controller.Net.IAuthorizationContext.GetAuthorizationInfo(HttpContext)`
  returns `AuthorizationInfo { User, UserId, DeviceId, Device, Client, Version, Token, IsApiKey, IsAuthenticated }`
  (`MediaBrowser.Controller/Net/IAuthorizationContext.cs:16`, `AuthorizationInfo.cs:22-61`). It is in NuGet and is
  usable from middleware and filters too.
- **Policies** (`MediaBrowser.Common/Api/Policies.cs`; wiring in `ApiServiceCollectionExtensions.cs:53-89`):
  - The default `[Authorize]` uses `DefaultAuthorizationRequirement`, and **fails non-admins outside their
    `AccessSchedules`** (`Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs:81-85`).
  - API keys are "unrestricted" (lines 51-56) and admins succeed (lines 74-78).
  - Remote users without `EnableRemoteAccess` fail (lines 67-71).
  - `Policies.IgnoreParentalControl` (`"IgnoreParentalControl"`) is the same requirement **without** the schedule
    check (`ApiServiceCollectionExtensions.cs:76`). **Use it for the plugin endpoints a kid or a parent must reach
    outside allowed hours**: `Parent/Verify`, `WatchTime`, `Profile` GET and `Status`.
  - `Policies.RequiresElevation` means the Administrator role claim, which includes API keys (`:86-89`).
  - There is no fallback policy, so actions without `[Authorize]` are anonymous. Still mark `Info` `[AllowAnonymous]`
    explicitly.
- **Token carriers in 12.x** (`Jellyfin.Server.Implementations/Security/AuthorizationContext.cs:84-110, 229-259`):
  - `EnableLegacyAuthorization` defaults to false (`MediaBrowser.Model/Configuration/ServerConfiguration.cs:290`), and the
    migration `Jellyfin.Server/Migrations/Routines/20260531160000_DisableLegacyAuthorization.cs:27` forces it off on
    upgraded installs.
  - **Accepted**: `Authorization: MediaBrowser Client=…, Device=…, DeviceId=…, Version=…, Token=…`, and the
    **`?ApiKey=`** query parameter (line 103-106, not gated by the legacy flag).
  - **Rejected** unless the legacy flag is on: the `X-Emby-Token`, `X-MediaBrowser-Token` and `X-Emby-Authorization`
    headers, the `?api_key=` query parameter, and the `Emby` scheme name.
- **Disabled users**: a token of a user with `IsDisabled` throws `SecurityException` on every request
  (`Emby.Server.Implementations/HttpServer/Security/AuthService.cs:35-38`).
- **A plugin can add enforcement to the pipeline.** All three options below land in the shared container (§2.1):
  - an extra `IAuthorizationHandler`: ASP.NET calls every registered handler, and `context.Fail()` wins;
  - a global MVC filter through `services.Configure<MvcOptions>(o => o.Filters.Add(...))`, which runs for anonymous
    actions too;
  - an `IStartupFilter` middleware.

  Jellyfin-Enhanced (12.x) already ships global `IAsyncActionFilter`s and `IStartupFilter`s this way (§5). No
  surveyed plugin registers its own `IAuthorizationHandler`. Feature 4 still starts with a short spike against the
  playback actions.

### 2.3 Sessions and playback

`MediaBrowser.Controller/Session/ISessionManager.cs`:

- **Events** (lines 27-59): `PlaybackStart`, `PlaybackProgress`, `PlaybackStopped`, `SessionStarted`, `SessionEnded`,
  `SessionActivity`, `SessionControllerConnected`, `CapabilitiesChanged`. The same playback args are **also
  published through `IEventManager`** (`Emby.Server.Implementations/Session/SessionManager.cs:826, 959, 1171`), so an
  `IEventConsumer<PlaybackProgressEventArgs>` and the C# event are equivalent.
- **`PlaybackProgressEventArgs`** (`MediaBrowser.Controller/Library/PlaybackProgressEventArgs.cs:24-46`):
  - `Users` (a list, which includes `AdditionalUsers`), `Item`, `PlaybackPositionTicks`, `IsPaused`, `IsAutomated`;
  - `DeviceId`, `DeviceName`, `ClientName`, `PlaySessionId`, `Session`.
  - `PlaybackStopEventArgs` adds `PlayedToCompletion`.
- **The server synthesises progress every second** while a session is playing and not paused (`IsAutomated = true`,
  `MediaBrowser.Controller/Session/SessionInfo.cs:383-450`). It keeps doing this until the client reports again, or
  until the idle check runs (every 5 min, sessions with no check-in for more than 5 min,
  `SessionManager.cs:607-672`). Counting must therefore:
  - use wall-clock deltas between events, clamped to about 2× the expected cadence;
  - count only non-paused intervals;
  - ignore automated ticks that come after the last real check-in by more than about 60 s, to avoid counting a
    dead client for up to 5 minutes.
- **Commands**:
  - `SendPlaystateCommand(string? controllingSessionId, string sessionId, PlaystateRequest, CancellationToken)`
    (`SessionManager.cs:1542-1559`). If `controllingSessionId` is null or empty, the control check is skipped.
    `PlaystateRequest` is `{ Command, SeekPositionTicks?, ControllingUserId? }`
    (`MediaBrowser.Model/Session/PlaystateRequest.cs`), and `PlaystateCommand.Stop` and `Pause` exist.
    **The server itself stops paused sessions this way**, with `SendPlaystateCommand(session.Id, session.Id, Stop)`
    (`SessionManager.cs:688-697`). Copy that pattern.
  - `SendMessageCommand` sends a `GeneralCommand` named `DisplayMessage` with `Header`, `Text` and `TimeoutMs`
    arguments (`SessionManager.cs:1289-1307`).
  - Messages reach a session only through its `SessionControllers`, which means the WebSocket. A client without an
    open socket never sees them (`SessionManager.cs:1325-1334`).
  - `SessionMessageType` and `GeneralCommandType` are fixed enums, so a plugin **cannot define custom socket
    messages**. Clients must poll plugin endpoints, or react to `Playstate` and `UserUpdated`.
- **Transcodes**: `ITranscodeManager.KillTranscodingJobs(deviceId, playSessionId, deleteFiles)`
  (`MediaBrowser.Controller/MediaEncoding/ITranscodeManager.cs:43`) ends server-side transcodes for a device, which
  is a hard stop for transcoded and HLS playback.
- **Wholphin today**:
  - it posts capabilities with `SupportsMediaControl = true` and the `DisplayMessage` and `SendString` commands
    (`app/.../services/ServerEventListener.kt:66-75`);
  - it shows `DisplayMessage` as a toast (lines 89-118);
  - it applies `UserUpdated` socket pushes (line 123);
  - its player handles `PlaystateMessage` (`ui/playback/PlaybackViewModel.kt:1481`).

  So server-side stop already works for Wholphin with no app change.

### 2.4 Users and policy

- **`IUserManager`** (`MediaBrowser.Controller/Library/IUserManager.cs`):
  - lookups: `GetUsers()`, `GetUsersIds()`, `GetUserById`, `GetUserByName`, `GetUserDto(user)` (whose `.Policy` is the
    current `UserPolicy`);
  - writes: `UpdatePolicyAsync(Guid, UserPolicy)`, `UpdateConfigurationAsync`;
  - the event `OnUserUpdated`.
- **`UpdatePolicyAsync` replaces the whole policy** (`Jellyfin.Server.Implementations/Users/UserManager.cs:848-927`).
  Always read `GetUserDto(user).Policy`, modify it, and write it back. Unlike the HTTP endpoint it does **not**:
  - check "last admin" or "admins can't be disabled";
  - revoke tokens on disable (`Jellyfin.Api/Controllers/UserController.cs:428-454`).
- **Each policy update publishes `UserUpdatedEventArgs`** (`UserManager.cs:926`), which `UserUpdatedNotifier` pushes
  to the user's sessions over the WebSocket (`Jellyfin.Server.Implementations/Events/Consumers/Users/UserUpdatedNotifier.cs:16-37`).
- **User events** (`Jellyfin.Data/Events/Users/`), published from `UserManager.cs:365, 415, 448, 688, 926`:
  - `UserCreatedEventArgs`, `UserDeletedEventArgs`, `UserUpdatedEventArgs`, `UserPasswordChangedEventArgs`,
    `UserLockedOutEventArgs`;
  - all are `GenericEventArgs<User>`.
  - Use `IEventConsumer<UserDeletedEventArgs>` to drop orphaned kid entries.
- **`UserPolicy` fields that matter** (`MediaBrowser.Model/Users/UserPolicy.cs:74-197`):
  - `IsAdministrator`, `IsHidden`, `IsDisabled`;
  - `MaxParentalRating` (an int score), `MaxParentalSubRating`, `BlockUnratedItems` (`UnratedItem[]`);
  - `AllowedTags`, `BlockedTags`;
  - `EnableAllFolders` with `EnabledFolders` (`Guid[]`), `BlockedMediaFolders`;
  - `AccessSchedules`;
  - `EnableUserPreferenceAccess`, `EnableRemoteControlOfOtherUsers`, `EnableSharedDeviceControl`, `EnableRemoteAccess`;
  - `EnableLiveTvAccess`, `EnableLiveTvManagement`;
  - `EnableMediaPlayback`;
  - `EnableContentDeletion`, `EnableContentDownloading`, `EnableCollectionManagement`, `EnableSubtitleManagement`,
    `EnablePublicSharing`;
  - `MaxActiveSessions`, `SyncPlayAccess`;
  - `AuthenticationProviderId` and `PasswordResetProviderId` are `[Required]` over HTTP.
- **`AccessSchedule`** has `DayOfWeek` (`DynamicDayOfWeek`: Sunday…Saturday, `Everyday = 7`, `Weekday = 8`,
  `Weekend = 9`), `StartHour` and `EndHour` (doubles), and `UserId`
  (`src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/AccessSchedule.cs:20-60`).
  - It passes if the list is empty, or if any entry matches **server local time**, with both ends inclusive
    (`Jellyfin.Data/UserEntityExtensions.cs:148-152, 227-236`).
  - It is enforced at login (`UserManager.cs:643-649`) and on every default-policy request (§2.2).
- **`EnableMediaPlayback` is not enforcement.** It only sets `BaseItemDto.PlayAccess = None`
  (`MediaBrowser.Controller/Entities/BaseItem.cs:1128-1141`, `Emby.Server.Implementations/Dto/DtoService.cs:692`) and
  blocks remote `SendPlayCommand` (`SessionManager.cs:1394`). Neither `PlaybackInfo` nor the stream endpoints check it.
  It is still a cheap, native **hint** that well-behaved clients may honour.

### 2.5 Library, collections, playlists and user data

- **`ILibraryManager`**: `GetItemById(Guid)`, `GetItemById<T>(Guid, User?)` and
  `GetItemList(InternalItemsQuery)` (`MediaBrowser.Controller/Library/ILibraryManager.cs:201-229, 669`). Use them to
  validate picked ids (`BoxSet` or `Playlist`) and to list candidates for the config page.
- **Visibility**:
  - `BaseItem.IsVisible(User)`.
  - A **BoxSet** is visible when its children pass parental checks (`MediaBrowser.Controller/Entities/Movies/BoxSet.cs:155-175`).
  - A **Playlist** is visible to a kid only if it has `OpenAccess`, is owned by the kid, or **is shared with the kid**
    (`MediaBrowser.Controller/Playlists/Playlist.cs:234-260`).
  - Helper: when a parent picks a playlist, offer to share it with the kid through
    `IPlaylistManager.AddUserToShares` (`MediaBrowser.Controller/Playlists/IPlaylistManager.cs:48`).
- **`IUserDataManager`**: `GetUserData`, `SaveUserData` and the `UserDataSaved` event
  (`MediaBrowser.Controller/Library/IUserDataManager.cs:20-39`). Not needed for v1. It could feed "activity history"
  later.

### 2.6 Storage

| Option | Facts | Use for |
|---|---|---|
| Plugin XML (`BasePlugin<T>.Configuration`) | Admins write it through `POST /Plugins/{id}/Configuration` (RequiresElevation). Saving replaces the whole object. It lives in `<datadir>/plugins/configurations` | Small admin-owned settings: defaults, policy template, feature flags |
| JSON or SQLite under `DataFolderPath`, or `<datadir>/data/kids/` | Written only by the plugin. `Microsoft.Data.Sqlite` 10.0.11 already ships with the server (`Directory.Packages.props:35`), so reference it with `ExcludeAssets=runtime` and nothing extra is shipped | Kid profiles (with revisions), the verifier, daily usage and bonus ledgers |
| Server EF database (`JellyfinDbContext`) | There is no plugin extension point: the model is built only from the server assembly (`src/Jellyfin.Database/Jellyfin.Database.Implementations/JellyfinDbContext.cs:332`), so a plugin cannot add tables | **Do not use** |
| `DisplayPreferences.CustomPrefs` or `UserConfiguration` | The kid's own token can write them | **Never** use for anything trust-relevant |

- **Backups**: the 12.x system backup covers only:
  - top-level `*.xml` and `*.json` in the config dir, plus `config/users` and `config/ScheduledTasks`;
  - the root folder;
  - `data/collections`, `data/playlists`, `data/ScheduledTasks`, and optionally `data/subtitles` and `data/trickplay`
    (`Jellyfin.Server.Implementations/FullSystemBackup/BackupService.cs:407-443`).

  **The plugins folder, including plugin XML, is not backed up**, and neither is `data/<anything else>`. Offer
  export and import of plugin state from the config page (phase 4).
- **Recommendation**: a single SQLite file `kids.db` in `<DataPath>/kids/` (under `DataFolderPath` until 0.0.9.0; spec 014
  moves it), with `WAL` and a single writer, holding:
  - `kid_profile(user_id PK, revision, json)`;
  - `parent_lock(id PK CHECK (id = 1), lock_type, verifier, changed_at)` for the household lock (schema v2, feature 2;
    a single row enforced by the table, instead of a generic `household(key, value)`);
  - (schema v3, feature 3) `usage_day(user_id, day, used_ms, PK(user_id, day))`, where `day` is the `yyyy-MM-dd` day
    under the household's "day starts at" hour and `used_ms` only ever grows;
  - `bonus(id PK, user_id, day, minutes CHECK 1–240, granted_by, created_at)`;
  - `reported_segment(user_id, segment_id, day, started_at, seconds, received_at, PK(user_id, segment_id))`, which
    makes offline reports idempotent.

  Keep the XML config for admin defaults only, so that a dashboard save never overwrites profile writes made through
  the API.

### 2.7 Dashboard configuration pages (jellyfin-web 12.2)

- **Still the legacy mechanism, nothing new**:
  - `IHasWebPages.GetPages()` returns `PluginPageInfo { Name, DisplayName, EmbeddedResourcePath, EnableInMainMenu,
    MenuSection, MenuIcon }` (`MediaBrowser.Model/Plugins/PluginPageInfo.cs:11-36`);
  - the server serves them from `GET /web/ConfigurationPage?name=` (**anonymous**,
    `Jellyfin.Api/Controllers/DashboardController.cs:73-94`) and lists them from `GET /web/ConfigurationPages`
    (admin, line 49-50).
- **In jellyfin-web 12.2**:
  - the React dashboard route `configurationpage` renders `<ServerContentPage view='/web/configurationpage'>`
    (`src/apps/dashboard/routes/routes.tsx:46-50`), behind `ConnectionRequired level='admin'`;
  - that component fetches the HTML and hands it to the legacy `viewManager` (`src/components/ServerContentPage.tsx:32-44`);
  - a `data-controller="__plugin/<name>"` attribute is resolved to `/web/configurationpage?name=<name>` and
    **imported as an ES module** (`src/components/viewContainer.js:9-28`);
  - `window.ApiClient` and `window.Dashboard` are still global (`src/lib/jellyfin-apiclient/ServerConnections.js:95`,
    `src/utils/dashboard.js:267`).
  - `EnableInMainMenu` pages appear in the dashboard drawer (`src/apps/dashboard/components/drawer/sections/PluginDrawerSection.tsx:18-36`).
- **Conclusion**: write `<div data-role="page" class="page type-interior pluginConfigurationPage"
  data-controller="__plugin/KidsJs">` plus a JS module, or use inline scripts as the template does. The page talks to
  the plugin's own endpoints through `ApiClient.fetch` and `ApiClient.getUrl`.
- The page HTML is served anonymously, so **never embed state in the HTML**. Load everything through authenticated
  calls.

## 3. Enforcement design per feature

Legend: **R** = robustness (does it cover every client, and does it fail closed); **I** = intrusiveness (does it
change admin-owned state, or risk lockouts if the plugin breaks).

### 3.1 Daily screen-time allowance (counting)

| Option | R | I | Notes |
|---|---|---|---|
| A. Count server playback events (`PlaybackStart`, `PlaybackProgress`, `PlaybackStopped`) per kid user and server-local date | High: counts every client that reports playback, which is all mainstream clients | None | Handle automated ticks as in §2.3. Only the session owner (`Session.UserId`) is counted: a kid in `AdditionalUsers` of someone else's session is not charged, although `Users` lists them (decided 2026-10-18, feature 3 Q3) |
| B. Client-reported segments (`POST WatchTime/Report`) | Low on its own | None | Keep it as an optional supplement for offline playback. It can only **add** usage |
| C. Count active sessions instead of playback (screen time while browsing) | Medium | None | "Screen time" in kids mode means watching. Browsing time is not counted in v1 |

**Recommendation: A, plus B as an add-only supplement.**
- The day boundary is server local midnight, the same clock as `AccessSchedules`. A per-household "day starts at"
  hour is optional.
- Flush to SQLite every 30 to 60 s and on stop. Keep in-memory accumulators per `(userId, sessionId)`.

### 3.2 Stopping playback when time is up

| Option | R | I | Notes |
|---|---|---|---|
| A. `SendPlaystateCommand(null, session.Id, Stop)` plus `SendMessageCommand` "Time's up" on every playing session of the kid | Medium: needs a client with an open WebSocket that honours Playstate. Wholphin, jellyfin-web, Android and others do | None | This is what the server does for inactive sessions (§2.3) |
| B. A, then `ITranscodeManager.KillTranscodingJobs(deviceId, …)` after a grace period | Higher: transcoded and HLS playback stops server-side regardless of the client | Low | Direct-play static files already being streamed are not cut by this |
| C. Revoke the kid's tokens (`ISessionManager.RevokeUserTokens`) | High | **High**: every device is logged out and needs the kid's password again | Last resort only |

**Recommendation: A + B.**
- Warnings at 5 and 1 minutes remaining go out as `DisplayMessage`, rate-limited, and only to sessions that support
  it.
- On expiry, send Stop, then kill transcodes after 15 s if the session still reports `NowPlayingItem`.
- Repeat every tick while the session keeps reporting playback. This catches clients that ignore Stop, and pairs
  with the block in §3.3.

**Built in feature 5 (spec `005-kids-enforcement`), revised from the above:**
- The decision runs in memory on Jellyfin's 1 s progress tick (`EnforcementState`, fed by `PlaybackTracker`), with no
  per-kid timers (review §D.3).
- Per denial and session, once: a modal `DisplayMessage`, Stop about 5 s later, `KillTranscodingJobs`, the live
  stream close, `HttpContext.Abort()` on the kid's in-flight deliveries, and the end of the playback on the server
  (`OnPlaybackStopped`, as the idle sweep does), so the tick and the transcode pings stop. Only a real report from the
  denied session, or the 30 s sweep, repeats the stop part, at most every 10 s. One info log line per denial and per
  stopped session (the repro's fork logged one every ~10 s).
- Warnings at 5 and 1 minutes are toasts (`TimeoutMs` 10 s) for the allowance, the sitting and the allowed hours.
- The spike (spec 005 research R1): the abort cut a socket-less curl download within 4 s, and jellyfin-web in
  Chromium, when it ignores Stop, kept playing ~35 s from what it had buffered before showing "Playback of this media
  is not allowed".

### 3.3 Blocking new playback for out-of-time kids

What has to be gated:
- `MediaInfoController` `GET/POST Items/{id}/PlaybackInfo` (class `[Authorize]`, `Jellyfin.Api/Controllers/MediaInfoController.cs:29, 72, 116`);
- `DynamicHlsController` master and main playlists (class `[Authorize]`, `DynamicHlsController.cs:39`);
- `UniversalAudio` (`[Authorize]`, `UniversalAudioController.cs:94`);
- `Sessions/Playing` (`PlaystateController.cs:26, 201`).

Note that `Videos/{id}/stream`, `Audio/{id}/stream` and HLS segments **have no `[Authorize]`** (`VideosController.cs:314-318`,
`AudioController.cs:88-89`, and the comment in `HlsSegmentController.cs:52-56, 124-126`: "Can't require authentication
just yet").

| Option | R | I | Notes |
|---|---|---|---|
| A. A global **MVC action filter** registered by the plugin (`Configure<MvcOptions>`). For authenticated kid callers that are out of time, it short-circuits PlaybackInfo, HLS playlists, UniversalAudio and `Sessions/Playing` with **403** and a JSON body `{"kids":"timeUp"}`. It can also match anonymous stream requests by the `deviceId` or `ApiKey` query when the device belongs to a kid session | High for normal clients. Every client calls PlaybackInfo or an HLS playlist first | Low: no server state is modified, and removing the plugin removes the block | Needs the feature 4 spike. Match actions by `ControllerActionDescriptor` (controller and action names), so the filter is resilient to route changes. Never answer 401 |
| B. A plugin `IAuthorizationHandler` that fails `DefaultAuthorizationRequirement` on those endpoints | Same as A for `[Authorize]` endpoints, and it cannot see anonymous ones | Low | The requirement type is internal to `Jellyfin.Api`, so it would have to be matched by name. Responses are a bare 403. Inferior to A |
| C. Temporarily rewrite `AccessSchedules` to an empty window (for example `Everyday 0–0`) until midnight or a bonus, then restore it | High: native, it also blocks login and all `[Authorize]` calls, so the whole app shows "not allowed now" | **High**: it mutates admin-owned policy and needs a persisted "original schedule" for restore. If the plugin is removed mid-block, the kid stays locked out. The admin sees a strange schedule. It conflicts with admin edits | A fallback switch only ("hard lock"). Restore at startup and on uninstall (`IEventConsumer<PluginUninstalledEventArgs>`, best effort) |
| D. Set `EnableMediaPlayback = false` | Low: it is not enforced on streams (§2.4) | Medium | Use it only as an advisory hint alongside A, or skip it |
| E. `IsDisabled = true` | High | **Very high**: tokens die and every TV is logged out | Reject |

**Recommendation: A as the default.**
- B is unnecessary if A works.
- Offer C as an opt-in "strict lock" per kid, with a startup reconciler. Its semantics are already honoured by every
  client, which is attractive for non-Wholphin clients.
- Wholphin reacts to 403 `timeUp` by showing the Time's up screen, and also learns state from `GET /Kids/Status`.

**Built in feature 5:** A, but narrower than a global filter. An `IApplicationModelConvention` attaches one
`IAsyncResourceFilter` to the 22 playback actions only (PlaybackInfo GET and POST, `LiveStreams/Open`, UniversalAudio,
the HLS master, variant, live and segment routes, the legacy HLS routes, `Videos`/`Audio` streams, `Items/{id}/File`,
`Items/{id}/Download`, the two LiveTv file routes; spec 005 research R3), plus any action Jellyfin marks as producing
video, audio or HLS. PlaybackInfo answers 200 `ErrorCode: NotAllowed`; the rest 403 with
`X-Application-Error-Code: PlaybackNotAllowed` and `X-Kids-Reason` (`timeUp`, `outsideSchedule`, `sessionLimit`),
never `ParentalControl`. `Sessions/Playing*` is not gated. Behind `EnforcementEnabled`, off by default. A startup
self-check reports the guarded routes in `GET /Kids/Info` (`Enforcement.Gate`). C (the strict lock) is not built.

### 3.4 Bedtime and allowed hours

**Native `AccessSchedules`, with no plugin enforcement.** The plugin only provides:
- a friendlier editor in the config page and the Wholphin parent menu, which writes through `UpdatePolicyAsync` after
  a read-modify-write, and requires a grant or admin;
- (planned for feature 5, built differently) Status reports `DeniedReason: outsideSchedule` while enforcement is on,
  from `UserEntityExtensions.IsParentalScheduleAllowed`; a `withinSchedule` flag and `nextAllowedAt` were not added.

Caveats:
- the schedule uses **server** local time and has inclusive ends;
- admins bypass it;
- an existing direct-play stream is not cut when a window closes.

The allowance tracker should also stop playing sessions when the schedule closes. This reuses §3.2 at little extra
cost.

**Built in feature 5:** with enforcement on, the tick evaluates Jellyfin's own `IsParentalScheduleAllowed` on a cached
`User` (refreshed on `OnUserUpdated` and every 60 s), warns 5 and 1 minutes before the window closes, and runs the
§3.2 stop at the boundary. The filter also refuses the anonymous stream routes after hours, which the core does not.
Stock 12.2 keeps direct play running and HLS ffmpeg alive for ~10 minutes after the window ends
([repro](repro-schedule-playback.md)).

### 3.5 Content restrictions

**Native policy only.** The plugin adds an "apply recommended policy" helper (admin or grant). It reads the policy
and sets:
- `MaxParentalRating` to a chosen score, from the server's ratings list;
- `BlockUnratedItems` to all types;
- `EnableAllFolders = false` with the chosen `EnabledFolders`;
- `EnableLiveTvAccess = false` (unless the kid has live TV channels, feature 6), `EnableContentDeletion = false`,
  `EnableContentDownloading = false`;
- `EnableRemoteControlOfOtherUsers = false`, `EnableSharedDeviceControl = false`, `EnablePublicSharing = false`,
  `EnableCollectionManagement = false`, `EnableSubtitleManagement = false`;
- `EnableUserPreferenceAccess = false`, `SyncPlayAccess = None`, and optionally `IsHidden = true`.

It then writes the policy back. It shows a diff before applying, and warns when:
- the kid is an administrator (admins bypass everything);
- the kid has no password while remote access is enabled;
- `HasContentRestrictions()` is false (`Jellyfin.Data/UserEntityExtensions.cs:170-180`).

### 3.6 Parent lock

- The server stores `{lockType: NUMERIC|REMOTE, verifier}` per household in `kids.db` (`parent_lock`, one row), with
  an optional per-kid override later.
- The format is the app's `pbkdf2-sha256$<iter>$<saltHex>$<hashHex>` (`app/.../kids/gate/PasscodeHasher.kt:11-48`).
  It is a single-block PBKDF2-HMAC-SHA256 with 120 000 iterations and a 16-byte salt, and equals
  `Rfc2898DeriveBytes.Pbkdf2(utf8(code), salt, iter, SHA256, 32)`. Compare with `CryptographicOperations.FixedTimeEquals`.
- `REMOTE` codes use the symbols `U D L R 0-9 r g y b p w f + - i` (`kids/gate/ParentLockKeys.kt`). The server treats
  the code as an opaque UTF-8 string, so it stays alphabet-agnostic.
- `CHALLENGE` stores nothing, so the server **cannot** issue a grant from it. In plugin mode with CHALLENGE, the
  device-local gate still works, and server mutations require the parent's admin login (a Wholphin admin profile
  token) instead of a grant. The config page should recommend a code when kids are managed server-side.

### 3.7 Live TV per kid

**Built in feature 6 (spec `006-kids-live-tv`, plugin 0.0.6.0).** `UserPolicy.EnableLiveTvAccess` is all or nothing, so
each kid profile holds `LiveTvChannelIds` (JSON settings, no migration). An `IAsyncActionFilter`, attached by the
feature 5 convention to the playback actions and to Jellyfin's `LiveTv` browse and recording actions, holds kids to
that list **always**, whatever `EnforcementEnabled` says (a content rule, clarified 2026-10-19):

- playback of another channel (by item id, a guide program's channel, the open token `LiveTvChannel_<id>_…`, or a
  live stream id the plugin recorded when it let a kid open it) answers PlaybackInfo `NotAllowed` or 403 with
  `X-Kids-Reason: liveTvChannel`; an unknown live TV stream id is refused;
- recordings, timers and series timers answer 403 `liveTvRecording`;
- `LiveTv/Channels`, `LiveTv/Programs` (GET, POST) and `Programs/Recommended` are cut to the kid's channels,
  `Channels/{id}` and `Programs/{id}` of other channels answer 404.

The effective set is empty while the kid's live TV access is off. Kids read their channels and guide from
`/Kids/Users/{id}/LiveTv/Channels|Programs` (Jellyfin's `BaseItemDto` shape). Jellyfin 12.2 only accepts HTTP entries
in an M3U tuner, so the dev loop serves two live MPEG-TS channels from jellyfin-ffmpeg (`-listen 1`).

## 4. Proposed plugin architecture

```text
Jellyfin.Plugin.Kids (assembly; plugin name "Kids"; guid d62ee188-270c-49d8-b7b9-5e524fec2335 per build.yaml)
├── Plugin.cs                      BasePlugin<PluginConfiguration>, IHasWebPages (KidsConfig html + KidsJs module)
├── PluginServiceRegistrator.cs    DI wiring (below)
├── Configuration/                 PluginConfiguration (admin defaults only), configPage.html, configPage.js
├── Storage/KidsDatabase.cs        Microsoft.Data.Sqlite, kids.db in <DataPath>/kids, schema migrations by user_version
├── Storage/LegacyDataMigration.cs moves kids.db from DataFolderPath (0.0.9.0 and earlier), verified and crash-safe
├── Allowance/                     (feature 3) KidRoster, IKidsSettings/PluginKidsSettings, DayClock, PlaybackClock,
│                                  PlaybackEvent, StatusCalculator, SegmentValidator, UsageService (pending time, flush,
│                                  status, bonus, reports, purge, prune); UsageStore lives in Storage/
├── Services/
│   ├── KidProfileService          CRUD with revision (optimistic concurrency), validation (picks are BoxSet/Playlist)
│   ├── ParentLockService          (ParentLock/) verifier storage, PBKDF2 verify, lock type; ThrottleService beside it
│   ├── GrantService               in-memory grants: 256-bit random, bound to (deviceId, issuing userId), TTL 5 min; throttling
│   ├── AllowanceService           (built as Allowance/UsageService in feature 3)
│   ├── PolicyHelper               read-modify-write UserPolicy: recommended policy, AccessSchedules editor, strict-lock apply/restore
│   └── CallerContext              (Auth/) wraps IAuthorizationContext: IsAdmin (role or API key), UserId; CallerResolver adds
│                                  the token's registered device (IDeviceDirectory) and the X-Kids-Grant check
├── Hosted/
│   ├── OrphanProfileSweeper       IHostedService; at startup, drops profiles and usage of users deleted while stopped
│   ├── PlaybackTracker            IHostedService; subscribes ISessionManager.PlaybackStart/Progress/Stopped; counts the
│   │                              session owner if a kid (PlaybackClock), flushes UsageService every 30 s
│   └── EnforcementLoop            IHostedService; 5 s timer: warnings, Stop + DisplayMessage, transcode kill, schedule-close stop
├── Enforcement/PlaybackGateFilter IAsyncActionFilter via Configure<MvcOptions>; 403 {"kids":"timeUp"} for gated actions
├── Events/UserDeletedConsumer     IEventConsumer<UserDeletedEventArgs>: drop profile/usage rows
├── Tasks/PruneUsageTask           IScheduledTask (found by assembly scanning, not DI): prune screen time older than 90 days
│                                  daily at 03:30; strict-lock reconciliation comes later
└── Api/                       KidsController (route prefix /Kids), ParentLockController (/Kids/Parent) and
                               AllowanceController (/Kids/Users/{userId}: Status, WatchTime/Bonus, WatchTime/Report)
```

Registrator:

```csharp
services.AddSingleton<KidsDatabase>();
services.AddSingleton<KidProfileService>();
services.AddSingleton<ParentLockService>();
services.AddSingleton<GrantService>();
services.AddSingleton<AllowanceService>();
services.AddSingleton<PolicyHelper>();
services.AddHostedService<PlaybackTracker>();
services.AddHostedService<EnforcementLoop>();
services.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedConsumer>();
services.Configure<MvcOptions>(o => o.Filters.Add<PlaybackGateFilter>());   // feature 4, behind a config flag
```

NuGet packages (more detail in §6.4):
- `Jellyfin.Controller` and `Jellyfin.Model` at `12.0.0` or `12.2.0`, with `ExcludeAssets=runtime`;
- `Jellyfin.Data` only if referenced directly (it already comes transitively through `Jellyfin.Controller` → `Jellyfin.Model`);
- `Microsoft.Data.Sqlite` 10.0.11 with `ExcludeAssets=runtime`;
- `<FrameworkReference Include="Microsoft.AspNetCore.App"/>`.

### 4.1 Client contract (route prefix `/Kids`, API version 1)

**Auth model**
- **Reads** of a kid's own data: the kid's token ("self") or an admin. These use `Policies.IgnoreParentalControl`, so
  they work outside allowed hours.
- **Mutations**: an admin token or API key, **or** a parent grant in `X-Kids-Grant`, valid for the same `DeviceId`
  that obtained it. A kid token alone is never enough, even for its own user id.
- `Parent/Verify` never returns 401, because clients treat 401 as session expiry. A wrong code returns 200 with
  `{valid:false}`. Throttling is per `(userId, deviceId)`: 5 tries, then 30 s, doubling up to 15 min, plus a cap of
  20 wrong tries per user across all devices. The `deviceId` is the device Jellyfin registered for the token, not the
  request header, which Jellyfin does not check against the token (`AuthorizationContext.cs:144-147`); grants are
  bound to the same `(userId, deviceId)`.
- Grants live only in memory and are dropped on restart.
- `?ApiKey=` works for scripts. `api_key` does not.
- **JSON**: Jellyfin's own formatters (feature 1). Responses are PascalCase by default and camelCase with
  `Accept: application/json; profile="CamelCase"`; request bodies are case-insensitive. The field names below are
  written in camelCase only as notation. ProblemDetails (400, 404) are always camelCase (`errors`).

| Method and path | Auth | Request → response |
|---|---|---|
| `GET /Kids/Info` | anonymous | → `{pluginVersion, apiVersion:1, features:["profiles","parentLock","allowance","sessionLimit","enforcement","schedules"]}`. Detection: 200 means plugin mode, 404 means no plugin (no kids features), a network error keeps the last known mode. Null fields are omitted, so a client checks the feature (for example `sessionLimit`) before reading a missing field as "none". Nothing per-household, so no lock type (constitution IV; decided 2026-10-18 in feature 2): clients read it from `GET /Kids/Parent/Lock` after signing in |
| `GET /Kids/Profiles` | `[Authorize(IgnoreParentalControl)]` | → `[{userId, revision}]`, so the user switcher knows who the kids are. Ids only |
| `GET /Kids/Users/{userId}/Profile` | self or admin | → `KidProfileDto {userId, revision, searchAllowed, autoplayEnabled, autoplayCap, pickedItemIds[], sections[{section,visible}], dailyAllowanceMinutes?, sessionLimitMinutes? (5–120, stored only, counted per device), strictLock}`, or 404 if the user is not a kid |
| `PUT /Kids/Users/{userId}/Profile` | admin or grant | body with the base `revision` (required; **0 marks the user as a kid**, 409 if already a kid) → 200 with the new revision, or **409 with the current DTO**. The revision is checked before validation, so stale and invalid gives 409 |
| `DELETE /Kids/Users/{userId}/Profile` | admin or grant | unmarks the kid; usage is kept unless `?purge=true` (feature 3) |
| `POST /Kids/Parent/Verify` | `[Authorize(IgnoreParentalControl)]` | `{code}` → `{valid:true, grant, expiresAt}`, or `{valid:false, retryAfterSeconds?}` |
| `GET /Kids/Parent/Lock` | `[Authorize(IgnoreParentalControl)]`, any signed-in user | → `{lockType:"NUMERIC"\|"REMOTE"\|null, changedAt?}`, so the client draws the right gate. Nothing else about the lock |
| `PUT /Kids/Parent/Lock` | admin, or grant plus the `current` code | `{lockType, code}` sets a new code. `{lockType, verifier}` imports one during migration, and only when none is set (admin only, as is setting the first lock) |
| `DELETE /Kids/Parent/Lock` | admin | removes the household lock and drops every grant |
| `GET /Kids/Parent/Verifier` | admin or grant | → `{lockType, verifier}`, cached by the device for offline unlock |
| `GET /Kids/Users/{userId}/Status` | self or admin, IgnoreParentalControl | → `{date, usedSeconds, allowanceSeconds?, bonusSeconds, remainingSeconds?, timeUp, serverTime, nextDayStartsAt, warnAtSeconds:[300,60]}` (feature 3; feature 5 adds `enforced`, `deniedReason?`, `deniedUntil?` and the sitting fields, spec 005 contract). The client polls it about every 60 s, and immediately after a Playstate Stop or a 403 `timeUp` |
| `POST /Kids/Users/{userId}/WatchTime/Report` | self | `{segments:[{id, startedAt, seconds}]}`, add-only and idempotent by `id`, for offline playback |
| `POST /Kids/Users/{userId}/WatchTime/Bonus` | admin or grant | `{minutes}` (1–240) → the new Status |
| `POST /Kids/Users/{userId}/WatchTime/Reset` | admin or grant | no body → the new Status: today's usage and bonus back to 0, the sitting ended, logged (feature 7, spec 007; the one exception to "usage only grows") |
| `PUT /Kids/Users/{userId}/Schedule` | admin or grant | `{schedules:[{dayOfWeek, startHour, endHour}]}`, written into `UserPolicy.AccessSchedules` |
| `POST /Kids/Users/{userId}/Policy/Recommended` | admin or grant | `{maxParentalRating, enabledFolders[], hide:bool, dryRun:bool}` → a diff, which is applied unless `dryRun` |
| `GET /Kids/Admin/Overview` | admin | **Not needed** (feature 3, research R10): the Kids page uses Jellyfin's `GET /Users`, `GET /Kids/Profiles` and one `GET Status` per kid. Kept here only as a possible later addition |
| `GET /Kids/Admin/PickCandidates` | admin | `?search=&ids=&limit=` → `[{id, name, type:"BoxSet"\|"Playlist"}]`, every collection and playlist on the server whoever owns them (Jellyfin's `/Items` hides other users' private playlists from admins), for the picks chooser |
| `GET /Kids/Users/{userId}/LiveTv/Channels`, `…/LiveTv/Programs` | self or admin, IgnoreParentalControl | (feature 6) the kid's allowed channels with the program on now, and their guide for a window (default 24 h, at most 7 days), in Jellyfin's `QueryResult<BaseItemDto>` shape |
| `GET /Kids/Admin/LiveTvChannels` | admin | (feature 6) every live TV channel `[{id, name, number, imageTag, type}]`, for the kid editor |

Errors use `ProblemDetails`. The gate filter's 403 body is `{"kids":"timeUp","remainingSeconds":0}` or
`{"kids":"outsideSchedule"}`.

**Device lock.** The prior study's `Devices/{id}/Lock` endpoint is dropped from v1. The device lock stays local, and
"remote unlock" is out of scope until there is a use case.

**Wholphin side** (for the app's own spec): it adds a `ServerPluginSource` behind `KidsConfigSource`, and runs the
one-time migration of local profiles and the verifier after the parent gate. Profiles use a Room read-through cache
with `revision` and `syncedAt`. If the plugin disappears, the app fails closed.

## 5. Learnings from other plugins

The repos were shallow-cloned into `.reference/<owner>_<repo>`. Stars and pushes are as of 2026-10-17.

**Licences.** Copying code from a GPL-3.0 plugin makes this plugin GPL-3.0. Licence-less repos are "all rights
reserved", so study them but do not copy from them. Decide the licence early (open question 8).

| Plugin | Licence | 12.x status | What to reuse |
|---|---|---|---|
| **benben42/jellyfin-kids-limit-plugin** | **none** (study only) | Yes: net10.0, Controller 12.0.0, targetAbi 12.0.0.0 | The closest prior art. Per-weekday daily limits counted from session events plus a 5 s `Sessions` sweep, and bonus time. Enforcement is a warning, then **Stop repeated every 5 s** plus `KillTranscodingJobs`. It **removed** its earlier policy-based block (`EnableMediaPlayback` off or an always-denied schedule) and ships `LegacyBlockRestorer` to undo stuck blocks. Its `REQUIREMENTS.md` §2.1 measured the 12.x Android TV client: Stop, Pause, Seek and DisplayMessage are honoured; GoHome, Back, logout and policy changes do **not** stop current playback. Its auth is weak: an anonymous shared token in the query string, so don't copy it |
| n00bcodr/Jellyfin-Enhanced | GPL-3.0 | Yes (dual jf12/jf10 build) | **Precedent for the gate filter**: global `IAsyncActionFilter`s registered with `Configure<MvcOptions>(o => o.Filters.AddService<…>())` (`PluginServiceRegistrator.cs:184-196`, `Services/HiddenContentResponseFilter.cs`), and `IStartupFilter` middleware (`:33-34`). When it changes `IsDisabled`, it writes a journal *before* the change (`Services/MaintenanceModeService.cs:285-330`), which is the pattern for the strict lock |
| intro-skipper/intro-skipper | GPL-3.0 | Yes (branch `12.0`, Controller 12.2.0) | Its own EF Core `DbContext` on SQLite with migrations, at EF 10.0.11 to match the server. The `AddSingleton<T>` + `AddHostedService(sp => sp.GetRequiredService<T>())` pattern. One branch per server major version |
| jellyfin/jellyfin-plugin-trakt | MIT | Yes (targetAbi 12.1.0.0) | A hosted service on PlaybackStart, Progress and Stopped (`Trakt/ServerMediator.cs`). MIT, so its code can be copied |
| jellyfin/jellyfin-plugin-playbackreporting | GPL-3.0 | Yes (targetAbi 12.0.0.0) | Watch-time accounting from playback events, SQLite storage, several config pages with menu entries, and the csproj and `build.yaml` for 12.x |
| jellyfin/jellyfin-plugin-reports | MIT | Yes | Minimal `EnableInMainMenu` page (`Plugin.cs`) |
| jellyfin/jellyfin-plugin-kodisyncqueue | GPL-2.0 | Yes | A `GetUserId(ClaimsPrincipal)` helper on `Jellyfin-UserId` (`API/KodiSyncQueueController.cs:290`). LiteDB storage, which we don't need |
| jellyfin/jellyfin-plugin-ldapauth | MIT | Yes | Registering providers in DI (`LDAP-Auth/ServiceRegistrator.cs`) |
| jellyfin/jellyfin-plugin-sessioncleaner | GPL-3.0 | Yes | An `IScheduledTask` that calls `ISessionManager.Logout(device)` (`SessionCleanerTask.cs:66-78`) |
| streamyfin/jellyfin-plugin-streamyfin | MPL-2.0 | main is 10.11; the 12 port is on a branch | Tabbed multi-page config (`withTabs`, `__plugin/X.js` controllers, `Plugin.cs:50-93`). Client-facing authenticated API, and Microsoft.Data.Sqlite storage (`Storage/Database.cs`) |
| voc0der/jellyfin-plugin-session-provisioning | MIT | Yes | `ISessionManager.AuthenticateDirect` with a rate limiter and a constant-time secret check (`Api/SessionProvisioningController.cs:185-200`). The model for a possible future "parent code → kid session on a new TV" flow |
| 9p4/jellyfin-plugin-sso | GPL-3.0 | No (archived, 10.11) | `AuthenticateDirect` for passwordless login. Reference only |
| Trewqa/jellyfin-plugin-sessionflowlimits | GPL-3.0 | No (10.11) | Stop on PlaybackStart when over a limit (`SessionEntryPoint.cs:72-122`) |
| n00bcodr/Jellyfin-JavaScript-Injector, IAmParadox27/file-transformation | GPL-3.0 | Yes | Web JS injection through `IStartupFilter`. Not needed (non-goal), but it is the right way if it ever is |
| nicknsy/jellyscrub | MIT | No (10.10) | Anti-pattern: it writes `index.html` on disk |
| jellyfin/jellyfin-plugin-template | GPL-3.0 | **master is still 10.11** (net9.0); branch `unstable` targets 13.x nightlies | Skeleton only. Pin 12.x by hand |
| External tools (Joker-KP/jelly-watch-wise MIT, Endif12/jelly-kids-guard, irfadrazick/jellyfin-time-limiter) | mixed | n/a (they use the HTTP API) | All of them enforce by flipping `IsDisabled` or `EnabledFolders` through `/Users/{id}/Policy`, reading usage from Playback Reporting. This confirms that the policy-flip approach is common but intrusive |

**Takeaways**
- Layered enforcement is proven on 12.x: count on the server, then warn, Stop (repeated), and kill transcodes.
- A global MVC filter in a plugin works in practice (Jellyfin-Enhanced). That makes the §3.3 gate low-risk.
- No plugin registers its own `IAuthorizationHandler`.
- Avoid policy mutation. If the strict lock is ever used, journal the change first and reconcile on startup.
- There is no licensed prior art for screen-time enforcement to copy. Write it fresh, borrowing patterns (not code)
  from benben42.

## 6. Dev loop notes (for the infra agent)

Verified against the 12.2 sources. Paths are in `.reference/jellyfin`.

### 6.1 Running the server
- **Flags** (`Jellyfin.Server/StartupOptions.cs`):
  - `-d/--datadir` :18, `-C/--cachedir` :38, `-c/--configdir` :45, `-l/--logdir` :52, `-w/--webdir` :31,
    `--nowebclient` :24;
  - `--ffmpeg` :56, `--published-server-url` :68, `--nonetchange` :74;
  - `--mode MediaServer|MigrateSystem|SeedSystem` :87.
- **Environment variables** (`Jellyfin.Server/Helpers/StartupHelpers.cs:84-127`): `JELLYFIN_DATA_DIR`,
  `JELLYFIN_CONFIG_DIR`, `JELLYFIN_CACHE_DIR`, `JELLYFIN_LOG_DIR` and `JELLYFIN_WEB_DIR`. A flag beats its env var.
  - Always pass `--configdir` explicitly. Otherwise it may fall back to `$XDG_CONFIG_HOME/jellyfin` (:90-103).
- Any IConfiguration key can be set as `JELLYFIN_<Key>`, with `__` for `:` (`Program.cs:400-405`;
  keys in `MediaBrowser.Controller/Extensions/ConfigurationExtensions.cs`). Examples: `JELLYFIN_hostwebclient=false`,
  `JELLYFIN_ffmpeg=…`, `JELLYFIN_FFmpeg__novalidation=true`.
- **Port**: 8096 by default (`MediaBrowser.Common/Net/NetworkConfiguration.cs:15`). There is no flag or env var for
  it. Pre-seed `<configdir>/network.xml` with `<NetworkConfiguration><InternalHttpPort>…</InternalHttpPort></NetworkConfiguration>`.
- **Web client**: if `hostwebclient` is true and the web dir is missing, the server exits with code 1
  (`Program.cs:119-134`). `--nowebclient` redirects `/` to Swagger. jellyfin-web is needed to view config pages.
- **ffmpeg is mandatory**: startup throws without a valid ffmpeg 4.4 or newer (`Emby.Server.Implementations/ApplicationHost.cs:429-433`,
  `MediaBrowser.MediaEncoding/Encoder/EncoderValidator.cs:209-211`), unless `JELLYFIN_FFmpeg__novalidation=true`.
- **nixpkgs** (unstable) has `jellyfin` 12.2, `jellyfin-web` 12.2 and `jellyfin-ffmpeg` 8.1.2-5. Its wrapper already
  passes `--ffmpeg` and `--webdir` (`pkgs/by-name/je/jellyfin/package.nix:45-49`).
- **Readiness**: poll until `curl -s :8096/health` returns the body `Healthy`. During boot a SetupServer answers
  `/System/Info/Public` and reports Degraded (`Jellyfin.Server/ServerSetupApp/SetupServer.cs:160-292`).
- **Logging**: `JELLYFIN_Serilog__MinimumLevel__Override__Jellyfin.Plugin.Kids=Debug`, or
  `<configdir>/logging.json` with `{"Serilog":{"MinimumLevel":{"Override":{"Jellyfin.Plugin.Kids":"Debug"}}}}`.
  Assume a restart is needed.

### 6.2 Plugin loading
- Plugins live in `<datadir>/plugins/<Folder>/`. Every top-level folder is a plugin candidate
  (`Emby.Server.Implementations/Plugins/PluginManager.cs:793-878`).
- All `*.dll` files are loaded recursively, unless `meta.json` has an `assemblies` whitelist (:891-934).
- **`meta.json` is optional**: without it the server synthesises one (name from the folder, `targetAbi` set to the
  app version, Active), then rewrites it with the real id, name and version (:762-786, :630-653).
- **The folder must be writable** because the server writes `meta.json`. Copy the build output in, and do not
  symlink to `/nix/store`.
- **ABI and version checks**:
  - A plugin is supported only if `appVersion >= targetAbi` (:755). An unparseable value counts as 0.0.0.1.
  - When several folders hold the same plugin, the highest version wins and **older folders are deleted** (:829-866).
- **Status is persisted**: after a failed load, `Malfunctioned` or `NotSupported` is written into `meta.json`
  (:564-573), and later starts skip the plugin. The dev loop should reset `"status":"Active"` or delete `meta.json`
  on each deploy. Disabled plugins also stay disabled across restarts since 12.0.
- **No hot reload**: assemblies load once at startup (`ApplicationHost.cs:883`). Restart the process after every
  build. In-process `POST /System/Restart` exists but is unverified for picking up new DLLs.
- **Paths**: plugin config is `<datadir>/plugins/configurations/Jellyfin.Plugin.Kids.xml`. `DataFolderPath` is
  `<datadir>/plugins/Jellyfin.Plugin.Kids`. That folder sits beside the install folder and contains no DLLs, so the
  loader ignores it (:870-871). Since spec 014 it holds only the renamed pre-0.0.10.0 database and a README; the live
  database is `<DataPath>/kids/kids.db`.
  - **Use an install folder name that differs from the assembly name**, for example `Kids_0.0.1.0`, so the data
    folder is never treated as the install folder.

### 6.3 Provisioning a dev server
- **The wizard is required.**
  - Pre-seeding `system.xml` with `IsStartupWizardCompleted=true` and an empty database throws on purpose
    (`Jellyfin.Server/Migrations/JellyfinMigrationService.cs:140, 198-240`).
  - The first user is created only by `GET /Startup/User` (`UserManager.InitializeAsync`, `UserManager.cs:745-771`).
- **Wizard endpoints** (`Jellyfin.Api/Controllers/StartupController.cs`): they use `FirstTimeSetupOrElevated`, so
  they are open until the wizard completes.

  ```sh
  J=http://127.0.0.1:8096; H='MediaBrowser Client="kids-dev", Device="script", DeviceId="kids-dev-1", Version="0.1"'
  until [ "$(curl -s $J/health)" = Healthy ]; do sleep 1; done
  curl -sf $J/Startup/User >/dev/null                                   # creates the first (admin) user
  curl -sf -XPOST $J/Startup/Configuration -H 'Content-Type: application/json' \
    -d '{"ServerName":"kids-dev","UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
  curl -sf -XPOST $J/Startup/User -H 'Content-Type: application/json' -d '{"Name":"admin","Password":"admin"}'  # non-empty password required in 12.x
  curl -sf -XPOST "$J/Library/VirtualFolders?name=Kids&collectionType=movies&paths=$PWD/.dev/media/kids&refreshLibrary=false" \
    -H 'Content-Type: application/json' -d '{"LibraryOptions":{}}'      # path must exist; duplicate names get suffixed
  curl -sf -XPOST $J/Startup/RemoteAccess -H 'Content-Type: application/json' -d '{"EnableRemoteAccess":false}'
  curl -sf -XPOST $J/Startup/Complete
  TOK=$(curl -sf -XPOST $J/Users/AuthenticateByName -H "Authorization: $H" -H 'Content-Type: application/json' \
    -d '{"Username":"admin","Pw":"admin"}' | jq -r .AccessToken)
  A="Authorization: $H, Token=\"$TOK\""
  curl -sf -XPOST "$J/Auth/Keys?app=kids-dev" -H "$A"                   # 204; read the key back:
  KEY=$(curl -sf $J/Auth/Keys -H "$A" | jq -r '.Items[]|select(.AppName=="kids-dev")|.AccessToken' | head -1)
  # Kids: POST /Users/New {Name,Password}; then GET /Users/{id} → edit .Policy → POST /Users/{id}/Policy (full object)
  ```
- `Startup/Configuration`, `Startup/RemoteAccess` and `GET Startup/User` are marked `[Obsolete]` in 12.x but still
  present.
- `--mode SeedSystem` (`StartupOptions.cs:87`) only creates the database schema and migration history. It takes no
  input and creates no users or keys, and the server does not serve afterwards (`JellyfinMigrationService.cs:101-129`,
  `Program.cs:233,251`). It is useless for provisioning, so use the API sequence above.
- **Also unverified**: `GET /Library/VirtualFolders` returns `[].ItemId` (from memory of `VirtualFolderInfo`), and the
  sequence has not been run against a live server.
- Make the script idempotent: check `GET /System/Info/Public` → `StartupWizardCompleted`, and
  `GET /Library/VirtualFolders`.

### 6.4 NuGet packages
- **Versions on nuget.org** for `Jellyfin.Controller`, `Jellyfin.Model`, `Jellyfin.Common`, `Jellyfin.Data`,
  `Jellyfin.Database.Implementations` and `Jellyfin.Extensions`: 12.0.0, 12.1.0 and 12.2.0 (plus rc builds). The
  last 10.x is 10.11.11.
- **The 12.2.0 dependency chain**:
  - `Jellyfin.Controller` → Naming, Common, Model, MediaEncoding.Keyframes;
  - `Jellyfin.Model` → Data and Extensions;
  - `Jellyfin.Data` → Database.Implementations, which brings EF Core Relational 10.0.11.
  - So `Jellyfin.Controller` alone transitively provides `User` and `UserEntityExtensions`.
- The server is on .NET 10 with ASP.NET and EF Core 10.0.11 (`Directory.Packages.props:29-48`).
- **Recommendation**:
  - compile against **12.0.0**, so one build runs on 12.0 to 12.2 with `targetAbi 12.0.0.0`;
  - in CI, also build against 12.2.0;
  - use `ExcludeAssets=runtime` on every Jellyfin and Microsoft package, and keep the `build.yaml` `artifacts`
    whitelist to `Jellyfin.Plugin.Kids.dll`.
- The template repo is still on 10.11 and net9.0. Take the 12.x details from Playback Reporting and Webhook
  (`net10.0`, `12.*-*`, `targetAbi 12.0.0.0`).

## 7. Phased roadmap (spec-kit features)

| # | Feature name | Scope | Acceptance ideas |
|---|---|---|---|
| 1 | `kids-plugin-foundation` | Plugin skeleton, SQLite store, `Info`, `CallerContext`, profiles CRUD with revisions, user-deleted cleanup, admin config page that lists users, marks kids and warns on admin kids | Plugin loads on 12.0 and 12.2; anonymous `Info` returns 200; a kid token gets 403 on `PUT Profile`; concurrent PUT returns 409 with the current DTO; deleting a user removes the profile |
| 2 | `kids-parent-lock` | Lock storage (NUMERIC or REMOTE), verifier import, `Verify` with grants and throttling, `Verifier` for offline use, lock management in the config page | An app verifier validates server-side unchanged; a wrong code returns 200 `valid:false` and never 401; the 6th attempt is throttled; a grant from device A is rejected on device B; grants vanish on restart |
| 3 | `kids-allowance-tracking` | `PlaybackTracker`, daily usage, bonus, `Status`, `Report` (add-only), allowance in the profile and config page, prune task | 10 min of playback from jellyfin-web counts within ±30 s; paused time is not counted; two concurrent devices both count; a client report cannot reduce usage; the day resets at the configured "day starts at" hour |
| 4 | `kids-enforcement` (built as spec 005, plugin 0.0.5.0) | Spike: verify a plugin MVC filter fires on PlaybackInfo and HLS. Then `EnforcementLoop` (warnings, Stop, transcode kill) and `PlaybackGateFilter` (403 `timeUp` or `outsideSchedule`), with stops when the schedule closes, behind a config flag | Expiry stops Wholphin and jellyfin-web within 10 s; a new play attempt gets 403 with the JSON body; a bonus re-enables playback immediately; an admin or API key is never blocked; disabling the plugin removes all blocking |
| 5 | `kids-policy-helpers` | Recommended policy with dry-run diff, `AccessSchedules` editor (config page plus endpoint), share picked playlists with the kid, optional strict lock (schedule rewrite) with reconciler | Dry run shows the diff without writing; apply changes only the listed fields; the strict lock is restored after restart and when time is added; the schedule editor round-trips the existing entries |
| 6 | `kids-extras` | Per-kid lock override, activity history, export and import of plugin state (not covered by system backup), optional server-side session cap, optional 10.11 build | Export, wipe and import restore profiles and lock; history lists today's items per kid |

Spec numbers follow the order features are built, not this table: spec `004-kids-session-limit` (2026-10-19) adds the
per-kid `SessionLimitMinutes` that each TV counts locally, so enforcement (row 4) becomes spec 005. Spec
`006-kids-live-tv` (2026-10-20, plugin 0.0.6.0) adds per-kid live TV channels (§3.7), outside this table. Spec `007-kids-dashboard-ux` (2026-10-22, plugin
0.0.7.0) makes the dashboard show each kid's protection gaps with Fix links, the enforcement state, explicit "No limit"
choices and the live TV state, and adds Reset today. Spec `012-kids-avatar` (2026-10-24, plugin 0.0.8.0, named after
its branch) adds kid avatars to the profile and the list of kids (API 1.1, `ApiMinorVersion`), warns on the dashboard
when Jellyfin's parental control hides a picked live TV channel from a kid, and names Peke for Jellyfin as the client.
Spec `013-bonus-undo-break` (2026-10-26, plugin 0.0.9.0) gives bonuses ids and lets a grown-up undo one within 10
minutes, and end the break after the sitting limit early, for the Peke TV app's "Add time" widget (API 1.2).
Spec `014-kids-db-location` (2026-10-29, plugin 0.0.10.0) moves `kids.db` from `plugins/Jellyfin.Plugin.Kids/` (the
default `DataFolderPath`, which an admin nearly deleted as a stray install) to `<DataPath>/kids/` with a verified,
crash-safe move, a `README.txt` beside it and the location on the Kids page.

The Wholphin counterpart is a separate app feature (`ServerPluginSource`, detection, migration, the `Status` poller,
and handling 403 `timeUp`). It can start after feature 2 and gain the allowance after feature 3.

## 8. Open questions and risks

**Open questions**
1. Should time spent browsing (no playback) count? The proposal is no for v1.
2. Is a household-wide lock enough, or is a per-kid override needed for the MVP?
3. Is the day boundary server local midnight, or a configurable hour? Should time zones be per kid (a household
   across time zones)?
4. Should the **strict lock** (schedule rewrite) be offered at all, given its lockout risk?
5. Does an allowance of "0 minutes" on some weekdays need weekday-specific allowances in v1?
6. Should playback by the kid **as an additional user** in someone else's session count and be stopped? Decided 2026-10-18 (feature 3): it does not count; only the session owner is counted.
7. What should the route prefix be: keep `/Kids` (what the app study uses) or rename to `/Kids` to match the
   plugin name? The proposal is to keep `/Kids`.
8. Which licence? MIT keeps the option of copying from Trakt, Reports and session-provisioning. GPL-3.0 allows
   borrowing from Playback Reporting, Jellyfin-Enhanced and intro-skipper.

**Risks**
- **Jellyfin does not bind the header DeviceId to the token** (`AuthorizationContext.cs:144-147`): a token can claim
  any `DeviceId`. The plugin resolves the device from the token's registered device row (`IDeviceManager`) for
  throttling and grant binding, and caps wrong codes per user across devices (feature 2, research R2).
- **The gate filter on playback actions** (proven in feature 5: a convention-attached filter fires on every guarded
  route in 12.2, anonymous ones included when a token is in the query). Earlier note: global MVC filters from plugins
  work (Jellyfin-Enhanced), but
  gating `PlaybackInfo` and HLS specifically must be proven by the feature 4 spike. The fallback is stop-only
  enforcement plus the opt-in strict lock.
- **Direct play can be cut, but only as fast as the client's buffer drains** (feature 5 spike: ~4 s for a paced
  client, ~35 s for Chromium at 4 Mb/s). Earlier note: logout, ending the session and policy changes don't stop current
  playback on the 12.x Android TV client (benben42 measurements). We rely on clients honouring Stop, which Wholphin
  does.
- **Anonymous stream endpoints** (`Videos/{id}/stream`, HLS segments) let an already known URL keep playing.
  Mitigations: Stop commands, transcode kill, repeated stops while playback is reported, and short-lived direct URLs.
  Document it as a known limit.
- **Clients without a WebSocket**, or ones that ignore Playstate, will not stop on their own. The gate filter
  prevents the next item, but the current direct-play item may finish.
- **Automated progress ticks** can over-count up to 5 min for a crashed client unless filtered (§2.3).
- **Admins bypass everything**, both schedules and the gate. Warn loudly, and never let a kid be an admin.
- **Offline verifier cache**: a 4-digit code under PBKDF2 at 120k iterations is brute-forceable offline in seconds
  by anyone who extracts it from the TV. This is no worse than local mode, but recommend longer REMOTE codes and do
  not serve the verifier to kid tokens.
- **Policy read-modify-write races** with admin edits in the dashboard. `UpdatePolicyAsync` replaces everything.
  Keep the plugin's writes small and immediate, and re-read before each write.
- **Plugin data is not in Jellyfin system backups.** An export feature is needed.
- **12.x minor releases** might change controller or action names that the gate filter matches. Feature 5 checks them
  at startup and reports `Enforcement.Gate` in `/Kids/Info`; renamed media actions are still caught by their
  attributes. Original note: add a startup
  self-check that logs a warning if the expected actions are not found in `IActionDescriptorCollectionProvider`.
- **Loading on 12.0 and 12.1 is not verified at runtime** (accepted risk, decided 2026-10-17 for feature 1). The
  plugin compiles against the 12.0.0 packages with `targetAbi 12.0.0.0`, but the dev loop only runs 12.2
  (nixpkgs). Revisit if a load failure is reported, or before a stable release.
- **Bundled SQLite advisory**: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which Jellyfin 12.2 ships with
  Microsoft.Data.Sqlite 10.0.11, is flagged by GHSA-2m69-gcr7-jv3q. The plugin ships no SQLite of its own and uses
  the server's; only the test project pins a newer 2.1.x.

## Corrections to the prior study

- **API keys** get the `Administrator` role (`CustomAuthenticationHandler.cs:54-57`), and `Jellyfin-IsApiKey` is
  `"True"`. `IsInRole("Administrator")` therefore covers both admins and API keys.
- **Legacy auth**: the disabled carriers are confirmed. `?ApiKey=` (capitalised, no underscore) **still works**; only
  `?api_key=` is gone.
- **`[Authorize]` plugin endpoints fail for kids outside `AccessSchedules`**. Use `Policies.IgnoreParentalControl` for
  `Verify`, `Status` and profile reads, or a parent cannot unlock at bedtime from the kid's TV.
- **Plugin XML is not included in 12.x system backups** (the prior study said it was).
- **Storage**: profiles should not live in plugin XML. A dashboard save rewrites the whole object and races with
  API writes. Use SQLite in `<DataPath>/kids` (spec 014; first `DataFolderPath`), which is free because
  `Microsoft.Data.Sqlite` ships with the server.
- **Lock types**: the app now has `CHALLENGE`, `NUMERIC` and `REMOTE`, not only digits. The server stores the lock
  type, and CHALLENGE cannot back a grant.
- **Hard enforcement is feasible without touching policy**:
  - plugins share the server's DI and MVC options, so a global action filter can gate PlaybackInfo and HLS;
  - `EnableMediaPlayback` is **not** an enforcement lever;
  - `IsDisabled` revokes all tokens, so reject it.
- **Stream endpoints are anonymous**, so a stream-level block cannot rely on authorization.
- **Stop command**: `controllingSessionId` may be null (the control check is skipped). The server's own pattern is
  `SendPlaystateCommand(session.Id, session.Id, Stop)`. Messages need the client's WebSocket. Wholphin already handles
  Playstate and DisplayMessage.
- **The user-deleted event** is `Jellyfin.Data.Events.Users.UserDeletedEventArgs`, consumed with `IEventConsumer<T>`
  registered in DI.
- **Config pages**: 12.2 still renders legacy `data-role="page"` HTML through the React `ServerContentPage` and
  `viewManager`. `data-controller="__plugin/X"` is imported as an ES module.
- **12.1 and 12.2 release notes** contain no plugin-API breaks. Relevant fixes: device access revocation now logs out
  sessions, parental rating matching is case-insensitive, and the rating score is recomputed on save.
- **Device-lock endpoint** is dropped from v1 (the lock stays local).
