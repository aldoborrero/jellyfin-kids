---

description: "Task list for the Kids session limit"
---

# Tasks: Kids session limit

**Input**: Design documents from `specs/004-kids-session-limit/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R4), data-model.md, contracts/kids-api-v1-session-limit.md,
contracts/dashboard-session-limit.md, quickstart.md

**Tests**: included. Validation, storage and the migration are unit tested (constitution VI); the API is checked
against the dev server. Write each phase's unit tests first and see them fail.

**Organization**: tasks are grouped by user story, with story numbers from spec.md:

| Story | Title | Priority |
|---|---|---|
| US1 | A parent sets how long one sitting may last | P1 |
| US2 | A client knows whether the server holds the session limit | P1 |
| US3 | The dashboard editor shows and checks the session limit | P2 |

## Conventions for every task

- Follow `DailyAllowanceMinutes` (feature 3) line by line: same validator pattern, same DTO and update docs, same
  editor field pattern.
- `TreatWarningsAsErrors` with StyleCop; every public member has XML docs. No new packages.
- Server checks: start the dev loop only while testing; new dev-check checks go before the final "Leave bob
  unthrottled" block.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Bump the version to 0.0.4.0 in `build.yaml` (with a changelog line) and `Directory.Build.props`

---

## Phase 2: Foundational

- [X] T002 Write failing tests in `tests/Jellyfin.Plugin.Kids.Tests/KidsDatabaseTests.cs`: migration 3 → 4 adds
  `"SessionLimitMinutes": null` to every profile, keeps the other settings, the lock and the usage tables, and leaves
  a row that already has the key unchanged; update the 1 → latest and 2 → latest expectations for the rewritten JSON
- [X] T003 Add migration step 4 (`json_insert`) and `SchemaVersion = 4` in `src/Jellyfin.Plugin.Kids/Storage/KidsDatabase.cs`
- [X] T004 Add `SessionLimitMinutes`, `MinSessionLimit = 5` and `MaxSessionLimit = 120` to
  `src/Jellyfin.Plugin.Kids/Profiles/KidProfileSettings.cs`, and the field to `ProfileComparer.cs`

**Checkpoint**: tests build; the database tests pass.

---

## Phase 3: User Story 1 - A parent sets how long one sitting may last (P1)

**Goal**: the profile stores, validates and returns the session limit.

**Independent Test**: quickstart §2 checks as, at and au.

- [X] T005 [P] [US1] Failing tests in `KidProfileValidatorTests.cs`: null, 5, 30 and 120 kept; -1, 0, 4 and 121
  rejected with "Must be between 5 and 120." keyed `SessionLimitMinutes`; reported together with an `AutoplayCap`
  error
- [X] T006 [P] [US1] Failing tests in `KidProfileStoreTests.cs`: insert and update round-trip the value; a stored row
  without the key reads null
- [X] T007 [P] [US1] Failing test in `KidProfileServiceTests.cs`: a save without the field clears it
- [X] T008 [US1] Add `SessionLimitMinutes` to `Profiles/KidProfileInput.cs` and the 5–120 rule to
  `Profiles/KidProfileValidator.cs`
- [X] T009 [US1] Add `SessionLimitMinutes` to `Api/KidProfileUpdate.cs` (full-replacement doc, mapped in `ToInput`) and
  `Api/KidProfileDto.cs` (absent when none)

**Checkpoint**: unit tests pass.

---

## Phase 4: User Story 2 - A client knows whether the server holds the session limit (P1)

**Goal**: `GET /Kids/Info` lists `sessionLimit`.

**Independent Test**: quickstart §2 check ar.

- [X] T010 [P] [US2] Failing test in a new `tests/Jellyfin.Plugin.Kids.Tests/KidsInfoTests.cs`: `KidsInfo.SupportedFeatures`
  is `profiles, parentLock, allowance, sessionLimit` in that order
- [X] T011 [US2] Move the features list to a static `KidsInfo.SupportedFeatures` in `Api/KidsInfo.cs` (used by `KidsController.GetInfo`) and
  add `"sessionLimit"`

---

## Phase 5: User Story 3 - The dashboard editor shows and checks the session limit (P2)

**Goal**: the kid editor sets, clears and checks the session limit.

**Independent Test**: quickstart §4.

- [X] T012 [US3] Add the "Session limit (minutes)" field with its help text and `SessionLimitMinutes` error slot to
  `Configuration/kidsUser.html`
- [X] T013 [US3] Load, check (on input and save) and send `SessionLimitMinutes` in `Configuration/kidsUser.js`

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T014 [P] Add dev-check checks ar–au to `scripts/dev-check.sh` (Info lists `sessionLimit`; 4 and 121 are 400
  keyed `SessionLimitMinutes`; round trip with alice's read and her 403; a PUT without the field leaves no key)
- [X] T015 [P] Constitution principle V wording, version 1.0.1, Sync Impact Report and Last Amended 2026-10-19 in
  `.specify/memory/constitution.md`
- [X] T016 [P] "Day starts at" changed mid-day note in `specs/003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md`
- [X] T017 [P] `docs/plan.md` §1, §4.1 and §7 (plan.md "docs/plan.md updates")
- [X] T018 `dotnet build` with 0 warnings and `dotnet test` all passing
- [X] T019 Run the dev loop, `scripts/dev-check.sh`, and the migration and dashboard steps; write "Last run" in
  `quickstart.md`; stop the dev loop

---

## Dependencies & Execution Order

- Phase 1 and Phase 2 first (T004's settings field is needed by every later test).
- US1 (Phase 3) and US2 (Phase 4) are independent; US3 needs US1's DTO field.
- T014 needs US1 and US2; T019 needs everything.

### Parallel Opportunities

- T005, T006 and T007 touch different test files.
- T015, T016 and T017 are documentation only.

## Implementation Strategy

US1 and US2 are the MVP: together they give Wholphin a server-stored, detectable setting. US3 adds the dashboard. The
whole feature is small enough to land as one change.
