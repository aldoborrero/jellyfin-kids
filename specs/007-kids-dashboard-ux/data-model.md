# Data model: Kids dashboard UX and "Reset today"

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

No schema change (`kids.db` stays at its current version), no new profile or configuration field.

## Day reset (operation on schema v3 tables)

| Table | Effect for (kid, current day) | Other rows |
|---|---|---|
| `usage_day` | the row is deleted (used time reads 0); the next counted time creates it again | untouched |
| `bonus` | the day's rows are deleted (bonus reads 0) | untouched |
| `reported_segment` | **kept**: a segment id reported before the reset still adds nothing when re-sent | untouched |

In memory:

| State | Effect |
|---|---|
| `UsageService` pending `(kid, day)` | removed, under the flush lock, so no flush writes it back |
| `EnforcementState` sitting | reset: no playing time, no break; the next counted interval starts a new sitting |
| `EnforcementState` budget | reloaded from the store (used 0, bonus 0, same allowance) |

### DayReset (result, not stored)

| Field | Type | Meaning |
|---|---|---|
| `Day` | `yyyy-MM-dd` | the kid's current day under the "day starts at" rule |
| `UsedMs` | long | stored plus pending time cleared |
| `BonusMinutes` | int | bonus minutes cleared |

Logged with the caller's name and the time (FR-023).

### Feature 3 rule, amended

Feature 3 (FR-012a, clarification Q5) said no one may lower a day's recorded usage. From this feature: **no client
report and no other API lowers usage; the only exception is Reset, by an administrator, an API key or a parent grant
holder, logged.** `usage_day.used_ms` still only ever grows through writes; Reset deletes the row.

## Health issue (dashboard, not stored)

| Field | Values |
|---|---|
| `kind` | `admin`, `allFolders`, `noRating`, `unrated`, `liveTvOff`, `liveTvNone`, `downloads` |
| `text` | the line shown (contracts/dashboard-ux.md) |
| `fix` | `#/dashboard/users/{id}/profile` · `/access` · `/parentalcontrol`, or the kid editor's channels |

Derived from `UserDto.Policy`, the profile's `LiveTvChannelIds` and the server's channel count (research R2).

## Live TV state (dashboard, not stored)

- `picked` = profile ids that are server channels; `total` = server channels (null if unreadable).
- "No live TV" when `Policy.EnableLiveTvAccess === false` or `picked` is 0 (with `total` known); else
  "Watches {picked} of {total} channels" ("1 channel" singular), or "Watches {n} channels" without `total`.

## Form snapshot (dashboard, not stored)

The JSON of what Save would send (minus `Revision`), taken after load and after each save; "Unsaved changes" while the
current JSON differs.
