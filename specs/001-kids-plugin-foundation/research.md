# Research: Kids plugin foundation

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-17

Citations are relative to `.reference/` (Jellyfin and jellyfin-web at tag `v12.2`); `docs/plan.md` holds the wider
research this builds on. "12.0.0 package" means the NuGet `Jellyfin.*` 12.0.0 packages the plugin compiles against
(checked in `~/.nuget/packages/jellyfin.*/12.0.0`, XML docs and DLL strings).

## R1. Storage: SQLite through the server's Microsoft.Data.Sqlite

- **Decision**: one file `kids.db` in `Plugin.DataFolderPath`, opened with `Microsoft.Data.Sqlite`. Reference
  `Microsoft.Data.Sqlite.Core` 10.0.x with `ExcludeAssets=runtime` so nothing is shipped. Connection string
  `Data Source=<path>;Cache=Private;Pooling=False`. On open: `PRAGMA journal_mode=WAL`, `PRAGMA synchronous=NORMAL`,
  `PRAGMA foreign_keys=ON`. Writes go through a single `SemaphoreSlim(1)` writer; reads open their own connection.
  Schema versions with `PRAGMA user_version` and ordered, idempotent migration steps run in one transaction.
- **Initialisation timing**:
  - `KidsDatabase` takes `IApplicationPaths` and builds its folder as `PluginsPath/Jellyfin.Plugin.Kids`, which is
    what `BasePlugin` uses for `DataFolderPath` (`jellyfin/MediaBrowser.Common/Plugins/BasePluginOfT.cs:50`).
  - Its constructor touches nothing; `EnsureInitialized()` creates and migrates the file lazily, once.
  - The reason: `IPluginServiceRegistrator.RegisterServices` runs in `ApplicationHost.Init`
    (`jellyfin/Emby.Server.Implementations/ApplicationHost.cs:492`), before `CreatePlugins()` creates the plugin
    instance (`:652`, `:728`). `Plugin.Instance` is therefore null during registration, and an exception there
    marks the plugin Malfunctioned.
  - The orphan sweep calls `EnsureInitialized()` from its hosted service `StartAsync`, which runs after
    `InitializeServices` (`jellyfin/Jellyfin.Server/Program.cs:222, 235`).
- **Rationale**:
  - The server ships the assembly and the native `e_sqlite3`: `jellyfin/Directory.Packages.props:35` pins
    Microsoft.Data.Sqlite 10.0.11, referenced by `jellyfin/Emby.Server.Implementations/Emby.Server.Implementations.csproj:27`.
  - Assemblies that are not in the plugin's `deps.json` fall back to the server's copy
    (`jellyfin/Emby.Server.Implementations/Plugins/PluginLoadContext.cs:23-32`). The assembly version is 10.0.0.0 on
    every 10.0.x patch, so compiling against 10.0.9 binds to whatever 10.0.x the server ships.
  - **Never shared cache**: it is process-wide, and the server warns that a plugin enabling it causes SQLITE_LOCKED
    in the server (`jellyfin/src/Jellyfin.Database/Jellyfin.Database.Providers.Sqlite/SqliteDatabaseProvider.cs:64-73`).
  - The server calls `SqliteConnection.ClearAllPools()` on shutdown and backup (same file :155, :176, :319). With
    `Pooling=False` the plugin never holds the file open between calls, so the plugin folder can be deleted on
    uninstall (a known pooling pitfall on Windows).
  - Kid profiles are written rarely (parents editing), so per-call connections cost nothing measurable.
  - `user_version` migrations keep data across layout changes (FR-005). Playback Reporting drops its table on a
    schema mismatch (`jellyfin-plugin-playbackreporting/.../Data/ActivityRepository.cs:62-77`), which loses data;
    Streamyfin (`streamyfin_jellyfin-plugin-streamyfin/Jellyfin.Plugin.Streamyfin/Storage/Database.cs:22-52, 222-255`)
    uses WAL and a writer lock but no versioning. We take WAL and the writer lock, not their schema handling.
