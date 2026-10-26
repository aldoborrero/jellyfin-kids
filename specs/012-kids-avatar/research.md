# Research: Kid avatars, hidden live TV channels and the Peke client

Citations are against `.reference/jellyfin` and `.reference/jellyfin-web` at tag `v12.2`, and the plugin source at
0.0.7.0.

## R1. Versioning the API for an additive change

- **Decision**: `GET /Kids/Info` gains `ApiMinorVersion` (int), 1 from this feature; `ApiVersion` stays 1; `Features`
  gains `avatar`.
- **Findings**: `KidsInfo` has a single `int ApiVersion`, "raised on breaking changes" (constitution V, feature 1
  contract "Compatibility rules"); features 2–7 added fields without touching it and signalled them through
  `Features`. A minor number lets a client log or require "1.1" without a list lookup, and does not conflict with
  either rule.
- **Rationale**: the coordinator asked for a minor bump; there was no minor number, so the first one is 1 (the
  releases before it are 1.0 by definition).
- **Alternatives**: a string `"1.1"` (breaks clients that parse `ApiVersion` as a number); raising `ApiVersion` to 2
  (not a breaking change, so the constitution does not allow it); capability only (does not answer the request).

## R2. Where the avatar lives and how it is validated

- **Decision**: `KidProfileSettings.Avatar` (`string?`), serialised in `kid_profile.settings`; `KidAvatars.Ids` =
  `leo, max, nico, lucy, sara, mia`; input trimmed and matched case-insensitively, stored lowercase; blank = null;
  anything else → `errors.Avatar = ["Unknown avatar 'x'. Use one of: leo, max, nico, lucy, sara, mia, or null."]`.
- **Findings**: `KidProfileStore` deserialises with `PropertyNameCaseInsensitive` and ignores unknown properties, and
  missing ones keep their defaults (comment at `_jsonOptions`), so rows written before this feature read as no avatar
  and no migration is needed.
- **Unknown stored ids**: a stored value is answered as stored. On save it is validated like any input, so a stored id
  the server no longer knows must be replaced or cleared (edge case in the spec). The dashboard keeps the stored value
  as an extra option, so it round-trips on servers that know it.

## R3. `/Kids/Profiles` with avatars

- **Decision**: `KidProfileStore.ListAsync` selects `settings` too and deserialises it; `KidSummary` and
  `KidSummaryDto` gain `Avatar`.
- **Rationale**: a household has a few kids; deserialising a small JSON each is negligible and avoids depending on
  SQLite's JSON functions. `OrphanProfileSweeper` and `LoadRosterAsync` only read `UserId`.
- **Alternatives**: `json_extract(settings, '$.Avatar')` (ties the query to the serialised property name and to JSON1
  being compiled in); a separate column (a migration for one string).

## R4. Which channels Jellyfin hides from a kid

- **Decision**: `ILiveTvChannelCatalog.VisibleTo(userId)` = the ids `ILiveTvManager.GetInternalChannels(new
  LiveTvChannelQuery { UserId = userId }, new DtoOptions(false), …)` returns. `GET /Kids/Admin/LiveTvChannels?userId=`
  marks each listed channel with `HiddenFromUser = !visible.Contains(id)`.
- **Findings**: `LiveTvManager.GetInternalChannels` builds `new InternalItemsQuery(user)` (`src/Jellyfin.LiveTv/
  LiveTvManager.cs:113-153`), whose constructor copies the user's `MaxParentalRatingScore`/`SubScore`,
  `BlockUnratedItems`, blocked and allowed tags (`MediaBrowser.Controller/Entities/InternalItemsQuery.cs:520-545`).
  `KidLiveTvService.Channels` uses the same call for the kid's own list, so "hidden" here is exactly what the kid's
  effective channels leave out for content reasons. It does not depend on the plugin's allowed list, so a channel
  ticked and saved a moment ago is judged without waiting for the enforcement reload.
- **Unknown user**: `?userId=` of a user that does not exist → 404 ("No such user."); a non-GUID is a 400 from model
  binding.

## R5. Naming the reason

- **Decision**: `LiveTvChannelInfo.Rating` = `CustomRating` when set, else `OfficialRating`, else null (the same order
  as `BaseItem.IsParentalAllowed`, `BaseItem.cs:1865-1910`). The dashboard words a hidden picked channel as:

  | Condition (policy from `UserDto.Policy`) | Reason |
  |---|---|
  | no `Rating` and `BlockUnratedItems` has `LiveTvChannel` | unrated live TV channels are blocked |
  | `Rating` and `MaxParentalRating` set | rated {Rating}, above the maximum allowed rating |
  | `Rating`, no maximum, `BlockUnratedItems` has `LiveTvChannel` | Jellyfin does not recognise its rating {Rating}, and unrated channels are blocked |
  | otherwise | hidden by Jellyfin's parental control (for example a blocked or missing tag) |

- **Rationale**: `IsParentalAllowed` returns `!blockUnrated` when the rating is empty or unknown to the localisation
  tables, and compares scores only when both exist; tags are checked first. The second row can also be an
  unrecognised rating with a maximum set; the fix (Parental control tab) is the same.
- **Labels**: jellyfin-web 12.2's Parental control tab shows "Block items with no or unrecognized rating information"
  (`HeaderBlockItemsWithNoRating`) with a "Live TV" box for `UnratedItem.LiveTvChannel`
  (`features/users/components/ParentalControl.tsx:99-101`) and "Maximum allowed parental rating"
  (`LabelMaxParentalRating`).

## R6. Avatar markup without Peke's artwork

- **Decision**: the editor's select uses the feature 7 pattern (hand-written `selectLabel`, `emby-select` classes and
  arrow, research R3 of feature 7). The initial is a `span.kidsAvatarInitial` circle with a background from the
  plugin's own six colours (Material-like tones chosen for white-text contrast ≥ 4.5:1) and the name as `title` and
  `aria-label`. On the Kids page it replaces the `listItemImage`/person icon for kids with an avatar.
- **Licence**: Peke for Jellyfin is GPLv2; the plugin is MIT and may not copy GPL code or artwork (constitution,
  Technical Constraints). The ids are household names, not artwork.

## R7. Naming the client

- **Decision**: `build.yaml` description: "Server side of kids mode for Jellyfin clients such as Peke for Jellyfin: …,
  through a client-agnostic API." The README note, the enforcement and dashboard rows and the Clients section name
  Peke for Jellyfin; the Kids page banner says "Limits only apply in the kids app" and the "App only" texts follow.
- **Kept**: "Wholphin" where it names the origin of a format (`Verifier`, `ParentCode`, `ParentLockType`), in
  research documents and earlier specs, and in constitution principle V (needs an amendment; out of scope).
