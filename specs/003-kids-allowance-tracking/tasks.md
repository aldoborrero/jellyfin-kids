---

description: "Task list for the Kids allowance tracking"
---

# Tasks: Kids allowance tracking

**Input**: Design documents from `specs/003-kids-allowance-tracking/`

**Prerequisites**: plan.md, spec.md (clarified Q1–Q5), research.md (R1–R11), data-model.md,
contracts/kids-api-v1-allowance.md, contracts/dashboard-allowance.md, quickstart.md

**Tests**: included. Constitution VI requires unit tests for allowance arithmetic and day boundaries. The plan adds the
counting rule, add-only storage and report idempotency. Behaviour that depends on Jellyfin (playback events,
authorization) is checked against the dev server. Write each phase's unit tests first and see them fail.

**Organization**: tasks are grouped by user story, with story numbers from spec.md:

| Story | Title | Priority | Plan slice |
|---|---|---|---|
| US1 | The server counts a kid's watching time on every client | P1 | A (clock), B |
| US2 | A kid's device shows how much time is left today | P1 | B |
| US3 | A parent sets each kid's daily allowance | P1 | C |
| US4 | A parent adds bonus time for today | P2 | C |
| US5 | Playback that the server never saw can still be counted | P2 | D |
| US6 | The day resets at the household's chosen hour | P2 | A (DayClock), E (setting) |
| US7 | The dashboard shows allowances and today's usage | P2 | E |

**Dependency order for the P1 stories is US2 → US1 → US3.** Every check of the count reads it through `GET Status`,
so the status endpoint comes first (it reports 0 used until counting exists). The allowance is a profile field read
by the status from Phase 2 on; US3 makes it settable and validated. The day rule (`DayClock`) of US6 is foundational,
because every count and status uses it; US6's own phase adds the dashboard setting and its checks.

## Conventions for every task

- **Projects**: the plugin is in `src/Jellyfin.Plugin.Kids/` (namespaces `Jellyfin.Plugin.Kids.*`) and the tests
  in `tests/Jellyfin.Plugin.Kids.Tests/`. Match the style of `Api/ParentLockController.cs`,
  `Storage/ParentLockStore.cs`, `ParentLock/GrantService.cs` and the existing tests.
- **Build gate**: `TreatWarningsAsErrors` with StyleCop, SerilogAnalyzer and MultithreadingAnalyzer. Every public
  member has XML docs.
- **No new NuGet packages**. Time comes from `TimeProvider`, zones from `TimeZoneInfo`.
- **No mocking library**: fakes go in `tests/Jellyfin.Plugin.Kids.Tests/Fakes/`.
- **Pure classes under `Allowance/`** (`DayClock`, `PlaybackClock`, `StatusCalculator`, `SegmentValidator`) use no
  Jellyfin types, so their tests need no server.
- **Event handlers do no I/O** (research R1): playback events update in-memory state under one
  `System.Threading.Lock`; only the flush and API calls touch SQLite.
- **Usage never decreases** (FR-012a): the only write to `usage_day.used_ms` is `used_ms = used_ms + $delta` with
  `$delta > 0` checked in code. Only pruning, a purge and user deletion delete rows.
- **JSON**: Jellyfin's formatters, PascalCase in C#. Null properties are omitted (as feature 2's `VerifyResult`
  `Grant`), which is how "absent when unlimited" is produced. Days are `yyyy-MM-dd` strings.
- **Policies**: every new endpoint uses `[Authorize(Policy = Policies.IgnoreParentalControl)]`. A refusal is
  `Forbid()` (403), never 401. Callers come from `CallerResolver.ResolveAsync(HttpContext)`.
- **Unit tests**: `dotnet test Jellyfin.Plugin.Kids.slnx`.
- **Server checks**:
  - Start `JELLYFIN_KIDS_DEV=$PWD/.dev dev` (port 8096) only while testing, and stop it afterwards.
  - Run `scripts/dev-check.sh`. Checks are named after quickstart.md §2. New checks go before the final "Leave bob
    unthrottled" block, which stays last.

---

## Phase 1: Setup

**Purpose**: test fakes, dev media and the dev-check playback helpers that later phases use.

- [X] T001 [P] Add `tests/Jellyfin.Plugin.Kids.Tests/Fakes/FakeKidsSettings.cs`, which implements `IKidsSettings`
  (created in T006) with a settable `DayStartsAtHour` (default 0) and a settable `TimeZoneInfo Zone` (default
  `TimeZoneInfo.Utc`).
- [X] T002 [P] Extend `scripts/dev-provision.sh` (research R11, quickstart "Prerequisites"):
  - if `$dev/media/kids` has no video file, generate `$dev/media/kids/test-clip.mkv`: 12 minutes, 320×240,
    `ffmpeg -f lavfi -i testsrc=size=320x240:rate=10 -f lavfi -i sine=frequency=440 -t 720 -c:v libx264 -preset
    ultrafast -c:a aac`;
  - if `ffmpeg` is not on `PATH`, add `jellyfin-ffmpeg` to the packages in `nix/devshell.nix`, and use it;
  - if no library named "Kids" exists (`GET /Library/VirtualFolders`), create it with
    `POST /Library/VirtualFolders?name=Kids&collectionType=movies&paths=<abs path>&refreshLibrary=true`, then wait
    until `GET /Items?Recursive=true&IncludeItemTypes=Movie&SearchTerm=test-clip` returns the item with a
    `RunTimeTicks` (polling, at most 120 s);
  - stay idempotent, like the rest of the script.
