# Tasks: Undo a bonus and end a break

**Input**: Design documents from `specs/013-bonus-undo-break/` (plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md)

**Tests**: required by constitution VI for the window and day arithmetic, storage, the migration and the sitting
(xUnit); server behaviour is checked by `scripts/dev-check.sh` section `f` and the slow `bi` check against the dev
server, the page in the browser on the dev loop.

## Phase 1: Setup

- [X] T001 Bump the version to 0.0.9.0 in `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`) and `build.yaml` (`version`; changelog: bonus ids and undo within 10 minutes, today's bonuses list, End break, `ApiMinorVersion` 2 with `bonusUndo` and `breakEnd`, the kid editor's bonus list and End break, schema 5)

## Phase 2: Foundational

- [X] T002 Schema 5 in `src/Jellyfin.Plugin.Kids/Storage/KidsDatabase.cs`: `SchemaVersion = 5`; migration rebuilding `bonus` as `id INTEGER PRIMARY KEY AUTOINCREMENT, user_id TEXT NOT NULL, day TEXT NOT NULL, minutes INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 240), granted_by TEXT NOT NULL, created_at TEXT NOT NULL`, copying rows with their ids, then `CREATE INDEX bonus_user_day ON bonus (user_id, day)`
- [X] T003 `StoredBonus(long Id, string Day, int Minutes, string GrantedBy, DateTimeOffset CreatedAt)` in `src/Jellyfin.Plugin.Kids/Storage/StoredBonus.cs`; in `UsageStore.cs`: `AddBonusAsync` returns the new id (`last_insert_rowid()` in the same transaction), `ListBonusesAsync(userId, day)` newest first, `DeleteBonusAsync(userId, bonusId)` → rows deleted (`WHERE id = $id AND user_id = $user`)
- [X] T004 [P] Tests in `tests/Jellyfin.Plugin.Kids.Tests/KidsDatabaseTests.cs` (from version 4: rows and ids kept, AUTOINCREMENT in place, an id is not reused after deleting the newest row) and `UsageStoreTests.cs` (add returns increasing ids, list newest first and per kid and day, delete only that kid's row, second delete returns 0)

## Phase 3: User Story 1 - Undo a bonus (P1)

**Goal**: FR-001–FR-008. **Independent test**: quickstart §2 `fb`–`fi`.

- [X] T005 [US1] `BonusUndo` in `src/Jellyfin.Plugin.Kids/Allowance/BonusUndo.cs`: `Window = 10 min`; `UndoUntil(createdAt, nextDayStart)` = "min(CreatedAt + Window, start of the next day after CreatedAt's day)"; `CanUndo(bonus day, today, now, undoUntil)` = "the bonus's day is today and now <= UndoUntil"
- [X] T006 [US1] `UsageOutcome.NotFound`, `UsageOutcome.TooLate`; `UsageResult` gains `Bonus` (the bonus added or undone) in `src/Jellyfin.Plugin.Kids/Allowance/`
- [X] T007 [US1] `UsageService` in `src/Jellyfin.Plugin.Kids/Allowance/UsageService.cs`: `GrantedByName(caller)` ("{LogName} with a parent grant" unless admin or API key); `AddBonusAsync` stores that name and returns the bonus in the result; `ListBonusesAsync(userId)` → today's day, bonuses, now and next day start (null when not a kid); `UndoBonusAsync(userId, bonusId)` → kid, find in today's or any day's rows of the kid, check window, delete, `UsageChanged`, status
- [X] T008 [US1] `KidStatusDto.BonusId`, `BonusUndoUntil` in `src/Jellyfin.Plugin.Kids/Api/KidStatusDto.cs`; `BonusDto`, `BonusListDto` in `src/Jellyfin.Plugin.Kids/Api/`
- [X] T009 [US1] `AllowanceController` in `src/Jellyfin.Plugin.Kids/Api/AllowanceController.cs`: POST Bonus answers `BonusId`/`BonusUndoUntil` and logs the recorded name; `GET WatchTime/Bonus` (CanRead); `DELETE WatchTime/Bonus/{bonusId:long}` (CanMutate → 404 kid → 404 "No such bonus." → 409 "This bonus can no longer be undone."), log `Bonus {BonusId} of {Minutes} min for {UserId} (given by {GrantedBy} at {CreatedAt}) undone by {Caller}`, reload enforcement before answering
- [X] T010 [P] [US1] Tests in `tests/Jellyfin.Plugin.Kids.Tests/UsageServiceTests.cs`: undo inside window, at exactly 10 min (allowed), after (TooLate), yesterday's bonus (TooLate), day boundary caps UndoUntil, another kid's id (NotFound), twice (NotFound), not a kid, status after undo (time's up back), grant name recorded; `KidStatusDtoTests.cs` for the new fields omitted by default
- [X] T011 [US1] dev-check `fa`–`fi` in `scripts/dev-check.sh` per quickstart

## Phase 4: User Story 2 - End a break (P1)

**Goal**: FR-009–FR-012. **Independent test**: quickstart §2 `fj`, `fk`, slow `bi`.

- [X] T012 [US2] `EnforcementState.EndBreak(Guid kid, DateTimeOffset now)` in `src/Jellyfin.Plugin.Kids/Enforcement/EnforcementState.cs`: under the lock, when the kid has a sitting limit and `Sitting.Read(...).BreakEndsAt > now`, `Sitting.Reset()` and return the time left; else null
- [X] T013 [US2] `POST WatchTime/Break/End` in `AllowanceController.cs`: CanMutate → kid (status null → 404) → `EndBreak`; log `Break of {UserId} ended by {Caller} with {Seconds} s left` only when ended; reload and answer the status
- [X] T014 [P] [US2] Tests in `tests/Jellyfin.Plugin.Kids.Tests/EnforcementStateTests.cs`: in break → ended, no denial, next interval starts a sitting at zero; not in break / under the limit / no limit / unknown kid → null and unchanged; budget (time's up) unaffected
- [X] T015 [US2] dev-check `fj`, `fk` and End break inside the slow `bi` in `scripts/dev-check.sh`

## Phase 5: User Story 3 - Discovery (P2)

- [X] T016 [US3] `KidsInfo.CurrentApiMinorVersion = 2`, `bonusUndo`, `breakEnd` appended in `src/Jellyfin.Plugin.Kids/Api/KidsInfo.cs`; `tests/Jellyfin.Plugin.Kids.Tests/KidsInfoTests.cs` updated; the `ea` check in dev-check no longer pins minor 1

## Phase 6: User Story 4 - Dashboard (P2)

- [X] T017 [US4] `kidsUser.html`: End break button after Reset today, `div.kidsBonusList` under the bonus help, styles per `contracts/dashboard-bonus-break.md`
- [X] T018 [US4] `kidsUser.js`: render End break from the status, `endBreak()`, `loadBonuses()` / `renderBonuses()` with Undo, reload after add/undo/reset/end break; header comment updated

## Phase 7: Polish

- [X] T019 [P] README API list (link this feature's contract), screen-time lines; `docs/plan.md` §7 spec 013 line
- [X] T020 `dotnet build -c Release` with 0 warnings and `dotnet test` green
- [X] T021 Dev loop on a free port, `scripts/dev-check.sh` green (and `DEV_CHECK_SLOW=1` for `bi`), browser check if possible; stop the dev loop. Browser check not run here: the editor changes were checked with `node --check` and against the API by dev-check; the Kids page is unchanged. `bi` now ends the break with End break instead of waiting it out, so the natural end of a break is covered by the unit tests (`SittingTests`), not dev-check
- [X] T022 Review the diff, mark tasks done, set the spec status

## Dependencies

- T002 → T003 → T004, T007. T005–T006 → T007 → T008–T009 → T010–T011.
- T012 → T013 → T014–T015. US3 independent. US4 after US1 and US2 server side. Polish last.

## Parallel examples

- T004 with T005; T010 with T012; T014 with T016; T019 any time.

## Implementation strategy

MVP: US1 (undo) and US2 (End break) on the server, the widget's needs. Then discovery, dashboard, release polish.
