# Research: Kids allowance tracking

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-18

Citations are relative to `.reference/jellyfin` at tag `v12.2` unless stated otherwise. "12.0.0 package" means the
NuGet `Jellyfin.*` 12.0.0 packages the plugin compiles against. Members without XML docs were checked in the 12.0.0
DLL's metadata strings. The research of features 1 and 2 still applies.

## R1. Where playback time comes from: `ISessionManager` playback events

- **Decision**: a hosted `PlaybackTracker` subscribes to `ISessionManager.PlaybackStart`, `PlaybackProgress` and
  `PlaybackStopped` in `StartAsync`, and unsubscribes in `StopAsync`. The events are synchronous, raised on the
  session manager's thread, so handlers only update in-memory state under a lock and never touch the database.
- **What the server sends**, from `Emby.Server.Implementations/Session/SessionManager.cs`:
  - **Start** (`:776-800`) calls `session.StartAutomaticProgress(info)`.
  - **Every real progress report** (`:903-965`) raises `PlaybackProgress` with `IsAutomated = false` and restarts
    automatic progress.
  - **Automatic progress** (`MediaBrowser.Controller/Session/SessionInfo.cs:383-450`) is a 1-second timer that
    re-raises `PlaybackProgress` with `IsAutomated = true`.
    - It skips ticks while the last real report said `IsPaused`, and stops at the item's runtime.
    - It keeps running until a Stop, or until the idle check ends the playback: every 5 minutes, it stops sessions
      whose `LastPlaybackCheckIn` is older than 5 minutes (`SessionManager.cs:640-672`).
  - **Stop** (`:1072`) stops the timer and raises `PlaybackStopped`.
- **Event args** (`MediaBrowser.Controller/Library/PlaybackProgressEventArgs.cs:17-46`): `Users`, `IsPaused`,
  `IsAutomated`, `PlaySessionId`, `DeviceId` and `Session`. `PlaybackStopEventArgs` derives from it.
  - The 12.0.0 DLL has `get_IsAutomated`, `get_IsPaused`, `get_PlaySessionId`, `get_Session` and `get_Users`.
  - `SessionInfo.UserId`, `Id`, `DeviceId` and `AdditionalUsers` are documented in the 12.0.0 XML.
- **Rationale**: these are the same events Playback Reporting uses (studied for patterns only, it is GPL). They cover
  every client that reports playback, which every mainstream client does (constitution II). `IEventConsumer<T>` would
  be equivalent (plan §2.3), but one hosted service with all three events keeps the state in one place.

## R2. Counting rule: wall-clock deltas, playing only, and a 60-second dead-client cut-off

- **Decision**: a pure `PlaybackClock` (no Jellyfin types, unit tested with a fake time) keeps one record per playback.
  - **Key**: `(Session.Id, PlaySessionId ?? "")`.
  - **Record**: `{ownerUserId, lastEventAt, lastRealAt, playing}`.
  - **For each event at time `now`, with `end = min(now, lastRealAt + 60 s)`**:
    1. If the record was `playing` and `end > lastEventAt`, add `min(end − lastEventAt, 30 s)` to the owner's usage.
    2. Set `playing = !IsPaused`, then `lastEventAt = now`.
    3. If `!IsAutomated`, set `lastRealAt = now`.
  - **Start** creates the record with `playing = !IsPaused` and adds nothing.
  - **Stop** applies step 1, then removes the record.
  - **A progress event for an unknown key** (the plugin started mid-playback, or the user was marked as a kid
    mid-playback) creates the record like a start, so counting begins from the next report (spec edge case).
  - **A sweep removes records** with no event for 6 minutes. That is longer than the server's own 5-minute idle check,
    so the server ends them first in normal cases.
- **Why each limit**:
  - **Per-gap cap of 30 s** (FR-002): automatic ticks come every 1 s and real reports about every 10 s, so 30 s is
    more than twice any normal cadence. A clock jump, a long server stall or a restart therefore never adds more than
    30 s from one gap (spec edge case: clock jumps).
  - **60-second cut-off after the last real report** (FR-003, SC-004): a crashed client's synthetic ticks stop
    counting a minute after it went quiet, instead of after up to 5 minutes. A real report brings the cut-off back.
  - **Pauses** (FR-002, SC-002): a pause report arrives at once from every mainstream client, and synthetic ticks stop
    while paused. Time between a pause and the next play report is never counted. The error is at most the gap before
    the pause report is processed, under a second.
- **Accuracy (SC-001)**: synthetic ticks every second plus real check-ins, so the error over 10 minutes is about the
  first and last tick, a few seconds at most. ±30 s leaves room for slow clients.
- **Concurrent devices (FR-004, SC-003)**: different sessions have different keys, so they add up. One playback is
  never counted twice, because each record advances its own `lastEventAt` monotonically and events with
  `now < lastEventAt` add nothing.

## R3. Who is counted: the session owner, and only kids (clarification Q3)

