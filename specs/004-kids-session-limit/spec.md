# Feature Specification: Kids session limit

**Feature Branch**: `004-kids-session-limit` (stacked on feature 3, commit `6ae5f93`)

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "Feature 4 kids-session-limit: a per-kid sitting limit. The kid profile gains an optional
SessionLimitMinutes (integer 5–120, null means no sitting limit), stored in kids.db with a schema migration (old rows
null), returned in the profile DTOs and validated on PUT like the other fields (400 ProblemDetails keyed
SessionLimitMinutes); PUT stays a full replacement. The server only stores it: the TV (Wholphin) counts a sitting
locally, per device, and the contract says so. GET /Kids/Info Features gains "sessionLimit", because null fields are
omitted from JSON and clients need the flag to know the plugin supports the field. The dashboard kid editor gets a
"Session limit" control (none, or 5–120 minutes) that saves with the profile and shows the 400 error next to the field.
Also: constitution principle V amended (clients keep working without kids features when the plugin is absent, PATCH
1.0.1), a doc note in the feature 3 allowance contract about changing 'day starts at' mid-day, and version 0.0.4.0."

Research input: [docs/plan.md](../../docs/plan.md) §1 (the "Per-session timer" row: device by default), §4.1 (the
profile DTO) and §7. Builds on [feature 1](../001-kids-plugin-foundation/spec.md) (kid profiles, revisions, validation)
and [feature 3](../003-kids-allowance-tracking/spec.md) (the daily allowance, which this mirrors).

## Clarifications

The decisions were made with the request, so `/speckit-clarify` was skipped:

- The limit is stored per kid on the server and counted on each device; the server does not count or enforce
  sittings.
- Range 5–120 minutes, or none. None is the default, so existing kids get no sitting limit.
- A client learns that the server supports the field from the `sessionLimit` capability, not from the field's
  presence, because a missing value is left out of the answer.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A parent sets how long one sitting may last (Priority: P1)

A grown-up decides that a kid may watch for at most 30 minutes in one go, then take a break, whatever is left of the
daily allowance. They set it in the kid's editor on the Jellyfin dashboard (or from the parent menu on a TV, with a
parent grant), and every TV that shows the kid's profile picks it up.

**Why this priority**: It is the whole feature: the setting must live on the server so that every TV in the house
uses the same value, instead of each one keeping its own.

**Independent Test**: Set `alice`'s session limit to 30 minutes in the dashboard; read her profile as `alice` and see
30. Clear it and see no limit.

**Acceptance Scenarios**:

1. **Given** a kid, **When** an administrator or a caller with a parent grant saves a session limit of N minutes
   (5–120), **Then** the profile holds N and the kid's own profile read returns it.
2. **Given** the session limit, **When** a value below 5 or above 120 is saved, **Then** the change is rejected naming
   the field, and nothing is saved.
3. **Given** a kid with a session limit, **When** the profile is saved again without it, **Then** the limit is cleared,
   as for every other setting (the update replaces the whole profile).
