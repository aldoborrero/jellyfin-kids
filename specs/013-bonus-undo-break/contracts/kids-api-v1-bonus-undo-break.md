# Contract: Kids client API 1.2 (feature 13 additions: undo a bonus, end a break)

Extends the contracts of features [3](../../003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md),
[5](../../005-kids-enforcement/contracts/kids-api-v1-enforcement.md),
[7](../../007-kids-dashboard-ux/contracts/kids-api-v1-reset.md) and
[12](../../012-kids-avatar/contracts/kids-api-v1-avatar.md). Everything here is **additive**: `ApiVersion` stays 1.
The JSON rules (PascalCase by default, camelCase with `profile="CamelCase"`, null fields omitted, ProblemDetails always
camelCase), the "self or admin" and "admin or grant" rules (`X-Kids-Grant` on the device that obtained it) still apply.
Every route below works outside the kid's allowed hours. Refusals are 403, never 401. Examples are PascalCase.

## GET /Kids/Info (changed, anonymous)

```json
{"PluginVersion": "0.0.9.0", "ApiVersion": 1, "ApiMinorVersion": 2,
 "Features": ["profiles", "parentLock", "allowance", "sessionLimit", "enforcement", "sitting", "liveTv",
              "watchTimeReset", "avatar", "bonusUndo", "breakEnd"], …}
```

- Show Undo only with `bonusUndo`, End break only with `breakEnd`.

## POST /Kids/Users/{userId}/WatchTime/Bonus (changed)

Unchanged request (`{"Minutes": 1–240}`), auth and errors. The 200 answer is the kid's status, as before, plus:

```json
{"Date": "2026-10-26", "UsedSeconds": 1800, "AllowanceSeconds": 3600, "BonusSeconds": 900, "RemainingSeconds": 2700,
 "TimeUp": false, …, "BonusId": 42, "BonusUndoUntil": "2026-10-26T18:12:00Z"}
```

- `BonusId` (integer): the bonus just given. Ids are unique on the server and **never reused**.
- `BonusUndoUntil` (UTC): until when `DELETE …/Bonus/{BonusId}` accepts it: 10 minutes after it was given, or the
  start of the kid's next day if that comes first.
- Both appear only in this answer. A bonus given through a parent grant is recorded as "{user} with a parent grant".

## GET /Kids/Users/{userId}/WatchTime/Bonus (new)

Today's bonuses (the kid's current day under "day starts at"), newest first.

- **Auth**: the kid themselves, administrators and API keys (as the Status). **403** otherwise. **404**: not a kid.

```json
{"Date": "2026-10-26", "ServerTime": "2026-10-26T18:05:10Z", "UndoWindowSeconds": 600,
 "Bonuses": [
   {"Id": 42, "Minutes": 15, "GrantedBy": "mum", "CreatedAt": "2026-10-26T18:02:00Z",
    "UndoUntil": "2026-10-26T18:12:00Z", "CanUndo": true},
   {"Id": 40, "Minutes": 30, "GrantedBy": "alice with a parent grant", "CreatedAt": "2026-10-26T16:00:00Z",
    "UndoUntil": "2026-10-26T16:10:00Z", "CanUndo": false}]}
```

- `CanUndo`: whether `DELETE` would accept it now (judged on `ServerTime`). An empty day answers `"Bonuses": []`.

## DELETE /Kids/Users/{userId}/WatchTime/Bonus/{bonusId} (new)

Undoes one bonus of today, within its window.

- **Auth**: administrator, API key or parent grant holder. **Body**: none.
- **200**: the kid's new Status (the same shape as `GET …/Status`). Removing a bonus can make `TimeUp` true again and,
  with enforcement on, bring back `DeniedReason: "timeUp"`; the kid's limits follow before the answer is sent.
- **Errors**, checked in this order:

  | Status | When | ProblemDetails `title` |
  |---|---|---|
  | 403 | a kid's own token without a grant, another user, a grant from another device | (empty Forbid) |
  | 404 | the user is not a kid | `This user is not a kid.` |
  | 404 | no bonus with that id for this kid (never existed, another kid's, already undone, cleared by Reset today) | `No such bonus.` |
  | 409 | the bonus is from an earlier day, or more than 10 minutes old | `This bonus can no longer be undone.` |
  | 400 | `bonusId` is not an integer (route binding) | |

- **Idempotency**: undoing the same id twice: the second gets 404 and changes nothing. Two TVs undoing their own ids
  never affect each other.
- **Log**: `Bonus {BonusId} of {Minutes} min for {UserId} (given by {GrantedBy} at {CreatedAt}) undone by {Caller}`,
  `{Caller}` ending in " with a parent grant" for grant holders.

## POST /Kids/Users/{userId}/WatchTime/Break/End (new)

Ends the kid's current break after the sitting limit, so they may play again at once.

- **Auth**: administrator, API key or parent grant holder. **Body**: none, or `{}`.
- **200**: the kid's new Status. After a break was ended: no `BreakRemainingSeconds`, `SittingUsedSeconds` 0, the full
  `SittingRemainingSeconds`, and no `DeniedReason: "sessionLimit"`. `UsedSeconds`, `BonusSeconds`, `RemainingSeconds`
  and `TimeUp` are unchanged: ending a break gives no screen time, and a kid whose time is up stays refused for it.
- **Not in a break** (no sitting limit, still within it, the break already over, or nothing known since a server
  restart): nothing changes, **200** with the status, nothing logged. Two TVs ending the same break both get 200.
- **403** / **404** (`This user is not a kid.`), in that order.
- **Next sitting**: starts with the kid's next counted playback, from zero, with the full sitting limit, as after a
  break that ran out. Playing time from before End break does not count toward it.
- **Not affected**: Jellyfin's allowed hours (`outsideSchedule` stays), the day's usage and bonus, the profile.
- **Log**: `Break of {UserId} ended by {Caller} with {Seconds} s left`.

## Compatibility

- Older servers: no `bonusUndo`/`breakEnd`, no `BonusId` in the bonus answer, and the new routes answer 404 (or 405).
- Clients that ignore `BonusId` keep working.
- `kids.db` moves to schema 5; a downgrade to 0.0.8.0 reports the database as newer until 0.0.9.0 is reinstalled.
