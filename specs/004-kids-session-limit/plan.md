# Implementation Plan: Kids session limit

**Branch**: `004-kids-session-limit` (stacked on feature 3, `6ae5f93`) | **Date**: 2026-10-19 | **Spec**:
[spec.md](spec.md)

**Input**: Feature specification from `specs/004-kids-session-limit/spec.md` (decisions given with the request;
`/speckit-clarify` skipped)

## Summary

Each kid profile gains an optional `SessionLimitMinutes` (5–120, null for none): the longest a single sitting may last
on a TV. The server stores, validates and returns it; the TV counts the sitting itself, per device (plan §1,
"Per-session timer: device by default").

- **Profile**: `KidProfileSettings`, `KidProfileInput`, `KidProfileUpdate` and `KidProfileDto` gain the field. The
  validator checks 5–120 and keys the error `SessionLimitMinutes`, exactly like `DailyAllowanceMinutes`.
- **Storage**: the field lives in the profile settings JSON. `kids.db` schema v4 is a data migration that writes
  `"SessionLimitMinutes": null` into every existing row (research R1).
- **Capability**: `GET /Kids/Info` lists `"sessionLimit"` (R2).
- **Dashboard**: the kid editor gets a "Session limit (minutes)" number field next to the daily allowance (R3).
- **Housekeeping shipped with it**: constitution principle V amended (1.0.1), a note in the feature 3 allowance
  contract about changing "day starts at" mid-day, and version 0.0.4.0.
- **Out of scope**: any server-side counting, warning or stopping of sittings.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); dashboard pages in plain ES modules and HTML

**Primary Dependencies**: unchanged (`Jellyfin.Controller` and `Jellyfin.Model` 12.0.0 with runtime excluded,
`Microsoft.Data.Sqlite.Core`). No new packages.

**Storage**: `kids.db` schema v4: `UPDATE kid_profile SET settings = json_insert(settings, '$.SessionLimitMinutes',
NULL)`. SQLite's JSON functions are built in since 3.38 (both the server's and the tests' bundled SQLite are newer).

**Testing**: xUnit v3 (validator bounds, store round trip, migration 3 → 4, Info features); `scripts/dev-check.sh`
gets checks ar–au.

**Target Platform**: Jellyfin server 12.0–12.2, checked at runtime on 12.2.

**Project Type**: server plugin plus embedded dashboard pages.

**Performance Goals**: none new; one more integer in the profile JSON.

**Constraints**: additive contract change only (`ApiVersion` stays 1); `TreatWarningsAsErrors`; no household data in
anonymous answers or page HTML.

**Scale/Scope**: 1 setting, 1 migration step, 1 capability, 1 editor field; about 15 tasks.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (below).*

| Principle | Check | Status |
|---|---|---|
| I. Leverage Jellyfin | Nothing Jellyfin does is duplicated: it has no per-sitting limit. The setting reuses feature 1's profile store, validation, revisions and grants. | PASS |
| II. The Server Decides | **Documented exception**: the sitting limit is counted and enforced on the TV, per device, not on the server. It is a per-sitting UX feature (plan §1); the daily allowance is the cross-device rule and stays server-side. The contract states the limit, so no client reads it as a server guarantee. A kid token still cannot change it. | PASS (exception justified, see Complexity Tracking) |
| III. Parents Are Never Locked Out | The plugin blocks nothing new. Profile reads keep `IgnoreParentalControl`. | PASS |
| IV. Secrets Stay Secret | Not a secret; returned only to self, admins and grant holders, as the rest of the profile. Info adds only a capability name. The page HTML stays static. | PASS |
| V. Stable, client-agnostic contract | Additive: a new optional field and a new capability, so `ApiVersion` stays 1. The capability is what tells clients the field is supported, since nulls are omitted (R2). The full-replacement semantics are written in the contract. Principle V's wording is amended in this feature (PATCH 1.0.1): clients keep working without kids features when the plugin is absent. | PASS |
| VI. Tested where it matters | Validation, storage and migration are unit tested; the API round trip, validation, null omission and capability are checked against the dev server. | PASS |
| Technical constraints | State stays in `kids.db`; the migration is one statement in the existing migration list. | PASS |
| Development workflow | spec-kit flow (clarify skipped, decisions recorded in spec.md); docs/plan.md §1, §4.1 and §7 updated. | PASS |

