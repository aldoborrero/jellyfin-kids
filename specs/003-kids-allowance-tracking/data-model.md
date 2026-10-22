# Data model: Kids allowance tracking

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

`kids.db` goes from schema version 2 to 3 (three new tables). The kid profile gains one setting, and the plugin
configuration gains one setting.

## KidProfileSettings (changed)

| Field | Type | Default | Rules |
|---|---|---|---|
| `DailyAllowanceMinutes` | int or null | null (unlimited) | 0–1440 inclusive, error key `DailyAllowanceMinutes` (FR-009). 0 means time is up from the start of the day until a bonus is added |

The field is stored in the profile's settings JSON, so no migration is needed. Existing profiles read it as null. A
PUT is a full replacement, so omitting the field clears it.

## DailyUsage (`usage_day`)

| Field | Type | Rules |
|---|---|---|
| `user_id` | text (Guid "N") | A kid, or a former kid when unmarked without purge |
| `day` | text `yyyy-MM-dd` | The day under the "day starts at" rule (DayClock) |
| `used_ms` | integer ≥ 0 | **Only ever increased** (FR-012a, FR-018): `used_ms = used_ms + delta` with `delta > 0`. *Feature 7*: a parent's Reset today deletes the current day's row (and its bonus rows); see [spec 007](../007-kids-dashboard-ux/data-model.md) |

Primary key `(user_id, day)`. Rows are removed only by the retention task (older than 90 days), a purge, or the user
being deleted.

## Bonus (`bonus`)

| Field | Type | Rules |
|---|---|---|
| `id` | integer | Row id |
| `user_id` | text | The kid |
| `day` | text `yyyy-MM-dd` | The kid's current day when the bonus was added |
| `minutes` | integer 1–240 | Per request (FR-011). Several bonuses on a day add up |
| `granted_by` | text | The user name, or "API key" |
| `created_at` | UTC timestamp | |

## ReportedSegment (`reported_segment`)

| Field | Type | Rules |
|---|---|---|
| `user_id` | text | The kid who reported it (always the caller, FR-020) |
| `segment_id` | text, 1–64 chars | The client's id. Primary key `(user_id, segment_id)` makes reports idempotent |
| `day` | text | The day of `started_at` |
| `started_at` | UTC timestamp | Not more than 5 minutes in the future, and not before the retention window |
| `seconds` | integer 1–43 200 | Added to `usage_day` once, only when the row is new |
| `received_at` | UTC timestamp | |

A request carries 1–100 segments. One invalid segment rejects the whole request (FR-019).

## PlaybackRecord (in memory, PlaybackClock)

Keyed by `(SessionId, PlaySessionId)`.

| Field | Meaning |
|---|---|
| `OwnerUserId` | `Session.UserId`. Only kids get records (clarification Q3) |
| `LastEventAt` | Time of the last event, real or synthetic |
| `LastRealAt` | Time of the last event with `IsAutomated = false` |
| `Playing` | `!IsPaused` of the last event |

**Transitions**:

```text
 start / unknown progress ──▶ [record: Playing = !IsPaused]
 progress at t:  add min(min(t, LastRealAt+60 s) − LastEventAt, 30 s) if Playing and positive; update fields
 stop at t:      add as for progress, then remove
 no event for 6 min ──▶ removed by the sweep
```

Added time is split at the next day start and accumulated in `Pending[(user, day)]` until it is flushed (every 30 s,
on stop and on shutdown).

## KidStatus (not stored)

| Field | Type | Rule |
|---|---|---|
| `Date` | `yyyy-MM-dd` | DayClock.DayOf(now) |
| `UsedSeconds` | int | (stored + pending ms) / 1000, rounded down |
| `AllowanceSeconds` | int or absent | DailyAllowanceMinutes × 60 |
| `BonusSeconds` | int | Sum of today's bonus minutes × 60 |
| `RemainingSeconds` | int or absent | max(0, allowance + bonus − used). Absent when unlimited |
| `TimeUp` | bool | Allowance set and used ≥ allowance + bonus (FR-014) |
| `ServerTime` | UTC timestamp | now |
| `NextDayStartsAt` | UTC timestamp | DayClock.NextStart(now) |
| `WarnAtSeconds` | int list | `[300, 60]` |

## PluginConfiguration (changed, plugin XML)

| Field | Type | Default | Rules |
|---|---|---|---|
| `DayStartsAtHour` | int | 0 | 0–23. Out-of-range values read as 0. Changing it applies to the next reads and counted intervals; stored days are not recalculated |

## KidRoster (in memory)

The set of kid user ids. It is loaded from the store at startup and updated on mark, unmark and user deletion. The
tracker only counts owners in this set.

## PluginInfo (changed)

`Features` becomes `["profiles", "parentLock", "allowance"]`.
