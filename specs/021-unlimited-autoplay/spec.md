# Feature Specification: Autoplay without a limit

**Feature Branch**: `unlimited-autoplay` (from `main` at d5e3017, plugin 0.11.0.0; spec directory
`021-unlimited-autoplay`, the next number after the README branches 017–020)

**Created**: 2026-11-16

**Status**: Implemented

**Input**: Owner request of 2026-11-16: a parent can set a kid's autoplay to "no limit", so the next episode always
plays on its own, from the plugin's dashboard and from Peke on the TV. Today `AutoplayCap` must be 1–10.

Builds on [feature 1](../001-kids-plugin-foundation/spec.md) (kid profiles, `AutoplayCap`, the validation rules) and
[feature 12](../012-kids-avatar/spec.md) (`ApiMinorVersion`).

## Contract agreed with Peke

Peke's client side is built in parallel against this contract (see
[contracts/kids-api-v1-unlimited-autoplay.md](contracts/kids-api-v1-unlimited-autoplay.md)):

- `GET /Kids/Info` lists the feature `unlimitedAutoplay`.
- `AutoplayCap = 0` means no limit and is accepted. 1–10 keep working. Negatives and values above 10 are still
  rejected with a 400 naming `AutoplayCap`.
- A missing `AutoplayCap` keeps the default, 3.
- Stored profiles keep their value: nothing is migrated.
- Older clients that read 0 coerce it to 1. The owner accepted this.

## User Scenarios

### User Story 1: No limit from the dashboard (P1)

A parent opens a kid in the Kids dashboard page, turns autoplay on and ticks "No limit: always play the next episode".
Saving stores `AutoplayCap = 0`. Reopening the kid shows the box ticked and the number field disabled.

**Acceptance**:

1. Ticking "No limit" disables the "Episodes in a row" field and saves 0.
2. Unticking it enables the field again, with the default 3 when the stored value was 0.
3. With autoplay off, both the box and the field are disabled, as the field was before.
4. Ticking or unticking the box marks the form as unsaved.

### User Story 2: No limit from Peke (P1)

Peke reads `unlimitedAutoplay` in `GET /Kids/Info`, offers "No limit", and sends `AutoplayCap: 0` in
`PUT /Kids/Users/{userId}/Profile`. The plugin stores and returns 0.

## Requirements

- **FR-001**: The validator MUST accept `AutoplayCap` 0 and 1–10, and reject anything else with "Must be 0 (no limit)
  or between 1 and 10."
- **FR-002**: A missing `AutoplayCap` MUST give 3, as before.
- **FR-003**: `GET /Kids/Info` MUST list `unlimitedAutoplay` after the earlier features. `ApiMinorVersion` becomes 3,
  since the change is additive.
- **FR-004**: The kid editor MUST offer "No limit" next to the autoplay limit and save it as 0.
- **FR-005**: Profile summaries MUST show "No limit" for 0. Today no summary shows the autoplay limit (the Kids page
  list shows the role and the daily allowance only), so there is nothing to change.

## Out of scope

- Enforcement: the plugin does not count episodes in a row. Clients apply the limit, as before.
- Live TV and every other setting.
- Migrating stored profiles.

## Constitution check

- I (Leverage Jellyfin): Jellyfin has no autoplay limit; this only widens a plugin setting.
- II (Server decides): the server still validates the value; only admins, API keys and parent grants can change it.
- III, IV: unchanged.
- V (Stable contract): additive. A new feature name and a minor version; no field changes type or meaning for 1–10.
  Older clients see 0 and fall back to 1, which the owner accepted.
- VI (Tested): validator, store round trip and `/Kids/Info` tests.