- [X] T003 [P] Extend `scripts/dev-check.sh` with playback helpers (no checks yet):
  - `clip_id`: looked up once as admin with the query from T002;
  - `play <token> <deviceId> <event> <playSessionId> [paused]`: `POST /Sessions/Playing`,
    `/Sessions/Playing/Progress` or `/Sessions/Playing/Stopped` (event `start`, `progress`, `stop`) through `req_dev`,
    with `{ItemId, PlaySessionId, IsPaused, PositionTicks, CanSeek: true, PlayMethod: "DirectPlay"}`;
  - `play_for <token> <deviceId> <playSessionId> <seconds>`: progress every 10 s for that long;
  - `used <token> <userId>`: prints `.UsedSeconds` from `GET /Kids/Users/{id}/Status`;
  - `near <actual> <expected> <tolerance> <name>`: prints PASS or FAIL and counts failures, like `check`.

---

## Phase 2: Foundational (blocking)

**Purpose**: the settings seam, the day rule, schema v3 and the usage store, the kid roster and the allowance field
on the profile model. Every story uses them. (Plan slice A, without `PlaybackClock`.)

### Tests first

- [X] T004 [P] Write `tests/Jellyfin.Plugin.Kids.Tests/DayClockTests.cs` (research R4) with the real
  `Europe/Madrid` zone from `TimeZoneInfo.FindSystemTimeZoneById`; skip with a reason (`Assert.Skip`) when it is
  missing:
  - **H = 0**: 2026-10-18T21:59Z (23:59 local) → day `2026-10-18`; 22:00Z → `2026-10-19`.
  - **H = 4**: 2026-10-19T01:30Z (03:30 local) → `2026-10-18`; 02:00Z (04:00 local) → `2026-10-19`.
  - **Spring forward** (2027-03-28, 02:00 → 03:00 local): with H = 2, `StartOf(2027-03-28)` is the first valid local
    time after the gap, 03:00 local = 01:00Z, and that day is 23 hours long.
  - **Fall back** (2026-10-25, 03:00 → 02:00 local): with H = 2, `StartOf(2026-10-25)` is the earlier of the two
    02:00 instants (00:00Z), and that day is 25 hours long.
  - `NextStart(utc)` equals `StartOf(DayOf(utc) + 1)` and is always after `utc`.
  - **`Split(from, to)`**: an interval that crosses H:00 gives two parts whose lengths add up to the interval, each
    on its own day; one that does not cross gives one part; an empty or reversed interval gives none.
  - A `DayClock` with H outside 0–23 throws `ArgumentOutOfRangeException`.
- [X] T005 [P] Extend `tests/Jellyfin.Plugin.Kids.Tests/KidsDatabaseTests.cs` and add
  `tests/Jellyfin.Plugin.Kids.Tests/UsageStoreTests.cs` (research R5, data-model), using `TempFolder`:
  - **Migration**: a v2 database with two profiles and a lock migrates to `user_version` 3, and both are unchanged.
  - **`AddUsageAsync(map of (userId, day) → ms)`**: inserts, then adds on a second call; a delta ≤ 0 throws
    `ArgumentOutOfRangeException` and writes nothing; all rows of one call are in one transaction.
  - **`GetUsedMsAsync(userId, day)`**: 0 for a missing row.
  - **Bonus**: `AddBonusAsync(userId, day, minutes, grantedBy, createdAt)` and `GetBonusMinutesAsync(userId, day)`,
    which sums the day's rows; the table refuses `minutes` 0 and 241 (`CHECK (minutes BETWEEN 1 AND 240)`).
  - **The table refuses** `used_ms` < 0 (`CHECK (used_ms >= 0)`).
  - **`PurgeUserAsync(userId)`**: deletes that user's rows in the three tables, and no one else's.
  - **`PruneAsync(cutoffDay)`**: deletes rows of the three tables whose `day` is before the cutoff, keeps the cutoff
    day, and returns the count.
  - **`DeleteUsersNotInAsync(IReadOnlySet<Guid>)`**: deletes the rows of every user id outside the set.

### Implementation

- [X] T006 [P] Add `DayStartsAtHour` (int, default 0, XML docs "0–23; out-of-range values read as 0") to
  `src/Jellyfin.Plugin.Kids/Configuration/PluginConfiguration.cs`. Create
  `src/Jellyfin.Plugin.Kids/Allowance/IKidsSettings.cs` (`int DayStartsAtHour { get; }`, `TimeZoneInfo Zone { get; }`)
  and `src/Jellyfin.Plugin.Kids/Allowance/PluginKidsSettings.cs`, which reads
  `Plugin.Instance?.Configuration.DayStartsAtHour` on every access ("Out-of-range values read as 0", data-model) and
  returns `TimeZoneInfo.Local`. It never caches, so a dashboard save applies to the next read (Story 6.3).
- [X] T007 [P] Create `src/Jellyfin.Plugin.Kids/Allowance/DayClock.cs` (research R4): constructor
  `(TimeZoneInfo zone, int startHour)`, `DateOnly DayOf(DateTimeOffset utc)`, `DateTimeOffset StartOf(DateOnly day)`,
  `DateTimeOffset NextStart(DateTimeOffset utc)`, `IEnumerable<(DateOnly Day, TimeSpan Length)> Split(DateTimeOffset
  from, DateTimeOffset to)` and `static string Format(DateOnly)` (`yyyy-MM-dd`, invariant culture). Use
  `zone.IsInvalidTime` (move forward to the first valid minute) and `zone.IsAmbiguousTime` (take the earlier offset).
  Make T004 pass.
- [X] T008 Add migration step 3 to `src/Jellyfin.Plugin.Kids/Storage/KidsDatabase.cs` and set `SchemaVersion = 3`,
  with research R5's SQL verbatim:

  ```sql
  CREATE TABLE usage_day (
    user_id TEXT NOT NULL, day TEXT NOT NULL, used_ms INTEGER NOT NULL CHECK (used_ms >= 0),
    PRIMARY KEY (user_id, day));
  CREATE TABLE bonus (
    id INTEGER PRIMARY KEY, user_id TEXT NOT NULL, day TEXT NOT NULL,
    minutes INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 240), granted_by TEXT NOT NULL, created_at TEXT NOT NULL);
  CREATE INDEX bonus_user_day ON bonus (user_id, day);
  CREATE TABLE reported_segment (
    user_id TEXT NOT NULL, segment_id TEXT NOT NULL, day TEXT NOT NULL, started_at TEXT NOT NULL,
    seconds INTEGER NOT NULL, received_at TEXT NOT NULL, PRIMARY KEY (user_id, segment_id));
  ```

  User ids are stored as Guid `"N"` strings, as in `kid_profile`.
