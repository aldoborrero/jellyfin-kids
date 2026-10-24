# Contract: Kids client API 1.1 (feature 12 additions: avatars, channel ratings)

Extends the contracts of features [1](../../001-kids-plugin-foundation/contracts/kids-api-v1.md) to
[7](../../007-kids-dashboard-ux/contracts/kids-api-v1-reset.md). Everything here is **additive**: `ApiVersion` stays
1. The JSON rules (PascalCase by default, camelCase with `profile="CamelCase"`, null fields omitted, ProblemDetails
always camelCase), the "self or admin" and "admin or grant" rules still apply. Examples are PascalCase.

## GET /Kids/Info (changed, anonymous)

```json
{"PluginVersion": "0.0.8.0", "ApiVersion": 1, "ApiMinorVersion": 1,
 "Features": ["profiles", "parentLock", "allowance", "sessionLimit", "enforcement", "sitting", "liveTv",
              "watchTimeReset", "avatar"],
 "Enforcement": {"Gate": "ok", "Missing": []}, "LiveTv": {"Gate": "ok", "Missing": []}}
```

- `ApiMinorVersion` (new): raised on every additive change to the client API, from 1 with this feature. It resets to
  0 when `ApiVersion` is raised. A missing `ApiMinorVersion` (plugin 0.0.7.0 and older) means 0.
- Clients still check a capability in `Features` before relying on its fields: `avatar` before reading `Avatar`.

## Kid profile (changed)

`GET /Kids/Users/{userId}/Profile` (self or admin), the `PUT` answer and the `409` body gain `Avatar`:

```json
{"UserId": "…", "Revision": 6, "SearchAllowed": false, "AutoplayEnabled": true, "AutoplayCap": 3,
 "Sections": […], "PickedItemIds": [], "DailyAllowanceMinutes": 60, "SessionLimitMinutes": 30,
 "LiveTvChannelIds": ["7f3c…"], "Avatar": "lucy"}
```

- `Avatar`: one of `leo`, `max`, `nico`, `lucy`, `sara`, `mia`, always lowercase. **Omitted** when the kid has none:
  show the Jellyfin profile image (`Users/{id}/Images/Primary`) or the client's default.
- The ids name pictures the client ships; the server holds only the id.

## PUT /Kids/Users/{userId}/Profile (changed)

- **Auth**: unchanged (administrator, API key or parent grant holder); a kid's own token gets 403.
- **Body**: `Avatar` (string or null, optional). Matched case-insensitively after trimming and stored lowercase. Null,
  omitted, `""` or whitespace clear it. The update is a **full replacement**: send the current `Avatar` on every
  update, or it is cleared.
- **400**: `errors.Avatar`: `Unknown avatar '{value}'. Use one of: leo, max, nico, lucy, sara, mia, or null.` The
  revision is checked first (stale and invalid gives 409).

## GET /Kids/Profiles (changed)

```json
[{"UserId": "…", "Revision": 6, "Avatar": "lucy"}, {"UserId": "…", "Revision": 2}]
```

- Each entry gains `Avatar`, omitted when none, so a "Who's watching" screen shows every kid from one request.
- Auth unchanged (any signed-in user, also outside allowed hours).

## GET /Kids/Admin/LiveTvChannels (changed, administrators)

- Each channel gains `Rating`: the rating Jellyfin compares for parental control (the custom rating, else the official
  one), omitted when the channel has none.
- New optional query `userId`: with it, each channel also carries `HiddenFromUser`: `true` when Jellyfin's parental
  control (maximum rating, blocked unrated items, blocked or allowed tags) hides the channel from that user, as in the
  user's own channel list. Without `userId` the field is omitted.
- `userId` of a user that does not exist: 404. Combined with `ids`, only those channels are listed and flagged.

```json
[{"Id": "7f3c…", "Name": "PekeTV", "Number": "1", "ImageTag": "c0ff…", "Type": "TV", "HiddenFromUser": true},
 {"Id": "a91e…", "Name": "Grown-ups News", "Number": "2", "Type": "TV", "Rating": "TV-14", "HiddenFromUser": true}]
```

- Auth unchanged: administrators and API keys only (a parent grant is not enough).

## Compatibility

- Clients that ignore `Avatar` keep working, but their full-replacement `PUT` clears it (as for every later field;
  feature 1 rule).
- Older servers omit `ApiMinorVersion`, `avatar`, `Avatar`, `Rating` and `HiddenFromUser`.
