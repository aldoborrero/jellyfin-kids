---

description: "Task list for the Kids plugin foundation"
---

# Tasks: Kids plugin foundation

**Input**: Design documents from `specs/001-kids-plugin-foundation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/kids-api-v1.md,
contracts/dashboard-pages.md, quickstart.md

**Tests**: included. The constitution (principle VI) requires unit tests for pure logic: revision conflicts,
validation, access decisions and migrations. It also requires checks against the dev server for behaviour that
depends on Jellyfin. Write the unit tests of each phase first, and see them fail before implementing.

**Organization**: tasks are grouped by user story. Story numbers follow spec.md:

| Story | Title | Priority |
|---|---|---|
| US1 | Detection | P1 |
| US2 | Mark and unmark from the dashboard | P1 |
| US3 | Profiles API with revisions | P1 |
| US4 | Dashboard editor | P2 |
| US5 | Cleanup on user deletion | P3 |

The three P1 stories run in dependency order US1 → US3 → US2, because the dashboard list (US2) calls the API that
US3 adds.

## Conventions for every task

- **Projects**: plugin in `src/Jellyfin.Plugin.Kids/` (namespace `Jellyfin.Plugin.Kids.*`), tests in
  `tests/Jellyfin.Plugin.Kids.Tests/`.
- **Build gate**: `TreatWarningsAsErrors` with StyleCop, SerilogAnalyzer and MultithreadingAnalyzer. Every public
  type and member needs XML docs. Match the style of the existing `Plugin.cs` and `Api/KidsController.cs`.
- **JSON**: Jellyfin's default formatters. PascalCase property names in C#, with no custom formatter or `[JsonPropertyName]`.
  Enums are serialised as strings by Jellyfin's options (research R5).
- **Jellyfin internals**: no `Jellyfin.Api` types (not on NuGet). Use `MediaBrowser.Common.Api.Policies`,
  `MediaBrowser.Controller.Net.IAuthorizationContext`, `IUserManager`, `ILibraryManager`, and
  `Jellyfin.Data.Events.Users.UserDeletedEventArgs` (research R4, R7, R9).
- **Server's own assemblies**: nothing from the server is shipped. Every package reference that the server already
  provides has `ExcludeAssets=runtime`, and the `build.yaml` artifacts stay `Jellyfin.Plugin.Kids.dll` only.
- **Unit tests**: `dotnet test Jellyfin.Plugin.Kids.slnx`.
- **Server checks**: `dev` running, then `scripts/dev-check.sh` (quickstart.md §2).

---

## Phase 1: Setup (shared infrastructure)

**Purpose**: dependencies, the test project and the server-check script skeleton.

- [ ] T001 Add `<PackageReference Include="Microsoft.Data.Sqlite.Core" Version="10.0.9"><ExcludeAssets>runtime</ExcludeAssets></PackageReference>` and `<InternalsVisibleTo Include="Jellyfin.Plugin.Kids.Tests" />` to src/Jellyfin.Plugin.Kids/Jellyfin.Plugin.Kids.csproj (research R1: the server ships Microsoft.Data.Sqlite and e_sqlite3; never ship them)
- [ ] T002 Create the test project tests/Jellyfin.Plugin.Kids.Tests/Jellyfin.Plugin.Kids.Tests.csproj.
  - Target `net10.0` with `Nullable` enabled and `IsPackable` false.
  - Packages: `xunit.v3`, `xunit.runner.visualstudio` 3.x and `Microsoft.NET.Test.Sdk`, plus `Microsoft.Data.Sqlite`
    10.0.9 **with** runtime assets so the tests can open SQLite.
  - Add a ProjectReference to the plugin csproj, and add the project to Jellyfin.Plugin.Kids.slnx.
  - Research R11: no Moq or AutoFixture. The first restore needs network.
  - Same quality gate as the plugin: `TreatWarningsAsErrors` true, `AnalysisMode` `AllEnabledByDefault`, and the
    StyleCop and SerilogAnalyzer references (`PrivateAssets=All`). Test-only suppressions (for example CA1707 for
    test method names) go in tests/Jellyfin.Plugin.Kids.Tests/.editorconfig, not in code.
- [ ] T003 [P] Create scripts/dev-check.sh (executable, `set -euo pipefail`) with helpers only:
  - **Helpers**:
    - `J=${JELLYFIN_URL:-http://127.0.0.1:8096}`;
    - an `auth` header builder (same client string style as scripts/dev-provision.sh);
    - `login <user> <password>` → token, via `POST /Users/AuthenticateByName`;
    - `admin_token` read from `$JELLYFIN_KIDS_DEV/admin.token`;
    - `check <name> <expected-status> <curl args…>` that prints `PASS`/`FAIL` and counts failures;
    - a non-zero exit if any check failed.
  - **Checks**: none yet; later tasks append them in quickstart.md §2 order.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the domain model, the SQLite store, caller identity and DI wiring that US3, US2, US4 and US5 all use.

**⚠️ No user story work can begin until this phase is complete.**

### Tests first

- [ ] T004 [P] Write tests/Jellyfin.Plugin.Kids.Tests/HomeSectionTests.cs:
  - `HomeSectionEntry.Normalize` appends missing sections as visible in the default order.
  - Normalize keeps the given order.
  - The default list is exactly `KeepWatching, Picked, Libraries, New, Favourites`, all visible.
- [ ] T005 [P] Write tests/Jellyfin.Plugin.Kids.Tests/KidsDatabaseTests.cs (temp dir per test, deleted in `Dispose`):
  - Opening a missing file creates it at `user_version` 1 with table `kid_profile`.
  - Opening again is a no-op.
  - A file with a higher `user_version` than the code knows makes `KidsDatabase` report "unusable" rather than
    touching it.
  - The connection string has `Cache=Private` and `Pooling=False`, and `journal_mode` is `wal`.
  - **Constructing `KidsDatabase` touches nothing**: no folder, no file, no connection. Only the first
    `EnsureInitialized()` (or the first store call) creates `kids.db`. Calling `EnsureInitialized()` concurrently
    runs the migrations once.
- [ ] T006 [P] Write tests/Jellyfin.Plugin.Kids.Tests/KidProfileStoreTests.cs (temp-dir SQLite):
  - `Insert` gives revision 1, and inserting the same user again reports AlreadyExists.
  - Two concurrent `Insert` calls for the same user (run with `Task.WhenAll`): exactly one is Created, and the other
    is AlreadyExists carrying the winner's profile.
  - `UpdateIfRevision(base = current)` gives revision current+1 and stores the settings.
  - `UpdateIfRevision(base = stale)` reports Conflict and leaves the row untouched.
  - Update on a missing row reports NotFound.
  - Two concurrent `UpdateIfRevision` calls with the same base (run with `Task.WhenAll`): exactly one succeeds.
  - `Delete` returns true then false.
  - `List` returns `(UserId, Revision)` pairs.
  - Settings JSON with unknown properties reads back fine, and missing properties take their defaults.
- [ ] T007 [P] Write tests/Jellyfin.Plugin.Kids.Tests/ProfileAccessTests.cs:
  - `CanRead`: self, admin and API key → true; another non-admin user → false.
  - `CanMutate`: admin and API key → true; any non-admin (including self) → false.

### Implementation

- [ ] T008 [P] Create src/Jellyfin.Plugin.Kids/Profiles/HomeSection.cs:
  - `enum HomeSection { KeepWatching, Picked, Libraries, New, Favourites }`. The order is the default order and
    matches Wholphin's `KidsHomeSection`.
  - `record HomeSectionEntry(HomeSection Section, bool Visible = true)`, with a static `Default` list.
  - `static IReadOnlyList<HomeSectionEntry> Normalize(IEnumerable<HomeSectionEntry>)` that appends missing sections
    as visible, in default order. It does not drop duplicates; the validator rejects them.
- [ ] T009 [P] Create src/Jellyfin.Plugin.Kids/Profiles/KidProfile.cs:
  - A record `KidProfileSettings` with `SearchAllowed` (default false), `AutoplayEnabled` (default false),
    `AutoplayCap` (default 3, "1–10 inclusive"), `Sections` (default `HomeSectionEntry.Default`) and
    `PickedItemIds` (default empty, ordered).
  - A record `KidProfile(Guid UserId, long Revision, KidProfileSettings Settings)`.
  - Constants `MinAutoplayCap = 1`, `MaxAutoplayCap = 10`, `DefaultAutoplayCap = 3`, `MaxPicks = 50`.
- [ ] T010 Create src/Jellyfin.Plugin.Kids/Storage/KidsDatabase.cs (data-model.md "Storage layout"; research R1):
  - **Constructor**: `KidsDatabase(IApplicationPaths paths, ILogger<KidsDatabase> logger)` (plus an internal
    constructor that takes a folder path, for tests).
    - The path is `Path.Combine(paths.PluginsPath, "Jellyfin.Plugin.Kids", "kids.db")`. This is the same folder as
      `BasePlugin.DataFolderPath` (MediaBrowser.Common/Plugins/BasePluginOfT.cs:50).
    - It must **not** use `Plugin.Instance`: plugin services are registered in `ApplicationHost.Init`
      (Emby.Server.Implementations/ApplicationHost.cs:492), before `CreatePlugins()` creates the plugin instance
      (:652, :728), so the instance is still null there.
    - The constructor only stores the path and logger. It touches no file and opens no connection.
  - `EnsureInitialized()` is lazy and thread-safe, and runs once:
    1. creates the folder if missing;
    2. reads `PRAGMA user_version`;
    3. runs migration steps up to the latest in one transaction;
    4. step 1 creates `kid_profile(user_id TEXT PRIMARY KEY, revision INTEGER NOT NULL CHECK (revision >= 1),
       settings TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL)` (no `STRICT`; the column
       types are enforced by the store).
  - **Failure handling**: a higher unknown version, or any open or migration failure, sets `IsUsable = false` and
    logs an error. It never throws to the caller, and profile calls then fail with 500 instead of answering
    "no kids".
  - **Callers**: every `KidProfileStore` method calls `EnsureInitialized()` first, and `OrphanProfileSweeper.StartAsync`
    (T035) calls it at startup.
  - `OpenConnection()` returns an open `SqliteConnection` with `Data Source=…;Cache=Private;Pooling=False`, and runs
    `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON`.
  - A `SemaphoreSlim(1)` writer lock is exposed as `WriteAsync(Func<SqliteConnection, SqliteTransaction, Task<T>>)`.
  - Never enable shared cache (SqliteDatabaseProvider.cs:64-73).
  - Makes T005 pass.
- [ ] T011 Create src/Jellyfin.Plugin.Kids/Storage/KidProfileStore.cs on top of `KidsDatabase` (research R2, R3):
  - `GetAsync(userId)` and `ListAsync()` → `(UserId, Revision)`.
  - `InsertAsync(userId, settings)` → Created(profile) | AlreadyExists(current).
  - `UpdateIfRevisionAsync(userId, baseRevision, settings)` → Updated(profile) | Conflict(current) | NotFound, using
    `UPDATE kid_profile SET revision = revision + 1, settings = $s, updated_at = $now WHERE user_id = $id AND revision = $base`.
    Zero rows → re-read to tell Conflict from NotFound.
  - `DeleteAsync(userId)` → bool.
  - `DeleteManyAsync(IEnumerable<Guid>)` → count.
  - Writes run under the writer lock in one transaction.
  - User ids are stored in `Guid.ToString("N")` format.
  - Settings use System.Text.Json; unknown properties are ignored and missing ones take defaults.
  - Timestamps are ISO 8601 UTC.
  - Makes T006 pass.
- [ ] T012 [P] Create src/Jellyfin.Plugin.Kids/Auth/ProfileAccess.cs (pure static rules `CanRead(caller, userId)` and `CanMutate(caller)`) and src/Jellyfin.Plugin.Kids/Auth/CallerContext.cs (record `UserId`, `IsAdmin`, `IsApiKey`, plus `static async Task<CallerContext> FromAsync(IAuthorizationContext, HttpContext)` using `GetAuthorizationInfo(HttpContext)`; `IsAdmin = info.IsApiKey || info.User?.HasPermission(PermissionKind.IsAdministrator) == true`) per research R4; makes T007 pass. Feature 2 will extend `CanMutate` with grants, so keep it the single place for that rule
- [ ] T013 [P] Create the seams src/Jellyfin.Plugin.Kids/Profiles/IKidsUserDirectory.cs (`bool UserExists(Guid)`, `IReadOnlyCollection<Guid> GetAllUserIds()`) and src/Jellyfin.Plugin.Kids/Profiles/IPickCatalog.cs (`IReadOnlyList<PickCandidate> Resolve(IEnumerable<Guid> ids)`, `IReadOnlyList<PickCandidate> Search(string? term, int limit)`; `record PickCandidate(Guid Id, string Name, PickType Type)`, `enum PickType { BoxSet, Playlist }`), plus test fakes tests/Jellyfin.Plugin.Kids.Tests/Fakes/FakeUserDirectory.cs and tests/Jellyfin.Plugin.Kids.Tests/Fakes/FakePickCatalog.cs
- [ ] T014 [P] Create src/Jellyfin.Plugin.Kids/Profiles/JellyfinUserDirectory.cs (`IUserManager.GetUserById(id) is not null`; `IUserManager.GetUsersIds()`) and src/Jellyfin.Plugin.Kids/Profiles/JellyfinPickCatalog.cs (`ILibraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.BoxSet, BaseItemKind.Playlist], Recursive = true, NameContains = term (substring, case-insensitive; not `SearchTerm`, which switches Jellyfin to its search mode), OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)], Limit = limit })` with **no User** so every collection and playlist is listed; `Resolve` uses `GetItemById(id)` and keeps only `BoxSet`/`Playlist`) per research R9 (LibraryManager.cs:1740-1756)
- [ ] T015 Create src/Jellyfin.Plugin.Kids/PluginServiceRegistrator.cs implementing `IPluginServiceRegistrator`:
  - Plain type registrations only, as singletons: `KidsDatabase`, `KidProfileStore`, `IKidsUserDirectory` →
    `JellyfinUserDirectory`, `IPickCatalog` → `JellyfinPickCatalog`.
  - Nothing is constructed or initialised here, and nothing reads `Plugin.Instance`. `RegisterServices` runs before
    the plugin instance exists (ApplicationHost.cs:492 vs :728), and any exception here marks the plugin
    Malfunctioned (docs/plan.md §2.1).
  - Singletons are built by DI on first resolution, after `CreatePlugins()`. The database file is created lazily by
    `KidsDatabase.EnsureInitialized()` (T010).
  - Later phases add the service, the consumer and the hosted service here.

**Checkpoint**: `dotnet build` and `dotnet test` pass, and the plugin still loads in the dev loop (status Active, no
Malfunctioned). `kids.db` appears in `.dev/data/plugins/Jellyfin.Plugin.Kids/` only after the first profile call.

---

## Phase 3: User Story 1 - A client detects the plugin (Priority: P1) 🎯 MVP

**Goal**: anonymous `GET /Kids/Info` reports the plugin version, `ApiVersion` 1 and `Features` `["profiles"]`, with
no household data.

**Independent Test**: on the 12.2 dev server, `curl http://127.0.0.1:8096/Kids/Info` with no token returns 200 with
those fields; a server without the plugin returns 404 (spec US1; quickstart §2a).

- [ ] T016 [US1] Extend src/Jellyfin.Plugin.Kids/Api/KidsInfo.cs:
  - Make it `record KidsInfo(string PluginVersion, int ApiVersion, IReadOnlyList<string> Features, string? LockType)`.
  - `LockType` stays null in this feature; Jellyfin's options omit nulls.
  - Update `GetInfo` in src/Jellyfin.Plugin.Kids/Api/KidsController.cs to return `Features = ["profiles"]`, keeping
    `[AllowAnonymous]` (contracts/kids-api-v1.md "GET /Kids/Info"; research R13).
- [ ] T017 [US1] Append check (a) to scripts/dev-check.sh. `GET /Kids/Info` without a token must return:
  - status 200;
  - `.ApiVersion == 1` and `.Features` containing `"profiles"`;
  - a body with no GUID-shaped strings.

**Checkpoint**: US1 is shippable on its own. Wholphin can detect plugin mode.

---

## Phase 4: User Story 3 - Clients read and change a kid's settings, with conflict detection (Priority: P1)

**Goal**: the `/Kids/Profiles` and `/Kids/Users/{userId}/Profile` (GET, PUT, DELETE) endpoints with self-or-admin
access, validation, revisions and 409 conflicts.

**Independent Test**: quickstart.md §2 checks b–i and m. These cover:
- a kid reads their own profile but gets 403 on another kid's profile and on any write;
- an admin update with a stale revision gets 409 with the current profile;
- invalid settings get 400 naming the field;
- everything is readable outside allowed hours (check m).

### Tests for User Story 3

- [ ] T018 [P] [US3] Write tests/Jellyfin.Plugin.Kids.Tests/KidProfileValidatorTests.cs (data-model.md "Validation"):
  - `AutoplayCap` 0 and 11 → error keyed `AutoplayCap`; 1 and 10 → ok.
  - A duplicate section → error keyed `Sections[i]`.
  - An unknown section **string** (`"Shorts"`), an empty or null section, or a number → error keyed `Sections[i]`.
    Known names match case-insensitively (`"keepwatching"` is accepted).
  - Missing sections are appended (normalised output).
  - Duplicate pick ids → error keyed `PickedItemIds[i]`.
  - 51 picks → error keyed `PickedItemIds`.
  - A new pick that `FakePickCatalog` does not resolve → error `PickedItemIds[i]` "Not a collection or playlist".
  - A pick already in the stored profile that no longer resolves → accepted.
  - Several errors are returned together.
- [ ] T019 [P] [US3] Write tests/Jellyfin.Plugin.Kids.Tests/KidProfileServiceTests.cs (`FakeUserDirectory`, `FakePickCatalog`, temp-dir store):
  - Mark (revision 0) of an existing Jellyfin user → profile at revision 1 with defaults.
  - Mark of an unknown user → NotFound.
  - Mark of an existing kid → Conflict carrying the current profile; nothing changes (research R14).
  - Update with the current revision → revision +1.
  - Update with a stale revision → Conflict(current).
  - Update of a non-kid → NotFound.
  - Invalid settings → Invalid(errors), with nothing stored.
  - Unmark → true, then NotFound.
  - Get of a non-kid → NotFound.

### Implementation for User Story 3

- [ ] T020 [US3] Create src/Jellyfin.Plugin.Kids/Profiles/KidProfileValidator.cs:
  - Add a domain input record `KidProfileInput` in src/Jellyfin.Plugin.Kids/Profiles/KidProfileInput.cs:
    - `bool? SearchAllowed`, `bool? AutoplayEnabled`, `int? AutoplayCap`;
    - `IReadOnlyList<SectionInput>? Sections`, where `record SectionInput(string? Section, bool? Visible)`; the
      **section is a string**, so unknown names reach the validator instead of failing JSON binding (research R6);
    - `IReadOnlyList<Guid>? PickedItemIds`.
  - `Validate(KidProfileInput input, IReadOnlyCollection<Guid> storedPicks, IPickCatalog catalog)` returns either
    `KidProfileSettings` (defaults applied, sections normalised) or `IReadOnlyDictionary<string, string[]>`
    errors.
  - Section names are parsed with `Enum.TryParse<HomeSection>(value, ignoreCase: true)`. Numeric strings are
    rejected (`Enum.IsDefined` plus a name check).
  - Rules (verbatim from data-model.md):
    - "AutoplayCap must be within 1–10";
    - unknown sections are rejected, keyed `Sections[i]` with "Unknown section '<value>'.";
    - duplicate sections are rejected;
    - missing sections are appended, visible, in the default order;
    - duplicate picks are rejected;
    - more than 50 picks are rejected;
    - "each id that is not already in the stored profile must resolve … to an existing BoxSet or Playlist";
    - stored ids are kept even if deleted;
    - visibility to the kid is not checked.
  - Makes T018 pass.
- [ ] T021 [US3] Create src/Jellyfin.Plugin.Kids/Profiles/KidProfileService.cs, using `KidProfileStore`,
  `IKidsUserDirectory`, `IPickCatalog` and `KidProfileValidator`, and register it in PluginServiceRegistrator.cs:
  - `GetAsync(userId)` → Ok(profile) | NotFound.
  - `ListAsync()` → summaries.
  - `SaveAsync(userId, baseRevision, KidProfileInput input)`:
    - with revision 0, create: an unknown Jellyfin user → NotFound, already a kid → Conflict(current);
    - with N ≥ 1, update: Ok | Conflict(current) | NotFound | Invalid(errors).
  - `DeleteAsync(userId)` → Ok | NotFound.
  - Throws `InvalidOperationException` when `KidsDatabase.IsUsable` is false, so the middleware answers 500 (edge
    case "store broken").
  - Makes T019 pass.
- [ ] T022 [P] [US3] Create the API DTOs per contracts/kids-api-v1.md:
  - src/Jellyfin.Plugin.Kids/Api/KidProfileDto.cs:
    - fields `UserId`, `Revision`, `SearchAllowed`, `AutoplayEnabled`, `AutoplayCap`, `Sections` and `PickedItemIds`;
    - `Sections` is `[{Section, Visible}]`, with `Section` written as the enum name string;
    - mapping from `KidProfile`.
  - src/Jellyfin.Plugin.Kids/Api/KidProfileUpdate.cs:
    - `[Required, Range(0, long.MaxValue)] long? Revision`. It is nullable so that a missing `Revision` gets the
      automatic [ApiController] 400 keyed `Revision`, instead of silently meaning "create".
    - Every settings field is nullable.
    - `Sections` is a list of `{ string? Section, bool? Visible }`. Strings, not the enum, so the validator
      reports unknown names as `Sections[i]`.
    - The DTO maps to `KidProfileInput`. Omitted fields take their defaults (full replacement).
  - src/Jellyfin.Plugin.Kids/Api/KidSummaryDto.cs: `UserId`, `Revision`.
- [ ] T023 [US3] Add to src/Jellyfin.Plugin.Kids/Api/KidsController.cs, all under
  `[Authorize(Policy = Policies.IgnoreParentalControl)]` and with `CallerContext`/`ProfileAccess` checks returning
  `Forbid()` (403, never 401):
  - **`GET Profiles`** → `KidSummaryDto[]`.
  - **`GET Users/{userId}/Profile`** (self or admin) → 200 or 404 `ProblemDetails`.
  - **`PUT Users/{userId}/Profile`** (admin; body `KidProfileUpdate`):
    - 200 `KidProfileDto`;
    - 400 `ValidationProblemDetails` keyed by field;
    - 404;
    - 409 with the **current `KidProfileDto` as the body**.
  - **`DELETE Users/{userId}/Profile`** (admin) → 204 or 404.
  - Add `ProducesResponseType` attributes and XML docs.
  - Inject `IAuthorizationContext` and `KidProfileService` through the constructor.
- [ ] T024 [US3] Append checks (b)–(i) to scripts/dev-check.sh, exactly as in quickstart.md §2:
  - **Setup**:
    - sign in as `admin` and `alice` (empty password);
    - resolve user ids with `GET /Users`;
    - make sure `bob` is marked;
    - delete `alice`'s profile first so the run is repeatable.
  - **b**: mark `alice` → 200, `Revision` 1, defaults.
  - **c**: `alice` reads her own profile → 200.
  - **d**: `alice` reads `bob`'s profile → 403.
  - **e**: `alice` updates her own profile → 403, and the profile is unchanged.
  - **f**: an update, then the same update again with the old revision → 200, then 409 with `Revision` 2.
  - **g**: three requests, each → 400:
    - `AutoplayCap` 11 → `Errors` has `AutoplayCap`;
    - `Sections:[{"Section":"Shorts"}]` → `Errors` has `Sections[0]`;
    - a body without `Revision` → `Errors` has `Revision`.
  - **h**: a random GUID pick → 400 naming `PickedItemIds[0]`.
  - **i**: `GET /Kids/Profiles` as `alice` → only `UserId` and `Revision` keys.
- [ ] T025 [US3] Append check (m) to scripts/dev-check.sh, which automates FR-014 (reads outside allowed hours;
  constitution III and VI):
  1. As admin, read `alice`'s policy with `GET /Users/{alice}` (`.Policy`) and save it to a temp file.
  2. Write it back with `POST /Users/{alice}/Policy`, adding one `AccessSchedules` entry that excludes the
     current server hour: `{"DayOfWeek":"Everyday","StartHour":h1,"EndHour":h2}`, a one-hour window that is not
     now.
  3. As `alice`, check:
     - `GET /Kids/Users/{alice}/Profile` → 200;
     - `GET /Kids/Profiles` → 200;
     - `GET /Users/Me` → refused (not 200), which proves the schedule is active.
  4. **Always** restore the saved policy, in a `trap` on EXIT, so a failed run never leaves `alice` locked.
  - Policy writes replace the whole object (docs/plan.md §2.4), so the script posts back the full saved policy
    with only `AccessSchedules` changed.

**Checkpoint**: unit tests pass and `scripts/dev-check.sh` a–i and m pass on the dev server. Wholphin can read and write
kid profiles.

---

## Phase 5: User Story 2 - A parent marks and unmarks kids from the Jellyfin dashboard (Priority: P1)

**Goal**: the `Kids` dashboard page (menu entry) lists every user with their kid and admin status, marks and
unmarks kids, and warns on kids who are administrators.

**Independent Test**: quickstart.md §5 steps 1–2 (mark `alice` and the admin, see the warning, unmark with
confirmation), plus check (j): the anonymous page HTML holds no data.

- [ ] T026 [US2] Replace src/Jellyfin.Plugin.Kids/Configuration/configPage.html with src/Jellyfin.Plugin.Kids/Configuration/kids.html and add src/Jellyfin.Plugin.Kids/Configuration/kids.js (contracts/dashboard-pages.md "Kids (list)"; research R10):
  - **HTML**: `<div data-role="page" class="page type-interior pluginConfigurationPage" data-controller="__plugin/KidsJs">`
    with a static list container, an error container and no data.
  - **JS**: `export default function (view, params)`, which on `viewshow`:
    1. loads `ApiClient.getUsers()` and `GET /Kids/Profiles` (via `ApiClient.ajax({type:'GET', url: ApiClient.getUrl('Kids/Profiles'), dataType:'json'})`);
    2. renders one row per user sorted by name: name, kid toggle and "Administrator" badge;
    3. for a kid who is `Policy.IsAdministrator`, shows the warning "Administrators bypass every restriction. Use a
       non-admin account for this kid.";
    4. for kids, adds an "Edit settings" link to `#/configurationpage?name=KidsUser&userId=<id>`.
  - **Mark**: `PUT Kids/Users/{id}/Profile` with `{"Revision":0}`. A 409 or 404 refreshes the list.
  - **Unmark**: `Dashboard.confirm` first, then `DELETE`. A 404 counts as done.
  - **Errors**: other failures show `Dashboard.alert` and revert the toggle. A load failure shows an error instead
    of an empty list.
  - **Feedback**: use `Dashboard.showLoadingMsg`/`hideLoadingMsg`. Plain `<input type=checkbox>` must still work
    when `emby-checkbox` is not registered (research R10 risk).
- [ ] T027 [US2] Update src/Jellyfin.Plugin.Kids/Plugin.cs `GetPages()` to return pages `Kids` (resource `….Configuration.kids.html`, `EnableInMainMenu = true`, `DisplayName = "Kids"`, `MenuIcon = "child_care"`) and `KidsJs` (resource `….Configuration.kids.js`), and update the `EmbeddedResource`/`None Remove` items in src/Jellyfin.Plugin.Kids/Jellyfin.Plugin.Kids.csproj for kids.html and kids.js (drop configPage.html)
- [ ] T028 [US2] Append check (j) to scripts/dev-check.sh. `GET /web/ConfigurationPage?name=Kids` and `?name=KidsJs`
  without a token must:
  - return 200;
  - contain no GUID-shaped strings;
  - contain no dev user names (`alice`, `bob`, `admin`).

**Checkpoint**: an administrator can set up the household's kids from the dashboard (SC-002, SC-007).

---

## Phase 6: User Story 4 - A parent edits a kid's settings from the Jellyfin dashboard (Priority: P2)

**Goal**: the `KidsUser` editor page edits:
- search allowed;
- autoplay and its limit;
- the order and visibility of the home sections;
- the picks, using a collection and playlist chooser.

It saves through the revision rule, and a conflict never overwrites.

**Independent Test**: quickstart.md §5 steps 3–6, plus check (l). The steps are: edit `alice` in under 2 minutes,
read the values back as `alice`, change her settings meanwhile from curl to trigger a conflict, and see a deleted
pick shown as missing.

- [ ] T029 [P] [US4] Create src/Jellyfin.Plugin.Kids/Api/PickCandidateDto.cs (`Id`, `Name`, `Type` as `"BoxSet"`/`"Playlist"`) and add `GET Admin/PickCandidates?search=&ids=&limit=` to src/Jellyfin.Plugin.Kids/Api/KidsController.cs:
  - **Auth**: `IgnoreParentalControl` policy, plus `ProfileAccess.CanMutate`; anyone else gets 403.
  - **`limit`**: default 50; values over 200 are clamped to 200.
  - **`ids`**: a comma-separated list. When given, return `IPickCatalog.Resolve(ids)`; ids that don't resolve are
    simply absent from the answer.
  - **Otherwise**: return `IPickCatalog.Search(search, limit)`, ordered by name.
  - Contract: contracts/kids-api-v1.md "GET /Kids/Admin/PickCandidates".
- [ ] T030 [US4] Create src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html (`data-controller="__plugin/KidsUserJs"`, static form: search checkbox, autoplay checkbox, autoplay limit number input `min=1 max=10`, sections list, picks list, add-pick search box and results, Save button, conflict banner with "Load current settings", missing-kid message) and register pages `KidsUser` and `KidsUserJs` (not in menu) in src/Jellyfin.Plugin.Kids/Plugin.cs and as embedded resources in src/Jellyfin.Plugin.Kids/Jellyfin.Plugin.Kids.csproj
- [ ] T031 [US4] Create src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js (`export default function (view, params)`;
  contracts/dashboard-pages.md "KidsUser (editor)").
  - **Load** on `viewshow`:
    - `GET Kids/Users/{params.userId}/Profile` and `ApiClient.getUser(userId)` for the name;
    - when the profile has picks, `GET Kids/Admin/PickCandidates?ids=…` for names and types;
    - picks absent from that answer show "Missing (deleted from the server)" with a remove button;
    - a 404 shows "This user is not a kid" with a link back to `#/configurationpage?name=Kids`.
  - **Sections**: render in profile order, each with a visibility checkbox and up and down
    `paper-icon-button-light` buttons.
  - **Picks**: render in order with up, down and remove buttons.
  - **Add-pick search box**: debounced at about 300 ms; calls `PickCandidates?search=…&limit=50`. Choosing a result
    appends it unless it is already picked.
  - **No candidates**: an empty search answer shows "No collections or playlists match"; with an empty search,
    "This server has no collections or playlists yet". The rest of the editor keeps working.
  - **Autoplay limit**: enabled only while autoplay is checked. A value outside 1–10 blocks saving with an inline
    message.
  - **Save**: `PUT` with the loaded `Revision` and every field.
    - 200: keep the returned profile and revision, then show "Saved".
    - 400: show each `Errors` entry next to its field.
    - 409: show the banner "These settings were changed somewhere else". "Load current settings" re-renders from
      the 409 body. Never re-send automatically.
    - 404: "This user is no longer a kid".
  - Never report success unless the answer was 200.
- [ ] T032 [US4] Append check (l) to scripts/dev-check.sh. `GET /Kids/Admin/PickCandidates` as `alice` must return
  403, and as `admin` must return 200 with a JSON array.

**Checkpoint**: parents can configure each kid from the dashboard (SC-003, SC-005 for the editor).

---

## Phase 7: User Story 5 - Deleting a Jellyfin user cleans up their kid profile (Priority: P3)

**Goal**: a kid's profile is removed when their Jellyfin user is deleted. Orphans left while the plugin was not
running are swept at startup.

**Independent Test**: quickstart.md §2 check (k), deleting a marked throwaway user; and §4, the orphan sweep after a
restart.

### Tests for User Story 5

- [ ] T033 [P] [US5] Write tests/Jellyfin.Plugin.Kids.Tests/OrphanProfileSweeperTests.cs (temp-dir store, `FakeUserDirectory`):
  - profiles whose user ids are missing from `GetAllUserIds()` are deleted, and the others are kept;
  - an empty user list deletes nothing;
  - an unusable database is skipped without throwing.

### Implementation for User Story 5

- [ ] T034 [P] [US5] Create src/Jellyfin.Plugin.Kids/Events/UserDeletedConsumer.cs: `IEventConsumer<UserDeletedEventArgs>` whose `OnEvent` calls `KidProfileService.DeleteAsync(e.Argument.Id)` and logs at Information when a profile was removed (namespaces `Jellyfin.Data.Events.Users`, `MediaBrowser.Controller.Events`; research R7)
- [ ] T035 [P] [US5] Create src/Jellyfin.Plugin.Kids/Hosted/OrphanProfileSweeper.cs, an `IHostedService`:
  - In `StartAsync`:
    - call `KidsDatabase.EnsureInitialized()`, so the store is created and migrated at startup. This runs after
      `CreatePlugins()` (Program.cs:222 → 235);
    - read `IKidsUserDirectory.GetAllUserIds()`;
    - skip if it is empty or the database is unusable;
    - otherwise call `KidProfileStore.DeleteManyAsync` for profile ids that are not in that list;
    - log "Removed {Count} orphaned kid profile(s)" when the count is above 0.
  - `StopAsync` is a no-op.
  - Makes T033 pass.
- [ ] T036 [US5] Register `services.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserDeletedConsumer>()` and `services.AddHostedService<OrphanProfileSweeper>()` in src/Jellyfin.Plugin.Kids/PluginServiceRegistrator.cs
- [ ] T037 [US5] Append check (k) to scripts/dev-check.sh, as admin:
  1. Create user `tmpkid` with `POST /Users/New`, deleting any leftover `tmpkid` first.
  2. Mark `tmpkid`.
  3. Delete `tmpkid` with `DELETE /Users/{id}`.
  4. `GET /Kids/Profiles` must not contain that id.

**Checkpoint**: there are no orphaned kids, either immediately or after a restart (SC-006).

---

## Phase 8: Polish & cross-cutting concerns

- [ ] T038 [P] Update README.md: replace the "Development" curl examples with `scripts/dev-check.sh` and `dotnet test`, and add a short "API" pointer to specs/001-kids-plugin-foundation/contracts/kids-api-v1.md
- [ ] T039 [P] Apply the docs/plan.md changes listed in specs/001-kids-plugin-foundation/plan.md "docs/plan.md updates":
  - §4.1 JSON casing: Jellyfin defaults.
  - §4.1: `PUT` with `Revision: 0` creates the profile; 409 if the user is already a kid.
  - §4.1: add `GET /Kids/Admin/PickCandidates`.
  - §4: `Admin/Overview` moves to feature 3.
  - §8: note that 12.0 and 12.1 runtime loading is not verified (accepted risk).
- [ ] T040 Run the full quickstart.md:
  - §1: `dotnet build` with zero warnings, and `dotnet test`.
  - §2: `scripts/dev-check.sh`; all checks pass.
  - §3: allowed hours (automated by check m; the manual run is optional).
  - §4: orphan sweep.
  - §5: dashboard, timing SC-002 and SC-003.
  - §6: restart persistence.

  Fix whatever fails, and record the results in specs/001-kids-plugin-foundation/quickstart.md under a
  "Last run" note.
- [ ] T041 Check src/Jellyfin.Plugin.Kids/bin output and the deployed folder `.dev/data/plugins/Kids_0.0.1.0/` contain only `Jellyfin.Plugin.Kids.dll`/`.pdb`/`meta.json` (no Microsoft.Data.Sqlite or SQLitePCL DLLs shipped), matching the build.yaml `artifacts` whitelist

---

## Dependencies & execution order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks every story.
- **US1 (Phase 3)**: depends only on Foundational (T015, for the plugin to load with DI). It can actually start
  right after Setup, since `Info` needs no store.
- **US3 (Phase 4)**: depends on Foundational.
- **US2 (Phase 5)**: depends on US3: the page calls `Profiles` and `PUT`/`DELETE Profile`.
- **US4 (Phase 6)**: depends on US3 (the `PUT` endpoint) and on US2 (the list page links to the editor).
  T029 depends only on Foundational.
- **US5 (Phase 7)**: depends on Foundational and on T021 (`KidProfileService.DeleteAsync`). It is independent of
  the pages, so it can run in parallel with US2 and US4.
- **Polish (Phase 8)**: after all stories.

### Within each phase

- Tests (T004–T007, T018–T019, T033) are written before the code they cover, and must fail first.
- Domain → store → service → controller → dev-check.
- `scripts/dev-check.sh` and `PluginServiceRegistrator.cs` are touched by several tasks, so those edits are
  sequential and never [P] with each other.

### Story completion order

```text
Setup → Foundational → US1 ─┐
                      └→ US3 → US2 → US4
                            └→ US5 (parallel with US2/US4)
→ Polish
```

## Parallel opportunities

These tasks touch different files and have no unmet dependencies:

- **Phase 1**: T003 ∥ T001/T002.
- **Phase 2**:
  - Tests T004 ∥ T005 ∥ T006 ∥ T007.
  - Then T008 ∥ T009 ∥ T012 ∥ T013 ∥ T014.
  - T010 → T011, then T015 last.
- **US3**: T018 ∥ T019 ∥ T022. T020 → T021 → T023 → T024 → T025.
- **US4**: T029 can start as soon as Foundational is done, in parallel with US3 and US2. T030 → T031 → T032.
- **US5**: T033 ∥ T034 ∥ T035, then T036 → T037.
- **Polish**: T038 ∥ T039.

```text
# Example: Phase 2 kick-off
Task: "T004 HomeSectionTests"      Task: "T005 KidsDatabaseTests"
Task: "T006 KidProfileStoreTests"  Task: "T007 ProfileAccessTests"
# then
Task: "T008 HomeSection.cs"  Task: "T009 KidProfile.cs"  Task: "T012 Auth/"  Task: "T013 seams+fakes"  Task: "T014 Jellyfin adapters"
```

## Implementation strategy

### MVP

1. Phases 1–2 (foundation), then Phase 3 (US1, detection), then Phase 4 (US3, profiles API).
2. **Stop and validate**: `dotnet test` and `scripts/dev-check.sh` a–i.
3. This is the minimum Wholphin needs to start its plugin mode (`ServerPluginSource`, migration with
   `Revision: 0`). Kids can be marked with curl or an API key while the dashboard pages are pending.

US1 alone can ship even earlier as a detection-only build.

### Incremental delivery

1. MVP: US1 + US3.
2. \+ US2: the household marks kids from the dashboard.
3. \+ US5: cleanup. It is small, and can go together with US2.
4. \+ US4: the editor. This is the natural split point (plan.md "Delivery order and size") if it is ever moved to
   a feature 1b.
5. Polish: T038–T041.

### Notes

- Commits are made only when the user asks. They are signed with the YubiKey and carry no AI attribution.
- Runtime checks run on Jellyfin 12.2 only. Loading on 12.0 and 12.1 is not verified (accepted risk, plan.md
  "Decisions").
