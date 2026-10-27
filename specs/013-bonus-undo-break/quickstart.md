# Quickstart: validating bonus undo and End break

## 1. Build and unit tests

```sh
dotnet build Jellyfin.Plugin.Kids.slnx -c Release     # 0 warnings (TreatWarningsAsErrors)
dotnet test Jellyfin.Plugin.Kids.slnx
```

## 2. Dev loop and dev-check

From the devshell, on a free port (never against the household server):

```sh
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> dev
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> scripts/dev-check.sh
DEV_CHECK_SLOW=1 …                                    # adds bi, which now also ends a break
```

New checks, section `f` ([contract](contracts/kids-api-v1-bonus-undo-break.md)):

| Check | Expected |
|---|---|
| `fa` Info | `ApiMinorVersion` 2, `bonusUndo` and `breakEnd` in `Features` |
| `fb` admin gives alice 30 min | 200, `BonusId` and `BonusUndoUntil` present |
| `fc` alice lists her bonuses | 200, the bonus with `CanUndo` true |
| `fd` alice undoes it with her own token | 403, bonus unchanged |
| `fe` admin undoes it | 200, `BonusSeconds` 30 min lower; logged with who gave and who undid |
| `ff` undo the same id again; an unknown id; a non-numeric id | 404; 404; 400 |
| `fg` two bonuses, undo the first | only the first is gone |
| `fh` a grant holder on alice's TV gives and undoes a bonus | 200 and 200; the list said "alice with a parent grant" |
| `fi` with time used and enforcement on, undoing the bonus | `TimeUp` true and `DeniedReason` `timeUp` in the answer |
| `fj` End break while not in a break | 200, status unchanged, nothing logged |
| `fk` End break as alice; for a non-kid | 403; 404 |
| `bi` (slow) in the break, admin ends it | no `BreakRemainingSeconds`, no denial, same `UsedSeconds`; PlaybackInfo allowed; logged |

The 10-minute window and the day boundary are covered by unit tests (a fake clock), not by dev-check.

## 3. Browser (jellyfin-web 12.2)

1. Kid editor: Add 15: "Today's bonuses" shows "15 min at HH:mm by admin  Undo".
2. Undo: toast, the line goes, the usage line drops by 15 min.
3. With the kid in a break (slow check, or a 5-minute limit and 1-minute break), End break shows beside Reset today;
   press it: toast, the sitting line no longer says "on a break", the button hides.