- [X] T009 Create `src/Jellyfin.Plugin.Kids/Storage/UsageStore.cs`, following `ParentLockStore` (it uses
  `KidsDatabase.WriteAsync` and throws if `EnsureInitialized()` is false), with the methods of T005. The usage upsert
  is `INSERT INTO usage_day … ON CONFLICT (user_id, day) DO UPDATE SET used_ms = used_ms + excluded.used_ms`. Leave
  `AddSegmentsAsync` to T036. Make T005 pass.
- [X] T010 [P] Create `src/Jellyfin.Plugin.Kids/Allowance/KidRoster.cs` (research R3): a thread-safe set of kid user
  ids with `Contains(Guid)`, `Add(Guid)`, `Remove(Guid)` and `Replace(IEnumerable<Guid>)`, plus an `IsLoaded` flag.
  Add `KidRosterTests` cases to a new `tests/Jellyfin.Plugin.Kids.Tests/KidRosterTests.cs` (add, remove, replace,
  concurrent add/contains).
- [X] T011 Add `int? DailyAllowanceMinutes` to `src/Jellyfin.Plugin.Kids/Profiles/KidProfileSettings.cs`, with
  `MinDailyAllowance = 0` and `MaxDailyAllowance = 1440` constants and the XML doc "0–1440 inclusive; null means
  unlimited and is the default" (data-model, FR-009). Add a case to `KidProfileStoreTests.cs`: a settings JSON stored
  without the property reads as null (research R7, no migration).
- [X] T012 Change `src/Jellyfin.Plugin.Kids/Profiles/KidProfileService.cs` to take `KidRoster`: a successful create
  calls `Add`, and a successful `DeleteAsync` calls `Remove`. Add `LoadRosterAsync(CancellationToken)`, which fills
  the roster from `ListAsync`. Update `KidProfileServiceTests.cs` (the constructor at line 23) and add cases: marking
  adds to the roster, unmarking removes, a failed create does not add.
- [X] T013 Register in `src/Jellyfin.Plugin.Kids/PluginServiceRegistrator.cs` (keep the "declare types only" rule):
  `IKidsSettings → PluginKidsSettings`, `KidRoster` and `UsageStore` as singletons. Later phases add `UsageService`,
  the `PlaybackTracker` hosted service and `PruneUsageTask`.

**Checkpoint**: `dotnet build` has 0 warnings, and T004, T005, T010–T012 pass.

---

## Phase 3: User Story 2 – A kid's device shows how much time is left today (Priority: P1)

**Goal**: `GET /Kids/Users/{id}/Status` for the kid and administrators, with used, allowance, bonus, remaining,
time-up, server time, next day start and warning thresholds. `Info` lists `allowance`.

**Independent Test**: as alice, `GET /Kids/Users/{alice}/Status` → 200 with `UsedSeconds`, `TimeUp` false, no
`AllowanceSeconds`, `NextDayStartsAt` in the future and `WarnAtSeconds` `[300,60]`. bob → 403. A non-kid → 404.
(The 25 + 5 minute story test needs counting and the allowance, so it runs at the end of Phase 5, check al.)

### Tests first

- [X] T014 [P] [US2] Write `tests/Jellyfin.Plugin.Kids.Tests/StatusCalculatorTests.cs` (research R6, FR-014):
  - **unlimited**: no allowance, no remaining, `TimeUp` false even with 10 hours used;
  - **under**: allowance 30 min, used 25 min → remaining 300 s, not time up;
  - **at**: used = allowance → remaining 0, time up;
  - **over**: used > allowance + bonus → remaining 0 (never negative), time up;
  - **allowance 0**: time up at 0 used; with a 15-minute bonus, remaining 900 s;
  - **bonus**: allowance 30 + bonus 10, used 35 → remaining 300 s;
  - **rounding**: `UsedSeconds` is `ms / 1000` rounded down (59 999 ms → 59).
- [X] T015 [P] [US2] Write the status cases of `tests/Jellyfin.Plugin.Kids.Tests/UsageServiceTests.cs`, with a
  temp-dir database, `FakeTimeProvider`, `FakeKidsSettings` and a real `KidProfileService` over `KidProfileStore`,
  `FakeUserDirectory`, `FakePickCatalog` and a `KidRoster`:
  - a kid with stored usage today → `UsedSeconds` from the store, `Date` = today, `NextDayStartsAt` =
    `DayClock.NextStart(now)`, `ServerTime` = now, `WarnAtSeconds` `[300, 60]`;
  - yesterday's usage and bonus are not in today's status;
  - a non-kid → "not a kid" (null);
  - a store that is not initialised (unreadable database file) → throws, never "0 used" (FR-016).

### Implementation

- [X] T016 [US2] Create `src/Jellyfin.Plugin.Kids/Allowance/StatusCalculator.cs`, a pure
  `Calculate(DateOnly day, long usedMs, int? allowanceMinutes, int bonusMinutes, DateTimeOffset now,
  DateTimeOffset nextDayStartsAt)` returning `src/Jellyfin.Plugin.Kids/Allowance/KidStatus.cs` (`Date`, `UsedSeconds`,
  `AllowanceSeconds?`, `BonusSeconds`, `RemainingSeconds?`, `TimeUp`, `ServerTime`, `NextDayStartsAt`,
  `WarnAtSeconds`), with the rules of data-model "KidStatus". Make T014 pass.
