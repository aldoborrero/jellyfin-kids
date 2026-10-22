# Contract: Kids client API, version 1 (feature 7 additions)

Extends [feature 3's contract](../../003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md) and
[feature 5's](../../005-kids-enforcement/contracts/kids-api-v1-enforcement.md). The JSON rules, status codes, the
"admin or grant" rule and the compatibility rules still apply. No existing endpoint changes.

## GET /Kids/Info (changed)

`Features` gains `"watchTimeReset"`, after `"liveTv"`. `ApiVersion` stays 1. Clients show a Reset action only when
the capability is listed.

## POST /Kids/Users/{userId}/WatchTime/Reset (new)

Sets the kid's used time and bonus for the **current day** to 0 and ends the current sitting and its break.

- **Auth**: administrator, API key or parent grant holder (`X-Kids-Grant` on the device that obtained it), as for
  bonus minutes. Works outside the kid's allowed hours (`IgnoreParentalControl`).
- **Body**: none, or `{}`.
- **200**: the kid's new Status (feature 3 and 5 shape): `UsedSeconds` 0 (plus anything counted after the reset),
  `BonusSeconds` 0, and no `DeniedReason` unless the denial is `outsideSchedule`, or `timeUp` with an allowance of 0.
- **403**: the kid's own token, another user, or a grant from another device. **404**: not a kid. Checks run in this
  order: authorization, then kid.
- **Effects**:
  - today's stored and not-yet-stored usage and today's bonus rows are removed; other days are untouched;
  - reported offline segment ids are kept, so re-sending a segment reported before the reset adds nothing;
  - the kid's limits are reloaded before answering, so the next PlaybackInfo already follows the reset;
  - the server log records `Screen time of {UserId} reset for {Day} by {Caller}: {UsedMinutes} min used and
    {BonusMinutes} min bonus cleared`, where `{Caller}` ends in " with a parent grant" unless the caller is an administrator
    or an API key.
- **Not affected**: the profile (allowance, sitting limit, channels), Jellyfin's allowed hours, the household settings.
- **Known limit**: a playing client's last progress interval (at most about 30 s) that started before the reset may be
  counted after it.

## Feature 3 note, amended

Feature 3's "Usage only grows. Parents give time back with bonus minutes (clarification Q5)" now reads: usage only
grows through counting and reports; a parent may also reset the current day (this endpoint, user decision
2026-10-20), which is logged.