- **Alternatives considered**:
  - Plugin XML configuration: a dashboard save replaces the whole object and races with API writes (docs/plan.md
    §2.6). Rejected by the constitution (Technical Constraints).
  - The server's EF database: no plugin extension point (docs/plan.md §2.6).
  - EF Core on SQLite (intro-skipper's approach): heavier, and must match the server's EF version exactly; one
    table does not need it.
  - JSON file per kid: no atomic compare-and-set for revisions without extra locking; harder to extend for usage
    data in feature 3.

## R2. Profile row layout: one row per kid, settings as JSON

- **Decision**: table `kid_profile(user_id TEXT PRIMARY KEY, revision INTEGER NOT NULL, settings TEXT NOT NULL,
  created_at TEXT NOT NULL, updated_at TEXT NOT NULL)`. `user_id` is the Jellyfin user id in `N` format. `settings`
  is the JSON of the profile settings (see [data-model.md](data-model.md)).
- **Rationale**: the settings are always read and written as a whole; adding a field later (feature 3 allowance)
  is a JSON change with a default, not a migration. The revision stays a real column so the compare-and-set is a
  single `UPDATE … WHERE user_id = $id AND revision = $base`.
- **Alternatives considered**: one column per setting plus child tables for sections and picks: more migrations for
  no query benefit.

## R3. Revisions and conflicts

- **Decision**:
  - Create (mark): `INSERT` with revision 1; a primary-key violation means the user is already a kid → 409 with the
    current profile.
  - Update: `UPDATE … SET revision = revision + 1 … WHERE user_id = $id AND revision = $base`. Zero rows → read the
    current row: missing → 404, present → 409 with it.
  - Delete (unmark): `DELETE … WHERE user_id = $id`; zero rows → 404.
  - All under the single writer, each in one transaction.
- **Rationale**: SQLite serialises writers, and the conditional update makes the check and the write one statement,
  so exactly one of two concurrent changes on the same revision wins (SC-005). This is pure logic that the
  constitution requires to be unit tested (principle VI).

## R4. Authorization and caller identity

- **Decision**:
  - Every `/Kids` endpoint except `Info` uses `[Authorize(Policy = Policies.IgnoreParentalControl)]`, and the
    controller decides "self", "admin" or "forbidden" through a small `CallerContext`.
  - Mutations require `CallerContext.IsAdmin`. In feature 2 the same check also accepts a valid `X-Kids-Grant`, so
    only `CallerContext` changes.
  - `Info` is `[AllowAnonymous]`.
  - `CallerContext` uses `IAuthorizationContext.GetAuthorizationInfo(HttpContext)`: `IsApiKey`, `UserId`, and
    `User.HasPermission(PermissionKind.IsAdministrator)` decide admin.
  - Refusals use `Forbid()` (403); signed-in callers never get 401 for lack of rights (FR-016).
- **Rationale**:
  - Policy wiring: `IgnoreParentalControl` is the default requirement without the schedule check
    (`jellyfin/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:76`). The plain `[Authorize]` fails kids
    outside their `AccessSchedules` (`jellyfin/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs:81-85`),
    which would break FR-014.
  - `RequiresElevation` is only the Administrator role claim (`ApiServiceCollectionExtensions.cs:86-89`). Using it
    on mutations would have to be undone in feature 2, so it is not used.
  - The constants are public in `MediaBrowser.Common/Api/Policies.cs:16,26` and present in the 12.0.0 package.
  - `IAuthorizationContext.GetAuthorizationInfo(HttpContext)` is a public, async API in the 12.0.0 package
    (`jellyfin/MediaBrowser.Controller/Net/IAuthorizationContext.cs:16`; fields in `AuthorizationInfo.cs:16-67`).
    Unlike the claim names (`Jellyfin.Api/Constants/InternalClaimTypes.cs`, not on NuGet), it does not depend on
    internal strings, so no startup self-check is needed (constitution V).
  - API keys are administrators (`jellyfin/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs:53-58`) and have
    `UserId == Guid.Empty`, so `IsApiKey` counts as admin.
  - Jellyfin's own self-or-admin helpers (`jellyfin/Jellyfin.Api/Helpers/RequestHelpers.cs:67-111`) are internal, so
    the plugin keeps its own equivalent: a pure function, unit tested.
  - Status codes: `CustomAuthenticationHandler` does not override Challenge or Forbid
    (`CustomAuthenticationHandler.cs:19-89`), so a missing or invalid token gets 401 and a failed policy or `Forbid()`
    gets 403.
- **Alternatives considered**:
  - Reading the `Jellyfin-UserId` and `Jellyfin-IsApiKey` claims directly: it is synchronous, but depends on
    internal names.
  - A custom `IAuthorizationHandler`: unnecessary for this feature.

## R5. JSON shape of the contract

- **Decision (user, 2026-10-17: Q1 A)**: follow Jellyfin's formatters, with no custom formatter. Responses are PascalCase by
  default, and camelCase when the client sends `Accept: application/json; profile="CamelCase"`. Enums are strings,
  and null properties are omitted. Request bodies are case-insensitive.
- **Rationale**:
  - The formatters are global and apply to plugin controllers with no code
    (`jellyfin/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:121-160`;
    `jellyfin/Jellyfin.Api/Formatters/PascalCaseJsonProfileFormatter.cs:16-21`;
    `jellyfin/src/Jellyfin.Extensions/Json/JsonDefaults.cs:29-45`).
  - Every Jellyfin client already handles both shapes.
  - docs/plan.md §4.1 wrote the DTOs in camelCase only as notation.
- **Alternatives considered**: forcing camelCase for `/Kids` with a per-controller output formatter. It would differ
  from the rest of the Jellyfin API and needs its own formatter wiring.

## R6. Errors and validation answers

- **Decision**:
  - Validation errors return 400 with `ValidationProblemDetails`, built in the controller and keyed by field
    (`AutoplayCap`, `Sections[1]`, `PickedItemIds[2]`).
  - **Section names travel as strings** in the request DTO and are converted by the validator. With an enum
    property, an unknown name would fail JSON binding in `JsonStringEnumConverter`
    (`jellyfin/src/Jellyfin.Extensions/Json/JsonDefaults.cs:35-45`), and `[ApiController]` would answer its
    own 400 keyed like `$.Sections[0].Section` before the validator runs.
  - `Revision` is `long?` with `[Required]`, so a missing value gets the automatic 400 keyed `Revision` and never
    means "create".
  - A malformed GUID in `PickedItemIds` still fails at binding (key `$.PickedItemIds[i]`). This is acceptable,
    since the editor only sends ids it received from the server.
  - Conflicts return 409 with the current `KidProfileDto`.
  - Not a kid, or an unknown user, returns 404 with `ProblemDetails`.
  - A store failure returns 500, and is never turned into an empty list.
- **Rationale**: Jellyfin's `ExceptionMiddleware` writes `text/plain` and maps `ArgumentException` to 400 and
  `ResourceNotFoundException` to 404 (`jellyfin/Jellyfin.Api/Middleware/ExceptionMiddleware.cs:92-99, 123-134`).
  That is not enough to name the invalid field (FR-010, FR-027), so the controller returns explicit results.
  Unexpected exceptions still reach the middleware and become a 500, which matches the "store broken" edge case.

## R7. Cleanup of deleted users

- **Decision**:
  - `IEventConsumer<UserDeletedEventArgs>`, registered scoped in `PluginServiceRegistrator`, deletes the profile.
  - A hosted service `OrphanProfileSweeper` removes, in `StartAsync`, the profiles whose user id is not in
    `IUserManager.GetUsersIds()`.
- **Rationale**:
  - The event is published after the user row is removed (`jellyfin/Jellyfin.Server.Implementations/Users/UserManager.cs:410-415`).
    The type is `Jellyfin.Data.Events.Users.UserDeletedEventArgs`, and the consumer interface is
    `MediaBrowser.Controller.Events.IEventConsumer<T>`; both are in the 12.0.0 packages.
  - The server registers its own consumer the same way (`Jellyfin.Server.Implementations/Events/EventingServiceCollectionExtensions.cs:65-66`).
  - Consumers are resolved in a new scope, and their exceptions are logged without breaking the delete
    (`Jellyfin.Server.Implementations/Events/EventManager.cs:44-58`).
  - Startup order:
    1. Migrations run.
    2. `appHost.InitializeServices`.
    3. `_jellyfinHost.StartAsync()`, which starts the plugin hosted services.
    4. Startup tasks (`jellyfin/Jellyfin.Server/Program.cs:203-253`).
  - `GetUsersIds()` reads the database directly (`UserManager.cs:99-114`), so the sweep is safe in `StartAsync`.
  - On a fresh server with no users yet (wizard pending) the sweep must skip, not delete: an empty user list means
    "nothing to compare", and there are no profiles anyway.
- **Alternatives considered**:
  - A scheduled task: it runs later and adds a task to the dashboard for no benefit.
  - A lazy check on each read: it spreads the cleanup across endpoints.

## R8. Listing users for the dashboard page

- **Decision**: the Kids page uses Jellyfin's own `GET /Users` (`ApiClient.getUsers()`) for names and
  `Policy.IsAdministrator`, and `GET /Kids/Profiles` for the kid flags. No plugin "overview" endpoint in this
  feature.
- **Rationale**:
  - `GET /Users` returns every user, including hidden and disabled ones, with the full `UserPolicy`
    (`jellyfin/Jellyfin.Api/Controllers/UserController.cs:91-100, 602-637`; `IsAdministrator` at
    `UserManager.cs:486, 497`).
  - This follows principle I (leverage Jellyfin).
  - `GET /Kids/Admin/Overview` (docs/plan.md §4.1) belongs to feature 3, when usage data exists.

## R9. Collections and playlists for the picks chooser

- **Decision**:
  - Validation and listing go through `ILibraryManager` with `User = null`.
  - A new admin-only endpoint `GET /Kids/Admin/PickCandidates?search=&ids=&limit=` returns `{Id, Name, Type}` for
    BoxSets and Playlists, ordered by name.
  - `ids=` resolves stored picks, so the editor can show names and spot missing ones.
  - Validation of a new pick uses `GetItemById(id)` and checks `is BoxSet or Playlist`.
- **Rationale**:
  - With `User == null`, `LibraryManager.GetItemList` skips user filtering
    (`jellyfin/Emby.Server.Implementations/Library/LibraryManager.cs:1740-1756`).
  - The server lists all playlists and collections the same way (`Emby.Server.Implementations/Playlists/PlaylistManager.cs:70-77`,
    `Library/Validators/CollectionPostScanTask.cs:108-113`).
  - Query fields: `IncludeItemTypes`, `Recursive`, `SearchTerm`/`NameContains`, `OrderBy`, `Limit`
    (`jellyfin/MediaBrowser.Controller/Entities/InternalItemsQuery.cs:172-228, 415, 507`).
  - Classes: `MediaBrowser.Controller.Entities.Movies.BoxSet` (`BoxSet.cs:18`) and
    `MediaBrowser.Controller.Playlists.Playlist` (`Playlist.cs:22`).
  - Jellyfin's `/Items` cannot replace this endpoint. An admin does not see other users' private playlists there,
    because `Playlist.IsVisible` has no admin bypass (`Playlist.cs:234-258, 278-285`;
    `Folder.cs:1009-1033`). The spec requires all of the server's playlists (Assumptions; Clarification Q1).
- **Alternatives considered**: `/Items` with an API key (no user filter, `ItemsController.cs:344`). The dashboard
  page has the admin's token, not an API key, so that route is unavailable to it.

## R10. Dashboard pages in jellyfin-web 12.2

- **Decision**: two embedded pages, each with an ES module controller.
  - `Kids` (list, `EnableInMainMenu = true`, `MenuIcon = "child_care"`) with `data-controller="__plugin/KidsJs"`.
  - `KidsUser` (editor, reached at `#/configurationpage?name=KidsUser&userId=<id>`) with
    `data-controller="__plugin/KidsUserJs"`.

  Modules use `export default function (view, params)` and the `viewshow` event.
  - Calls go through `ApiClient.getUsers`, `ApiClient.ajax` / `ApiClient.getUrl`.
  - Feedback uses `Dashboard.showLoadingMsg` / `hideLoadingMsg` / `alert` / `confirm` / `navigate`.
  - Section order uses up and down `paper-icon-button-light` buttons, like the library options editor.
  - The picks chooser is an `emby-input` search box with a result list, plus up, down and remove buttons per pick.
- **Rationale**:
  - The `configurationpage` route is admin-only and fetches the page HTML (`jellyfin-web/src/apps/dashboard/routes/routes.tsx:15,48-49`;
    `jellyfin-web/src/components/ServerContentPage.tsx:22,38`).
  - `__plugin/X` is dynamically imported as `configurationpage?name=X` (`jellyfin-web/src/components/viewContainer.js:14-25`)
    and constructed as `new module.default(view, params)`, where `params` comes from the URL query
    (`jellyfin-web/src/components/viewManager/viewManager.js:23-28, 100-108`).
  - The globals exist: `jellyfin-web/src/lib/jellyfin-apiclient/ServerConnections.js:95`;
    `jellyfin-web/src/utils/dashboard.js:135-147, 240-267`. There is no `window.require`, so no `toast` module.
  - `sortablejs` is not global (`jellyfin-web/src/elements/emby-itemscontainer/emby-itemscontainer.js:2,133,306`).
    The core uses up and down buttons (`jellyfin-web/src/components/libraryoptionseditor/libraryoptionseditor.js:85-87`).
  - Precedent for multi-page plugins: Playback Reporting `Pages/*.js` (`export default function (view, params)`)
    and Streamyfin `Pages/shared.js:150-171`. Both are studied only, not copied.
  - The page HTML is served anonymously (docs/plan.md §2.7), so it holds no data (FR-022).
- **Alternatives considered**:
  - A single page with an inline editor panel: a longer module, and no deep link to a kid.
  - React or a bundler: not available to plugin pages.
  - Drag and drop: it needs its own implementation, and the buttons also work with a TV remote and a keyboard.

## R11. Tests

- **Decision**:
  - Add `tests/Jellyfin.Plugin.Kids.Tests` (net10.0) using xUnit v3 (the stack Jellyfin itself uses:
    `jellyfin/Directory.Packages.props:86-87`) and `Microsoft.NET.Test.Sdk`.
  - No mocking library. The services depend on two small plugin-owned interfaces (`IKidsUserDirectory`,
    `IPickCatalog`) with Jellyfin-backed implementations, so the tests use hand-written fakes.
  - The store is tested against a real SQLite file in a temp dir.
  - Server-dependent behaviour is checked by `scripts/dev-check.sh` (curl against the dev loop): authorization,
    anonymous `Info`, 403 for a kid token, the user-deleted cleanup, and the page HTML holding no data.
- **Rationale**:
  - Constitution VI: pure logic is unit tested (validation, revisions and conflicts, access decisions, migrations,
    orphan sweep); server behaviour is verified on the dev server.
  - Jellyfin's integration test host (`jellyfin/tests/Jellyfin.Server.Integration.Tests/JellyfinApplicationFactory.cs:28`)
    needs `Jellyfin.Server` from source, which is not on NuGet.
  - The MIT plugins have no tests to reuse.
- **Alternatives considered**:
  - Moq and AutoFixture as in Jellyfin: these are not cached locally, and two interfaces don't justify them.
  - xUnit v2.9.2: it is cached for offline restore, but it differs from the server's stack. This can switch if
    restores must work offline. Note that xunit.v3 is not in the local cache, so the first restore needs network.

## R12. Loading on 12.0 and 12.2

- **Decision**: keep compiling against `Jellyfin.Controller`/`Jellyfin.Model` 12.0.0 with `targetAbi 12.0.0.0`
  (already in the csproj and `build.yaml`). Every API used here was checked to exist in the 12.0.0 packages (R4,
  R7, R9). Runtime checks run only on 12.2, the version in the current dev loop (nixpkgs).
- **Accepted risk (user, 2026-10-17: Q2 C)**:
  - Loading on 12.0 and 12.1 is **not verified at runtime**: there is no `dev-12.0` loop and no manual 12.0 check.
  - The guarantee rests on compiling against the 12.0.0 reference assemblies, plus the 12.1 and 12.2 release notes
    listing no plugin-API breaks (docs/plan.md, "Corrections").
  - A break would only show up as the plugin being marked `Malfunctioned` on a 12.0 or 12.1 server.
  - To revisit if a user reports a load failure there, or before a stable release.
- **Rationale**: constitution V and docs/plan.md §6.4.
- **Alternatives considered (rejected by the user)**:
  - a `dev-12.0` variant of the dev loop with the official 12.0.x build;
  - a manual one-off check on 12.0 before the first release.

## R13. What the plugin information reports

- **Decision**: `GET /Kids/Info` returns `{PluginVersion, ApiVersion: 1, Features: ["profiles"], LockType: null}`.
  With null omission, `LockType` is absent until feature 2.
- **Rationale**: FR-002 and FR-003. Clients detect capabilities by `Features`, so later features only append to it,
  with no API version bump.

## R14. Marking a kid that already exists (Wholphin's migration)

- **Decision (user, 2026-10-17: Q3 A)**: `PUT … {"Revision":0, …}` on an existing kid returns 409 with the
  server's profile, and nothing is overwritten. Wholphin's one-time migration then keeps the server's profile.
  There is no `?overwrite` option.
- **Rationale**: it is the same conflict rule as every other write. The server stays the source of truth for
  household-wide settings (docs/plan.md §1).
- **Alternatives considered**: `?overwrite=true` for admins (rejected: it is a second write path that bypasses
  revisions).