- [X] T017 [US2] Create `src/Jellyfin.Plugin.Kids/Allowance/UsageService.cs` (singleton) over `UsageStore`,
  `KidProfileService`, `IKidsSettings` and `TimeProvider`:
  - `DayClock CurrentDayClock()` builds a clock from the settings on every call (Story 6.3);
  - `GetStatusAsync(Guid userId, CancellationToken)` → `KidStatus?`: null when not a kid; otherwise the profile's
    `DailyAllowanceMinutes`, the stored ms for today plus the pending ms for (user, today) (the pending map comes in
    T024; read an empty map until then), and today's bonus sum. Store exceptions propagate.
  - Register it in `PluginServiceRegistrator.cs`. Make T015 pass.
- [X] T018 [US2] Create `src/Jellyfin.Plugin.Kids/Api/KidStatusDto.cs` (the fields of contracts/kids-api-v1-allowance.md
  "GET Status", `Date` as a `yyyy-MM-dd` string, `ServerTime` and `NextDayStartsAt` as UTC `DateTime`) and
  `src/Jellyfin.Plugin.Kids/Api/AllowanceController.cs`, routed `[Route("Kids/Users/{userId}")]` with the same
  `[Produces]` as `KidsController`:
  - `GET Status`: `ProfileAccess.CanRead` false → 403; not a kid → 404 ProblemDetails "This user is not a kid.";
    200 `KidStatusDto`. A store failure propagates as 500 (FR-016).
- [X] T019 [US2] Change `KidsController.GetInfo` in `src/Jellyfin.Plugin.Kids/Api/KidsController.cs` to
  `Features: ["profiles", "parentLock", "allowance"]` (FR-023), and every test that asserts the features.
- [X] T020 [US2] Update `scripts/dev-check.sh`:
  - check a: also asserts `(.Features | index("allowance")) != null`;
  - **ae**: alice reads her status → 200 with `.UsedSeconds >= 0 and (has("AllowanceSeconds") | not) and
    .TimeUp == false and (.WarnAtSeconds == [300,60])` and `NextDayStartsAt` later than `ServerTime`;
  - **af**: bob reads alice's status → 403; admin reads a non-kid's status (bob after admin `DELETE`s his profile,
    then marks him again) → 404;
  - **m (outside allowed hours)**: inside check m's schedule window, alice reads her status → 200 (FR-015, Story 2.5).

**Checkpoint**: unit tests pass; with the dev loop up, checks a, ae, af and m pass.

---

## Phase 4: User Story 1 – The server counts a kid's watching time on every client (Priority: P1) 🎯 MVP

**Goal**: a hosted `PlaybackTracker` turns Jellyfin's playback events into playing time for kids who own the
session, splits it by day, and flushes it every 30 s.

**Independent Test**: checks ag–ak against the dev server, then quickstart §3 (10 minutes in jellyfin-web, with a
pause) and §5 (restart).

### Tests first

- [X] T021 [P] [US1] Write `tests/Jellyfin.Plugin.Kids.Tests/PlaybackClockTests.cs` (research R2, data-model
  "PlaybackRecord"), driving the clock with `PlaybackEvent`s at chosen instants:
  - **playing**: start, then real progress every 10 s for 60 s, then stop → 60 s;
  - **synthetic ticks**: start, one real report, then automated ticks every 1 s for 30 s → 30 s;
  - **per-gap cap**: two real reports 5 minutes apart while playing → 30 s ("capped at 30 s per gap");
  - **pause**: play 10 s, a paused report, 30 s of paused reports, a playing report, 10 s more → 20 s;
  - **dead client**: one real report, then automated ticks for 120 s → 60 s (`end = min(now, lastRealAt + 60 s)`),
    and a later real report counts again from that report;
  - **concurrent**: two keys (different `SessionId`) playing 30 s at the same time → 30 s each, 60 s for the owner;
  - **out of order**: an event with `now < LastEventAt` adds nothing and does not move `LastEventAt` back;
  - **unknown key**: a progress event with no record creates one and adds nothing;
  - **stop**: counts the last gap, then removes the record; a second stop adds nothing;
  - **sweep**: `Sweep(now)` removes records with no event for 6 minutes and keeps the others.
- [X] T022 [P] [US1] Add the counting cases to `tests/Jellyfin.Plugin.Kids.Tests/UsageServiceTests.cs`:
  - `AddPlayed(userId, from, to)` splits by `DayClock` and accumulates pending; the status includes pending before
    any flush;
  - an interval across the day start counts on both days (FR-008, Story 6.2);
  - `FlushAsync` writes every pending (user, day) in one store call, then clears what it wrote; time added during a
    flush stays pending;
  - a failing flush (store not initialised) keeps the pending amounts, and the next successful flush writes them once;
  - `DropPending(userId)` removes that user's pending amounts (used by the purge in T044).

### Implementation

- [X] T023 [P] [US1] Create `src/Jellyfin.Plugin.Kids/Allowance/PlaybackEvent.cs` (record: `Kind` start / progress /
  stop, `SessionId`, `PlaySessionId?`, `OwnerUserId`, `IsPaused`, `IsAutomated`, `At`) and
  `src/Jellyfin.Plugin.Kids/Allowance/PlaybackClock.cs` (research R2): `IReadOnlyList<(Guid UserId, DateTimeOffset
  From, DateTimeOffset To)> Apply(PlaybackEvent)` returns the intervals to count, keyed by
  `(SessionId, PlaySessionId ?? "")`, with the 30 s cap, the 60 s cut-off after the last real report and the
  monotonic `LastEventAt`; `Sweep(DateTimeOffset now)` with the 6-minute idle rule. Not thread-safe: the tracker holds
  the lock. Make T021 pass.
