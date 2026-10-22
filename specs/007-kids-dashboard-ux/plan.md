# Implementation Plan: Kids dashboard UX and "Reset today"

**Branch**: `011-kids-dashboard-ux` (spec directory `007-kids-dashboard-ux`) | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-kids-dashboard-ux/spec.md`

## Summary

The dashboard says the truth about each kid's protection, with data it already loads, and gains one server action:

- **Kids page** (`kids.html`, `kids.js`): a household banner (enforcement off, degraded self-checks) with a "Turn on"
  button; per kid an "App only" pill, the allowance wording ("No daily limit" / "No screen time today"), the live TV
  state ("Watches N of M channels" / "No live TV") and health lines with Fix links to Jellyfin's user editor tabs; the
  server time with its UTC offset by "Day starts at"; toasts for every success; an unsaved note on the screen-time form.
- **Kid editor** (`kidsUser.html`, `kidsUser.js`): the same health lines; "No daily limit" and "No sitting limit"
  checkboxes; the household break next to the sitting limit; allowed hours read-only; server-time labels; live TV
  state; "Reset today" with a confirmation; one save model (toast, inline errors), an unsaved note and an unload
  warning; Status read again after Save; live regions.
- **Server**: `POST /Kids/Users/{id}/WatchTime/Reset` (admin, API key or parent grant) clears today's usage (stored
  and pending, serialised with the flush) and bonus, resets the in-memory sitting, reloads the kid, logs the reset and
  answers the Status; `watchTimeReset` joins `/Kids/Info` features.
- **Markup**: final `emby-select` classes, label and arrow written by hand; no `emby-toggle`/`emby-collapse`.
- **Docs**: feature 3's contract and data model get a note that the add-only rule now has one parent exception; the
  constitution gets a PATCH clarification (1.0.2) of principle II.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); dashboard pages in plain ES modules (no build step).

**Primary Dependencies**: `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0 (compile only); `Microsoft.Data.Sqlite`
(shipped with the server). jellyfin-web 12.2 globals in the pages: `ApiClient`, `Dashboard.alert/confirm/
showLoadingMsg/hideLoadingMsg`.

**Storage**: no schema change: Reset deletes the day's `usage_day` row and the day's `bonus` rows (schema v3 tables).

**Testing**: xUnit for the store reset, the service reset (pending dropped, flush serialisation, other days and segment
ids kept), the sitting reset, the controller-level access rule (through `ProfileAccess`) and `KidsInfo`;
`scripts/dev-check.sh` gains Reset checks (section `da`–`dg`); a Playwright script outside the repo drives jellyfin-web
12.2 in headless Chromium (nixpkgs) for both pages, with screenshots at 1920 and 400 px in dark and light.

**Target Platform**: Jellyfin 12.0–12.2 plugin (`targetAbi 12.0.0.0`); the dev loop runs 12.2.

**Project Type**: server plugin (one project plus tests).

**Performance Goals**: the Kids page makes one extra request per kid (its profile, for the channel list) and one for
the server's channels; a household has a few kids.

**Constraints**: no change to existing endpoints (FR-027); escape every user string (text nodes only); re-read the
configuration before every write; no secrets or household data in page assets; no git operations.

**Scale/Scope**: a household: a few kids, tens of channels.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How the design complies |
|---|---|
| I. Leverage Jellyfin | Content restrictions and allowed hours stay Jellyfin's: the plugin only reads the user policy and links to Jellyfin's own user editor tabs (Profile, Access, Parental control). Nothing in the policy is written. |
| II. The server decides | Reset is a server action under the bonus rule (admin, API key or grant); a kid's token is refused. Client reports stay add-only, and a reset keeps reported segment ids so a re-sent report adds nothing. **Change recorded**: feature 3's "no API lowers usage" (FR-012a, clarification Q5) gets one exception, the explicit, logged parent Reset (user decision 2026-10-20); the constitution's wording is clarified (PATCH 1.0.2). |
| III. Parents never locked out | Reset and the new reads work outside allowed hours (`IgnoreParentalControl`). The banner's "Turn on" writes only `EnforcementEnabled` after re-reading the configuration and confirming. No admin-owned state is changed otherwise. |
| IV. Secrets stay secret | Page HTML and scripts stay static; all data comes from authenticated calls; `/Kids/Info` gains only a capability name. |
| V. Stable contract | Additive to API v1: one endpoint, one capability; no field or behaviour of existing endpoints changes. |
| VI. Tested where it matters | Unit tests for the reset arithmetic and races; dev-check for authorization, denial lifting and logging against the dev server; browser run for both pages. 0 warnings. |

No violations.

## Project Structure

### Documentation (this feature)

```text
specs/007-kids-dashboard-ux/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── kids-api-v1-reset.md      # POST WatchTime/Reset, capability
│   └── dashboard-ux.md           # Kids page and kid editor: banner, health, wording, save model, Reset
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── Storage/UsageStore.cs              # + ResetDayAsync (delete the day's usage and bonus rows, one transaction)
├── Allowance/UsageService.cs          # + ResetDayAsync (serialised with flushes, drops pending, raises UsageChanged)
├── Allowance/DayReset.cs              # new: what a reset cleared (for the log and tests)
├── Enforcement/Sitting.cs             # + Reset()
├── Enforcement/EnforcementState.cs    # + ResetSitting(kid)
├── Api/AllowanceController.cs         # + POST WatchTime/Reset
├── Api/KidsInfo.cs                    # + "watchTimeReset"
└── Configuration/kids.html, kids.js, kidsUser.html, kidsUser.js

tests/Jellyfin.Plugin.Kids.Tests/      # UsageStore, UsageService, Sitting, EnforcementState, KidsInfo
scripts/dev-check.sh                    # Reset checks
specs/003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md, data-model.md   # exception note
.specify/memory/constitution.md         # 1.0.2
README.md, build.yaml, Directory.Build.props, docs/plan.md                           # 0.0.7.0, release note
```

**Structure Decision**: no new folders; Reset sits with bonus in `AllowanceController` and `UsageService`.

## Phases

- **Phase 0**: research.md R1–R10, from jellyfin-web 12.2 and plugin source.
- **Phase 1**: data-model.md, contracts/, quickstart.md.
- **Phase 2 (tasks.md)**: Reset server side (store → service → sitting → controller → Info) with tests → dev-check →
  Kids page → kid editor → version and docs → dev loop, dev-check, browser run → review.

## Complexity Tracking

None.
