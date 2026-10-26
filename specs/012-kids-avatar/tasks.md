# Tasks: Kid avatars, hidden live TV channels and the Peke client

**Input**: Design documents from `specs/012-kids-avatar/` (plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md)

**Tests**: required by constitution VI for validation, storage and mapping (xUnit); server behaviour is checked by
`scripts/dev-check.sh` section `e` against the dev server, the pages in the browser on the dev loop.

## Phase 1: Setup

- [X] T001 Bump the version to 0.0.8.0 in `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`) and `build.yaml` (`version`; changelog: avatars in the profile and the list of kids, `ApiMinorVersion` 1 and the `avatar` capability, channel ratings and hidden flags for the dashboard, hidden channel health lines, Peke for Jellyfin named as the client)

## Phase 2: Foundational

- [X] T002 Add `KidAvatars` in `src/Jellyfin.Plugin.Kids/Profiles/KidAvatars.cs`: `Ids` = `leo, max, nico, lucy, sara, mia` (in that order) and `TryNormalize(string? value, out string? id)`: "omitted, null, `""`, whitespace" → true with null; "a known id in any case, with surrounding spaces" → true with "the lowercase id"; anything else → false

## Phase 3: User Story 1 - Avatar in the profile and the list of kids (P1)

**Goal**: FR-001–FR-005. **Independent test**: quickstart §2 `ea`–`ee`.

- [X] T003 [US1] `KidProfileSettings.Avatar` (`string?`, default null, doc "one of KidAvatars.Ids; null means the Jellyfin profile image") in `src/Jellyfin.Plugin.Kids/Profiles/KidProfileSettings.cs`; `KidProfileInput.Avatar` in `KidProfileInput.cs`
- [X] T004 [US1] Validate in `src/Jellyfin.Plugin.Kids/Profiles/KidProfileValidator.cs`: `errors["Avatar"] = ["Unknown avatar '{value}'. Use one of: leo, max, nico, lucy, sara, mia, or null."]`, else store the normalised id
- [X] T005 [P] [US1] `KidProfileUpdate.Avatar` (doc: full replacement, send it on every update) and `ToInput` in `src/Jellyfin.Plugin.Kids/Api/KidProfileUpdate.cs`; `KidProfileDto.Avatar` (last parameter) and `From` in `KidProfileDto.cs`
- [X] T006 [US1] `KidSummary(UserId, Revision, Avatar)` in `src/Jellyfin.Plugin.Kids/Storage/KidSummary.cs`; `KidProfileStore.ListAsync` selects `settings` and deserialises it (R3); `KidSummaryDto(UserId, Revision, Avatar)` in `src/Jellyfin.Plugin.Kids/Api/KidSummaryDto.cs`; map it in `KidsController.GetProfiles`
- [X] T007 [US1] `KidsInfo(PluginVersion, ApiVersion, ApiMinorVersion, Features, …)` with `"avatar"` appended to `SupportedFeatures` in `src/Jellyfin.Plugin.Kids/Api/KidsInfo.cs`; `ApiMinorVersion: 1` in `KidsController.GetInfo` as a named constant
- [X] T008 [P] [US1] Tests: `KidProfileValidatorTests.cs` (each id, mixed case and spaces, null/blank → null, unknown → error), `KidProfileStoreTests.cs` (round trip, list carries avatars, settings JSON without `Avatar` reads null), `KidsInfoTests.cs` (minor version 1, `avatar` last), DTO mapping in `KidProfileServiceTests.cs` or a new test where DTOs are tested
- [X] T009 [US1] dev-check section `e` in `scripts/dev-check.sh`: `ea`–`ee` per quickstart, restoring alice's profile afterwards

## Phase 4: User Story 3 - Hidden live TV channels (P1)

**Goal**: FR-009–FR-012. **Independent test**: quickstart §2 `ef`–`eh`, §3 step 3.