- [X] T024 [US1] Add the pending accumulator to `UsageService` (research R5): `AddPlayed(Guid userId,
  DateTimeOffset from, DateTimeOffset to)` (split with `CurrentDayClock().Split`, `pending[(user, day)] += ms`, under
  a `Lock`), `FlushAsync(CancellationToken)` (snapshot, `UsageStore.AddUsageAsync`, subtract what was written; a
  `SemaphoreSlim` serialises flushes with purges), `DropPending(Guid)`, and include pending in `GetStatusAsync`. Log
  a failed flush at Warning "Could not store kids screen time; keeping {Count} pending amount(s)" once per failure
  streak. Make T022 pass.
- [X] T025 [US1] Create `src/Jellyfin.Plugin.Kids/Hosted/PlaybackTracker.cs`, an `IHostedService` over
  `ISessionManager`, `KidRoster`, `KidProfileService`, `KidsDatabase`, `UsageService`, `TimeProvider` and a logger:
  - **`StartAsync`**: if `KidsDatabase.EnsureInitialized()`, call `KidProfileService.LoadRosterAsync`; subscribe to
    `PlaybackStart`, `PlaybackProgress` and `PlaybackStopped`; start a 30 s timer (`TimeProvider.CreateTimer`) that
    flushes and runs `PlaybackClock.Sweep`.
  - **Roster retry**: while `KidRoster.IsLoaded` is false (the database was unavailable or the load threw), each timer
    tick retries `LoadRosterAsync`. Log the first failure at Warning "Kids screen time is not counted until the kids
    database can be read", and log at Information once the load succeeds (spec edge case: store unavailable).
  - **Handlers** (research R1, R3): map the event args to `PlaybackEvent` with `OwnerUserId = e.Session.UserId`
    (never `e.Users`, clarification Q3), `IsAutomated` only on progress, `At = TimeProvider.GetUtcNow()`. Ignore the
    event when `e.Session` is null or the owner is not in `KidRoster` (FR-005, FR-006). Under one lock, apply it to
    the clock and pass each returned interval to `UsageService.AddPlayed`. A stop also triggers a flush on the thread
    pool (`_ = FlushSafeAsync()`), never awaited in the handler (FR-007).
  - **`StopAsync`**: unsubscribe, dispose the timer, and flush once.
  - Handlers catch and log exceptions at Error: an exception must never reach the session manager.
  - Register it with `AddHostedService<PlaybackTracker>()` after `OrphanProfileSweeper`.
- [X] T026 [US1] Add dev-check checks ag–ak to `scripts/dev-check.sh` (quickstart §2), as alice with the T003
  helpers, each with a fresh `PlaySessionId` and the status read before and after:
  - **ag**: start, progress every 10 s for 60 s, stop → `near` +60 ± 5 s;
  - **ah**: 10 s playing, a paused progress, paused progress every 10 s for 30 s, a playing progress, 10 s, stop →
    +20 ± 5 s;
  - **ai**: sessions on devices `kids-check-A` and `kids-check-B` play at the same time for 30 s (interleaved
    progress), then both stop → +60 ± 6 s;
  - **aj**: start, one progress, no more reports for 120 s (no stop) → at most +75 s; read again 30 s later → no
    further growth; then stop it;
  - **ak**: admin starts a session (device `kids-check-admin`), finds its id with `GET /Sessions?deviceId=…`, adds
    alice with `POST /Sessions/{id}/User/{alice}`, plays 30 s and stops → alice +0 (± 1 s).

  Leave alice without an allowance at the end, so later checks start from a known profile.

**Checkpoint**: unit tests pass; checks ag–ak pass. With Phase 3 this is the MVP: the server counts every client and
clients read the count.

---

## Phase 5: User Story 3 – A parent sets each kid's daily allowance (Priority: P1)

**Goal**: `DailyAllowanceMinutes` is part of the profile API, validated 0–1440, under feature 1's rules.

**Independent Test**: admin sets alice to 45 minutes; alice's profile and status show it; clearing it shows
unlimited; 1441 → 400 keyed `DailyAllowanceMinutes`.

### Tests first

- [X] T027 [P] [US3] Add to `tests/Jellyfin.Plugin.Kids.Tests/KidProfileValidatorTests.cs`: `DailyAllowanceMinutes`
  null, 0, 45 and 1440 are accepted and kept; -1 and 1441 are rejected with the error keyed `DailyAllowanceMinutes`
  ("0–1440 inclusive"). Add to `KidProfileServiceTests.cs`: a save that omits the field clears a stored allowance
  (full replacement, research R7). Who may change it needs no new test: it is feature 1's `CanMutate`, which
  `ProfileAccessTests.cs` already covers (Story 3.4).

### Implementation

- [X] T028 [US3] Add `DailyAllowanceMinutes` to `src/Jellyfin.Plugin.Kids/Profiles/KidProfileInput.cs`,
  `src/Jellyfin.Plugin.Kids/Api/KidProfileUpdate.cs` (and `ToInput`) and `src/Jellyfin.Plugin.Kids/Api/KidProfileDto.cs`
  (and `From`), with XML docs from contracts/kids-api-v1-allowance.md "Kid profile". Validate it in
  `src/Jellyfin.Plugin.Kids/Profiles/KidProfileValidator.cs` with the message "Must be between 0 and 1440." and copy
  it into the normalised settings. Make T027 pass.
- [X] T029 [US3] Add dev-check check **al** to `scripts/dev-check.sh`: admin `PUT`s alice's profile (current revision,
  every other field unchanged) with `DailyAllowanceMinutes` 1441 → 400 `errors | has("DailyAllowanceMinutes")`; then
  1 → 200 `.DailyAllowanceMinutes == 1`, and alice's status has `.AllowanceSeconds == 60`; then play until her
  `UsedSeconds` ≥ 60 (she already has more from ag–ai, so read once) → `.TimeUp == true and .RemainingSeconds == 0`
  (Stories 2.3 and 3.5). Alice without a grant `PUT`s her profile with an allowance → 403 (Story 3.4).

