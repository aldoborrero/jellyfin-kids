---

description: "Task list for kids enforcement on every client"
---

# Tasks: Kids enforcement on every client

**Input**: Design documents from `specs/005-kids-enforcement/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R12), data-model.md, contracts/kids-api-v1-enforcement.md,
quickstart.md

**Tests**: included. Pure logic is unit tested first (constitution VI); the filter, the stop chain and the refusals are
checked against the dev server (`scripts/dev-check.sh`) and in headless Chromium (scripts outside the repo). The abort
mechanism is proven by a spike (Phase 2) before the stop chain depends on it.

**Organization**: by user story, numbered as in spec.md:

| Story | Title | Priority |
|---|---|---|
| US6 | Proof that cutting a direct stream works (spike) | P1, blocking |
| US1 | Time's up stops playback on any client | P1 |
| US2 | New playback is refused while time is up | P1 |
| US5 | Parents stay in control of enforcement | P1 |
| US3 | Bedtime stops running playback | P2 |
| US4 | The sitting limit holds on every client | P2 |

## Conventions for every task

- `TreatWarningsAsErrors` with StyleCop; every public member has XML docs; match the comment density of
  `Allowance/` and `Hosted/PlaybackTracker.cs`. No new packages.
- Nothing on the playback-event path or in the filter does I/O or throws: catch, log (rate limited), fail open.
- Never edit `src/Jellyfin.Plugin.Kids/Configuration/*.html` or `*.js`.
- Server checks: start the dev loop on a free port only while testing (`JELLYFIN_KIDS_DEV=$PWD/.dev
  JELLYFIN_KIDS_PORT=<p> dev -p <p+1> -t=false`), stop it after. Spike and browser scripts live in the job scratch
  directory, not the repo.
- No git writes (no add, commit, push).

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Bump the version to 0.0.5.0 in `build.yaml` (changelog: "Enforcement: optional server-side enforcement of
  the allowance, allowed hours and sitting limit for every client") and `Directory.Build.props`
- [X] T002 [P] Add `JELLYFIN_KIDS_PORT` (default 8096) to the dev loop: `scripts/dev-jellyfin.sh` writes
  `<InternalHttpPort>` into a new `network.xml` (and patches an existing one), `scripts/dev-provision.sh` and
  `scripts/dev-check.sh` default `JELLYFIN_URL` to `http://127.0.0.1:$JELLYFIN_KIDS_PORT`, and the readiness probe in
  `process-compose.yaml` uses `${JELLYFIN_KIDS_PORT:-8096}`
- [X] T003 Add the settings. `src/Jellyfin.Plugin.Kids/Configuration/PluginConfiguration.cs`: `EnforcementEnabled`
  (bool, default false) and `SittingBreakMinutes` (int, default 15, "1–240; other values read as 15");
  `src/Jellyfin.Plugin.Kids/Allowance/IKidsSettings.cs` and `PluginKidsSettings.cs`: `EnforcementEnabled`,
  `SittingBreak` (`TimeSpan`); `tests/Jellyfin.Plugin.Kids.Tests/Fakes/FakeKidsSettings.cs` settable; tests in
  `tests/Jellyfin.Plugin.Kids.Tests/PluginKidsSettingsTests.cs` (defaults, 0 and 241 read as 15, 1 and 240 kept)

---

## Phase 2: User Story 6 - Spike: cutting a direct stream (P1, BLOCKING)

**Goal**: prove (or disprove) that `HttpContext.Abort()` on an in-flight kid delivery plus refusing later requests
stops jellyfin-web and a socket-less range client, and measure how fast (research R1). The gate pieces built here are
production code; only the trigger endpoint is temporary.

**Independent Test**: research.md R1 holds the clients, media, timings and decision.

- [X] T004 [P] [US6] Write failing tests in `tests/Jellyfin.Plugin.Kids.Tests/GateConventionTests.cs`: on a hand-built
  `ApplicationModel`, the convention adds one `ServiceFilterAttribute(typeof(KidsPlaybackFilter))` to each R3 action by
  name, to an unnamed action carrying an attribute whose type is named `ProducesVideoFileAttribute`, and to one with a
  `ProducesFileAttribute` whose `ContentTypes` has `audio/*`; not to `Subtitle`, `Trickplay`, `VideoAttachments`,
  `Playstate`, or `ProducesFile("text/*")`; coverage records kinds (negotiate/open/deliver) and the missing names
- [X] T005 [US6] Implement `src/Jellyfin.Plugin.Kids/Enforcement/GateActions.cs` (the R3 table: expected
  `(Controller, Action, Kind)`), `GateCoverage.cs` (thread-safe matches, `Missing`, `State` pending/ok/degraded,
  `Kind? KindOf(ActionDescriptor)` via a marker in the action's properties) and `PlaybackGateConvention.cs`
- [X] T006 [P] [US6] Implement `src/Jellyfin.Plugin.Kids/Enforcement/DeliveryRegistry.cs`: `Track(kid, HttpContext)`
  returns an `IDisposable` that unregisters; `AbortAll(kid)` returns the count aborted; tests in
  `tests/Jellyfin.Plugin.Kids.Tests/DeliveryRegistryTests.cs` with `DefaultHttpContext` and a fake
  `IHttpRequestLifetimeFeature`
- [X] T007 [US6] Write failing tests in `tests/Jellyfin.Plugin.Kids.Tests/KidsPlaybackFilterTests.cs`, then implement
  `src/Jellyfin.Plugin.Kids/Enforcement/DenialReason.cs`, `Denial.cs`, `IPlaybackGate.cs` (what the filter asks:
  enabled, `IsKid`, `TryGetDenial(kid, now)`, `KidForDevice(deviceId)`) and `KidsPlaybackFilter.cs`
  (`IAsyncResourceFilter`, R4/R5): disabled → pass; `Administrator` role → pass; user from claim `Jellyfin-UserId`,
  else `deviceId` query; non-kid → pass; denied + negotiate → 200 `PlaybackInfoResponse { ErrorCode = NotAllowed }`
  and `X-Kids-Reason`; denied otherwise → 403 ProblemDetails `{title: "Playback is not allowed now.", reason, until}`
  with `X-Application-Error-Code: PlaybackNotAllowed` and `X-Kids-Reason`; allowed delivery → tracked in the registry
  around `next()`; an exception in the plugin's own logic → pass (logged at most once a minute), never swallowing
  exceptions from `next()`; a warning once if a signed-in non-admin principal has no `Jellyfin-UserId` claim
  (constitution V: the claim name is a Jellyfin internal)
- [X] T008 [US6] Register in `src/Jellyfin.Plugin.Kids/PluginServiceRegistrator.cs`: `GateCoverage` (one instance,
  shared with the convention), `DeliveryRegistry`, `KidsPlaybackFilter`, `services.Configure<MvcOptions>(o =>
  o.Conventions.Add(new PlaybackGateConvention(coverage)))`; a temporary `SpikeGate : IPlaybackGate` with an
  admin-only `POST /Kids/Spike/Deny/{userId}` and `DELETE` in `src/Jellyfin.Plugin.Kids/Api/SpikeController.cs` that
  deny the kid and call `AbortAll`, returning the aborted count
- [X] T009 [US6] Spike harness outside the repo (job scratch): build a 12-minute 1280×720 H.264/AAC MP4 with
  `+faststart` at ~4 Mb/s; run the dev loop on a free port; curl client = `PlaybackInfo` + `Sessions/Playing` + one
  long `Videos/{id}/stream?static=true&ApiKey=` download at ~520 kB/s + 1 MiB range requests every 10 s; trigger
  `Spike/Deny` at +60 s; record the reset time, bytes after, and each later status
- [X] T010 [US6] Playwright + headless Chromium against jellyfin-web 12.2 (served by the dev server): sign in as alice,
  direct-play the MP4, disable Stop handling in the page (patch `playbackManager.stop` to a no-op) for the "ignores
  Stop" run, deny at +60 s, sample `currentTime`, `readyState`, buffered end, URL hash and network errors every
  second for 180 s; screenshot; repeat with HLS (force a transcode via a low `MaxStreamingBitrate`)
- [X] T011 [US6] Write the results and decision into `specs/005-kids-enforcement/research.md` R1 (and note the
  ExoPlayer gap); if the abort does not stop a client, keep it in the chain only where it helps and document the limit
  in the contract
- [X] T012 [US6] Remove `SpikeController.cs` and `SpikeGate`; nothing temporary remains (`rg -i spike src`)

**Checkpoint**: the gate filter and registry exist and are proven; research R1 has numbers.

---

## Phase 3: Foundational (blocking for US1–US5)

- [X] T013 [P] Tests then `src/Jellyfin.Plugin.Kids/Enforcement/KidBudget.cs` (data-model "KidBudget"): load, add
  intervals split by `DayClock`, `TimeUp` when `LimitMs` set and `UsedMs ≥ LimitMs`, unlimited never, a new day restarts
  at 0 with the allowance and no bonus and reports `NeedsReload`; `tests/.../KidBudgetTests.cs`
- [X] T014 [P] Tests then `src/Jellyfin.Plugin.Kids/Enforcement/Sitting.cs` (data-model "Sitting"): restart when the gap
  ≥ break, concurrency adds, limit reached → denied until `LastPlayedAt + break`, continuing playback moves the break,
  lazy reset after the break, used/remaining/break remaining; `tests/.../SittingTests.cs`
- [X] T015 [P] Tests then `src/Jellyfin.Plugin.Kids/Enforcement/ScheduleClock.cs` (R8, warnings only): `IsAllowedAt`
  with Jellyfin's formula (server zone, `DynamicDayOfWeek` incl. `Everyday`/`Weekday`/`Weekend`, inclusive fractional
  `StartHour`/`EndHour`), `ClosesWithin(schedules, now, zone, horizon)`; empty schedules never close;
  `tests/.../ScheduleClockTests.cs` using a fixed zone
- [X] T016 Tests then `src/Jellyfin.Plugin.Kids/Enforcement/EnforcementState.cs` (implements `IPlaybackGate`, R6):
  per-kid budget, sitting and cached `User`; `OnPlayed(kid, from, to)`; `Evaluate(kid, now)` → `Denial?` with the order
  `OutsideSchedule`, `TimeUp`, `SessionLimit` and their `Until`; `DueWarnings(kid, now)` → `(kind, threshold,
  instance)` for 300/60 s before each limit; `Describe(kid, now)` for Status; `SetBudget`, `SetUser`, `Forget(kid)`;
  `TryGetDenial` returns nothing when disabled; `KidForDevice` from `ISessionManager.Sessions`;
  `tests/.../EnforcementStateTests.cs`
- [X] T017 Add change notifications: `UsageService.UsageChanged` (raised after a bonus or a report) in
  `src/Jellyfin.Plugin.Kids/Allowance/UsageService.cs`, and `KidProfileService.ProfileChanged` (save, delete) in
  `src/Jellyfin.Plugin.Kids/Profiles/KidProfileService.cs`; subscribers' exceptions never reach the callers
- [X] T018 Feed the state from `src/Jellyfin.Plugin.Kids/Hosted/PlaybackTracker.cs`: inside the existing lock, each
  counted interval also goes to `EnforcementState.OnPlayed`; after the lock, the kid event (session, kind, automated)
  goes to the enforcer hook (T021); the tracker's 30 s timer also calls the enforcement sweep

**Checkpoint**: `dotnet test` passes; nothing is refused yet beyond the spike.

---

## Phase 4: User Story 1 - Time's up stops playback on any client (P1) MVP

**Goal**: warnings, then message → Stop → kill → close live → abort → server-side end, once; escalation only on real
reports, ≤ every 10 s.

**Independent Test**: quickstart §2 check 7 and §4 step 1.

- [X] T019 [P] [US1] `src/Jellyfin.Plugin.Kids/Enforcement/EnforcementMessages.cs`: the texts in the contract table
  (headers, warning and denial texts per reason, the break minutes)
- [X] T020 [US1] Write failing tests in `tests/.../PlaybackEnforcerTests.cs` with fake `ISessionManager`,
  `ITranscodeManager`, `IMediaSourceManager` and `TimeProvider`: chain order and the 5 s wait; only sessions the kid
  owns with a `NowPlayingItem`; once per denial; real Start/Progress while denied escalates at most every 10 s;
  automated ticks never escalate; the sweep escalates sessions still playing; a new denial (different reason/until)
  runs the full chain again; warnings once per (session, kind, threshold, instance); one information log per denial
  and per stopped session
- [X] T021 [US1] Implement `src/Jellyfin.Plugin.Kids/Enforcement/PlaybackEnforcer.cs` (R9): `OnKidEvent(session, kind,
  automated, now)` evaluates and queues work with `Task.Run`; the chain uses `SendMessageCommand(null, …)`,
  `SendPlaystateCommand(null, …, Stop)`, `KillTranscodingJobs(deviceId, null, _ => true)`,
  `CloseLiveStream(LiveStreamId)`, `DeliveryRegistry.AbortAll`, `OnPlaybackStopped(PlaybackStopInfo{…
  LastPlaybackCheckInPositionTicks})`; per-session records dropped on `SessionEnded`
- [X] T022 [US1] Implement `src/Jellyfin.Plugin.Kids/Enforcement/EnforcementService.cs` (`IHostedService`): at start,
  load every kid's budget (`UsageService.GetStatusAsync` + profile) and `User`; reload a kid on `UsageChanged`,
  `ProfileChanged` and a new day; on reload, if the kid is now denied and playing, enforce; register everything in
  `PluginServiceRegistrator.cs` (replacing `SpikeGate` with `EnforcementState`)
- [X] T023 [US1] Add dev-check check 7 (live cutoff with curl: alice with 1 minute left, a held download is reset,
  later ranges 403, `/Sessions` shows no `NowPlayingItem`, the log has one line per denial) to `scripts/dev-check.sh`

**Checkpoint**: an out-of-time kid is stopped on any client.

---

## Phase 5: User Story 2 - New playback is refused while time is up (P1)

**Goal**: every guarded route refuses a denied kid with the contract's answers; nothing else changes.

**Independent Test**: quickstart §2 checks 3–6.

- [X] T024 [US2] Extend `KidsPlaybackFilterTests.cs` with the real `EnforcementState`: bonus lifts at once, new day
  lifts, unmarked kid passes, tokenless `deviceId` of a denied kid's session refused and of an adult's session passed
- [X] T025 [US2] Add dev-check checks 3–6 (all guarded routes, headers and bodies; unguarded routes unchanged; admin and
  API key pass; tokenless deviceId; bonus lifts within 2 s) to `scripts/dev-check.sh`

---

## Phase 6: User Story 5 - Parents stay in control (P1)

**Goal**: off by default, admin/API key exempt, switch lifts at once, self-check reported, fail open.

**Independent Test**: quickstart §2 checks 1, 2, 4 and 11.

- [X] T026 [US5] Self-check in `EnforcementService.cs` on `ApplicationStarted`: read
  `IActionDescriptorCollectionProvider.ActionDescriptors.Items`, set `GateCoverage` state, warn per missing action,
  log the guarded count once
- [X] T027 [US5] Contract: `src/Jellyfin.Plugin.Kids/Api/KidsInfo.cs` gains `Enforcement { Gate, Missing }` and the
  capabilities `enforcement` and `sitting`; `Api/KidsController.cs` fills it from `GateCoverage`;
  `Api/KidStatusDto.cs` gains `Enforced`, `DeniedReason`, `DeniedUntil`, `SittingLimitSeconds`, `SittingUsedSeconds`,
  `SittingRemainingSeconds`, `BreakRemainingSeconds`, `BreakSeconds` (null fields omitted); `Api/AllowanceController.cs`
  fills them from `EnforcementState.Describe`; tests in `tests/.../KidsInfoTests.cs` and a new
  `tests/.../KidStatusDtoTests.cs`
- [X] T028 [US5] Add dev-check checks 1, 2 and 11 (Info gate ok, off by default changes nothing, switch off lifts) to
  `scripts/dev-check.sh`, restoring `EnforcementEnabled=false` at the end

---

## Phase 7: User Story 3 - Bedtime stops running playback (P2)

**Goal**: the schedule denial and stop, warnings before the close, refusal of tokenless/ApiKey streams after hours.

**Independent Test**: quickstart §2 check 8 and §4 step 4.

- [X] T029 [US3] In `EnforcementService.cs`: subscribe to `IUserManager.OnUserUpdated` (replace a kid's cached
  `User`), refresh cached users every 60 s in the sweep, and re-evaluate kids whose sessions are playing so a closing
  window triggers the chain even without a counted interval; tests for the schedule path in
  `EnforcementStateTests.cs` with a `User` whose `AccessSchedules` exclude now, and the 5/1-minute bedtime warnings
  from `DueWarnings`
- [X] T030 [US3] Add dev-check check 8 (schedule ending in 90 s; stop at the boundary; `outsideSchedule` 403 on an
  `ApiKey` stream; restore the policy) to `scripts/dev-check.sh`

---

## Phase 8: User Story 4 - The sitting limit holds on every client (P2)

**Goal**: per-kid sitting across devices with the household break; reported in Status.

**Independent Test**: quickstart §2 check 9 (`DEV_CHECK_SLOW=1`).

- [X] T031 [US4] Load the profile's `SessionLimitMinutes` into the kid's state and apply `SittingBreak` from settings in
  `EnforcementState.cs`; tests: two devices one after the other share a sitting, a break resets it, Status values
- [X] T032 [US4] Add dev-check check 9 behind `DEV_CHECK_SLOW=1` to `scripts/dev-check.sh`

---

## Phase 9: Polish & cross-cutting

- [X] T033 [P] Update `docs/plan.md` §3.2–3.4, §7 row 4 and §8 risks with what was built and the spike's numbers;
  update `README.md` / `docs/install.md` with the enforcement switch and its limits (no dashboard control yet)
- [X] T034 [P] Contract cross-reference: note in `specs/004-kids-session-limit/contracts/` (a short "superseded by
  feature 5 when the `sitting` capability is present" line) and in feature 3's contract that `WithinSchedule` became
  `DeniedReason: outsideSchedule`
- [X] T035 Browser validation (quickstart §4) with Playwright outside the repo; record results in research.md R1
  (validation table)
- [X] T036 `dotnet build` (0 warnings) and `dotnet test`; full `scripts/dev-check.sh` on the dev server; time guarded and
  unguarded requests (kid and admin, enforcement on and off) for SC-006; stop the dev
  loop
- [X] T037 Send the validation summary to the `wholphin-kids` session; follow-ups: dashboard controls for
  `EnforcementEnabled` and `SittingBreakMinutes`, Wholphin reading `DeniedReason` and the sitting fields, rebase onto
  main 0.0.4.1

---

## Dependencies

- Phase 1 → Phase 2 (spike) → Phase 3 → US1 → US2, US5 (parallel) → US3, US4 (parallel) → Polish.
- T005 needs T004; T007 needs T005 and T006; T008 needs T007; T009–T011 need T008; T012 after T011.
- T016 needs T013–T015; T018 needs T016; T021 needs T019–T020 and T018; T022 needs T021 and T017.
- US2–US4 need T022 (the real state in the filter).

## Parallel examples

- Phase 2: T004 and T006 together (different files).
- Phase 3: T013, T014, T015 together.
- Polish: T033 and T034 together.

## Implementation strategy

MVP = Phases 1–4 (spike, state, US1 stop chain). Then US2 (refusals are already wired by the filter; checks and
attribution), US5 (contract and self-check), then US3 and US4.