- **Decision**: the counted user is `Session.UserId`, the user who owns the session, and only when that user is a kid.
  - `PlaybackProgressEventArgs.Users` includes `AdditionalUsers` (`SessionManager.GetUsers`), so it is deliberately
    **not** used (clarification Q3; docs/plan.md §3.1 updated).
- **Kid lookup per event**: a `KidRoster` keeps the set of kid user ids in memory.
  - It loads from `KidProfileStore.ListAsync` on startup.
  - `KidProfileService` updates it on mark and unmark (create and delete), and the user-deleted consumer also updates
    it.
  - Events never query SQLite. With per-second synthetic ticks on every playing session, a query per event would be
    wasteful.
- **Alternatives considered**:
  - Counting every user in `Users`: rejected by the user (Q3).
  - Querying the store per event: about 1 query per second per playing session. Rejected.

## R4. Day boundary: "day starts at" H in server local time, with daylight-saving time handled

- **Decision**: a pure `DayClock(TimeZoneInfo zone, int startHour)`.
  - `DayOf(utc)` returns the local date `d`, minus one day when the local hour is below `H`. It is written as
    `yyyy-MM-dd`.
  - `StartOf(d)` is the UTC instant of `d` at H:00 local:
    - if H:00 is invalid on that date (a spring-forward gap), the day starts at the first valid local time after it;
    - if H:00 is ambiguous (fall-back), the earlier instant is used.
  - `NextStart(utc)` is `StartOf(DayOf(utc) + 1)`.
- **Splitting**: each counted interval is split at `NextStart(intervalStart)`, so playback across the boundary counts
  for both days (FR-008).
- **Settings**:
  - `H` comes from `PluginConfiguration.DayStartsAtHour` (0–23, default 0), read through a small `IKidsSettings` seam
    so services and tests do not depend on `Plugin.Instance`.
  - The zone is `TimeZoneInfo.Local`, the same clock Jellyfin uses for `AccessSchedules` (plan §3.4).
  - A change of H applies to the next read and the next counted interval. Stored days are never recalculated (Story
    6.3).
- **Rationale**: plan Decisions (2026-10-17), "configurable day starts at hour in server local time". The settings
  live in the plugin XML, because they are small admin-owned settings (constitution, Technical Constraints).
- **Tests**: a fixed zone (`Europe/Madrid` from the system tz database, which Linux and macOS ship; skipped if it is
  missing) covers both daylight-saving changes, H = 0, H = 2 (inside the spring gap) and H = 4.

## R5. Storage: schema version 3, add-only usage

- **Decision**: migration step 3 adds three tables.

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

  - **Usage only grows** (FR-012a, FR-018, SC-005). The only write is
    `INSERT … ON CONFLICT DO UPDATE SET used_ms = used_ms + $delta`, with `$delta > 0` checked in code. No code path
    lowers `used_ms`. Pruning deletes whole days past retention, and a purge deletes everything for a user.
  - **Reports** insert each segment with `ON CONFLICT (user_id, segment_id) DO NOTHING`, and add `seconds` to
    `usage_day` only when a row was inserted. Both happen in one transaction (FR-018 idempotency).
- **Flushing (FR-007)**:
  - The tracker accumulates `pending[(userId, day)] += ms` in memory.
  - A timer flushes every 30 s. Playback stop and `StopAsync` flush too.
  - A failed flush keeps the pending amounts and retries on the next tick (spec edge case: store unavailable).
  - Status reads add the not-yet-flushed amounts, so clients see live numbers without a write per tick.
- **Restart loss (SC-009)**: at most the last 30 s plus the in-flight gap, so under 1 minute.

## R6. Status computation

- **Decision**: a pure `StatusCalculator`:
  - `used = stored + pending` for (kid, today);
  - `bonus = sum(minutes) × 60` for today;
  - `allowance = profile.DailyAllowanceMinutes × 60`, or none;
  - `remaining = max(0, allowance + bonus − used)` when an allowance is set;
  - `timeUp = allowance is set and used ≥ allowance + bonus`.
- **The answer**: date, `UsedSeconds`, `AllowanceSeconds?`, `BonusSeconds`, `RemainingSeconds?`, `TimeUp`,
  `ServerTime`, `NextDayStartsAt` and `WarnAtSeconds: [300, 60]` (spec FR-013). Allowed hours are left out until
  feature 5 (clarification Q4).
- **Access**: `ProfileAccess.CanRead` (self or admin), with `IgnoreParentalControl`. A non-kid answers 404, and a store
  failure answers 500 (FR-016).

## R7. The allowance lives in the profile settings JSON

- **Decision**: `KidProfileSettings.DailyAllowanceMinutes` is an `int?`. The validator checks 0–1440 under the error
  key `DailyAllowanceMinutes`. `KidProfileDto` and `KidProfileUpdate` gain the field.
  - **No migration** is needed. Settings are JSON with missing properties defaulted (feature 1 research R2), so
    existing kids read as `null`, which means unlimited (FR-009).
  - **PUT stays a full replacement** (feature 1 contract): a client that omits the field clears the allowance. The
    contract states this, and the dashboard editor and Wholphin always send the whole profile.

