# Contract: Kids client API, version 1 (feature 3 additions)

This extends [feature 1's contract](../../001-kids-plugin-foundation/contracts/kids-api-v1.md) and
[feature 2's](../../002-kids-parent-lock/contracts/kids-api-v1-parent-lock.md). The JSON rules, status codes, the
"admin or grant" rule and the compatibility rules all still apply. Examples are PascalCase. Every endpoint below uses
`IgnoreParentalControl`.

## GET /Kids/Info (changed)

`Features` gains `"allowance"`: `["profiles","parentLock","allowance"]`.

## Kid profile (changed)

`KidProfile` and `KidProfileUpdate` gain `DailyAllowanceMinutes`: an integer 0–1440, or null / omitted for unlimited.

- PUT is still a full replacement, so **omitting the field clears the allowance**. Send it on every update.
- 400 `errors.DailyAllowanceMinutes` when it is out of range.

## DELETE /Kids/Users/{userId}/Profile (changed)

`?purge=true` also deletes the kid's usage, bonus and reported segments. Without it, they are kept, so marking the kid
again keeps today's count. On a user who is no longer a kid, `?purge=true` still deletes their screen time and
answers 404, so a purge that failed after the unmark can be retried.

## GET /Kids/Users/{userId}/Status

- **Auth**: self or admin. Works outside allowed hours.
- **200**:

```json
{
  "Date": "2026-10-18",
  "UsedSeconds": 1520,
  "AllowanceSeconds": 2700,
  "BonusSeconds": 600,
  "RemainingSeconds": 1780,
  "TimeUp": false,
  "ServerTime": "2026-10-18T16:02:11.0000000Z",
  "NextDayStartsAt": "2026-10-18T22:00:00.0000000Z",
  "WarnAtSeconds": [300, 60]
}
```

- **Unlimited kids**: `AllowanceSeconds` and `RemainingSeconds` are absent, and `TimeUp` is always false.
- **`UsedSeconds`** includes the time counted so far that is not yet stored, so clients polling every 60 s see live
  numbers.
- **`NextDayStartsAt`** is the next "day starts at" boundary (server local time, given in UTC).
- **403**: another non-admin user. **404**: not a kid. **500**: the store is unavailable, never "0 used".
- Allowed hours (`WithinSchedule`, `NextAllowedAt`) are added by feature 5. They are additive.

## POST /Kids/Users/{userId}/WatchTime/Bonus

- **Auth**: admin or grant.
- **Body**: `{"Minutes": 15}`, with minutes 1–240.
- **200**: the new status. The bonus applies to the kid's current day only, and several bonuses add up.
- **400** `errors.Minutes`. **403** a kid without a grant. **404** not a kid.

## POST /Kids/Users/{userId}/WatchTime/Report

For playback the server did not see (offline downloads). Clients MUST NOT report playback they also reported to the
server through Jellyfin's playstate API; the server does not cross-check (clarification Q2).

- **Auth**: self only. Admins, API keys and other users get 403.
- **Body**:

```json
{"Segments": [{"Id": "b7c1…", "StartedAt": "2026-10-18T09:40:00Z", "Seconds": 600}]}
```

- **Limits**:
  - 1–100 segments per request;
  - `Id` 1–64 chars, unique per kid (a client UUID is enough);
  - `Seconds` 1–43 200;
  - `StartedAt` at most 5 minutes in the future and within the last 90 days.
- **Effect**:
  - each new segment adds `Seconds` to the day `StartedAt` falls in;
  - a segment `Id` already reported is ignored, so retries are safe;
  - usage is never lowered.
- **200**: the new status for today.
- **400**: one invalid segment rejects the whole request, keyed `Segments[i].Id`, `Segments[i].Seconds`,
  `Segments[i].StartedAt` or `Segments`.
- **403**, **404** as above. The checks run in this order: authorization, then kid, then validation.

## Counting rules (informative)

- **What counts**: only playback in sessions the kid owns. A kid added as an additional user to someone else's
  session is not counted (clarification Q3).
- **Pauses**: paused time does not count. The server's synthetic progress counts for at most 60 s after the client's
  last real report.
- **Concurrent sessions**: they add up.
- **Day boundaries**: the day runs from the household's "day starts at" hour (server local time, default midnight).
  Playback across the boundary is split between the two days.
- **Usage only grows**. Parents give time back with bonus minutes (clarification Q5).