**Post-design re-check (after Phase 1)**: the design adds no endpoint, no table and no new mechanism. All gates pass.

## Project Structure

### Documentation (this feature)

```text
specs/004-kids-session-limit/
├── spec.md
├── plan.md                              # this file
├── research.md                          # Phase 0 (R1–R4)
├── data-model.md                        # Phase 1
├── quickstart.md                        # Phase 1
├── contracts/
│   ├── kids-api-v1-session-limit.md     # additions to the client API
│   └── dashboard-session-limit.md       # editor change
├── checklists/requirements.md
└── tasks.md                             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── Profiles/
│   ├── KidProfileSettings.cs            # + SessionLimitMinutes, Min/MaxSessionLimit (5, 120)
│   ├── KidProfileInput.cs               # + SessionLimitMinutes
│   └── KidProfileValidator.cs           # + 5–120 rule
├── Api/
│   ├── KidProfileDto.cs / KidProfileUpdate.cs   # + SessionLimitMinutes
│   ├── KidsInfo.cs                      # SupportedFeatures + "sessionLimit"
│   └── KidsController.cs                # Info uses KidsInfo.SupportedFeatures
├── Storage/KidsDatabase.cs              # SchemaVersion 4, migration step 4
└── Configuration/kidsUser.html / kidsUser.js    # + Session limit field

tests/Jellyfin.Plugin.Kids.Tests/
├── KidProfileValidatorTests.cs          # + bounds
├── KidProfileStoreTests.cs              # + round trip, old rows null
├── KidsDatabaseTests.cs                 # + 3 → 4
├── KidProfileServiceTests.cs            # + omitted clears
├── KidsInfoTests.cs                     # new: Info lists sessionLimit
└── ProfileComparer.cs                   # + field

scripts/dev-check.sh                     # + checks ar–au
build.yaml, Directory.Build.props        # 0.0.4.0
.specify/memory/constitution.md          # principle V, 1.0.1
specs/003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md   # "day starts at" note
docs/plan.md                             # §1, §4.1, §7
```

**Structure decision**: no new plugin classes; the feature follows `DailyAllowanceMinutes` line by line. The Info
features list moves to a static `KidsInfo.SupportedFeatures`, so that a test can check it without a server (the
controller reads `Plugin.Instance`, which tests cannot load).

## docs/plan.md updates

- §1 non-goals and the device table: Wholphin has no local kids mode any more; the per-session timer row names the
  server-stored `SessionLimitMinutes`.
- §4.1: `sessionDefaultMinutes?` becomes `sessionLimitMinutes?`; Info features list `sessionLimit`.
- §7: a note that spec 004 is the session limit and enforcement takes spec 005.

## Risks

- **Rollback**: schema v4 makes a 0.0.3 plugin refuse the database (feature 1's newer-schema rule), so a downgrade
  makes kids data unavailable until 0.0.4 is reinstalled. Accepted: the migration was requested, and the rule fails
  closed without touching the file.
- **Old clients clear the field**: a client built before this feature PUTs without it and clears it (full
  replacement), the same accepted behaviour as the allowance. The capability lets new clients know to send it.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Principle II: the sitting limit is enforced on the device, not the server | It is per sitting and per TV by design (plan §1); a kid switching TVs starting a new sitting is accepted | Server-side sitting tracking would need a definition of "sitting" across devices and its own enforcement loop; plan §7 feature 6 keeps it as a later option |
