# Contract: Kids client API, version 1 (feature 6 additions: live TV)

This extends the contracts of features [1](../../001-kids-plugin-foundation/contracts/kids-api-v1.md) to
[5](../../005-kids-enforcement/contracts/kids-api-v1-enforcement.md). Everything here is **additive**: `ApiVersion`
stays 1. The JSON rules (PascalCase by default, camelCase with `profile="CamelCase"`, null fields omitted,
ProblemDetails always camelCase), the "self or admin" and "admin or grant" rules all still apply. Examples are
PascalCase.

**Upgrade note.** The live TV rules are **always on**, whatever `EnforcementEnabled` says. A kid with no channel list
(every kid right after upgrading) has no live TV on any client until a parent picks channels.

## GET /Kids/Info (changed, anonymous)

`Features` gains `"liveTv"`. A new object reports whether the plugin found every live TV route it must guard (plugin
and Jellyfin version facts only):

```json
"LiveTv": { "Gate": "ok", "Missing": [] }
```

`Gate`: `ok`, `degraded` (`Missing` lists `Controller.Action`) or `pending`. Clients check `liveTv` before reading
`LiveTvChannelIds` or calling the endpoints below.

## Kid profile (changed)

`KidProfile` and `KidProfileUpdate` gain `LiveTvChannelIds`: the live TV channels the kid may watch, in order.

```json
{"UserId": "…", "Revision": 5, "SearchAllowed": false, "AutoplayEnabled": true, "AutoplayCap": 3,
 "Sections": […], "PickedItemIds": [], "DailyAllowanceMinutes": 60, "SessionLimitMinutes": 30,
 "LiveTvChannelIds": ["7f3c…", "a91e…"]}
```

- Always present in answers (`[]` for none). Null, omitted or `[]` in an update all mean **no live TV**: PUT is a full
  replacement, so send the list on every update.
- 400 `errors["LiveTvChannelIds[i]"]`: `Not a live TV channel.` or `Listed more than once.`; `errors.LiveTvChannelIds`:
  `At most 200 channels.` The revision is checked first (stale and invalid gives 409). Ids already stored stay valid
  even if the channel was removed from the server.
- A kid token still cannot change it (admin or parent grant only).

## GET /Kids/Users/{userId}/LiveTv/Channels (new)

- **Auth**: self or admin; works outside allowed hours. Anyone else: 403. Not a kid: 404.
- **200**: Jellyfin's `QueryResult<BaseItemDto>`, the same shape as `GET /LiveTv/Channels`, holding only the kid's
  **effective** channels: listed in the profile, the kid's Jellyfin live TV access on, still on the server and visible
  to the kid under Jellyfin's content rules. Jellyfin's channel order. Each item has at least `Id`, `Name`,
  `ChannelNumber`, `Type: "TvChannel"`, `ImageTags.Primary` (when the channel has a logo) and `CurrentProgram` (the
  program on now, a `BaseItemDto` with `Name`, `StartDate`, `EndDate`), when known.

```json
{"Items": [{"Id": "7f3c…", "Name": "PekeTV", "ChannelNumber": "1", "Type": "TvChannel",
            "ImageTags": {"Primary": "c0ff…"},
            "CurrentProgram": {"Id": "…", "Name": "Bluey", "StartDate": "2026-10-20T17:00:00.0000000Z",
                               "EndDate": "2026-10-20T17:30:00.0000000Z", "ChannelId": "7f3c…"}}],
 "TotalRecordCount": 1, "StartIndex": 0}
```

