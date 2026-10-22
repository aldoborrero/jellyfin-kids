# Tasks: Kids dashboard UX and "Reset today"

**Input**: Design documents from `specs/007-kids-dashboard-ux/` (plan.md, spec.md, research.md, data-model.md,
contracts/, quickstart.md)

**Tests**: required by constitution VI for the reset logic (xUnit); server behaviour is checked by
`scripts/dev-check.sh`, the pages by a headless Chromium run outside the repo.

## Phase 1: Setup

- [X] T001 Bump the version to 0.0.7.0 in Directory.Build.props and build.yaml (`version`, changelog: health lines with Fix links, enforcement banner and "App only" pill, "No daily limit" / "No screen time today", live TV state, Reset today, one save model with toasts)

## Phase 2: Foundational (blocks the dashboard stories)

- [X] T002 [P] Write the select boxes by hand in `src/Jellyfin.Plugin.Kids/Configuration/kids.html` (`#kidsDayStart`, `#kidsLockType`): `<label class="selectLabel" for=…>`, `class="emby-select-withcolor emby-select"`, no `label` attribute, the `selectArrowContainer` markup (research R3)
- [X] T003 [P] Add shared page helpers, duplicated as the pages already do, in `kids.js` and `kidsUser.js`: `serverClock(status, dayStartHour)` → `{ offsetMinutes, format(dateUtc) → 'HH:mm', label → 'UTC±hh:mm' }` (research R5), `healthIssues(user, pickedCount, serverChannelCount)` (research R2, order admin first), `liveTvState(user, pickedIds, serverChannels)` (data-model "Live TV state"), `allowanceText(allowanceSeconds)` ("No daily limit" / "No screen time today" / "{n} min a day"), `toast(text)` → `Dashboard.alert(text)`

## Phase 3: User Story 5 - A parent resets a kid's day (P2, server first because the editor needs it)

**Goal**: `POST /Kids/Users/{id}/WatchTime/Reset`. **Independent test**: quickstart §2 `da`–`dg`.

