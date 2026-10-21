# Tasks: Live TV for kids

**Input**: Design documents from `specs/006-kids-live-tv/` (plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md)

**Tests**: required by constitution VI for the pure logic; server behaviour is verified by `scripts/dev-check.sh` and
the browser run.

## Phase 1: Setup

- [X] T001 Bump the version to 0.0.6.0 in Directory.Build.props and build.yaml (`version`), with a changelog that says the live TV rules are always on and that upgrading stops kids' live TV until a parent picks channels

## Phase 2: Foundational (blocks every story)

- [X] T002 Move the kid lookup out of `KidsPlaybackFilter.KidOf` into `src/Jellyfin.Plugin.Kids/Enforcement/KidRequest.cs` (`Resolve(HttpContext, IKidLookup)`: admins/API keys → none; `Jellyfin-UserId` claim → kid if in roster; token-less → `KidForDevice(deviceId)`), keeping the one-time missing-claim warning; `KidsPlaybackFilter` keeps its `IsEnabled` check before calling it
- [X] T003 Move the refusal answers into `src/Jellyfin.Plugin.Kids/Enforcement/PlaybackRefusal.cs` (`Create(HttpContext, GateKind, string reason, string title, DateTimeOffset? until)`: Negotiate → 200 `PlaybackInfoResponse{ErrorCode=NotAllowed}`; else 403 problem+json with `X-Application-Error-Code: PlaybackNotAllowed`; always `X-Kids-Reason`) and use it from `KidsPlaybackFilter`
- [X] T004 Add `LiveTvChannelIds` (`IReadOnlyList<Guid>`, default `[]`, "At most 200") to `src/Jellyfin.Plugin.Kids/Profiles/KidProfileSettings.cs` (`MaxLiveTvChannels = 200`), `KidProfileInput.cs`, `Api/KidProfileUpdate.cs` (doc: full replacement, omitting clears) and `Api/KidProfileDto.cs` (always present)

## Phase 3: User Story 1 - A parent chooses each kid's channels (P1)

**Goal**: the per-kid list is stored, validated and edited. **Independent test**: PUT/GET round-trip; editor ticks persist.

- [X] T005 [P] [US1] Create `src/Jellyfin.Plugin.Kids/Profiles/ILiveTvChannelCatalog.cs` (`Resolve(IEnumerable<Guid>)` → channels that exist; `List()` → every channel) and `LiveTvChannel` record (`Id, Name, Number, ImageTag, IsRadio`), with `JellyfinLiveTvChannelCatalog.cs` over `ILibraryManager.GetItemById` (is `LiveTvChannel`) and `ILiveTvManager.GetInternalChannels` (no user)
- [X] T006 [US1] Validate in `src/Jellyfin.Plugin.Kids/Profiles/KidProfileValidator.cs`: more than 200 → `errors["LiveTvChannelIds"] = ["At most 200 channels."]`; per index `LiveTvChannelIds[i]`: "Listed more than once." / "Not a live TV channel." (stored ids are kept); null → `[]`; pass the catalog through `KidProfileService.cs`
- [X] T007 [P] [US1] Tests in `tests/Jellyfin.Plugin.Kids.Tests/KidProfileValidatorTests.cs` (valid, unknown, duplicate, stored-but-gone kept, >200, null → empty) with a fake catalog in `Fakes/`, and a store round-trip plus an old-JSON-without-field read in `KidProfileStoreTests.cs`; update `KidProfileServiceTests.cs` construction
- [X] T008 [US1] Admin list `GET /Kids/Admin/LiveTvChannels[?ids=]` in `src/Jellyfin.Plugin.Kids/Api/LiveTvController.cs` with `Api/LiveTvChannelDto.cs` (`Id, Name, Number, ImageTag?, Type: TV|Radio`), administrators only
- [X] T009 [US1] Kid editor "Live TV channels" section in `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html` and `kidsUser.js` per `contracts/dashboard-live-tv.md`: checkbox list with logos, missing ids, prominent "No live TV channels allowed" notice, live-TV-access-off and unrated-blocked warnings from `GET /Users/{id}`, saved as `LiveTvChannelIds`, 400 errors shown in the section

## Phase 4: User Story 2 - Kids play only their channels (P1)

**Goal**: always-on playback gate. **Independent test**: alice plays PekeTV, is refused News on every route; admin plays both.

