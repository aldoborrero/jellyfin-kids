# Implementation Plan: Kids enforcement on every client

**Branch**: `008-kids-enforcement` (spec directory `005-kids-enforcement`) | **Date**: 2026-10-19 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-kids-enforcement/spec.md`

## Summary

The server already counts kids' playing time from Jellyfin's playback events (feature 3). This feature makes it act on
the count, for every client:

- **Decide in memory, on the tick.** `PlaybackTracker` already turns Jellyfin's 1 s progress events into played
  intervals. They are also fed to an in-memory `EnforcementState` (allowance budget per kid and day, the kid's sitting
  across devices, a cached copy of the kid's Jellyfin user for the schedule). Each event re-evaluates the kid, with no
  I/O and no timers per kid (review §D.3, §F.3).
- **Refuse.** An `IApplicationModelConvention` attaches one `IAsyncResourceFilter` to the ~22 playback actions only
  (review §F.2). A denied kid gets `PlaybackInfo` 200 with `ErrorCode = NotAllowed`, and 403 with
  `X-Application-Error-Code: PlaybackNotAllowed` everywhere else. Admins and API keys pass. Other actions carry no
  filter at all.
- **Stop.** When a kid becomes denied, `PlaybackEnforcer` sends a modal message, waits ~5 s, sends Stop, kills the
  session's transcodes, closes its live stream, aborts the kid's in-flight deliveries (`HttpContext.Abort`), and ends
  the playback server-side (as Jellyfin's idle sweep does), so the 1 s tick stops and no repeat fires. Only a real
  playback report from a denied session escalates again, at most every 10 s (repro §3, `wholphin-kids` feedback).
- **Warn.** Notices with a timeout at 5 and 1 minutes before the allowance, the sitting or the allowed hours end.
- **Self-check.** At startup the convention's matches are compared with the expected action list; misses are logged
  and reported in `GET /Kids/Info` (`Enforcement.Gate`).
- **Switch.** `EnforcementEnabled` (plugin XML, off by default) and `SittingBreakMinutes` (default 15). No dashboard
  page change; the controls are a follow-up.
- **Spike first** (blocking): measure `HttpContext.Abort` + refusing the next range request against jellyfin-web 12.2
  in headless Chromium, a socket-less curl client, and an ExoPlayer stand-in (research R1).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0 (compile only; `Jellyfin.Data` comes
transitively and exposes `UserEntityExtensions.IsParentalScheduleAllowed`), ASP.NET Core MVC (framework reference
already present): `IApplicationModelConvention`, `IAsyncResourceFilter`, `ServiceFilterAttribute`,
`IActionDescriptorCollectionProvider`

**Storage**: none new. Enforcement state is in memory; the two settings are plugin XML (`PluginConfiguration`), like
`DayStartsAtHour`. No schema change.

**Testing**: xUnit (existing `tests/Jellyfin.Plugin.Kids.Tests`) for the pure logic (budget, sitting, schedule
prediction, decision, escalation rate limits, convention matching against fake application models, filter decisions
on a fake `HttpContext`). `scripts/dev-check.sh` gains enforcement checks against the dev server (curl), plus
throwaway Playwright scripts kept outside the repo for the browser spike and validation.

**Target Platform**: Jellyfin server 12.0–12.2 plugin (`targetAbi 12.0.0.0`); dev loop runs 12.2.

**Project Type**: server plugin (single project plus test project)

**Performance Goals**: unguarded requests: zero added work (no filter attached). Guarded requests: one claims read
and dictionary lookups, < 1 ms (SC-006). Decision within ~1 s of the limit (FR-010).

**Constraints**: no I/O on the playback-event path or in the filter (FR-009); never throw into the session manager or
the MVC pipeline from plugin logic (fail open, FR-017); log one information line per denial and per stopped session,
debug otherwise (FR-013a); no edits to `Configuration/*.html|*.js`.

**Scale/Scope**: a household: a handful of kids, tens of sessions; HLS segment traffic 600–1200 requests per hour per
playing kid (review §C.1).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How the design complies |
|---|---|
| I. Leverage Jellyfin | Uses Jellyfin's own mechanisms: playback events and the 1 s tick, `SendMessageCommand`, `SendPlaystateCommand`, `ITranscodeManager.KillTranscodingJobs`, `IMediaSourceManager.CloseLiveStream`, `ISessionManager.OnPlaybackStopped` (the idle sweep's own call), MVC conventions and filters, `PlaybackErrorCode.NotAllowed`. The allowed-hours decision calls Jellyfin's `IsParentalScheduleAllowed` on a cached `User`; it never copies or edits `AccessSchedules`. Only the 5/1-minute *warning* needs a prediction Jellyfin cannot make (its rule is private and uses `UtcNow`); that small predictor (`ScheduleClock`) is used for notices only, and is tested against the same cases. |
| II. The server decides | That is the feature. Documented limits (FR-008, edge cases, contract): tokenless stream URLs with a foreign or missing `deviceId`, DLNA, offline downloads, clients without a socket that ignore everything until their buffer drains (spike R1 measures it). |
| III. Parents never locked out | Off by default (FR-003); admins and API keys always pass (`IsInRole("Administrator")` covers both); only playback actions are guarded, so `/Kids`, login and the dashboard stay usable; fail open on any internal error; no admin-owned state is modified; turning the flag off lifts every refusal at the next request; disabling the plugin removes the convention at the next start. |
| IV. Secrets stay secret | `/Kids/Info` (anonymous) gains only plugin facts (`Enforcement.Gate`, missing route names), not whether the household turned enforcement on; that goes in the signed-in Status. No new secrets. |
| V. Stable contract | Additive to API v1: new optional Status fields, new Info object and capabilities `enforcement` and `sitting`. Action names checked at startup and reported (FR-018). Compiles against 12.0.0. |
| VI. Tested where it matters | Pure logic unit-tested; filter and stop path verified against the dev server by `dev-check.sh` and a browser run; the risky abort mechanism is proven by a spike before the chain depends on it (Phase 1 of tasks, blocking). `TreatWarningsAsErrors` stays on. |

No violations. Re-checked after Phase 1 design: unchanged (see Complexity Tracking: none).

## Project Structure

### Documentation (this feature)

```text
specs/005-kids-enforcement/
├── plan.md              # This file
├── research.md          # Decisions R1–R12; R1 is the spike, filled in by its task
├── data-model.md        # In-memory entities and settings
├── quickstart.md        # How to validate on the dev server
├── contracts/
│   └── kids-api-v1-enforcement.md   # Status, Info, refusals, messages
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── Configuration/PluginConfiguration.cs     # + EnforcementEnabled, SittingBreakMinutes
├── Allowance/
│   ├── IKidsSettings.cs, PluginKidsSettings.cs   # + EnforcementEnabled, SittingBreak
│   └── UsageService.cs                      # + UsageChanged event (bonus, report)
├── Profiles/KidProfileService.cs            # + ProfileChanged event (save, unmark)
├── Enforcement/                             # new
│   ├── DenialReason.cs, Denial.cs           # timeUp | outsideSchedule | sessionLimit, until
│   ├── KidBudget.cs                         # pure: today's limit and used time per kid
│   ├── Sitting.cs                           # pure: per-kid sitting across devices, break
│   ├── ScheduleClock.cs                     # pure: when the allowed window closes (warnings only)
│   ├── EnforcementState.cs                  # in-memory deny set + warnings; Evaluate(kid, now)
│   ├── PlaybackGate.cs                      # what the filter asks: kid? denied? kid for device?
│   ├── DeliveryRegistry.cs                  # in-flight kid deliveries, Abort()
│   ├── PlaybackEnforcer.cs                  # message, Stop, kill, close live, abort, end; escalation
│   ├── EnforcementMessages.cs               # texts
│   ├── EnforcementService.cs                # IHostedService: warm-up, user cache, sweep, self-check
│   ├── GateCoverage.cs, GateActions.cs      # expected actions, matches, report
│   ├── PlaybackGateConvention.cs            # IApplicationModelConvention
│   └── KidsPlaybackFilter.cs                # IAsyncResourceFilter
├── Hosted/PlaybackTracker.cs                # + feeds EnforcementState on each kid event
├── Api/KidsInfo.cs, KidStatusDto.cs, AllowanceController.cs, KidsController.cs   # contract additions
└── PluginServiceRegistrator.cs              # registrations, MvcOptions convention

tests/Jellyfin.Plugin.Kids.Tests/
├── KidBudgetTests.cs, SittingTests.cs, ScheduleClockTests.cs, EnforcementStateTests.cs,
├── PlaybackEnforcerTests.cs (rate limits, ordering, with fakes), GateConventionTests.cs, KidsPlaybackFilterTests.cs
└── KidsInfoTests.cs (updated)

scripts/dev-check.sh            # + enforcement checks (curl); JELLYFIN_KIDS_PORT support across dev scripts
scripts/dev-jellyfin.sh, dev-provision.sh, process-compose.yaml   # free-port support
Directory.Build.props, build.yaml   # 0.0.5.0
docs/plan.md                    # §3.2–3.4, §7 row 4 updated with what was built and measured
```

**Structure Decision**: one new `Enforcement/` folder in the existing plugin project, following the folder-per-feature
layout of `Allowance/` and `ParentLock/`; tests in the existing test project.

## Phases

- **Phase 0 (research.md)**: decisions R2–R12 below are resolved from source; R1 (the spike) is a blocking
  implementation task whose result is written back into research.md before the stop chain uses the abort.
- **Phase 1 (design)**: data-model.md, contracts/kids-api-v1-enforcement.md, quickstart.md.
- **Phase 2 (tasks.md)**: spike → settings and contract → state and tracker → filter and convention → enforcer →
  schedule and sitting → self-check → dev-check and browser validation → docs and version.

## Complexity Tracking

None.