**Checkpoint**: the P1 stories are complete.

---

## Phase 6: User Story 4 – A parent adds bonus time for today (Priority: P2)

**Goal**: `POST /Kids/Users/{id}/WatchTime/Bonus {Minutes}` for administrators and grant holders, adding to the
current day only.

**Independent Test**: with alice at time up, a grant holder on her TV adds 15 minutes → her status shows 15 minutes
more remaining. Without a grant → 403.

- [X] T030 [US4] Add bonus cases to `tests/Jellyfin.Plugin.Kids.Tests/UsageServiceTests.cs`: `AddBonusAsync`
  stores the minutes on today's day with `granted_by` = the caller's `LogName` ("API key" for API keys) and returns the
  new status; two bonuses add up; after `FakeTimeProvider` passes the next day start, the status has `BonusSeconds` 0
  (Story 4.3); minutes 0, 241 and null → errors keyed `Minutes`, nothing stored; a non-kid → not a kid.
- [X] T031 [US4] Implement `UsageService.AddBonusAsync(CallerContext caller, Guid userId, int? minutes,
  CancellationToken)` returning a small result (`Saved` with the status, `Invalid` with errors, `NotAKid`), and
  `src/Jellyfin.Plugin.Kids/Api/BonusRequest.cs` (`int? Minutes`). Add `POST WatchTime/Bonus` to
  `AllowanceController`: `ProfileAccess.CanMutate` false → 403 (FR-012); then not a kid → 404; then invalid → 400
  `ValidationProblemDetails` keyed `Minutes`; 200 `KidStatusDto`. Log at Information "Bonus of {Minutes} min for
  {UserName} by {Caller}". Make T030 pass.
- [X] T032 [US4] Add dev-check check **am** to `scripts/dev-check.sh`: alice without a grant adds 5 → 403; admin adds
  0 and 241 → 400 `errors | has("Minutes")` twice; admin adds 5 → 200 `.BonusSeconds == 300`; alice A with a fresh
  grant (verify the known code, as check r) adds 5 → 200 `.BonusSeconds == 600` (SC-007). Record the starting
  `BonusSeconds` first and compare deltas, so a second run the same day passes.

---

## Phase 7: User Story 5 – Playback that the server never saw can still be counted (Priority: P2)

**Goal**: `POST /Kids/Users/{id}/WatchTime/Report` from the kid's own device: add-only, idempotent by segment id,
all or nothing per request. (Plan slice D.)

**Independent Test**: as alice, a new 10-minute segment from earlier today adds 600 s; the same again adds 0; a
negative or oversized segment → 400 and nothing added; admin and bob → 403.

### Tests first

- [X] T033 [P] [US5] Write `tests/Jellyfin.Plugin.Kids.Tests/SegmentValidatorTests.cs` (research R8, data-model
  "ReportedSegment"), with `now` fixed:
  - 0 and 101 segments → error keyed `Segments`;
  - `Id` empty, whitespace and 65 chars → `Segments[i].Id`; 64 chars accepted;
  - duplicate ids within one request → `Segments[i].Id`;
  - `Seconds` 0, -1 and 43 201 → `Segments[i].Seconds`; 1 and 43 200 accepted;
  - `StartedAt` missing, 5 min + 1 s in the future, or older than now − 90 days → `Segments[i].StartedAt`; 5 min in
    the future and exactly now − 90 days accepted;
  - one invalid segment among valid ones reports only that index, and the request is invalid as a whole.
- [X] T034 [US5] Add report cases to `tests/Jellyfin.Plugin.Kids.Tests/UsageStoreTests.cs` and
  `UsageServiceTests.cs`:
  - **store `AddSegmentsAsync`**: new segments insert and add their seconds × 1000 to their day's `usage_day` in one
    transaction; a segment id already stored adds nothing (`ON CONFLICT (user_id, segment_id) DO NOTHING`, then the
    change count); two concurrent calls with the same id add once;
  - **service `ReportAsync`**: the segment is added to the day `StartedAt` falls in under `DayClock` (a segment from
    before today's start lands on yesterday); an invalid request adds nothing to any day; the result is today's
    status; no report lowers any day (SC-005).

### Implementation

- [X] T035 [US5] Create `src/Jellyfin.Plugin.Kids/Allowance/SegmentValidator.cs` (pure, `Validate(IReadOnlyList<…>
  segments, DateTimeOffset now)` returning errors keyed as T033, with the constants `MaxSegments = 100`,
  `MaxIdLength = 64`, `MaxSeconds = 43200` and `MaxFutureSkew = 5 min`). The retention window is
  `UsageStore.RetentionDays = 90` (add the constant to `UsageStore.cs`), which `PruneUsageTask` (T043) also uses.
  Make T033 pass.
- [X] T036 [US5] Add `UsageStore.AddSegmentsAsync(Guid userId, IReadOnlyList<(string Id, string Day, DateTimeOffset
  StartedAt, int Seconds)>, DateTimeOffset receivedAt, CancellationToken)` and `UsageService.ReportAsync(Guid userId,
  …)` (validate, then map each segment to its day, then store). Create `src/Jellyfin.Plugin.Kids/Api/ReportRequest.cs`
  (`IReadOnlyList<ReportSegment>? Segments`) and `src/Jellyfin.Plugin.Kids/Api/ReportSegment.cs` (`string? Id`,
  `DateTime? StartedAt`, `int? Seconds`). Add `POST WatchTime/Report` to `AllowanceController`, checks in the
  contract's order: **self only**, `caller.UserId == userId && !caller.IsApiKey`, else 403 (FR-020; an administrator
  who is the kid themselves may report); then not a kid → 404; then invalid → 400 keyed as T033; 200 today's
  `KidStatusDto`. Make T034 pass.