## R8. Bonus, reports and purge endpoints

- **`POST /Kids/Users/{id}/WatchTime/Bonus {Minutes}`**:
  - `ProfileAccess.CanMutate`, which means admin or grant, through `CallerResolver` from feature 2;
  - minutes 1–240, otherwise 400 keyed `Minutes`;
  - not a kid: 404;
  - success: 200 with the new status;
  - `granted_by` stores the user name, or "API key".
- **`POST /Kids/Users/{id}/WatchTime/Report {Segments:[{Id, StartedAt, Seconds}]}`**:
  - **Self only**: `caller.UserId == id`, which also excludes API keys (FR-020);
  - **Limits**: 1–100 segments; `Id` 1–64 chars; `Seconds` 1–43 200;
  - **`StartedAt`** must be at most 5 minutes in the future (clock skew) and not before the retention window;
  - **Order of checks**: authorization (403), then not a kid (404), then validation (400). One invalid segment rejects
    the whole request (FR-019), keyed `Segments[i].Field`;
  - **Response**: 200 with the new status.
- **`DELETE /Kids/Users/{id}/Profile?purge=true`** also deletes the user's usage, bonus and reported segments (plan
  §4.1). Without `purge`, usage is kept (FR-022).
- **The user-deleted consumer and the orphan sweep** delete usage too (FR-022).

## R9. Retention task: `IScheduledTask`

- **Decision**: `PruneUsageTask : IScheduledTask`.
  - It shows under "Kids" in the dashboard's Scheduled Tasks, where an admin can also run it on demand (FR-021).
  - The default trigger is daily at 03:30 (`TaskTriggerInfoType.DailyTrigger`).
  - It deletes `usage_day`, `bonus` and `reported_segment` rows whose `day` is older than today − 90 days.
- **12.0.0 check**: `IScheduledTask.Name`, `Key`, `Description`, `Category`, `ExecuteAsync(IProgress<double>,
  CancellationToken)` and `GetDefaultTriggers()` are all in `MediaBrowser.Model.xml`.
- **Rationale**: this is Jellyfin's own task system (constitution I). Jellyfin finds tasks by scanning plugin
  assemblies (`ApplicationHost.GetExports<IScheduledTask>`, `ApplicationHost.cs:424`) and builds them from the
  server's services, so the task needs no DI registration (corrected during implementation). The plan §4
  tree names this task.

## R10. Dashboard: status per kid, and the setting through Jellyfin's plugin configuration API

- **Decision**:
  - The Kids list loads each kid's `GET Status` (a household has 1–5 kids) and shows "12 of 45 min today" or "12 min
    today (no limit)", with a "+ minutes" bonus control that posts `WatchTime/Bonus`.
  - The kid editor gains a "Daily allowance (minutes)" number field, empty for unlimited.
  - "Day starts at" is a select (0–23) on the Kids page. It is saved with jellyfin-web's own
    `ApiClient.getPluginConfiguration(id)` and `updatePluginConfiguration(id, config)`, which call
    `GET`/`POST /Plugins/{id}/Configuration` (RequiresElevation).
- **No `Admin/Overview` endpoint** in this feature (spec assumption). One status call per kid is enough, and it reuses
  the client contract.
- **Rationale**: these reuse Jellyfin's plugin configuration API for the one admin setting (constitution I), and add
  no new admin endpoint.

## R11. Proving it against the dev server

- **Simulated checks in `scripts/dev-check.sh`**, against the process-compose loop:
  - **Playback without media**: playback is driven through Jellyfin's own `POST /Sessions/Playing`,
    `/Sessions/Playing/Progress` and `/Sessions/Playing/Stopped` as alice, with a real item from the dev library.
    Synthetic ticks then run for real. The checks take about 3 minutes:
    - 60 s of playing counts 60 ± 5 s;
    - 30 s paused adds ≤ 5 s;
    - two sessions at once add up;
    - after the client stops reporting, counting stops within about 60 s;
    - a session owned by an admin with alice as an additional user adds nothing to alice.
  - **API checks**: reports (add-only, idempotent, rejected segments), bonus (admin, grant, kid refused), status
    fields, and `Info` with `allowance`.
- **Real-client check (SC-001)**, a quickstart step:
  - The dev provisioning creates a Kids library with a generated 12-minute test video (jellyfin-ffmpeg `testsrc`),
    if it is missing.
  - A Playwright script (out of the repo, as in features 1 and 2) plays it for 10 minutes in jellyfin-web as alice,
    with a 3-minute pause, and reads her status.
- **No spike is needed**. The counting relies only on the documented playback events and on the server's 1-second
  synthetic ticks, read from source (R1). The simulated dev-check proves both on 12.2 before the real-client run.
