# Research: Undo a bonus and end a break

Citations are against the plugin source at 0.0.8.0.

## R1. Bonus ids that are never reused

- **Decision**: expose `bonus.id` as `BonusId`; schema 5 rebuilds `bonus` as `id INTEGER PRIMARY KEY AUTOINCREMENT`
  (copying every row with its id, then recreating `bonus_user_day`).
- **Findings**: `bonus.id` is `INTEGER PRIMARY KEY` (`KidsDatabase.cs` migration 3). Without `AUTOINCREMENT` SQLite
  hands out max(rowid)+1, so deleting the newest row (an undo, or Reset today) gives its id to the next bonus. TV A
  undoing bonus 5, TV B adding bonus 5, then TV A's retried `DELETE .../5` would remove B's bonus. With
  `AUTOINCREMENT`, `sqlite_sequence` keeps the highest id ever used.
- **Rationale**: integer ids stay small, readable in logs and safe in JSON; the rebuild is the documented SQLite way
  to add `AUTOINCREMENT` and runs once inside the migration transaction.
- **Alternatives**: "delete the last bonus" (two TVs race, retries remove two; rejected in the spec); a GUID column
  (a new column and index for the same guarantee); an opaque `rowid.timestamp` string (encodes a workaround in the
  contract); keeping undone rows with a flag (the `minutes` CHECK and every sum would need to change).
- **Downgrade**: 0.0.8.0 sees schema 5 and reports the database as newer, like every earlier schema step.

## R2. The undo window

- **Decision**: a bonus can be undone while `now <= created_at + 10 min` **and** its `day` is the kid's current day
  (`BonusUndo.Window`, `BonusUndo.UndoUntil(createdAt, nextDayStart) = min(createdAt + 10 min, next day start)`).
- **Rationale**: 10 minutes covers a wrong tap noticed on the spot and caps what undo can take away at a bonus the
  kid has had for minutes; afterwards a bonus behaves as before and only the explicit, logged Reset today clears a
  day. A fixed constant keeps it out of the household settings (no UI, no validation).
- **Order of checks**: authorization (403) → kid (404 "This user is not a kid.") → bonus of this kid (404 "No such
  bonus.") → window (409 "This bonus can no longer be undone."). Another kid's id is 404, not 403, so ids reveal
  nothing.
- **Race**: the delete is `DELETE FROM bonus WHERE id = $id AND user_id = $user`; when two undos meet, the one that
  deletes 0 rows answers 404.
- **Effect on enforcement**: `UsageChanged` is raised and the controller reloads the kid before answering, as for a
  bonus, so time's up comes back at once and the tracker stops playback as for any time's up (feature 5).

## R3. Ending a break

- **Decision**: `EnforcementState.EndBreak(kid, now)` → under the lock, read the sitting with the kid's limit; when
  `BreakEndsAt > now`, call `Sitting.Reset()` (feature 7) and return the time that was left; otherwise return null.
  The controller logs only when a break was ended and answers the status either way.
- **Findings**: the break is `Sitting.Read(...).BreakEndsAt` = `LastPlayedAt + break` once the sitting reached the
  limit (`Sitting.cs`); `EvaluateLocked` denies `sessionLimit` while it is set. `Sitting.Reset()` clears `_played`, so
  the next counted interval starts a new sitting at that interval (`Add`: `!_played` → `UsedMs = 0`,
  `StartedAt = from`).
- **Next sitting**: counts from the next counted playback, from zero, with the full limit, as after a break that ran
  out. Time played before End break is not carried. Warnings key on `StartedAt`, so the new sitting warns again.
- **Not in a break**: no sitting limit, under the limit, break over, or a kid not loaded (after a restart): nothing
  changes, 200. Ending a sitting that has not reached its limit would give extra time beyond what was asked.
- **Kept**: usage, bonus, budget (no reload needed, but the controller reloads anyway for a consistent answer).

## R4. Listing today's bonuses

- **Decision**: `GET /Kids/Users/{id}/WatchTime/Bonus` answers `{Date, ServerTime, UndoWindowSeconds, Bonuses: [{Id,
  Minutes, GrantedBy, CreatedAt, UndoUntil, CanUndo}]}`, newest first, for the kid's current day. Readable by the kid and
  administrators (`ProfileAccess.CanRead`), like the status.
- **Rationale**: the dashboard and a TV that reloaded need the ids; `ServerTime` and `CanUndo` spare clients a clock
  comparison against a possibly wrong TV clock.

## R5. Naming grant holders

- **Decision**: `AddBonusAsync` stores `granted_by` as `caller.LogName` for administrators and API keys and
  `caller.LogName + " with a parent grant"` otherwise (the wording of Reset today's log, feature 7). The bonus and undo
  log lines use the same name.
- **Rationale**: with a grant the token is the kid's, so `LogName` is the kid's name; showing "by alice" on alice's
  own bonus would be wrong. Older rows keep what they had (no data migration).

## R6. Dashboard

- **Decision**: in the kid editor's Screen time section: an "End break" button (`raised`, not `button-delete`) beside
  Reset today, shown while `BreakRemainingSeconds > 0`; under the bonus field, "Today's bonuses" as `fieldDescription`
  lines "{n} min at {HH:mm} by {name}" with a small "Undo" `button-link` while `CanUndo`, or "· can no longer be
  undone". The list loads with the status and after Add, Undo, Reset today and End break. No confirmation dialogs: undo
  is itself the correction and End break only gives.
- **Times**: shown on the server clock (`serverClock` from feature 7) when known, else the browser's.
- **Style**: the feature 7/12 patterns: `emby-button`, `fieldDescription`, toasts for success, `Dashboard.alert` for
  failures, text nodes only.

## R7. Capabilities and version

- **Decision**: `Features` gains `bonusUndo` and `breakEnd` after `avatar`; `KidsInfo.CurrentApiMinorVersion = 2`.
  camelCase like the existing names. Plugin 0.0.9.0.
