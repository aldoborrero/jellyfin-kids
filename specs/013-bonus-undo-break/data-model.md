# Data model: Undo a bonus and end a break

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## Bonus (changed: schema 5)

```sql
CREATE TABLE bonus (
  id INTEGER PRIMARY KEY AUTOINCREMENT, user_id TEXT NOT NULL, day TEXT NOT NULL,
  minutes INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 240), granted_by TEXT NOT NULL, created_at TEXT NOT NULL);
CREATE INDEX bonus_user_day ON bonus (user_id, day);
```

Migration 4 → 5 copies every row with its id into the new table, drops the old one and renames (R1). Nothing else
changes. Ids are never reused afterwards.

| Field | Rule |
|---|---|
| `id` | assigned on insert, answered as `BonusId` / `Id`; never reused |
| `granted_by` | `LogName`, plus " with a parent grant" for grant holders (from 0.0.9.0) |
| `created_at` | UTC, ISO 8601 round-trip |

`StoredBonus(Id, Day, Minutes, GrantedBy, CreatedAt)`: a row as read by `UsageStore.ListBonusesAsync`.

## Undo rule (`BonusUndo`)

| Value | Definition |
|---|---|
| `Window` | 10 minutes |
| `UndoUntil` | min(`CreatedAt` + `Window`, start of the next day after `CreatedAt`'s day) |
| `CanUndo(now)` | the bonus's `day` is today and `now <= UndoUntil` |

Undo outcomes: `Saved` (row deleted, new status), `NotAKid`, `NotFound` (no row with that id for that kid, or deleted
meanwhile), `TooLate`.

## Sitting (changed behaviour)

`EnforcementState.EndBreak(kid, now)`: when the sitting's `BreakEndsAt > now`, `Sitting.Reset()` and return
`BreakEndsAt - now`; else null and no change.

| Before | After End break |
|---|---|
| in the break (`sessionLimit` denial, `BreakRemainingSeconds` > 0) | no sitting: `SittingUsedSeconds` 0, full `SittingRemainingSeconds`, no break; the next playback starts a sitting |
| any other state | unchanged |

Usage, bonus and budget are never touched.

## Status (changed answer of POST Bonus only)

`KidStatusDto` gains `BonusId` (long) and `BonusUndoUntil` (UTC), set only in the answer to `POST .../Bonus`; omitted
elsewhere.

## Bonus list (new)

`BonusListDto(Date, ServerTime, UndoWindowSeconds, Bonuses)`; `BonusDto(Id, Minutes, GrantedBy, CreatedAt, UndoUntil,
CanUndo)`, newest first.

## Kids info (changed)

`ApiMinorVersion` 2; `Features` + `bonusUndo`, `breakEnd`.
