# Data model: Kids enforcement

Nothing new is stored in `kids.db`. Two settings join the plugin XML; everything else is in memory and rebuilt at
startup.

## Settings (`PluginConfiguration`, plugin XML)

| Field | Type | Default | Rule |
|---|---|---|---|
| `EnforcementEnabled` | bool | `false` | Off: no request is refused, no message or stop is sent. Read on every check, so a change applies at once |
| `SittingBreakMinutes` | int | `15` | 1–240; other values read as 15. A sitting resets after this long with nothing playing; after the limit, the kid waits this long |

`IKidsSettings` gains `EnforcementEnabled` and `SittingBreak` (`TimeSpan`).

## DenialReason

`TimeUp` (`timeUp`), `OutsideSchedule` (`outsideSchedule`), `SessionLimit` (`sessionLimit`). When several hold, the
reported one is the first in this order: `OutsideSchedule`, `TimeUp`, `SessionLimit` (the one that lasts longest
first).

## Denial

| Field | Meaning |
|---|---|
| `Reason` | A `DenialReason` |
| `Until` | When it ends if nothing changes: the next day start (`TimeUp`), the end of the break (`SessionLimit`), or null (`OutsideSchedule`; Jellyfin's schedule decides) |

Identity for escalation records: `(Reason, Until)`.

## KidBudget (per kid)

| Field | Meaning |
|---|---|
| `Day` | The day it describes, under the "day starts at" rule |
| `LimitMs` | (Allowance + bonus) × 60 000, or null when unlimited |
| `UsedMs` | Stored + pending at load, plus every interval counted since for that day |
| `AllowanceMinutes` | The profile's allowance, to start the next day without I/O |
| `SittingLimitMinutes` | The profile's sitting limit, or null |

Transitions: `Load` (from `UsageService.GetStatusAsync` and the profile) → `Add(interval)` per counted interval (split
at day starts) → on a new day, restart at `UsedMs = 0`, `LimitMs = allowance` (no bonus yet) and queue a reload.
`TimeUp` holds while `LimitMs` is set and `UsedMs ≥ LimitMs`. Missing budget (load failed): no `TimeUp` (fail open).

## Sitting (per kid)

| Field | Meaning |
|---|---|
| `UsedMs` | Playing time in the current sitting |
| `StartedAt` | When the current sitting's first interval started (identifies it for warnings) |
| `LastPlayedAt` | End of the last counted interval |

Rules: on an interval `[from, to]`, if `from − LastPlayedAt ≥ break` the sitting restarts (`UsedMs = 0`,
`StartedAt = from`); then `UsedMs += to − from`, `LastPlayedAt = max(LastPlayedAt, to)`. With a sitting limit `L`:
`SessionLimit` holds while `UsedMs ≥ L` and `now < LastPlayedAt + break`; `Until = LastPlayedAt + break`. Reads after
`LastPlayedAt + break` report a new, empty sitting.

Reported in Status (when the kid has a sitting limit): `SittingLimitSeconds`, `SittingUsedSeconds`,
`SittingRemainingSeconds = max(0, L − UsedMs)`, and `BreakRemainingSeconds` while denied for `SessionLimit`.

## Schedule cache (per kid)

A detached Jellyfin `User` entity, from `IUserManager.GetUserById`; replaced on `OnUserUpdated` and every 60 s. The
denial calls `user.IsParentalScheduleAllowed()`. Warnings use `ScheduleClock` over `user.AccessSchedules`. No user
(not found, or load failed): no `OutsideSchedule` (fail open; Jellyfin itself still refuses `[Authorize]` routes).

## Per-session enforcement record

Keyed by Jellyfin session id; removed when the session ends or the kid is no longer denied and the record is idle.

| Field | Meaning |
|---|---|
| `Denial` | The denial it was stopped for |
| `StoppedAt` | When the full chain ran (once per denial) |
| `LastEscalationAt` | When stop/kill/abort/end last ran; repeats need ≥ 10 s |
| `Warned` | Set of `(kind, threshold, instance)` notices already sent |

## In-flight delivery (per kid)

`HttpContext` of each guarded delivery request being served to a kid, from the moment the filter lets it through until
the response completes. `AbortAll(kid)` calls `HttpContext.Abort()` on each.

## Guard coverage

Expected action names (research R3), the actions the convention matched (with kind: negotiate, open, deliver), the
expected ones not found, and the check state (`pending` until the startup check ran, then `ok` or `degraded`).
