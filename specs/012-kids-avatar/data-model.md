# Data model: Kid avatars, hidden live TV channels and the Peke client

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

No schema change (`kids.db` stays at its current version), no new configuration field.

## Kid profile settings (changed)

| Field | Type | Rule | Default |
|---|---|---|---|
| `Avatar` | string or null | one of `leo`, `max`, `nico`, `lucy`, `sara`, `mia`, stored lowercase | null (use the Jellyfin profile image) |

Validation (`KidProfileValidator`, R2):

| Input | Result |
|---|---|
| omitted, null, `""`, whitespace | null |
| a known id in any case, with surrounding spaces | the lowercase id |
| anything else | `errors.Avatar = ["Unknown avatar '{value}'. Use one of: leo, max, nico, lucy, sara, mia, or null."]` |

Stored JSON written before this feature has no `Avatar` and reads as null. A save is a full replacement, so a `PUT`
without `Avatar` clears it.

## Kid summary (changed)

`KidSummary(UserId, Revision, Avatar)`: `/Kids/Profiles` entries; `Avatar` from the stored settings.

## Live TV channel (changed)

`LiveTvChannelInfo(Id, Name, Number, ImageTag, IsRadio, Rating)`: `Rating` = `CustomRating` ?? `OfficialRating`, null
when both are empty.

`LiveTvChannelDto` gains `Rating` (omitted when null) and `HiddenFromUser` (`bool?`, present only when the list was
asked for a user).

## Kids info (changed)

`KidsInfo(PluginVersion, ApiVersion = 1, ApiMinorVersion = 1, Features + "avatar", Enforcement, LiveTv)`.

## Hidden channel line (dashboard, not stored)

| Field | Values |
|---|---|
| `kind` | `liveTvHidden` |
| `channel` | the channel's name (or id when nameless) |
| `reason` | `unrated`, `rating`, `unrecognised`, `other` (research R5) |
| `fix` | `#/dashboard/users/{id}/parentalcontrol` |

Shown for a picked channel that the server lists with `HiddenFromUser: true`, while the kid's `EnableLiveTvAccess` is
not false. Health line order: admin, libraries, rating, unrated, live TV access, hidden channels (Jellyfin's channel
order), downloads.

## Avatar initial (dashboard, not stored)

| Id | Label | Initial | Colour |
|---|---|---|---|
| `leo` | Leo | L | `#1565c0` |
| `max` | Max | M | `#2e7d32` |
| `nico` | Nico | N | `#6a1b9a` |
| `lucy` | Lucy | L | `#c62828` |
| `sara` | Sara | S | `#ad1457` |
| `mia` | Mia | M | `#00695c` |

An id the page does not know: its first letter in upper case on `#546e7a`, labelled with the id.
