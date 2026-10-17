# Implementation Plan: Kids plugin foundation

**Branch**: `001-kids-plugin-foundation` (no git branch yet: the repository has no commits) | **Date**: 2026-10-17 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-kids-plugin-foundation/spec.md`

## Summary

This feature turns the plugin skeleton into the base the other features build on:

- **Detection**: an anonymous `GET /Kids/Info`.
- **Storage**: kid profiles in a plugin-owned SQLite file (`kids.db` in the plugin data folder), using the
  `Microsoft.Data.Sqlite` that the server already ships.
- **Profiles API**: under `/Kids`, with optimistic revisions (a stale write gets 409 with the current profile)
  and a self-or-admin access rule.
- **Cleanup**: deleted Jellyfin users have their profiles removed, through an event consumer and a sweep at
  startup.
- **Dashboard**: two pages. One lists the users, marks kids and warns when a kid is an admin; the other edits a
  kid's Wholphin settings, with a chooser for collections and playlists.

Jellyfin is reused wherever it already does the job:
- the `IgnoreParentalControl` policy;
- `IAuthorizationContext`;
- the user-deleted event;
- `GET /Users` for the dashboard list;
- `ILibraryManager` for picks;
- the legacy dashboard page mechanism.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); dashboard pages in plain ES modules and HTML (jellyfin-web 12.x)

**Primary Dependencies**:
- `Jellyfin.Controller` and `Jellyfin.Model` 12.0.0 (`ExcludeAssets=runtime`) and the `Microsoft.AspNetCore.App`
  framework reference, as today.
- New: `Microsoft.Data.Sqlite.Core` 10.0.x (`ExcludeAssets=runtime`). It is provided by the server
  (research R1).

**Storage**: SQLite file `kids.db` in the plugin data folder (`IApplicationPaths.PluginsPath`/`Jellyfin.Plugin.Kids`,
the same folder as `DataFolderPath`). WAL mode, private cache, no pooling and a single writer. `PRAGMA user_version`
migrations run lazily on first use, and the startup sweep triggers them (research R1–R3, [data-model.md](data-model.md))

**Testing**: xUnit v3 unit tests in `tests/Jellyfin.Plugin.Kids.Tests`, with no mocking library (plugin-owned
seams and fakes) and a real SQLite file for the store. `scripts/dev-check.sh` runs curl checks against the
process-compose dev server, plus manual dashboard checks ([quickstart.md](quickstart.md); research R11)

**Target Platform**: Jellyfin server 12.0–12.2 on Linux, Windows, macOS and Docker; the jellyfin-web 12.x dashboard

**Project Type**: server plugin (a class library loaded by Jellyfin), plus embedded dashboard pages

**Performance Goals**: household scale. Profile reads and writes finish in well under 100 ms on a Raspberry-Pi-class
server. The picks chooser answers searches over hundreds of collections and playlists without noticeable delay
(capped `limit`, name search in the library query).

**Constraints**:
- Constitution V: the plugin loads on 12.0, uses no Jellyfin internals and does not ship server assemblies.
- No 401 for signed-in callers who lack rights.
- No household data in anonymous responses or page HTML.
- `TreatWarningsAsErrors`, with StyleCop and the other analyzers already configured.

**Scale/Scope**: one household, typically 1–10 users and 1–5 kids; up to 50 picks per kid; 5 endpoints and 2
dashboard pages

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (below).*

| Principle | Check | Status |
|---|---|---|
| I. Leverage Jellyfin | Content and hours are untouched. Built on: `Policies.IgnoreParentalControl` (`MediaBrowser.Common/Api/Policies.cs:26`), `IAuthorizationContext` (`MediaBrowser.Controller/Net/IAuthorizationContext.cs:16`), `IEventConsumer<UserDeletedEventArgs>` (`Jellyfin.Server.Implementations/Users/UserManager.cs:415`), `GET /Users` for the page (`Jellyfin.Api/Controllers/UserController.cs:91-100`), `ILibraryManager` queries (`Emby.Server.Implementations/Library/LibraryManager.cs:1740-1756`), and `IHasWebPages` with the jellyfin-web plugin page loader (`jellyfin-web/src/components/viewContainer.js:14-25`). New mechanisms are only the kid profile and its store. | PASS |
| II. The Server Decides | Mutations require admin or API key (grant in feature 2). A kid's token never mutates, even its own profile (FR-015), and is checked server-side in `CallerContext` and tested. | PASS |
| III. Parents Are Never Locked Out | Admins and API keys pass every check. Reads use `IgnoreParentalControl` (FR-014). No user policy or schedule is modified. Disabling or removing the plugin removes everything it adds (no blocking exists yet). | PASS |
| IV. Secrets Stay Secret | `Info` holds only version, API version and features. Page HTML is static, and data loads through authenticated calls. No telemetry. (There are no secrets in this feature; the lock is feature 2.) | PASS |
| V. Stable, client-agnostic contract | Everything lives under `/Kids`, detected by anonymous `Info` with `ApiVersion` 1 and `Features`. Compatibility rules are written in [contracts/kids-api-v1.md](contracts/kids-api-v1.md). The plugin compiles against 12.0.0, and every API used was confirmed in the 12.0.0 packages (research R4, R7, R9, R12). There is no dependency on internal controller or claim names, so no startup self-check is needed. Runtime loading is checked on 12.2 only; on 12.0 and 12.1 it rests on the 12.0.0 compile and is **not verified at runtime**, a risk the user accepted (Decisions Q2; research R12). | PASS (accepted risk: no runtime check on 12.0; recorded in Complexity Tracking) |
| VI. Tested where it matters | Unit tests cover revision conflicts, validation, the access decision, migrations and the orphan sweep. Server-dependent behaviour (authorization, the user-deleted event, page HTML) is checked by `scripts/dev-check.sh` against the dev loop. No spike is needed: no risky mechanism is involved. The analyzers stay at zero warnings. | PASS |
| Technical constraints | net10.0; Jellyfin 12.0.0 packages with runtime excluded; state in SQLite under the plugin data folder; the XML config stays empty (admin settings only). MIT: nothing is copied from GPL or MPL plugins; Playback Reporting and Streamyfin are studied for patterns only. Export and import are noted as owed before a stable release (feature 6). | PASS |
| Development workflow | spec-kit flow; docs/plan.md stays the research input (see "docs/plan.md updates" below). | PASS |

One recorded exception: principle V runtime evidence on 12.0 and 12.1 (see Complexity Tracking).

**Post-design re-check (after Phase 1)**: the design adds one admin endpoint that is not in docs/plan.md §4.1
(`GET /Kids/Admin/PickCandidates`, research R9). It is needed because Jellyfin's `/Items` hides other users'
private playlists from admins, so it does not duplicate a Jellyfin capability (principle I). It also drops
`GET /Kids/Admin/Overview` from this feature in favour of Jellyfin's `GET /Users`. All gates still pass.

## Project Structure

### Documentation (this feature)

```text
specs/001-kids-plugin-foundation/
├── spec.md
├── plan.md                    # this file
├── research.md                # Phase 0
├── data-model.md              # Phase 1
├── quickstart.md              # Phase 1
├── contracts/
│   ├── kids-api-v1.md         # client API, feature 1 subset
│   └── dashboard-pages.md     # Kids and KidsUser pages
├── checklists/requirements.md
└── tasks.md                   # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
Jellyfin.Plugin.Kids.slnx                 # + tests project
src/Jellyfin.Plugin.Kids/
├── Jellyfin.Plugin.Kids.csproj           # + Microsoft.Data.Sqlite.Core (runtime excluded), embedded pages, InternalsVisibleTo tests
├── Plugin.cs                             # GetPages: Kids (menu), KidsJs, KidsUser, KidsUserJs
├── PluginServiceRegistrator.cs           # DI: database, store, services, consumer, hosted sweeper
├── Api/
│   ├── KidsController.cs                 # /Kids: Info, Profiles, Users/{id}/Profile (GET/PUT/DELETE), Admin/PickCandidates
│   ├── KidsInfo.cs                       # + Features, LockType
│   ├── KidProfileDto.cs                  # response; also the 409 body
│   ├── KidProfileUpdate.cs               # PUT body
│   ├── KidSummaryDto.cs                  # Profiles list item
│   └── PickCandidateDto.cs
├── Auth/
│   ├── CallerContext.cs                  # from IAuthorizationContext: UserId, IsAdmin (admin or API key)
│   └── ProfileAccess.cs                  # pure rules: CanRead(caller, userId), CanMutate(caller)
├── Profiles/
│   ├── KidProfile.cs                     # domain record + defaults (cap 3, five sections)
│   ├── HomeSection.cs                    # enum + HomeSectionEntry + Normalize
│   ├── KidProfileInput.cs                # validator input; sections as strings (research R6)
│   ├── KidProfileValidator.cs            # rules from data-model, errors keyed by field
│   ├── KidProfileService.cs              # create/update/delete/get/list; result types (Ok, NotFound, Conflict, Invalid)
│   ├── IKidsUserDirectory.cs             # seam: UserExists, GetAllUserIds
│   ├── JellyfinUserDirectory.cs          # IUserManager-backed
│   ├── IPickCatalog.cs                   # seam: Resolve(ids), Search(term, limit)
│   └── JellyfinPickCatalog.cs            # ILibraryManager-backed, User = null, BoxSet/Playlist
├── Storage/
│   ├── KidsDatabase.cs                   # open kids.db, pragmas, writer lock, user_version migrations
│   └── KidProfileStore.cs                # insert / conditional update / delete / get / list
├── Events/
│   └── UserDeletedConsumer.cs            # IEventConsumer<UserDeletedEventArgs>
├── Hosted/
│   └── OrphanProfileSweeper.cs           # IHostedService: sweep in StartAsync
└── Configuration/
    ├── PluginConfiguration.cs            # unchanged (empty)
    ├── kids.html / kids.js               # replaces configPage.html
    └── kidsUser.html / kidsUser.js