- [X] T010 [P] [US2] `src/Jellyfin.Plugin.Kids/LiveTv/LiveTvIds.cs` (pure): `ChannelFromOpenToken(string)` (finds `LiveTvChannel_`/`LiveTvProgram_` + 32 hex), `IsLiveTvStreamId(string)` (prefix `md5("Jellyfin.LiveTv.LiveTvMediaSourceProvider")` + `_`), with tests in `tests/.../LiveTvIdsTests.cs`
- [X] T011 [P] [US2] `src/Jellyfin.Plugin.Kids/LiveTv/LiveStreamRegistry.cs` (live stream id and unique id → channel; `Record`, `TryGetChannel`, prune via a `Func<string,bool> isOpen`), with tests
- [X] T012 [P] [US2] `src/Jellyfin.Plugin.Kids/LiveTv/ILiveTvItems.cs` + `JellyfinLiveTvItems.cs`: `ChannelOf(Guid itemId)` (channel → itself, program → `ChannelId`, else null; bounded memo of 4096), `UniqueIdOf(liveStreamId)` and `IsOpen` via `IMediaSourceManager`
- [X] T013 [US2] Effective channels in `src/Jellyfin.Plugin.Kids/Enforcement/EnforcementState.cs`: keep the profile's channel ids in `SetKid`; `ILiveTvGate.AllowedChannels(kid)` → empty when not loaded (and ask a reload) or the cached user lacks `EnableLiveTvAccess`; `EnforcementService.LoadAsync` passes them; tests in `EnforcementStateTests.cs`
- [X] T014 [US2] `src/Jellyfin.Plugin.Kids/LiveTv/LiveTvRule.cs` + `LiveTvActions.cs` (expected browse actions, the `Recording`/`Timer` name rule, `GetLiveRecordingFile` → Recording) and extend `PlaybackGateConvention` to attach `ServiceFilterAttribute(KidsLiveTvFilter)` with the rule in `action.Properties` to every guarded playback action and the live TV actions; record live TV coverage in `GateCoverage`; tests in `GateConventionTests.cs`
- [X] T015 [US2] `src/Jellyfin.Plugin.Kids/LiveTv/KidsLiveTvFilter.cs` (`IAsyncActionFilter`, always on, fail open): Playback rule per data-model "Request judgement" (arguments `itemId`, `liveStreamId`, `openToken`, `streamId`, and the same-named properties of `Jellyfin.*`/`MediaBrowser.*` objects), refusal via `PlaybackRefusal` with reason `liveTvChannel` and title "This channel is not available.", and recording of live stream ids from `PlaybackInfoResponse` / `LiveStreamResponse` after an allowed Negotiate/Open; tests in `KidsLiveTvFilterTests.cs`
- [X] T016 [US2] Register `LiveStreamRegistry`, `ILiveTvItems`, `ILiveTvGate` (→ `EnforcementState`), `KidsLiveTvFilter`, `ILiveTvChannelCatalog` in `src/Jellyfin.Plugin.Kids/PluginServiceRegistrator.cs`
- [X] T017 [US2] Browse lists in `KidsLiveTvFilter` (clarification Q2, research R6): ChannelList, ProgramList (GET `channelIds`), ProgramListPosted (`body.ChannelIds` by reflection), RecommendedPrograms, Channel and Program (404), with tests

## Phase 5: User Story 3 - Live TV counts (P1)

- [X] T018 [US3] Verify in the dev loop that `PlaybackTracker` counts live TV (quickstart §2 counting, §3) and that the 005 stop closes the live stream; fix `src/Jellyfin.Plugin.Kids/Hosted/PlaybackTracker.cs` only if a gap shows; record the result in research.md R9

## Phase 6: User Story 4 - Kid channel and guide reads (P2)

- [X] T019 [US4] `src/Jellyfin.Plugin.Kids/LiveTv/KidLiveTvService.cs` (channels via `ILiveTvManager.GetInternalChannels` with the kid, cut to the effective set, DTOs via `IDtoService` with `AddCurrentProgram` and primary images; programs via `ILiveTvManager.GetPrograms`, window defaults now → +24 h, cut to 7 days) and the two `GET /Kids/Users/{userId}/LiveTv/Channels|Programs` actions in `Api/LiveTvController.cs` (self or admin, `IgnoreParentalControl`, 404 not a kid, 400 `endDate` ≤ `startDate`); window tests
- [X] T020 [US4] `liveTv` in `KidsInfo.SupportedFeatures` and a `LiveTv` gate report (`Api/EnforcementInfo.cs` reused for the live TV coverage) in `Api/KidsInfo.cs` / `KidsController.GetInfo`; `EnforcementService.SelfCheck` logs missing live TV actions; update `KidsInfoTests.cs`

## Phase 7: User Story 5 - No recordings (P2)

- [X] T021 [US5] Recording rule in `KidsLiveTvFilter`: 403 `liveTvRecording` "Recordings are not available." (already attached by T014); tests

## Phase 8: Polish & validation

- [X] T022 Dev loop live TV: `scripts/dev-provision.sh` generates the TS clips, M3U and XMLTV, adds the tuner (`/LiveTv/TunerHosts`, type `m3u`, `EnableStreamLooping`), the XMLTV provider (`/LiveTv/ListingProviders`, type `xmltv`), refreshes the guide and waits for both channels
- [X] T023 `scripts/dev-check.sh` section `l` per quickstart §2 (leaves alice with no channels and live TV access as found)
- [X] T024 Run `dotnet build` (0 warnings), `dotnet test`, the dev loop and dev-check on a free port; stop the dev loop after
- [X] T025 Headless Chromium against jellyfin-web 12.2 (quickstart §4): allowed channel plays, disallowed refused, lists show only PekeTV
- [X] T026 README.md (live TV section, upgrade warning), docs/plan.md (§3, §4.1 table, §7), contract cross-links
- [X] T027 Review pass: dotnet:csharp-expert, dotnet-aspnetcore:dotnet-webapi, dotnet-test:test-gap-analysis and test-anti-patterns; apply the findings
- [X] T028 Send the validation summary and client contract to the `wholphin-kids` session