4. **Given** a kid's own credentials without a grant, **When** they try to change their session limit, **Then** it is
   refused (feature 1's rule).
5. **Given** kids marked before this feature, **When** the plugin is updated, **Then** they have no session limit and
   their other settings are unchanged.

---

### User Story 2 - A client knows whether the server holds the session limit (Priority: P1)

Wholphin asks the plugin what it supports before it relies on a setting. When the plugin offers the session limit, the
TV uses the kid's server value for its sitting timer; on an older plugin, it knows that the absence of the field means
"not supported", not "no limit".

**Why this priority**: Without the capability, a client cannot tell an older plugin from a kid without a limit, and
could drop a limit set elsewhere.

**Independent Test**: Read the anonymous plugin information and see `sessionLimit` among the capabilities.

**Acceptance Scenarios**:

1. **Given** the plugin with this feature, **When** anyone reads the plugin information, **Then** the capabilities
   include `sessionLimit`, alongside the earlier ones.
2. **Given** a kid without a session limit, **When** their profile is read, **Then** the field is absent from the
   answer.

---

### User Story 3 - The dashboard editor shows and checks the session limit (Priority: P2)

In the kid editor, the grown-up sees a "Session limit" control next to the daily allowance: none, or 5 to 120 minutes.
It saves with the rest of the profile. If the server rejects the value, the message appears next to the control.

**Why this priority**: Parents need a place to set it without a TV; the API covers it otherwise.

**Independent Test**: Edit `alice`, set the session limit to 45, save and reload: it shows 45. Enter 200: the editor
shows the allowed range next to the control and nothing is saved.

**Acceptance Scenarios**:

1. **Given** the kid editor, **When** it loads, **Then** it shows the kid's session limit, or "No limit".
2. **Given** a value of 5–120 or none, **When** the administrator saves, **Then** the profile holds it, under feature
   1's revision and conflict rules.
3. **Given** a value outside 5–120, **When** the administrator saves, **Then** the editor shows the allowed range next
   to the control and sends nothing; a rejection from the server is also shown there.
4. **Given** the page is fetched without signing in, **When** its content is inspected, **Then** it holds no household
   data.

---

### Edge Cases

- A session limit longer than the time left today: the daily allowance still applies; the client stops at whichever
  comes first. The server does not combine them.
- A session limit set while a kid is watching: the TV uses the new value from its next profile read; the server does
  not track sittings, so nothing changes on the server side.
- The same kid watching on two TVs: each TV counts its own sitting (per device), by design.
- A kid who is unmarked and marked again: the profile starts from the defaults, so no session limit (feature 1's rule
  for every setting).
- An older client that does not send the field: its update clears the limit, as with the daily allowance. Clients that
  know the `sessionLimit` capability send it on every update.
- Rolling the plugin back to 0.0.3: the database has a newer schema version, and the older plugin refuses it (feature
  1's rule), so kids data is unavailable until the newer plugin is reinstalled.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A kid profile MUST hold an optional session limit in whole minutes, 5–120 inclusive. None means no
  sitting limit and is the default.
- **FR-002**: Profiles stored before this feature MUST read as having no session limit, with every other setting
  unchanged.
- **FR-003**: The profile read by the kid, by administrators and by grant holders MUST include the session limit when
  one is set, and leave it out when there is none.
- **FR-004**: Changing the session limit MUST follow feature 1's profile rules: administrator or parent grant, revision
  check, full replacement (omitting the value clears it) and validation naming the field, alongside any other field
  errors.
- **FR-005**: The server MUST only store the session limit. It MUST NOT count sittings, warn, stop or refuse playback
  because of it; counting a sitting is each device's job, and the contract MUST say so.
- **FR-006**: The anonymous plugin information MUST add the `sessionLimit` capability.
- **FR-007**: The kid editor MUST let an administrator set, change or clear the session limit (none, or 5–120
  minutes), check the range before saving, and show a server rejection next to the control.
- **FR-008**: The page content served by Jellyfin MUST NOT include any kid's session limit.

### Key Entities

- **Kid profile** (from feature 1): gains the optional session limit in minutes.
- **Plugin information** (from feature 1): gains the `sessionLimit` capability.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A session limit saved for a kid is returned unchanged by every later profile read, until it is changed or
  cleared, in 100% of attempts.
- **SC-002**: Every value outside 5–120 is rejected with a message naming the setting, and nothing is saved.
- **SC-003**: After the update, every existing kid reads as having no session limit, and no other setting changes.
- **SC-004**: An administrator can set a kid's session limit from the dashboard in under 1 minute.

## Assumptions

- The sitting is counted on the TV, per device, as plan §1 decided ("Per-session timer: device by default"). A
  server-enforced cap remains a possible later feature (plan §7 feature 6).
- The range 5–120 minutes matches what a TV timer offers; the server does not round values.
- The field name is `SessionLimitMinutes`; plan §4.1 called it `sessionDefaultMinutes` before it was decided, and is
  updated.
- This feature only adds a setting, so it takes the next spec number. Enforcement (plan §7 row 4) moves to spec 005;
  the plan's row numbers stay as they are.
- The constitution amendment (principle V, PATCH 1.0.1), the allowance contract note about changing "day starts at"
  mid-day and the version bump to 0.0.4.0 ship with this feature but are not part of its behaviour.
