# Implementation Plan: Kids allowance tracking

**Branch**: `003-kids-allowance-tracking` (stacked on feature 2, `ebd505c`) | **Date**: 2026-10-18 | **Spec**:
[spec.md](spec.md)

**Input**: Feature specification from `specs/003-kids-allowance-tracking/spec.md` (clarified 2026-10-18, Q1–Q5)

## Summary

The server counts each kid's watching time from Jellyfin's own playback events, whichever client is used.

- **Counting**: a hosted `PlaybackTracker` follows `ISessionManager.PlaybackStart`, `PlaybackProgress` and
  `PlaybackStopped`. The server synthesises a progress event every second while a session plays, and a pure
  `PlaybackClock` turns these events into playing time:
  - wall-clock deltas, capped at 30 s per gap;
  - paused time skipped;
  - synthetic ticks counting at most 60 s after the client's last real report;
  - only the session owner, and only if that owner is a kid (clarification Q3).
- **Days and storage**: time is split by a `DayClock` (a configurable "day starts at" hour in server local time,
  daylight-saving aware). It accumulates in memory and is flushed every 30 s into add-only `usage_day` rows (schema v3).
- **Allowance and bonus**: kids get an optional daily allowance in their profile. Parents add bonus minutes for the
  day (admin or feature 2 grant).
- **Offline reports**: Wholphin can report offline segments that only add time and are idempotent by id.
- **Status**: `GET /Kids/Users/{id}/Status` gives clients used, allowance, bonus, remaining and time-up.
- **Dashboard and retention**: the Kids page shows today's usage, takes bonus minutes and holds the "day starts at"
  setting. A scheduled task prunes data older than 90 days.
- **Out of scope**: stopping playback and blocking (feature 4), and allowed hours in the status (feature 5,
  clarification Q4).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); dashboard pages in plain ES modules and HTML

**Primary Dependencies**:
- Unchanged: `Jellyfin.Controller` and `Jellyfin.Model` 12.0.0 with runtime excluded, `Microsoft.Data.Sqlite.Core`,
  and `Microsoft.AspNetCore.App`.
- New uses of existing APIs: `ISessionManager` events, `IScheduledTask` and `TaskTriggerInfo`, and the plugin
  configuration. Time zones come from `TimeZoneInfo`.
- No new packages.

**Storage**: `kids.db` schema v3 adds `usage_day`, `bonus` and `reported_segment` (research R5). The allowance lives in
the profile settings JSON. `DayStartsAtHour` lives in the plugin XML configuration.

**Testing**:
- xUnit v3 with plugin-owned fakes (`FakeTimeProvider` from feature 2, fake roster and settings); `DayClock` is tested
  with the real `Europe/Madrid` zone.
- `scripts/dev-check.sh` gets checks ae–aq, which simulate playback through Jellyfin's playstate API.
- A real-client 10-minute run in jellyfin-web (quickstart §3).

**Target Platform**: Jellyfin server 12.0–12.2, with runtime checks on 12.2.

**Project Type**: server plugin plus embedded dashboard pages.

**Performance Goals**:
- **Event handling**: the server raises a progress event every second per playing session. Handlers do O(1)
  in-memory work under one lock, with no I/O.
- **Writes**: one write transaction per 30 s flush.
- **Status reads**: three indexed reads (profile, usage day, bonus sum) plus the in-memory pending amounts.

**Constraints**:
- Usage never decreases through any API (FR-012a).
- No counting for non-kids or for additional users.
- No blocking of anyone, since enforcement is feature 4.
- No household data in anonymous responses or page HTML.
- `TreatWarningsAsErrors`.

**Scale/Scope**:
- 1–5 kids and a few concurrent sessions.
- At most 90 days × kids rows per table.
- 3 new endpoints, 2 changed endpoints (Profile and its DELETE with purge), 1 scheduled task, 1 hosted service, and 2
  page changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (below).*