- [X] T004 [US5] `UsageStore.ResetDayAsync(Guid userId, string day)` in `src/Jellyfin.Plugin.Kids/Storage/UsageStore.cs`: one write transaction reads the day's `used_ms` and `sum(minutes)` of bonus, deletes the `usage_day` row and the day's `bonus` rows; `reported_segment` untouched; returns `(long UsedMs, int BonusMinutes)`; update the class remarks ("usage only grows" except a parent reset)
- [X] T005 [US5] `DayReset` record (`Day`, `UsedMs`, `BonusMinutes`, `Status`) in `src/Jellyfin.Plugin.Kids/Allowance/DayReset.cs` and `UsageService.ResetDayAsync(Guid userId)` in `UsageService.cs`: not a kid → `null`; else under `_flushLock` drop the pending `(kid, today)` amount (adding it to `UsedMs`) and call the store; raise `UsageChanged`; return the reset with the new status
- [X] T006 [P] [US5] `Sitting.Reset()` in `src/Jellyfin.Plugin.Kids/Enforcement/Sitting.cs` (no playing time, no break) and `EnforcementState.ResetSitting(Guid kid)` in `EnforcementState.cs`
- [X] T007 [US5] `POST WatchTime/Reset` in `src/Jellyfin.Plugin.Kids/Api/AllowanceController.cs`: `IgnoreParentalControl`, `ProfileAccess.CanMutate` else 403, not a kid → 404, `ResetSitting`, `TryLoadAsync`, log "Screen time of {UserId} reset for {Day} by {Caller}: {UsedMinutes} min used and {BonusMinutes} min bonus cleared", answer `KidStatusDto` with the enforcement view
- [X] T008 [P] [US5] Add `"watchTimeReset"` to `KidsInfo.SupportedFeatures` in `src/Jellyfin.Plugin.Kids/Api/KidsInfo.cs`
- [X] T009 [P] [US5] Tests: `UsageStoreTests.cs` (reset clears the day's usage and bonus, keeps other days and segment ids, a re-sent segment adds nothing, reset of an empty day returns zeros), `UsageServiceTests.cs` (pending dropped and counted in `UsedMs`, a later flush does not write it back, not a kid → null, `UsageChanged` raised), `SittingTests.cs` / `EnforcementStateTests.cs` (a reset sitting is empty with no break, a denial for the sitting limit is lifted), the Info features list test
- [X] T010 [US5] dev-check section `d` in `scripts/dev-check.sh` per quickstart §2 (`da`–`dg`), including the log line in `$JELLYFIN_KIDS_DEV/log`, restoring alice's profile and enforcement afterwards

## Phase 4: User Story 1 - Health lines and Fix links (P1)

**Goal**: every protection gap is visible with a Fix link. **Independent test**: a default-policy kid shows the lines.

- [X] T011 [US1] Kids page in `src/Jellyfin.Plugin.Kids/Configuration/kids.js`: load `Kids/Admin/LiveTvChannels` once and each kid's profile with the status; render health lines per `contracts/dashboard-ux.md` (text nodes, `emby-linkbutton button-flat` Fix links with aria-labels); none when the policy is missing; keep the admin line text
- [X] T012 [US1] Kid editor health lines at the top in `kidsUser.html` / `kidsUser.js`, admin first, from `ApiClient.getUser` and the loaded channels

## Phase 5: User Story 2 - Enforcement banner and "App only" (P1)

- [X] T013 [US2] Banner markup at the top of `kids.html` (`role="status"`, warning border with fallback colour) and logic in `kids.js`: enforcement off, Enforcement or LiveTv `degraded` with missing routes; "Turn on" with `Dashboard.confirm`, re-read config, set only `EnforcementEnabled`, write, toast "Enforcement is on", reload data
- [X] T014 [US2] "App only" pill on kid rows from `Status.Enforced === false` in `kids.js`; editor status line "App only: …" in `kidsUser.js`

## Phase 6: User Story 3 - "No limit" vs "No screen time today" (P1)

- [X] T015 [US3] Editor: "No daily limit" and "No sitting limit" checkboxes in `kidsUser.html`; `kidsUser.js` disables the field while ticked, sends null, validates 0–1440 / 5–120 when unticked (empty blocks the save: "Between 0 and 1440 minutes." / "Between 5 and 120 minutes."), live 0 help "0 minutes: No screen time today."
- [X] T016 [US3] Wording everywhere: kid row summary and usage lines in `kids.js`, editor status in `kidsUser.js` via `allowanceText`

## Phase 7: User Story 4 - Live TV state (P2)

- [X] T017 [US4] "Watches N of M channels" / "No live TV" on kid rows (`kids.js`) and above the editor's channel list (`kidsUser.js`, updated as boxes are ticked)

## Phase 8: User Story 6 - Save model, sitting and break, server time, allowed hours, a11y (P2)

- [X] T018 [US6] Toasts for every success in `kids.js` (screen-time Save "Screen time settings saved", lock "Parent lock saved"/"Parent lock removed", Kid marked/unmarked) and `kidsUser.js` (Save "Saved", bonus "Added {n} min for today"); no modal for success
- [X] T019 [US6] Unsaved-changes snapshots (research R9) for the editor form and the Kids screen-time form, the "Unsaved changes" note next to Save (`role="status"`), `beforeunload` while dirty
- [X] T020 [US6] Re-read Status after an editor save (`kidsUser.js`)
- [X] T021 [US6] Sitting block in the editor: "Sitting limit (minutes)" label, help with "whichever limit comes first", "Break between sittings: {n} min, set for the household on the Kids page" (from the plugin configuration's `SittingBreakMinutes`, link to `#/configurationpage?name=Kids`); "sitting" wording on the Kids page break field
- [X] T022 [US6] Server time: line by "Day starts at" on the Kids page; editor "Server time" line and `DeniedUntil` "until HH:mm (server time)" via `serverClock`, with `DayStartsAtHour` from the configuration read of T021
- [X] T023 [US6] Allowed hours read-only block in the editor (`Policy.AccessSchedules`: Everyday/Weekday/Weekend/day names, `StartHour`/`EndHour` decimal hours → HH:mm), "Any time: no allowed hours are set in Jellyfin.", link "Edit in Jellyfin" → parentalcontrol
- [X] T024 [US6] Live regions and icons: `role="status"` on errors, lock message, conflict, usage lines, banner, unsaved notes; `aria-hidden` on every material icon, including `iconButton` icons in `kidsUser.js`
- [X] T025 [US5] "Reset today" button in the editor status block (`button-delete`) with `Dashboard.confirm` naming the kid, `POST …/WatchTime/Reset`, redraw status, toast "Today's screen time for {name} was reset"; error dialog on failure

## Phase 9: Polish and validation

- [X] T026 [P] Amendment notes: `specs/003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md` ("Usage only grows" now has the Reset exception) and `specs/003-kids-allowance-tracking/data-model.md` (`used_ms` row), pointing to this feature; README (feature list, Reset), docs/plan.md (§ API table: Reset; roadmap row for feature 7)
- [X] T027 `dotnet build` (0 warnings) and `dotnet test`
- [X] T028 Dev loop on a free port (`JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> dev`), `scripts/dev-check.sh` all PASS (the existing sections prove FR-027), then stop the loop
- [X] T029 Headless Chromium run (quickstart §3) with screenshots in `.dev/screenshots/007/`; record results in quickstart.md §4
- [X] T030 Review pass with the dotnet skills (C# changes) and a code review of the pages (every user string inserted as text, every configuration write re-reads first, no household data in page assets: FR-020); fix findings
- [X] T031 Send the validation summary with screenshot paths to `wholphin-kids`

## Dependencies

- T001 → none. T002–T003 before Phases 4–8.
- US5 server (T004–T009) before T010 and T025.
- US1–US4, US6 depend only on Phase 2; they touch the same two JS files, so they run in sequence.
- T027–T031 last.

## Parallel examples

- T002 and T003; T006, T008 and T009 (different files) once T004–T005 are done.

## Implementation strategy

MVP = health lines (US1) and the banner (US2) on top of Phase 2; then the wording (US3), Reset (US5), live TV state
(US4) and the save model (US6). Every phase leaves both pages working.
