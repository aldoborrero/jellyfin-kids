# Implementation Plan: Live TV for kids

**Branch**: `010-kids-live-tv` (spec directory `006-kids-live-tv`) | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/006-kids-live-tv/spec.md`

## Summary

Jellyfin's live TV permission is all or nothing. This feature adds a per-kid channel list and makes the server hold it
on every client:

- **Profile**: `LiveTvChannelIds` in the kid profile (JSON settings, no migration), validated against real channels,
  full replacement like the other fields.
- **Gate, always on** (clarification Q1): a new action filter (`KidsLiveTvFilter`), attached by 005's convention to the
  playback actions and to Jellyfin's live TV browse and recording actions. For a kid: playback of a channel outside the
  effective set is refused (`liveTvChannel`); recordings and timers are refused (`liveTvRecording`); channel and guide
  lists are cut to the effective set (clarification Q2). Open live streams are attributed to channels in memory when
  the plugin lets a kid open them.
- **Counting**: unchanged code path (PlaybackTracker counts any kid playback); verified in the dev loop.
- **Client API**: `GET /Kids/Users/{id}/LiveTv/Channels` and `/LiveTv/Programs` in Jellyfin's `BaseItemDto` shape;
  `GET /Kids/Admin/LiveTvChannels` for the editor; `liveTv` in `/Kids/Info` plus a `LiveTv` gate report.
- **Dashboard**: a "Live TV channels" section in the kid editor only (kidsUser.html/js), with a prominent
  "No live TV channels allowed" state and warnings for live TV access off and unrated live TV blocked.
- **Dev loop**: an M3U tuner on generated TS clips plus an XMLTV guide; dev-check gains live TV checks; headless
  Chromium run against jellyfin-web 12.2.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**: `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0 (compile only): `ILiveTvManager`
(`GetInternalChannels`, `GetPrograms`), `ILibraryManager.GetItemById`, `IDtoService.GetBaseItemDtos`,
`IMediaSourceManager.GetLiveStreamInfo`, `LiveTvChannel`, `LiveTvProgram`, `PermissionKind.EnableLiveTvAccess`;
ASP.NET Core MVC `IAsyncActionFilter`.

**Storage**: none new: the field lives in the existing JSON profile settings (research R11). Live stream attribution is
in memory.

**Testing**: xUnit for validation, the filter's decisions (fake action contexts), the token and id parsing, the
registry, the list cutting and paging; `scripts/dev-check.sh` gains live TV checks; a throwaway Playwright script
(outside the repo) drives jellyfin-web 12.2 in headless Chromium.

**Target Platform**: Jellyfin 12.0–12.2 plugin (`targetAbi 12.0.0.0`); the dev loop runs 12.2.

**Project Type**: server plugin (one project plus tests).

**Performance Goals**: non-kid requests on guarded actions: one claims read (no lookups); kid requests: dictionary and
cached item lookups, < 1 ms (FR-012).

**Constraints**: fail open on plugin logic errors (FR-011); never 401 or `ParentalControl`; dashboard edits only in
kidsUser.html/js (009-enforcement-ui edits kids.html/js); no git operations beyond the working tree.

**Scale/Scope**: a household: a few kids, tens of channels, a 7-day guide.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How the design complies |
|---|---|
| I. Leverage Jellyfin | Channels and guide come from `ILiveTvManager` and `IDtoService` with the kid as user, so Jellyfin's ratings and unrated rules still apply; live TV access stays Jellyfin's switch (read, never written); 005's convention and refusal shapes are reused. The per-kid channel list is what Jellyfin lacks. |
| II. The server decides | Every client is gated at the playback, browse and recording actions; kid reads cannot widen the set. Documented limits: token-less stream URLs not attributable to a kid, generic `/Items` lookups of channel ids, images. |
| III. Parents never locked out | Admins and API keys are never filtered; only kids' live TV is affected; fail open on errors; removing the plugin removes every rule. Always-on is a deliberate, clarified exception to "off by default" (content, not time), announced in the release note, README and editor. No admin-owned state is modified. |
| IV. Secrets stay secret | `/Kids/Info` gains a capability and a route-coverage report only. Channel lists are signed-in reads (self or admin). |
| V. Stable contract | Additive to API v1: profile field, two kid endpoints, one admin endpoint, a capability, an Info object. Action names checked at startup and reported (`LiveTv.Gate`). Compiles against 12.0.0. |
| VI. Tested where it matters | Unit tests for the pure logic; dev-check and a browser run for the gate, counting and lists against a real tuner. 0 warnings. |

No violations.

## Project Structure

### Documentation (this feature)

```text
specs/006-kids-live-tv/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── kids-api-v1-live-tv.md       # profile field, kid endpoints, admin list, refusals, browse cutting, Info
│   └── dashboard-live-tv.md         # kid editor section
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── Profiles/
│   ├── KidProfileSettings.cs, KidProfileInput.cs   # + LiveTvChannelIds
│   ├── KidProfileValidator.cs                      # + channel validation
│   ├── ILiveTvChannelCatalog.cs, JellyfinLiveTvChannelCatalog.cs   # new: which ids are channels; admin list
│   └── KidProfileService.cs                        # passes the catalog
├── LiveTv/                                         # new
│   ├── LiveTvIds.cs                 # pure: open token and live stream id parsing, provider prefix
│   ├── LiveStreamRegistry.cs        # live stream id / unique id → channel
│   ├── ILiveTvItems.cs, JellyfinLiveTvItems.cs     # item → channel, memoised
│   ├── LiveTvRule.cs                # what an attached action needs: playback, list, single, recording
│   ├── LiveTvActions.cs             # expected browse/recording actions and the name rule
│   ├── KidsLiveTvFilter.cs          # IAsyncActionFilter
│   └── KidLiveTvService.cs          # kid endpoints' queries
├── Enforcement/
│   ├── KidRequest.cs                # new: shared kid identification (from KidsPlaybackFilter)
│   ├── PlaybackRefusal.cs           # new: shared refusal answers (from KidsPlaybackFilter)
│   ├── EnforcementState.cs          # + channel ids, effective set (ILiveTvGate)
│   ├── EnforcementService.cs        # + passes channel ids; logs live TV coverage
│   ├── PlaybackGateConvention.cs    # + attaches KidsLiveTvFilter
│   └── GateCoverage.cs              # + live TV coverage
├── Api/
│   ├── KidProfileDto.cs, KidProfileUpdate.cs       # + LiveTvChannelIds
│   ├── LiveTvController.cs          # new: kid endpoints + admin list
│   ├── LiveTvChannelDto.cs          # new: admin list row
│   └── KidsInfo.cs, EnforcementInfo.cs             # + liveTv feature, LiveTv gate report
├── Configuration/kidsUser.html, kidsUser.js        # Live TV section
└── PluginServiceRegistrator.cs

tests/Jellyfin.Plugin.Kids.Tests/   # validator, LiveTvIds, registry, filter, convention, state, Info
scripts/dev-provision.sh, dev-check.sh   # tuner + guide; live TV checks
README.md, build.yaml, Directory.Build.props, docs/plan.md   # 0.0.6.0, release note, plan update
```

**Structure Decision**: a new `LiveTv/` folder next to `Enforcement/`, reusing the convention, state and refusal code.

## Phases

- **Phase 0**: research.md R1–R12, all from source.
- **Phase 1**: data-model.md, contracts/, quickstart.md.
- **Phase 2 (tasks.md)**: profile field → shared kid/refusal helpers → filter (playback, recordings, browse) →
  endpoints and Info → dashboard → dev provisioning and checks → browser run → docs, version, review.

## Complexity Tracking

None.