- [X] T037 [US5] Add dev-check checks **an** and **ao** to `scripts/dev-check.sh`:
  - **an**: alice reports a new segment (id from `uuidgen`) `StartedAt` = now − 1 h, `Seconds` 120 → `UsedSeconds`
    +120 (± 2 s, playback is idle); the same request again → +0; a request with a `Seconds` 0 segment and a new valid
    one → 400 `errors | has("Segments[0].Seconds")` and +0. If now − 1 h is before today's start, use now − 1 min and
    `Seconds` 30 instead, so the segment lands on today.
  - **ao**: admin reports a segment for alice → 403; bob reports for alice → 403.

---

## Phase 8: User Story 6 – The day resets at the household's chosen hour (Priority: P2)

**Goal**: the administrator sets "day starts at" on the Kids page; the next status reads use it. The rule itself is
`DayClock` (Phase 2).

**Independent Test**: quickstart §4. Set the hour to the next hour; at that hour alice's status shows a new date with
0 used and no bonus.

- [X] T038 [US6] Add to `tests/Jellyfin.Plugin.Kids.Tests/UsageServiceTests.cs`: changing
  `FakeKidsSettings.DayStartsAtHour` changes today's `Date` and `NextDayStartsAt` on the next status read, and the
  usage already stored under the old day keeps its day (Story 6.3, no recalculation). Add to a new
  `tests/Jellyfin.Plugin.Kids.Tests/PluginKidsSettingsTests.cs`: -1 and 24 read as 0 (needs a seam: give
  `PluginKidsSettings` an internal constructor taking `Func<PluginConfiguration?>`; the public one reads
  `Plugin.Instance`).
- [X] T039 [US6] Add the "Screen time" section to `src/Jellyfin.Plugin.Kids/Configuration/kids.html` (static, under
  "Parent lock"): a select 00:00–23:00 with the help text "Each kid's screen time starts again from zero at this hour,
  server time." and a Save button (contracts/dashboard-allowance.md). In
  `src/Jellyfin.Plugin.Kids/Configuration/kids.js`, load it with
  `ApiClient.getPluginConfiguration('d62ee188-270c-49d8-b7b9-5e524fec2335')` on `viewshow` and save with
  `updatePluginConfiguration` (read, change `DayStartsAtHour`, write, so other settings are kept), then
  `Dashboard.processPluginConfigurationUpdateResult`. FR-026.

---

## Phase 9: User Story 7 – The dashboard shows allowances and today's usage (Priority: P2)

**Goal**: the Kids page shows each kid's usage against their allowance and takes bonus minutes; the kid editor sets
the daily allowance. (Plan slice E.)

**Independent Test**: quickstart §6.

- [X] T040 [P] [US7] In `src/Jellyfin.Plugin.Kids/Configuration/kids.js`, where the kid rows are built with
  `createElement` (after T039, which also edits this file): each kid row calls `GET /Kids/Users/{id}/Status` and shows "Today: 12
  of 45 min (33 min left)", "Today: 47 of 45 min, time is up" or "Today: 12 min (no limit)"; a failed read shows
  "Usage unavailable", never 0. Add a "+ minutes" control (number 1–240, "Add") that posts `WatchTime/Bonus`; a 200
  re-renders the row from the returned status, a 400 shows `errors.Minutes` inline (FR-024, Story 7.2). Minutes are
  `Math.floor(seconds / 60)`.
- [X] T041 [P] [US7] In `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` and `kidsUser.js`: a "Daily allowance
  (minutes)" number field (min 0, max 1440, empty = "No limit"). Load it from the profile, send
  `DailyAllowanceMinutes` (null when empty) with the rest of the profile on save, block saving with an inline
  "Between 0 and 1440 minutes, or empty for no limit." when out of range, and show a 400
  `errors.DailyAllowanceMinutes` inline. Revision and conflict handling stay as in feature 1 (FR-025).
- [X] T042 [US7] Add dev-check check **aq** to `scripts/dev-check.sh`: `GET /web/ConfigurationPage?name=Kids` and
  `?name=KidsUser` with no token → 200, and neither contains `UsedSeconds`, `Today:` or a `yyyy-MM-dd` date
  (FR-027). Then run quickstart §6 by hand or with the out-of-repo Playwright script, timing step 3 (SC-008).

---

## Phase 10: Retention, cleanup and polish

**Purpose**: the prune task, purge on unmark and user deletion (FR-021, FR-022), docs and the full quickstart run.

- [X] T043 [P] Create `src/Jellyfin.Plugin.Kids/Tasks/PruneUsageTask.cs`, an `IScheduledTask` (research R9): `Name`
  "Prune kids screen time", `Key` "KidsPruneUsage", `Category` "Kids", `Description` "Removes kids screen time older
  than 90 days.", a default `DailyTrigger` at 03:30 (`TimeOfDayTicks = TimeSpan.FromHours(3.5).Ticks`), and
  `ExecuteAsync` calling `UsageStore.PruneAsync(DayClock.Format(today − 90 days))` and logging the count. Register it
  with `AddSingleton<IScheduledTask, PruneUsageTask>()`. Add `tests/Jellyfin.Plugin.Kids.Tests/PruneUsageTaskTests.cs`:
  rows from today − 91 days are removed, today − 90 days kept, and the trigger is daily at 03:30.
