# Implementation Plan: Kid avatars, hidden live TV channels and the Peke client

**Branch**: `012-kids-avatar` (spec directory `012-kids-avatar`) | **Date**: 2026-10-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/012-kids-avatar/spec.md`

## Summary

- **Avatar**: `KidProfileSettings.Avatar` (string, nullable) in the profile's settings JSON, validated against the six
  ids (`KidAvatars`), answered in `KidProfileDto` and in each `/Kids/Profiles` entry; `GET /Kids/Info` gains
  `ApiMinorVersion: 1` and the `avatar` capability. No schema change: the settings column is JSON and unknown or
  missing properties already keep their defaults.
- **Hidden channels**: `GET /Kids/Admin/LiveTvChannels` gains `Rating` per channel and, with `?userId=`, a
  `HiddenFromUser` flag computed by asking Jellyfin which channels it lists for that user (`GetInternalChannels` with
  the user, as the kid's own list does). The Kids page and the kid editor turn hidden picked channels into health
  lines with the reason and a Fix link to the Parental control tab.
- **Dashboard avatar**: the editor gets an "Avatar" select with a coloured-initial preview; the Kids page shows the
  initial in place of the Jellyfin picture for kids with an avatar.
- **Client naming**: `build.yaml` description and the README name Peke for Jellyfin; the banner and the "App only"
  texts no longer name an app.
- **Release**: 0.0.8.0 in `build.yaml` (with changelog) and `Directory.Build.props`; `docs/plan.md` gets the feature
  line. Nothing is published.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); dashboard pages in plain ES modules (no build step).

**Primary Dependencies**: `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0 (compile only): `ILiveTvManager.
GetInternalChannels`, `LiveTvChannelQuery.UserId`, `BaseItem.OfficialRating` / `CustomRating` (all used by feature 6 or
present in 12.0.0). jellyfin-web 12.2 globals: `ApiClient`, `Dashboard`.

**Storage**: no schema change: `kid_profile.settings` JSON gains an optional `Avatar` property.

**Testing**: xUnit for the validator (ids, case, blank, unknown), the store (round trip, list with avatars, settings
written before this feature), `KidProfileDto`/`KidSummaryDto` mapping, `KidsInfo` (minor version, capability), the
catalog fake's hidden flag and the DTO's `Rating`. `scripts/dev-check.sh` gains section `e` for the avatar API and
the hidden flag against the dev server. The dashboard logic is checked in the browser on the dev loop when it can run.

**Target Platform**: Jellyfin 12.0–12.2 plugin (`targetAbi 12.0.0.0`); the dev loop runs 12.2.

**Project Type**: server plugin (one project plus tests).

**Performance Goals**: the Kids page makes one extra request per kid with live TV access on and picked channels (the
channel list for that kid); `/Kids/Profiles` deserialises each kid's settings (a household has a few kids).

**Constraints**: additive API only; `TreatWarningsAsErrors`; every user string inserted as text; no household data in
page assets; no Peke artwork; no git writes, no household server.

**Scale/Scope**: a household: a few kids, tens of channels.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How the design complies |
|---|---|
| I. Leverage Jellyfin | Hidden channels are what Jellyfin's own query hides for the kid; the plugin does not evaluate ratings or tags itself. The rating and the policy only choose the wording of the fix, which is done in Jellyfin's Parental control tab. The avatar is kids-mode data Jellyfin does not have (its own profile image stays the default). |
| II. The server decides | Avatar changes follow the existing profile rule (admin, API key or parent grant); a kid's token is refused. |
| III. Parents never locked out | All reads keep `IgnoreParentalControl`; nothing changes admin-owned state. |
| IV. Secrets stay secret | The pages stay static; the avatar ids and the colour palette are generic, not household data. `HiddenFromUser` is admin-only. |
| V. Stable contract | Additive: `Avatar` (profile, update, list), `Rating` and `HiddenFromUser` (admin list), `ApiMinorVersion` and `avatar` in Info. `ApiVersion` stays 1. Full-replacement semantics are written in the contract. Principle V still names Wholphin as the reference client; amending it is left to `/speckit-constitution` (out of scope, recorded in the spec). |
| VI. Tested where it matters | Unit tests for validation, storage and mapping; dev-check for authorization and the hidden flag; 0 warnings. |

Licence (Technical Constraints): Peke is GPLv2; nothing of it is copied. The six ids are names given by the household,
the initials and colours are the plugin's own.

No violations.

## Project Structure

### Documentation (this feature)

```text
specs/012-kids-avatar/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── kids-api-v1-avatar.md     # Avatar, ApiMinorVersion, Rating, HiddenFromUser
│   └── dashboard-avatar.md       # avatar select, row initial, hidden channel lines, wording
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── Profiles/KidAvatars.cs              # new: the six ids, Normalize
├── Profiles/KidProfileSettings.cs      # + Avatar
├── Profiles/KidProfileInput.cs         # + Avatar
├── Profiles/KidProfileValidator.cs     # + avatar rule
├── Profiles/LiveTvChannelInfo.cs       # + Rating
├── Profiles/ILiveTvChannelCatalog.cs   # + VisibleTo(userId)
├── Profiles/JellyfinLiveTvChannelCatalog.cs
├── Storage/KidSummary.cs               # + Avatar
├── Storage/KidProfileStore.cs          # ListAsync reads the settings
├── Api/KidProfileDto.cs, KidProfileUpdate.cs, KidSummaryDto.cs, LiveTvChannelDto.cs, KidsInfo.cs
├── Api/KidsController.cs               # Info minor version, Profiles avatar
├── Api/KidsLiveTvController.cs         # ?userId= on Admin/LiveTvChannels
└── Configuration/kids.html, kids.js, kidsUser.html, kidsUser.js

tests/Jellyfin.Plugin.Kids.Tests/       # validator, store, DTOs, KidsInfo, fake catalog
scripts/dev-check.sh                    # section e
README.md, build.yaml, Directory.Build.props, docs/plan.md
```

**Structure Decision**: no new folders; the avatar ids live next to the other profile rules.

## Phases

- **Phase 0**: [research.md](research.md) R1–R7.
- **Phase 1**: [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md).
- **Phase 2 ([tasks.md](tasks.md))**: avatar server side with tests → hidden flag server side with tests → dashboard →
  naming and release → build, tests, dev-check → review.

## Complexity Tracking

None.
