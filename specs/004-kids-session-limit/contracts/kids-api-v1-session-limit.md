# Contract: Kids client API, version 1 (feature 4 additions)

This extends [feature 1's contract](../../001-kids-plugin-foundation/contracts/kids-api-v1.md),
[feature 2's](../../002-kids-parent-lock/contracts/kids-api-v1-parent-lock.md) and
[feature 3's](../../003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md). The JSON rules, status codes, the
"admin or grant" rule and the compatibility rules all still apply. Examples are PascalCase.

## GET /Kids/Info (changed)

`Features` gains `"sessionLimit"`: `["profiles","parentLock","allowance","sessionLimit"]`.

Null fields are omitted from every answer, so a profile without `SessionLimitMinutes` can mean "no limit" or "this
plugin does not know the field". Clients MUST check `sessionLimit` in `Features` before treating a missing field as
"no limit", and MUST NOT send the field to a plugin without the capability.

## Kid profile (changed)

`KidProfile` and `KidProfileUpdate` gain `SessionLimitMinutes`: an integer 5–120, or null / omitted for no sitting
limit.

```json
{"UserId": "…", "Revision": 4, "SearchAllowed": false, "AutoplayEnabled": true, "AutoplayCap": 3,
 "Sections": […], "PickedItemIds": [], "DailyAllowanceMinutes": 60, "SessionLimitMinutes": 30}
```

- PUT is still a full replacement, so **omitting the field clears the limit**. Send it on every update, together with
  `DailyAllowanceMinutes`.
- 400 `errors.SessionLimitMinutes` when it is out of range, alongside any other field errors. The revision is checked
  first, as before (stale and invalid gives 409).
- Profiles stored before this version have no session limit.

## What the server does with it

**Nothing but store it.** The server does not count sittings, warn, stop or refuse playback because of the session
limit, and the status (`GET /Kids/Users/{userId}/Status`) does not include it.

The client counts a sitting **locally, per device**:

- each TV keeps its own sitting timer; the same kid on two TVs has two sittings;
- when and how a sitting starts, pauses and ends (for example a break before the next one) is the client's choice;
- the daily allowance still applies and is decided by the server; a client stops at whichever limit comes first;
- a client reads the limit from the profile (it is part of the cached profile, so it works offline) and applies a
  changed value from its next profile read.

A server-enforced session cap may come later (plan §7 feature 6) under its own capability.
