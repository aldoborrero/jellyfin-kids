# Contract: Kids client API, version 1 (feature 5 additions: enforcement)

This extends the contracts of features [1](../../001-kids-plugin-foundation/contracts/kids-api-v1.md),
[2](../../002-kids-parent-lock/contracts/kids-api-v1-parent-lock.md),
[3](../../003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md) and
[4](../../004-kids-session-limit/contracts/). Everything here is **additive**: `ApiVersion` stays 1, new fields are
optional, and null fields are left out of the JSON. Examples are PascalCase.

## GET /Kids/Info (changed, anonymous)

`Features` gains `"enforcement"` and `"sitting"`:
`["profiles","parentLock","allowance","sessionLimit","enforcement","sitting"]`.

A new object reports whether the plugin could guard every playback route on this server. It describes the plugin and
the Jellyfin version, not the household: whether the household turned enforcement **on** is in Status.

```json
"Enforcement": { "Gate": "ok", "Missing": [] }
```

- `Gate`: `ok` (every expected route is guarded), `degraded` (some are missing: `Missing` lists them as
  `Controller.Action`), or `pending` (the startup check has not run yet).

## GET /Kids/Users/{userId}/Status (changed)

New fields:

| Field | Type | When present | Meaning |
|---|---|---|---|
| `Enforced` | bool | always | Whether the household turned server enforcement on |
| `DeniedReason` | string | only while enforced and denied | `timeUp`, `outsideSchedule` or `sessionLimit` |
| `DeniedUntil` | UTC date | with `DeniedReason`, when known | When the denial ends if nothing changes: next day start (`timeUp`), end of the break (`sessionLimit`); absent for `outsideSchedule` |
| `SittingLimitSeconds` | int | the kid has a sitting limit | The profile's `SessionLimitMinutes` × 60 |
| `SittingUsedSeconds` | int | same | Playing time in the current sitting, across all the kid's devices |
| `SittingRemainingSeconds` | int | same | Limit minus used, never negative |
| `BreakRemainingSeconds` | int | during the break after the limit | Seconds until the kid may play again |
| `BreakSeconds` | int | the kid has a sitting limit | The household break length |

A **sitting** is the kid's playing time, on any device, since they last had nothing playing for the break length
(household setting, default 15 minutes). The sitting is counted whether or not enforcement is on; it is kept in memory
and a server restart counts as a break. Clients that know the `sitting` capability can show these values and stop
counting sittings locally; before this feature, each device counted its own (feature 4's contract), and that remains
the rule for older plugins.

Example (enforced, sitting reached):

```json
{
  "Date": "2026-10-19", "UsedSeconds": 2100, "AllowanceSeconds": 3600, "BonusSeconds": 0, "RemainingSeconds": 1500,
  "TimeUp": false, "ServerTime": "2026-10-19T17:30:00.0000000Z", "NextDayStartsAt": "2026-10-19T22:00:00.0000000Z",
  "WarnAtSeconds": [300, 60],
  "Enforced": true, "DeniedReason": "sessionLimit", "DeniedUntil": "2026-10-19T17:44:10.0000000Z",
  "SittingLimitSeconds": 1800, "SittingUsedSeconds": 1802, "SittingRemainingSeconds": 0,
  "BreakRemainingSeconds": 850, "BreakSeconds": 900
}
```

## Refusals (enforcement on, the caller is a denied kid)

Admins and API keys are never refused. Only these routes are checked; everything else, including playback reports
(`Sessions/Playing*`) and `/Kids`, behaves as before.

| Route | Answer |
|---|---|
| `GET` / `POST Items/{id}/PlaybackInfo` | **200** `PlaybackInfoResponse` with `"ErrorCode": "NotAllowed"` and no `MediaSources`; header `X-Kids-Reason`. jellyfin-web shows "Playback of this media is not allowed." |
| `POST LiveStreams/Open`, `Audio/{id}/universal`, `Videos|Audio/{id}/master.m3u8`, `main.m3u8`, `live.m3u8`, `hls1/…` segments, legacy `hls/…`, `Videos|Audio/{id}/stream[.{container}]`, `Items/{id}/File`, `Items/{id}/Download`, `LiveTv/LiveStreamFiles/{id}/stream.{container}`, `LiveTv/LiveRecordings/{id}/stream` | **403**, headers `X-Application-Error-Code: PlaybackNotAllowed` and `X-Kids-Reason: <reason>`, ProblemDetails body `{"status":403,"title":"Playback is not allowed now.","reason":"timeUp","until":"…"}` |

- Never `X-Application-Error-Code: ParentalControl`, which makes jellyfin-web sign the user out; never 401.
- A request without a token (anonymous stream routes) is refused only when its `deviceId` query matches a session a
  denied kid currently owns. Otherwise it is served: a documented limit.
- Wholphin reacts to `X-Kids-Reason` / `DeniedReason` with its own screen.

## Server-sent messages and commands (enforcement on)

Sent to every session the kid owns while it is playing, whatever the client (clarification Q3):

| When | What |
|---|---|
| 5 and 1 minutes before the allowance, the sitting or the allowed hours end | `DisplayMessage` with `TimeoutMs` 10 000 (a toast in jellyfin-web). Header "Screen time", "Break time" or "Bedtime"; text "5 minutes left", "1 minute left" |
| The kid becomes denied | `DisplayMessage` without timeout (a modal in jellyfin-web): "Time's up" / "Screen time is over for today.", "Time for a break" / "You can watch again in N minutes.", "Bedtime" / "It is past your allowed hours."; then after about 5 s `Playstate Stop`; the server then ends its transcodes and live streams, cuts its in-flight streams and ends the playback on the server |
| The client keeps reporting playback while denied | `Playstate Stop` again, with the transcode end, the stream cut and the server-side end, at most every 10 s |

Texts are English in this version. Clients that show their own screen (Wholphin) can hide the server message when
`DeniedReason` is set.

## Settings (admin, plugin configuration API)

`GET/POST /Plugins/{pluginId}/Configuration` gains `EnforcementEnabled` (bool, default false) and
`SittingBreakMinutes` (1–240, default 15). The dashboard controls are a follow-up; the existing page keeps both
values, since it saves the configuration it loaded.

## Documented limits

- Stream URLs with no token and a missing or foreign `deviceId`, DLNA renderers, and offline downloads are not
  attributable to a kid.
- Clients with no WebSocket get no message or Stop; their transcodes are ended, their in-flight streams cut and later
  requests refused (spike R1 records how fast real players stop).
- The sitting and the warnings are in memory: a restart is a break, and a notice may be sent again after a restart.
- jellyfin-web 12.2 shows two alerts for a `NotAllowed` answer (its own "not allowed" one and a generic one), and
  after allowed hours Jellyfin itself answers PlaybackInfo 403 first, which web shows as "Unable to find a valid media
  source to play." (research, Validation).

**Update (feature 6, plugin 0.0.6.0):** the same refusal shapes also carry `X-Kids-Reason: liveTvChannel` (a channel
outside the kid's list) and `liveTvRecording` (recordings and timers). Those rules are always on, whatever
`EnforcementEnabled` says; see [feature 6's contract](../../006-kids-live-tv/contracts/kids-api-v1-live-tv.md).