tests/Jellyfin.Plugin.Kids.Tests/
├── Jellyfin.Plugin.Kids.Tests.csproj     # xunit.v3, Microsoft.NET.Test.Sdk
├── .editorconfig                         # test-only analyzer suppressions (TreatWarningsAsErrors stays on)
├── Fakes/ (FakeUserDirectory, FakePickCatalog)
├── KidProfileValidatorTests.cs
├── KidProfileStoreTests.cs               # temp-dir SQLite: create, conditional update, concurrent conflict, delete, migrations
├── KidProfileServiceTests.cs             # mark/unmark/update flows, picks rules, 404/409 paths
├── ProfileAccessTests.cs
└── OrphanProfileSweeperTests.cs

scripts/dev-check.sh                      # quickstart §2 checks against the dev server
```

**Structure decision**:
- A single plugin project, plus one test project.
- The domain logic (`Profiles/`, `Storage/`, `Auth/ProfileAccess`) depends on two plugin-owned interfaces instead
  of Jellyfin services. This lets the logic that constitution VI requires to be tested run without a server and
  without a mocking library.
- Jellyfin-specific glue stays thin: the controller, the `Jellyfin*` adapters, the consumer and the sweeper.
- The pages are embedded resources. `process-compose.yaml` already watches `*.html` and `*.js` under `src/`.

## Delivery order and size

The spec has five stories. They map to slices that can each be shipped and checked on their own:

| Slice | Stories | Contents |
|---|---|---|
| A. Detection and store | US1 | `Info` with `Features`, `KidsDatabase` with migrations, registrator, test project |
| B. Profiles API | US3 | Validator, store, service, access, controller endpoints, `dev-check.sh` a–i, l |
| C. List page | US2 | `Kids` page: mark, unmark, admin warning |
| D. Cleanup | US5 | Consumer and sweeper, `dev-check.sh` k |
| E. Editor | US4 | `PickCandidates` endpoint and the `KidsUser` page |

**Size assessment**:
- A–D is a small, coherent foundation (roughly 20–25 tasks).
- E adds about 10 tasks, mostly page JavaScript (search, reorder, conflict handling) and one endpoint.
- The whole feature is still a reasonable single feature. The split point, if the user wants one, is E (story 4)
  as feature 1b. It has no dependants in features 2–3. I am **not** splitting it without the user's decision.

## docs/plan.md updates

These are to be applied with the implementation; this plan's write access is limited to its tmp dir:

- §4.1: JSON uses Jellyfin's default formatters (PascalCase, camelCase via `profile="CamelCase"`), not camelCase
  only.
- §4.1: `PUT Profile` with `Revision: 0` creates the profile, and gives 409 if the user is already a kid.
- §4.1: add `GET /Kids/Admin/PickCandidates`.
- §4: `Admin/Overview` moves to feature 3.
- §6 or §8: loading on 12.0 and 12.1 is not verified at runtime (accepted risk).

## Decisions (user, 2026-10-17)

- **Q1 → A. JSON casing.** Jellyfin's default formatters: PascalCase, and camelCase with
  `Accept: application/json; profile="CamelCase"`. No custom formatter (research R5).
- **Q2 → C. Loading on 12.0.** Compile against the 12.0.0 packages and run runtime checks on 12.2 only, with the
  current dev loop. No `dev-12.0` loop and no manual 12.0 check. Runtime loading on 12.0 and 12.1 is an **accepted,
  unverified risk** (research R12).
- **Q3 → A. Marking an existing kid.** `PUT` with `Revision: 0` on an existing kid returns 409 with the server's
  profile. Wholphin's migration keeps the server's profile. No `?overwrite` (research R14).

## Risks

- **Runtime loading on 12.0 and 12.1 is not verified** (accepted, Q2 C). It is mitigated by compiling against 12.0.0
  and by the release notes listing no plugin-API breaks.
- **Dashboard components.** `emby-checkbox`, `emby-select` and `emby-input` are registered as a side effect of other
  jellyfin-web modules, so a cold deep link to `KidsUser` may render them unstyled. The pages must stay usable with
  plain inputs (research R10).
- **First test restore.** xunit.v3 is not in the local NuGet cache, so the first restore of the test project needs
  network (research R11).

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|-----------|------------|--------------------------------------|
| Principle V: loading on Jellyfin 12.0 and 12.1 is not verified at runtime. The plugin is still built to load there (compiled against the 12.0.0 packages, `targetAbi 12.0.0.0`); only the runtime evidence is missing | User decision Q2 C (2026-10-17). nixpkgs ships only 12.2, and the dev loop runs that | A `dev-12.0` loop with the official 12.0.x build, or a manual 12.0 check before release: rejected by the user for cost. To revisit if a load failure is reported, or before a stable release (research R12) |