- [X] T010 [US3] `LiveTvChannelInfo.Rating` (`CustomRating` ?? `OfficialRating`, null when empty) in `src/Jellyfin.Plugin.Kids/Profiles/LiveTvChannelInfo.cs` and `JellyfinLiveTvChannelCatalog.ToInfo`; `ILiveTvChannelCatalog.VisibleTo(Guid userId)` → the ids of `GetInternalChannels(new LiveTvChannelQuery { UserId = userId }, new DtoOptions(false), …)` (R4)
- [X] T011 [US3] `LiveTvChannelDto(Id, Name, Number, ImageTag, Type, Rating, HiddenFromUser)` with `From(channel, hidden?)` in `src/Jellyfin.Plugin.Kids/Api/LiveTvChannelDto.cs`; `?userId=` on `GET Admin/LiveTvChannels` in `KidsLiveTvController.cs` (admin only, unknown user → 404 "No such user.") through `IKidsUserDirectory.UserExists`
- [X] T012 [P] [US3] Tests: `FakeLiveTvChannelCatalog` gains `VisibleTo`; DTO mapping (`Rating` omitted when null, `HiddenFromUser` only when asked)
- [X] T013 [US3] dev-check `ef`–`eh` in `scripts/dev-check.sh` (block unrated `LiveTvChannel` in alice's policy through `POST /Users/{id}/Policy`, then restore it)
- [X] T014 [US3] Kids page `src/Jellyfin.Plugin.Kids/Configuration/kids.js`: per kid with picked channels and live TV access on, `GET Kids/Admin/LiveTvChannels?userId=`; `hiddenChannelIssues(user, pickedIds, flagged)` per `contracts/dashboard-avatar.md` (reasons R5), appended to the health lines before downloads; text nodes only
- [X] T015 [US3] Kid editor `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.js`: load the channel list with `?userId=` (one request: the list and its flags), the same lines from the stored channels; the flags follow Jellyfin's policy, not the picks, so Save recomputes the lines without a new request

## Phase 5: User Story 2 - Dashboard avatar (P2)

**Goal**: FR-006–FR-008. **Independent test**: quickstart §3 steps 1–2.

- [X] T016 [US2] Editor markup in `src/Jellyfin.Plugin.Kids/Configuration/kidsUser.html`: Avatar select (feature 7 select pattern), preview span, help "Shown on kids apps that support avatars, such as Peke for Jellyfin.", error line `data-error-for="Avatar"`, `.kidsAvatarInitial` style
- [X] T017 [US2] Editor logic in `kidsUser.js`: avatar names and colours (data-model "Avatar initial"), keep an unknown stored id as an extra option, `Avatar` in `currentBody()` (null for ""), preview on change
- [X] T018 [US2] Kids page in `kids.html`/`kids.js`: replace the row picture with the coloured initial once the profile is read, `title`/`aria-label` "Avatar: {Name}"

## Phase 6: User Story 4 - Naming the client (P3)

- [X] T019 [P] [US4] `build.yaml` `description`: Peke for Jellyfin as the kids client, client-agnostic API
- [X] T020 [P] [US4] README: note, Enforcement and Dashboard rows, Clients section, API list (link `specs/012-kids-avatar/contracts/kids-api-v1-avatar.md`), the avatar in "Kid profiles", roadmap line
- [X] T021 [P] [US4] Banner and "App only" texts in `kids.html`, `kids.js`, `kidsUser.html` ("Limits only apply in the kids app"), and the file header comment of `kids.js`

## Phase 7: Polish

- [X] T022 `docs/plan.md` §7: the spec 012 line (2026-10-24, plugin 0.0.8.0)
- [X] T023 `dotnet build -c Release` with 0 warnings and `dotnet test` green
- [ ] T024 Dev loop on a free port, `scripts/dev-check.sh` green, browser check per quickstart §3 (if the dev loop can run here)
- [ ] T025 Review the diff (`git diff`), mark tasks done, set the spec status

## Dependencies

- T002 → T003–T004; T003 → T005–T007; US1 server (T003–T007) → T017 (editor sends `Avatar`), T018.
- T010 → T011 → T014–T015.
- US4 is independent. Polish last.

## Parallel examples

- US1: T005 and T008 after T004; US3: T012 while T014 is written; US4: T019, T020, T021 together.

## Implementation strategy

MVP: US1 (the TV can read avatars) and US3 (the household bug). Then US2, US4, release polish.