- [X] T044 Purge (FR-022, research R8):
  - Add `UsageService.PurgeAsync(Guid userId, CancellationToken)`: it takes the flush semaphore, calls `DropPending`,
    then `UsageStore.PurgeUserAsync`. The callers purge, not `KidProfileService`, because `UsageService` already
    depends on `KidProfileService` (no constructor cycle).
  - `KidsController.DeleteProfile` gains `[FromQuery] bool purge = false`, with the XML doc from the contract. After a
    successful unmark with `purge`, it calls `UsageService.PurgeAsync`. Without `purge`, usage is kept.
  - `src/Jellyfin.Plugin.Kids/Events/UserDeletedConsumer.cs` always purges usage, whether or not the user was a kid
    (a former kid's history is kept until then), and removes them from the roster.
  - `src/Jellyfin.Plugin.Kids/Hosted/OrphanProfileSweeper.cs` also calls `UsageStore.DeleteUsersNotInAsync(users)`
    when the user list is not empty.
  - Tests: in `KidProfileServiceTests.cs` or `UsageServiceTests.cs`, unmark without purge keeps today's usage and
    marking again shows it; with purge it is 0 and pending is dropped; in `OrphanProfileSweeperTests.cs`, usage rows
    of a missing user are deleted.
- [X] T045 Add dev-check check **ap** to `scripts/dev-check.sh`: note alice's `UsedSeconds`; admin `DELETE`s her
  profile without purge, marks her again (revision 0) → the same `UsedSeconds`; then `DELETE ?purge=true`, mark again
  → `UsedSeconds == 0` and `BonusSeconds == 0`. Then restore alice's profile (no allowance, the fields the earlier
  checks expect) and re-read `alice_rev`. Extend check k: after tmpkid is deleted, nothing of theirs is left (the
  status is 404, which is all the API can show; the unit tests cover the rows).
- [X] T046 [P] Update the contracts and plan:
  - `specs/001-kids-plugin-foundation/contracts/kids-api-v1.md`: under `DELETE Profile` and `KidProfile`, link
    `../../003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md` for `?purge=true` and
    `DailyAllowanceMinutes`;
  - `docs/plan.md` (plan.md "With the implementation"): §2.6 tables `usage_day`, `bonus`, `reported_segment`; §4
    tree: `AllowanceController`, the `Allowance/` folder, `PlaybackTracker`, `PruneUsageTask`; §4.1:
    `Admin/Overview` is not needed (research R10).
- [X] T047 [P] Bump `version` in `build.yaml` to `0.0.3.0` and set the changelog to "Screen time: server-side
  counting from playback on every client, daily allowance, bonus time, offline reports, status for clients,
  dashboard usage, retention task."
- [X] T048 Run the full quickstart:
  - **§1**: `dotnet build` with 0 warnings, and `dotnet test`.
  - **§2**: start the dev loop on :8096 (run `scripts/dev-provision.sh` if the Kids library is missing), run
    `scripts/dev-check.sh` twice in a row; both runs pass.
  - **§3**: the 10-minute jellyfin-web runs (SC-001, SC-002), with and without a 3-minute pause.
  - **§4**: the day boundary (SC-006).
  - **§5**: the restart during playback (SC-009).
  - **§6** and **§7**: the dashboard and the retention task.
  - Stop the dev loop.
  - Record the results in a "Last run" table at the end of `specs/003-kids-allowance-tracking/quickstart.md`, as
    features 1 and 2 did.

---

## Dependencies and execution order

```text
Phase 1 Setup (T001–T003)
   └─▶ Phase 2 Foundational (T004–T013)            slice A
          └─▶ Phase 3 US2 status (T014–T020)        slice B
                 └─▶ Phase 4 US1 counting (T021–T026)  slice B  🎯 MVP
                        └─▶ Phase 5 US3 allowance (T027–T029)  slice C
                               ├─▶ Phase 6 US4 bonus (T030–T032)  slice C
                               ├─▶ Phase 7 US5 reports (T033–T037)  slice D
                               ├─▶ Phase 8 US6 day setting (T038–T039)  slice E
                               └─▶ Phase 9 US7 dashboard (T040–T042)  slice E (T040 needs T031 and T039)
                                      └─▶ Phase 10 Retention and polish (T043–T048)  slice E
```

- **Inside a phase**: tests first (they fail), then the implementation, then dev-check.
- `UsageService.cs`, `UsageServiceTests.cs`, `AllowanceController.cs`, `kids.js` and `scripts/dev-check.sh` are each
  edited by several tasks. Those tasks are sequential, never [P] with each other.
- **US4, US5 and US6 are independent of each other** after US3. US7's bonus control (T040) needs the bonus endpoint
  (T031) and comes after T039 (both edit `kids.js`); its editor field (T041) needs T028.
- Check al reads usage counted by ag–ai, so the dev-check order is ae–ak, al, am, an–ao, ap, aq.

## Parallel opportunities

- **Phase 1**: T001 ∥ T002 ∥ T003.
- **Phase 2**: tests T004 ∥ T005; then T006 ∥ T007 ∥ T010; then T008 → T009, and T011 → T012 → T013.
- **Phase 3**: T014 ∥ T015.
- **Phase 4**: T021 ∥ T022, then T023 → T024 → T025 → T026.
- **Phases 6–8** can run side by side after Phase 5, except that T030, T034 and T038 all edit
  `UsageServiceTests.cs`, and T031 and T036 both edit `AllowanceController.cs`: run those in task order. T033 ∥ T034.
- **Phase 9**: T040 ∥ T041.
- **Phase 10**: T043 ∥ T046 ∥ T047.

## Implementation strategy

1. **MVP = Phases 1–4.** The server counts a kid's playback from every client, splits it by the day rule and stores
   it, and clients read it from `GET Status`. It is proven by unit tests and dev-check ae–ak, covering the §7 row 3
   acceptance criteria: 10 minutes count within ±30 s (ag, then §3), paused time is not counted (ah), two devices
   both count (ai), and the day resets at the configured hour (DayClock tests, then §4).
2. **Phase 5** completes the P1 stories: a settable allowance makes `TimeUp` meaningful for feature 4.
3. **Phases 6–9** add the P2 stories, each shippable on its own: bonus time, offline reports ("a client report cannot
   reduce usage", an), the day setting and the dashboard.
4. **Phase 10** adds retention and purge, syncs the docs and records the evidence.