| Principle | Check | Status |
|---|---|---|
| I. Leverage Jellyfin | Counting uses Jellyfin's own playback events and synthetic progress (`ISessionManager.PlaybackStart/Progress/Stopped`, `SessionInfo.StartAutomaticProgress`, `SessionManager.cs:776-965`). Retention uses `IScheduledTask`. The day setting goes through Jellyfin's plugin configuration API (`/Plugins/{id}/Configuration`). Feature 2's grants are reused. Allowed hours and content rules are untouched; allowed hours stay out of the status (Q4). The new mechanisms are only what the constitution names: allowances and usage. | PASS |
| II. The Server Decides | Usage is counted on the server from every client. Client reports can only add time (constitution, FR-018), and no API lowers usage, not even for admins (Q5, FR-012a). A kid token never changes the allowance or adds bonus time. **Documented limit**: offline reports are trusted to describe only unseen playback (Q2); a misbehaving client can only add time against its own kid. | PASS |
| III. Parents Are Never Locked Out | This feature blocks nothing. Status reads use `IgnoreParentalControl`. No user policy or schedule is modified. Disabling the plugin stops counting and nothing else. | PASS |
| IV. Secrets Stay Secret | No secrets. Status is self or admin only. Info adds only the capability name. The page HTML holds no usage. No telemetry. | PASS |
| V. Stable, client-agnostic contract | All additions are additive (new endpoints, new optional fields, a new feature flag), so `ApiVersion` stays 1. The full-replacement PUT semantics for the new field are written in the contract. The event members used (`IsAutomated`, `IsPaused`, `PlaySessionId`, `Session`, `Session.UserId`) and `IScheduledTask` exist in the 12.0.0 packages (R1, R9). No internal names are used, so no startup self-check is needed. | PASS (feature 1's accepted 12.0 runtime risk carries over) |
| VI. Tested where it matters | Allowance arithmetic and day boundaries (including daylight saving) are unit tested, as the constitution requires, along with the counting rule, add-only storage and report idempotency. Server-dependent counting is proven by dev-check through Jellyfin's real playstate pipeline, and by a 10-minute jellyfin-web run. No spike is needed (R11). | PASS |
| Technical constraints | State lives in SQLite under the plugin data folder. The plugin XML gets one small admin setting. Nothing is copied from GPL plugins (Playback Reporting was studied for event patterns only). | PASS |
| Development workflow | spec-kit flow. docs/plan.md §3.1, §3.4, §4.1 and §8 were updated for Q3 and Q4. | PASS |

**Post-design re-check (after Phase 1)**: the design adds no endpoint outside plan §4.1, and `Admin/Overview` is not
needed (R10). One deviation from plan §3.1 is user-approved (Q3, owner only). All gates still pass, and nothing is
added to Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/003-kids-allowance-tracking/
├── spec.md
├── plan.md                              # this file
├── research.md                          # Phase 0 (R1–R11)
├── data-model.md                        # Phase 1
├── quickstart.md                        # Phase 1
├── contracts/
│   ├── kids-api-v1-allowance.md         # additions to the client API
│   └── dashboard-allowance.md           # Kids page and editor changes
├── checklists/requirements.md
└── tasks.md                             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── PluginServiceRegistrator.cs          # + KidRoster, IKidsSettings, UsageStore, UsageService (builds the DayClock per read), PlaybackTracker (hosted), PruneUsageTask
├── Configuration/
│   ├── PluginConfiguration.cs           # + DayStartsAtHour (0–23, default 0)
│   ├── kids.html / kids.js              # + "Screen time": day starts at, per-kid usage and bonus
│   └── kidsUser.html / kidsUser.js      # + Daily allowance field
├── Api/
│   ├── KidsController.cs                # Info + "allowance"; DELETE Profile ?purge=true (calls UsageService.PurgeAsync)
│   ├── KidProfileDto.cs / KidProfileUpdate.cs   # + DailyAllowanceMinutes
│   ├── AllowanceController.cs           # new: GET Status, POST WatchTime/Bonus, POST WatchTime/Report (route Kids/Users/{userId})
│   ├── KidStatusDto.cs, BonusRequest.cs, ReportRequest.cs (+ ReportSegment)
├── Profiles/
│   ├── KidProfileSettings.cs            # + DailyAllowanceMinutes
│   ├── KidProfileValidator.cs           # + 0–1440 rule
│   └── KidProfileService.cs             # updates KidRoster on mark/unmark (the callers purge usage: UsageService depends on this service)
├── Allowance/
│   ├── KidRoster.cs                     # in-memory set of kid ids
│   ├── IKidsSettings.cs / PluginKidsSettings.cs   # DayStartsAtHour seam (reads Plugin.Instance.Configuration)
│   ├── DayClock.cs                      # DayOf, StartOf, NextStart, Split (DST aware)
│   ├── PlaybackClock.cs                 # pure counting rule (R2)
│   ├── PlaybackEvent.cs                 # the fields of an event the clock needs
│   ├── UsageService.cs                  # pending accumulator, flush, status, bonus, report
│   ├── StatusCalculator.cs              # pure (R6)
│   └── SegmentValidator.cs              # report rules (R8)
├── Hosted/
│   ├── PlaybackTracker.cs               # IHostedService: subscribes events → PlaybackClock → UsageService; 30 s flush timer; sweep
│   └── OrphanProfileSweeper.cs          # + delete usage of deleted users
├── Tasks/PruneUsageTask.cs              # IScheduledTask, daily 03:30, 90 days
├── Storage/
│   ├── KidsDatabase.cs                  # SchemaVersion 3, migration step 3
│   └── UsageStore.cs                    # add-only upsert, bonus, segments, purge, prune
└── Events/UserDeletedConsumer.cs        # + usage purge, roster removal

tests/Jellyfin.Plugin.Kids.Tests/
├── Fakes/ (FakeKidsSettings)
├── PlaybackClockTests.cs, DayClockTests.cs, StatusCalculatorTests.cs, SegmentValidatorTests.cs
├── UsageStoreTests.cs, UsageServiceTests.cs, PruneUsageTaskTests.cs
├── KidProfileValidatorTests.cs (+ allowance), KidsDatabaseTests.cs (+ 2 → 3)

scripts/dev-provision.sh                 # + Kids library with a generated 12-minute clip
scripts/dev-check.sh                     # + checks ae–aq (playstate simulation)
```

**Structure decision**:
- One project and one test project, as before.
- The counting rule, day boundaries, status arithmetic and segment rules are pure classes under `Allowance/` with no
  Jellyfin types, so constitution VI's tests run without a server.
- The Jellyfin glue stays thin: `PlaybackTracker` maps event args to `PlaybackEvent`, `PluginKidsSettings` reads the
  plugin XML, and `PruneUsageTask` wraps a store call.
- `AllowanceController` is separate, like feature 2's `ParentLockController`, and reuses `CallerResolver` for grants.

## Delivery order and size

| Slice | Stories | Contents |
|---|---|---|
| A. Clocks and store | US1 (part), US6 | `DayClock`, `PlaybackClock`, schema v3, `UsageStore`, `KidRoster`, settings seam, unit tests |
| B. Tracker and status | US1, US2 | `PlaybackTracker`, `UsageService`, `StatusCalculator`, `GET Status`, dev-check ae–ak, dev media provisioning |
| C. Allowance and bonus | US3, US4 | profile field and validation, bonus endpoint, dev-check al–am |
| D. Offline reports | US5 | `SegmentValidator`, report endpoint, dev-check an–ao |
| E. Dashboard, retention, cleanup | US6, US7 | Kids page and editor, "day starts at", prune task, purge, user deletion, dev-check ap–aq |

**Size**: about 40 tasks, a little bigger than feature 2, mostly because of the tracker and its server checks. It is
still one coherent feature. If the user wants a split, D (offline reports) has no dependants until Wholphin's
downloads integration and is the natural one. I am not splitting it without that decision.

## docs/plan.md updates

- **Done in this step**:
  - §3.1: only the session owner counts (Q3);
  - §3.4 and §4.1: allowed hours come in feature 5 (Q4), and the status gains `nextDayStartsAt`;
  - §8 question 6 is answered.
- **With the implementation**:
  - §2.6: the tables are `usage_day`, `bonus` and `reported_segment` (the plan sketch said `bonus.local_date` and an
    optional `usage_segment`);
  - §4 tree: add `AllowanceController` and the `Allowance/` folder;
  - §4.1: `Admin/Overview` is not needed (R10).

## Risks

- **Clients that report progress but skip pause reports** would count paused time until their next report. Every
  mainstream client reports pauses at once. The real-client run checks jellyfin-web, and Wholphin is a reference
  client that does too.
- **The synthetic-tick behaviour could change** in a 12.x release. Counting would still work from real reports alone
  (about 10 s cadence, under the 30 s cap), only less smoothly. The simulated dev-check catches a change.
- **Restart loss**: up to about 30 s of a kid's time per restart (SC-009 allows 1 minute).
- **Run time**: the playback checks add about 4 minutes to dev-check, and the real-client check takes about 25 minutes
  (it is a manual or Playwright step, not part of dev-check).

## Complexity Tracking

No violations. Feature 1's accepted 12.0 and 12.1 runtime risk carries over unchanged.

## Implementation notes (2026-10-18, `/speckit-implement`)

Decisions taken while implementing, and where the code differs from tasks.md. None changes the contract or the
household behaviour in spec.md.

| Task | What changed | Why |
|---|---|---|
| T002 | The generated clip is `Test Pattern (2026).mkv`, not `test-clip.mkv`; dev-check and provisioning search for "Test Pattern". Provisioning checks for that file (not "any video") and refreshes the library when it generated the clip into an existing library | Jellyfin's naming rules take a file ending in `-clip` for an extra (a clip of another movie) and never list it as a Movie, so the first provisioning timed out |
| T038 | `PluginKidsSettings`'s test seam is `Func<int?>` (the stored hour), not `Func<PluginConfiguration?>` | The test project cannot load Jellyfin assemblies: the plugin references them with `ExcludeAssets=runtime`, and `PluginConfiguration` derives from `BasePluginConfiguration` |
| T043 | `PruneUsageTask` is not registered in `PluginServiceRegistrator`. The pruning itself is `UsageService.PruneAsync` (today − 90 days under the day rule), which the task calls. Its unit tests live in `UsageServiceTests` instead of a `PruneUsageTaskTests` file, and the 03:30 daily trigger was checked on the dev server (log: "Daily trigger for Prune kids screen time set to fire at … 03:30") | Jellyfin builds scheduled tasks by scanning plugin assemblies (research R9, corrected); a DI registration would be unused. The task type implements `IScheduledTask`, so tests cannot load it (same reason as T038) |
| T039 | `kids.js` looks the plugin id up by name from `GET /Plugins` instead of hard-coding `d62ee188-…` | dev-check j refuses any GUID in the anonymously served page files, and a lookup keeps that check strict |
| T047 | `Directory.Build.props` is bumped to 0.0.3.0 too | `Info.PluginVersion` is the assembly version; feature 2 bumped both files |
| T031 | The bonus log line is "Bonus of {Minutes} min for {UserId} by {Caller}" | The controller has the user id, not the name; looking the name up only for a log line is not worth a dependency |
| T024 | `UsageService.FlushAsync` returns `false` on a failed write instead of throwing; the caller never has to catch | The tracker's timer and stop handler only need to know nothing was lost |
| T040 | With an allowance, the Kids page row shows today's bonus: "Today: 12 of 45 min + 10 min bonus (43 min left)". The unmark confirmation adds "Today's screen time is kept." | Otherwise a bonus makes "left" larger than "of" with no explanation; and unmarking no longer deletes everything |
| T036 | A `StartedAt` without an offset is read as UTC | The same as the rest of Jellyfin's API |
| T026 | Check ak signs in as the dev admin on device `kids-check-admin` (`DEV_ADMIN_USER` / `DEV_ADMIN_PASSWORD`, default admin/admin), so the admin's session is separate from the admin token's | `POST /Sessions/{id}/User/{alice}` needs a session the admin owns |

**Known behaviour, not a bug**: days are labelled by the date they start on, so changing "day starts at" to a later
hour makes today's date step back one day until the new hour (for example at 17:30, moving H from 0 to 18 makes
the date yesterday's until 18:00, and then today's again, with the time already recorded today under the old rule).
This is Story 6.3 ("already recorded days are not recalculated") and the quickstart §4 run used it as designed.

**Review fixes** (a read-only code review after the first full run; no contract change except the purge retry):

- `DELETE Profile?purge=true` also purges a user who is no longer a kid (answering 404), so a purge that failed after
  the unmark can be retried; before, the retry stopped at 404 and the screen time could only go with the user.
- `PlaybackTracker` checks the roster and stamps the event time inside its lock, so an event in flight cannot add time
  for a kid unmarked meanwhile, and a real report cannot lose the race to a synthesised one and be dropped as out of
  order. Timer ticks no longer overlap when the database is slow.
- `GetStatusAsync` reads stored and pending time under the flush lock, so a flush in between cannot hide up to 30 s.
- Not changed, reported instead: a client built against feature 1 that PUTs a profile without
  `DailyAllowanceMinutes` clears the allowance (full replacement, as the contract says); a roster reload racing an
  unmark can briefly keep a former kid in the roster (startup or database-outage retry only).