## Dependencies

- T002–T004 before every story. US1 (T005–T009) before US2's validation-dependent checks; T010–T013 before T015;
  T014 before T015/T017/T021; T016 after the types exist. US3 needs US2 and the dev loop (T022). US4 needs T013.
- Polish after the stories.

## Parallel examples

- T005, T010, T011, T012 touch separate new files.
- T007 alongside T008.

## Implementation strategy

MVP = US1 + US2 (the list and the gate). Then counting verification, kid reads, recordings, dev loop and docs.

## Review notes (T027)

Review pass with dotnet:csharp-expert, dotnet-aspnetcore:dotnet-webapi, dotnet-test:test-gap-analysis and
test-anti-patterns. Applied:

- **Open token bypass (security)**: Jellyfin opens the second `_` part of a live TV open token as the channel, in any
  GUID format, whatever the first part says. `LiveTvIds.ItemFromOpenToken` now parses it that way, and a live TV token
  the plugin cannot attribute to a channel is refused (`liveTvChannel`), like an unknown live stream id. Unit tests and
  a dev-check (`cf … crafted open token`) cover it.
- An empty GUID in `LiveTvChannelIds` or `ids=` gave 500 (`GetItemById` throws); both catalogs now skip it.
- `GuideWindow` no longer overflows near `DateTime.MaxValue`.
- `KidsLiveTvFilter` runs the action exactly once on every path.
- The kid channels read passes the request's cancellation token.
- Tests added: posted guide with only forbidden channels or none, own program, recommended paging, a failure after the
  action, Local and overflow windows, the exact validator message.

Not applied: typed `ProducesResponseType` and ProblemDetails bodies on 403 (the existing controllers use untyped
attributes and `Forbid()`; kept consistent).

Second pass (2026-10-21), same skills. The filter, the controller and the services hold up; no production change.
`test-gap-analysis` found three filter outcomes no test pinned, each now covered by a test that kills its mutation
(verified by applying and reverting it): a body (`OpenLiveStreamDto`/`PlaybackInfoDto`-like) naming another channel by
`ItemId`, a body naming an unknown live TV `LiveStreamId`, and `streamId` outside the LiveTv controller (must not be
read as a live stream file). Not applied: an unbounded guide read (at most 200 channels × 7 days; households pick a
few channels), and `ValidationProblemDetails` without `status` in 400 bodies (kept as the other controllers do).

## Validation (2026-10-21, T018, T023–T025)

- `dotnet build --no-incremental`: 0 warnings, 0 errors. `dotnet test`: 474 passed.
- `scripts/dev-check.sh` on port 8396: every check passes (215 with `bi` skipped as usual, 51 of them live TV `ca`–`cn`).
- jellyfin-web 12.2, headless Chromium: the kid editor lists both channels, shows the "No live TV channels allowed"
  notice with none ticked, keeps PekeTV after save and reload, no script errors. As alice: Live TV channels and the
  guide show PekeTV only; PekeTV plays (currentTime 1.0 → 8.4 s); Grown-ups News shows "Playback of this media is not
  allowed." (plus web's generic second alert, the 005 quirk), PlaybackInfo 200 `liveTvChannel`, nothing plays.
- Counting and the stop on a real live stream: research R9.

## Rebase onto main 0.0.5.1 (009 enforcement dashboard controls)

Checked with a three-way merge of each file both sides changed (base 1553c08): `kids.html` and `kids.js` are only
changed on main; `kidsUser.html`, `kidsUser.js` and `README.md` merge cleanly (main's sitting/denied status lines and
our Live TV section touch different hunks). Conflicts, all trivial: the version in `Directory.Build.props` and
`build.yaml` (keep 0.0.6.0) and the `build.yaml` changelog (keep ours). Main's Kids page reads only
`Info.Enforcement` for its self-check, so the new `Info.LiveTv` does not disturb it; showing the live TV gate there
too is a follow-up.

Done (2026-10-21): the worktree now sits on main 43bd406 with the 006 changes on top, resolved as above. Re-validated
on the merged tree: 0 warnings, 474 tests, dev-check all 215 pass (51 live TV). In headless Chromium:
- The Kids page: the self-check reads "all guarded"; the switch and the break save and survive a reload; 0 shows the
  range message.
- The kid editor shows main's usage, sitting and refusal lines with the channel picker. One save keeps the allowance
  and changes the sitting and the channels, and a bonus re-renders the lines.
- Kid playback and lists pass as before.
Known, from main: Save does not refresh the sitting line until the page is opened again.
