# Implementation Plan: Undo a bonus and end a break

**Branch**: `013-bonus-undo-break` (spec directory `013-bonus-undo-break`) | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/013-bonus-undo-break/spec.md`

## Summary

- **Bonus ids**: schema 5 rebuilds `bonus` with `AUTOINCREMENT`, so an id is never reused (R1). `UsageStore.AddBonusAsync`
  returns the id; new `ListBonusesAsync` and `DeleteBonusAsync`.
- **Undo**: `UsageService.UndoBonusAsync(caller, userId, bonusId)` checks kid → bonus of that kid → today and within
  `BonusUndo.Window` (10 minutes) → deletes; outcomes `Saved`, `NotAKid`, `NotFound`, `TooLate` (R2). Endpoints
  `GET` and `DELETE /Kids/Users/{id}/WatchTime/Bonus[/{bonusId}]` in `AllowanceController`; `POST .../Bonus` answers
  `BonusId` and `BonusUndoUntil` on top of the status.
- **End break**: `EnforcementState.EndBreak(kid, now)` forgets the sitting only while its break runs and returns the
  time that was left (R3). `POST /Kids/Users/{id}/WatchTime/Break/End`.
- **Grant name**: bonuses given through a grant are recorded as "{name} with a parent grant" (R5).
- **Info**: `ApiMinorVersion` 2, `bonusUndo` and `breakEnd` in `Features`.
- **Dashboard**: the kid editor lists today's bonuses with Undo and shows End break during a break (R6).
- **Release**: 0.0.9.0 in `build.yaml` (changelog) and `Directory.Build.props`; README API list and `docs/plan.md`.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); dashboard pages in plain ES modules (no build step).

**Primary Dependencies**: `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0 (compile only); `Microsoft.Data.Sqlite`
(shipped with the server). No new Jellyfin API: the feature extends the plugin's own allowance and sitting state.

**Storage**: `kids.db` schema 4 → 5: `bonus` rebuilt with `id INTEGER PRIMARY KEY AUTOINCREMENT`, rows and ids kept.

**Testing**: xUnit for the migration (rows and ids kept, ids not reused after a delete), the store (add returns id,
list, delete), `UsageService.UndoBonusAsync` (each outcome, window edge at exactly 10 minutes, day boundary, another
kid's id), `EnforcementState.EndBreak` (in break, not in break, next sitting from zero, no limit), DTOs and
`KidsInfo`. `scripts/dev-check.sh` gains section `f` (fast) and extends the slow `bi` sitting check with End break.

**Target Platform**: Jellyfin 12.0–12.2 plugin (`targetAbi 12.0.0.0`); the dev loop runs 12.2.

**Project Type**: server plugin (one project plus tests).

**Performance Goals**: one indexed read and one delete per undo; End break is an in-memory change.

**Constraints**: additive API only; `TreatWarningsAsErrors`; names inserted as text; no git writes, no household
server.

**Scale/Scope**: a household: a few kids, a few bonuses a day.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How the design complies |
|---|---|
| I. Leverage Jellyfin | Bonuses and sittings are kids-mode state Jellyfin does not have (allowances, enforcement). Jellyfin's allowed hours are not touched: End break never lifts `outsideSchedule`. |
| II. The server decides | Undo and End break are decided on the server for every client; a kid's token is refused (admin, API key or parent grant only). Usage is never lowered: undo removes a bonus, only within 10 minutes and on its day; End break lowers nothing. Both are logged. "Reset today" stays the only way to lower a day's usage; no amendment needed. |
| III. Parents never locked out | Every endpoint uses `IgnoreParentalControl`. The migration only rebuilds a plugin table, in the migration transaction. A downgrade to 0.0.8.0 meets schema 5 and reports the database as newer (existing rule since schema 2–4); the household reinstalls 0.0.9.0. |
| IV. Secrets stay secret | No secrets involved; the bonus list (who gave time) is readable only by the kid and administrators, not anonymously. |
| V. Stable contract | Additive: `BonusId`/`BonusUndoUntil` on the bonus answer, two new routes and one listing, `ApiMinorVersion` 2, `bonusUndo` and `breakEnd`. `ApiVersion` stays 1. |
| VI. Tested where it matters | Unit tests for the window and day arithmetic, the store, the migration and the sitting; dev-check for authorization, logging and the enforcement effect; 0 warnings. |

No violations.

## Project Structure

### Documentation (this feature)

```text
specs/013-bonus-undo-break/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── kids-api-v1-bonus-undo-break.md   # API 1.2: bonus ids, undo, list, End break, Info
│   └── dashboard-bonus-break.md          # editor: today's bonuses, Undo, End break
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── Storage/KidsDatabase.cs          # schema 5
├── Storage/UsageStore.cs            # AddBonusAsync returns id; ListBonusesAsync, DeleteBonusAsync
├── Storage/StoredBonus.cs           # new: a bonus row
├── Allowance/BonusUndo.cs           # new: window, UndoUntil, CanUndo
├── Allowance/TodayBonuses.cs, BonusState.cs   # new: the list as read, with undo state
├── Allowance/UsageService.cs        # AddBonusAsync id, ListBonusesAsync, UndoBonusAsync
├── Allowance/UsageOutcome.cs        # + NotFound, TooLate
├── Allowance/UsageResult.cs         # + Bonus
├── Auth/CallerContext.cs            # ActorName ("… with a parent grant")
├── Enforcement/EnforcementState.cs  # EndBreak (uses Sitting.Reset from feature 7)
├── Api/AllowanceController.cs       # GET/DELETE Bonus, Break/End, BonusId on POST
├── Api/KidStatusDto.cs              # + BonusId, BonusUndoUntil
├── Api/BonusListDto.cs, BonusDto.cs # new
├── Api/KidsInfo.cs                  # minor 2, features
└── Configuration/kidsUser.html, kidsUser.js

tests/Jellyfin.Plugin.Kids.Tests/    # KidsDatabaseTests, UsageStoreTests, UsageServiceTests,
                                     # EnforcementStateTests, KidStatusDtoTests, KidsInfoTests
scripts/dev-check.sh                 # section f, bi extended
README.md, build.yaml, Directory.Build.props, docs/plan.md
```

**Structure Decision**: no new folders; bonus rules next to the allowance code, the break next to the sitting.

## Phases

- **Phase 0**: [research.md](research.md) R1–R7.
- **Phase 1**: [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md).
- **Phase 2 ([tasks.md](tasks.md))**: schema and store → undo → End break → Info → dashboard → release → build, tests,
  dev-check.

## Complexity Tracking

None.