- Images: `GET /Items/{Id}/Images/Primary?tag={ImageTags.Primary}` (Jellyfin's image route).
- An empty `Items` means no live TV for this kid (no list, or live TV access off).

## GET /Kids/Users/{userId}/LiveTv/Programs (new)

- **Auth**: as above.
- **Query**: `startDate`, `endDate` (ISO 8601 UTC, optional): programs that end after `startDate` and start before
  `endDate`. Defaults: now and `startDate` + 24 h; a window longer than 7 days is cut to 7 days; `endDate` ≤
  `startDate` → 400 `errors.endDate`. `channelIds` (optional, comma-separated): narrows to those channels; ids that
  are not effective channels are ignored.
- **200**: `QueryResult<BaseItemDto>` of `Type: "Program"` items (`Id`, `Name`, `ChannelId`, `StartDate`, `EndDate`,
  `Overview` when known, `ImageTags`), ordered by `StartDate`. "Now and next" is the first two programs per channel
  from `startDate = now`.

## GET /Kids/Admin/LiveTvChannels (new)

- **Auth**: administrator (or API key); a parent grant is not enough, like `PickCandidates`. Otherwise 403.
- **200**: every live TV channel on the server, in Jellyfin's channel order:
  `[{"Id":"7f3c…","Name":"PekeTV","Number":"1","ImageTag":"c0ff…","Type":"TV"}]` (`Type`: `TV` or `Radio`; `ImageTag`
  absent when there is no logo). `?ids=` (comma-separated) resolves exactly those ids instead; unknown ids are left
  out.

## Refusals (always on, the caller is a kid)

Administrators, API keys and users who are not kids are never refused or filtered. The feature 5 time rules are
checked first; when they refuse, their reason is given.

| Route | Answer |
|---|---|
| `GET`/`POST Items/{id}/PlaybackInfo` for a channel (or a guide program) outside the kid's effective set, or naming an unknown live TV stream | **200** `PlaybackInfoResponse` with `"ErrorCode": "NotAllowed"`, no `MediaSources`; header `X-Kids-Reason: liveTvChannel` |
| `POST LiveStreams/Open`, HLS (`master.m3u8`, `main.m3u8`, `live.m3u8`, `hls1/…`, legacy `hls/…`), `Videos`/`Audio` streams, `UniversalAudio`, `LiveTv/LiveStreamFiles/{id}/stream.{container}` naming such a channel (by item id, `OpenToken`, `LiveStreamId` or stream id) | **403**, `X-Application-Error-Code: PlaybackNotAllowed`, `X-Kids-Reason: liveTvChannel`, ProblemDetails `{"status":403,"title":"This channel is not available.","reason":"liveTvChannel"}` |
| Any `LiveTv/Recordings…`, `LiveTv/Timers…`, `LiveTv/SeriesTimers…` route, and `LiveTv/LiveRecordings/{id}/stream` | **403**, `X-Kids-Reason: liveTvRecording`, ProblemDetails `{"status":403,"title":"Recordings are not available.","reason":"liveTvRecording"}` |

- Routes that need Jellyfin's live TV management permission (creating, changing or deleting timers and recordings)
  are refused by Jellyfin itself first, with a bare 403, for any kid without that permission.
- A live stream counts as known when the plugin let this server's kids open it for a channel (it remembers which); a
  kid request naming any other live TV stream is refused. A restart forgets them, as Jellyfin closes its live streams.
- Never `ParentalControl`, never 401.

## Jellyfin's own live TV lists (always on, the caller is a kid)

| Route | For a kid |
|---|---|
| `GET LiveTv/Channels` | only effective channels; `StartIndex`/`Limit` apply to that list; `TotalRecordCount` is its size |
| `GET`/`POST LiveTv/Programs` | only programs of effective channels (`channelIds` narrows within them) |
| `GET LiveTv/Programs/Recommended` | only programs of effective channels, paged after filtering |
| `GET LiveTv/Channels/{id}`, `GET LiveTv/Programs/{id}` | **404** when the channel (or the program's channel) is not effective |

Other Jellyfin routes are unchanged. Documented limits: generic `/Items` queries that name channel or program ids
directly, and images, are not filtered; stream URLs without a token that do not name a kid's device are not
attributable.

## Counting

Live TV is screen time like any playback: it counts toward `UsedSeconds` and the sitting, and the feature 5 warnings,
stop (which also closes the live stream) and refusals apply to it.
