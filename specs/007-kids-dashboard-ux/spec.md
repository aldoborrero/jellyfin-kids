# Feature Specification: Kids dashboard UX and "Reset today"

**Feature Branch**: `011-kids-dashboard-ux` (from `main`, plugin 0.0.6.0 → 0.0.7.0)

**Created**: 2026-10-22

**Status**: Implemented (not committed)

**Input**: User description: "Spec 007, kids dashboard UX and 'Reset today' (plugin 0.0.7.0). Source: GOAL.md in this
worktree and the report 'UX del dashboard del plugin Kids' (P0 + selected P1)."

Goal: make the Kids dashboard impossible to misread. A parent sees at a glance whether each kid is really protected,
fixes a problem in one click, and can reset a kid's day. Today the dashboard shows a freshly marked kid as configured
although Jellyfin's default user policy leaves them unrestricted (every library, no rating cap, unrated items shown,
live TV and downloads on), enforcement is off by default and no kid row says so, and an empty daily allowance means "no
limit" while 0 means "no screen time" in the same field.

Builds on [feature 1](../001-kids-plugin-foundation/spec.md) (the two dashboard pages),
[feature 3](../003-kids-allowance-tracking/spec.md) (usage, bonus, Status),
[feature 4](../004-kids-session-limit/spec.md) (the sitting limit),
[feature 5](../005-kids-enforcement/spec.md) (enforcement, the gate self-check, the household break) and
[feature 6](../006-kids-live-tv/spec.md) (per-kid live TV channels).

## Clarifications

### Session 2026-10-22

- "Reset today" is a user decision of 2026-10-20 (GOAL.md §7). It reverses feature 3's add-only clarification Q5
  ("no API lowers usage, not even for admins", FR-012a of feature 3) for one explicit, logged, parent-only action.
  Reports from clients stay add-only (constitution II).
- "Lifts any denial at once" (GOAL.md §7) is read as: every denial caused by used time is lifted, that is time's up
  (unless the allowance is 0) and the sitting limit with its break. A denial for allowed hours comes from Jellyfin's
  schedule (constitution I) and an allowance of 0 from the profile; Reset does not override either (User Story 5,
  scenario 4).
- `/speckit-clarify` found no open point that needs a household decision: none was sent to `wholphin-kids`.
- Defaults taken without asking (none changes what a parent can rely on; each is recorded in Assumptions): the
  derived server UTC offset instead of a time zone name; what Reset clears and keeps; the health checks' exact
  conditions; the "Turn on" button on the enforcement banner; the health list in the kid editor too.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A parent sees which kids are not really protected, and fixes it (Priority: P1)

On the Kids page, under each kid, a parent sees one line per protection problem found in the kid's Jellyfin user:
"Administrator: bypasses every restriction", "Sees every library", "No maximum parental rating", "Unrated movies and
shows are not blocked", "Live TV access is off, so the picked channels do not play" (or "Live TV access is on but no
channels are picked"), "Downloads are allowed". Each line has a "Fix" link that opens the right tab of Jellyfin's own
user editor. A kid with no problem shows none.

**Why this priority**: a kid who looks protected but is not is the worst failure of a parental tool.

**Independent Test**: mark a fresh user as a kid: the row shows the library, rating, unrated and downloads lines with
links to `#/dashboard/users/{id}/access`, `/parentalcontrol` and `/profile`; fix them in Jellyfin, come back: the lines
are gone.

**Acceptance Scenarios**:

1. **Given** a kid with Jellyfin's default policy, **When** the Kids page loads, **Then** the row lists "Sees every
   library" (Fix → Access tab), "No maximum parental rating" and "Unrated movies and shows are not blocked" (Fix →
   Parental control tab) and "Downloads are allowed" (Fix → Profile tab).
2. **Given** a kid who is an administrator, **When** the page loads, **Then** the row says the kid bypasses every
   restriction, with Fix → Profile tab, on the Kids page and in the kid editor.
3. **Given** a kid with picked channels and live TV access off in Jellyfin, **When** the page loads, **Then** the row
   warns that the channels do not play, with Fix → Profile tab; with access on and no channels picked (on a server that
   has channels), it says live TV is on but nothing is picked, with a link to the kid editor's channel list.
4. **Given** a kid whose libraries, rating, unrated items and downloads are all restricted and who is not an
   administrator, **When** the page loads, **Then** no health line is shown.
5. **Given** a user name containing HTML or `${…}`, **When** any line naming the user is shown, **Then** it is shown
   as text, never interpreted.

---

### User Story 2 - A parent sees whether limits are enforced on every app (Priority: P1)

When the household's "Enforce on every app" switch is off, a banner at the top of the Kids page says "Limits only apply
in Wholphin": other apps keep playing when time is up. It offers a "Turn on" button. Every kid row shows an "App only"
pill while it is off. When the server's self-check finds playback or live TV routes it cannot guard on this Jellyfin
version, the banner says so too, with the missing routes.

**Why this priority**: a limit that only warns while the parent believes it stops playback is the second worst
failure.

**Independent Test**: with enforcement off, open the Kids page: banner and pills are there; press "Turn on", confirm:
the banner and pills go away and a toast says it is on.

**Acceptance Scenarios**:

1. **Given** enforcement off, **When** the Kids page loads, **Then** the banner is at the top, above the users list,
   and each kid row has an "App only" pill.
2. **Given** the banner, **When** the parent presses "Turn on" and confirms, **Then** enforcement is turned on (other
   settings untouched), a toast confirms it, and the banner and pills disappear after the page reloads its data.
3. **Given** enforcement on and both self-checks `ok`, **When** the page loads, **Then** no banner and no pill is shown.
4. **Given** a degraded Enforcement or LiveTv self-check, **When** the page loads, **Then** the banner names the routes
   that cannot be guarded, whatever the switch says.

---

### User Story 3 - "No limit" and "No screen time today" can't be confused (Priority: P1)

In the kid editor, the daily allowance has an explicit "No daily limit" checkbox. With it ticked, the minutes field is
disabled and the kid has no daily limit. Unticked, the field takes 0–1440 minutes, and 0 reads "No screen time today"
in the field's help, on the kid's row and in the editor status. The sitting limit gets the same "No sitting limit"
checkbox.

**Why this priority**: today the same field means opposite things when empty and when 0.

**Independent Test**: tick "No daily limit", save, reload: ticked, field disabled, row says "No daily limit". Untick,
enter 0, save: the row says "No screen time today".

**Acceptance Scenarios**:

1. **Given** a kid without an allowance, **When** the editor loads, **Then** "No daily limit" is ticked and the field
   is disabled and empty.
2. **Given** "No daily limit" unticked and the field empty or out of range, **When** saving, **Then** the save is
   blocked with "Between 0 and 1440 minutes." next to the field and nothing is sent.
3. **Given** an allowance of 0, **When** shown anywhere, **Then** it reads "No screen time today", never "0 min" or
   "no limit".
4. **Given** "No sitting limit" ticked, **When** saving, **Then** the kid has no sitting limit; unticked, the field
   takes 5–120 minutes.

---

### User Story 4 - A parent sees each kid's real live TV access (Priority: P2)

Each kid row and the editor say "Watches N of M channels" (N = picked channels that exist on the server, M = the
server's channels) or "No live TV" when nothing is picked or Jellyfin's live TV access is off for the user.

**Why this priority**: feature 6 needs two switches to agree; one effective line is what a parent needs.

**Independent Test**: give Alice one of two channels: "Watches 1 of 2 channels"; turn her live TV access off in
Jellyfin: "No live TV" plus the health line.

**Acceptance Scenarios**:

1. **Given** a kid with 1 of the server's 2 channels and live TV access on, **Then** "Watches 1 of 2 channels".
2. **Given** live TV access off or no channel picked, **Then** "No live TV".
3. **Given** the server's channel list cannot be read, **Then** the line says "Watches N channels" without a total.

---

### User Story 5 - A parent resets a kid's day (Priority: P2)

In the kid editor a parent presses "Reset today", confirms, and the kid's used time and bonus for today go back to 0;
if the kid was refused for time or for the sitting limit, they may play again at once. The server log records who did
it, when, and what was cleared. A parent on a TV with a parent grant can do the same through the API.

**Why this priority**: a parent who counted wrongly, or whose kid's time was eaten by a stuck player, has no way back
today but adding bonus time.

**Independent Test**: with Alice at time's up and enforcement on, call Reset as admin: 200 with `UsedSeconds == 0`,
`BonusSeconds == 0`, no `DeniedReason`; PlaybackInfo has media again; the log has the reset line. As Alice: 403.

**Acceptance Scenarios**:

1. **Given** a kid with time used and a bonus today, **When** an administrator or a parent grant holder resets the
   day, **Then** the answer is the kid's status with 0 used and 0 bonus, and playback refused for time or the sitting
   limit is allowed again on the next request.
2. **Given** the kid's own token, another user's token or a grant from another device, **When** resetting, **Then**
   403 and nothing changes.
3. **Given** a user who is not a kid, **When** resetting, **Then** 404.
4. **Given** a kid outside allowed hours or with an allowance of 0, **When** the day is reset, **Then** the usage is
   cleared but the kid stays refused: those denials come from the schedule and the allowance, not from used time.
5. **Given** the editor, **When** the parent presses "Reset today", **Then** a confirmation names the kid and what is
   cleared; on confirm the status is redrawn and a toast confirms; on cancel nothing is sent.
6. **Given** an offline segment that was already reported before the reset, **When** it is reported again, **Then** it
   adds nothing (its id is still known).

---

### User Story 6 - Settings save the same way everywhere, and say what they will do (Priority: P2)

Every settings form on the two pages saves with its own Save button and confirms with a toast; errors stay next to the
field or in a dialog. Immediate actions (marking a kid, adding a bonus, Reset today, removing the lock, turning on
enforcement) confirm with a toast too. An "Unsaved changes" note appears next to Save when the form differs from what
was loaded, and leaving the page with unsaved changes asks first where the browser allows it. After Save, the kid's
status is read again. The editor shows the household break next to the sitting limit ("Break between sittings: 15 min,
set on the Kids page"), always using the word "sitting". Times say they are server time, with the server's UTC offset.
The kid's allowed hours from Jellyfin are shown read-only with a Fix link. Status messages are announced to screen
readers.

**Why this priority**: three save models, a modal "Saved", unlabelled time zones and settings spread over pages make
parents doubt what is in force.

**Independent Test**: change a field: "Unsaved changes" appears; Save: a toast, the note goes, the status line follows
the new allowance; the sitting block shows the break; the allowed hours list matches Jellyfin's Parental control tab.

**Acceptance Scenarios**:

1. **Given** a loaded editor, **When** any field changes, **Then** "Unsaved changes" is shown; **When** the change is
   undone or saved, **Then** it disappears.
2. **Given** a save, **When** it succeeds, **Then** a toast says so (no modal), and the status is read again.
3. **Given** a kid with a sitting limit, **Then** the editor shows the household break next to it with a link to the
   Kids page, and the help says the sitting ends at whichever comes first.
4. **Given** a kid with allowed hours, **Then** the editor lists them read-only ("Every day 08:00–20:00") with a link
   to the Parental control tab; with none, "Any time: no allowed hours are set in Jellyfin".
5. **Given** a denial with an end time, **Then** the end time is shown in server time with its UTC offset.
6. **Given** the Kids page, **Then** the "Day starts at" setting shows the server's current time and UTC offset.

### Edge Cases

- A kid's user cannot be read (deleted meanwhile): the row keeps the "unavailable" texts and shows no health lines,
  never "all fine".
- The Status of a kid cannot be read: "Usage unavailable", no pill derived from it, no server time.
- No kid at all: no server time can be derived from a Status; the server time line is left out.
- Reset while the kid is playing: counting continues from the reset; time counted just before it (at most one progress
  interval, about 30 s) may still land after it.
- Reset races with the 30 s flush: the reset waits for the flush and the flush cannot write the cleared time back.
- Reset at the start of a new day: it clears the kid's current day as the server computes it.
- Two tabs edit the same kid: the existing revision conflict banner still applies; the unsaved note stays until the
  parent reloads or saves.
- Turn on from the banner while another tab changed the configuration: the page reads the configuration again and
  changes only the enforcement switch.
- Daylight saving: the UTC offset shown is the one at the next day start, which can differ from the current one for a
  few hours a year (see Assumptions).
- An admin kid: Jellyfin ignores their allowed hours; the admin health line covers it.

## Requirements *(mandatory)*

### Functional Requirements

**Health (Kids page and kid editor)**

- **FR-001**: For each kid, the Kids page MUST show one health line per issue found in the kid's Jellyfin user policy,
  built only from data the page already loads (the user list with policies, the kid profile, the Status, the server's
  channel list):
  - administrator → Fix: Profile tab;
  - every library allowed (`EnableAllFolders`) → Fix: Access tab;
  - no maximum parental rating → Fix: Parental control tab;
  - unrated movies or shows not blocked → Fix: Parental control tab;
  - live TV access off while channels are picked → Fix: Profile tab; live TV access on with no channel picked while
    the server has channels → link to the kid editor's channels;
  - content downloads allowed → Fix: Profile tab.
- **FR-002**: Fix links MUST be `#/dashboard/users/{id}/profile`, `/access` or `/parentalcontrol`, Jellyfin 12.2's own
  user editor routes.
- **FR-003**: The kid editor MUST show the same health lines, the administrator one first, above the settings.
- **FR-004**: Health lines MUST never be shown as "no issue" when the user's policy could not be read.

**Enforcement visibility**

- **FR-005**: The Kids page MUST show a banner at the top when enforcement is off ("Limits only apply in Wholphin"),
  and when the Enforcement or LiveTv self-check of `GET /Kids/Info` is `degraded` (with the missing routes).
- **FR-006**: The banner's "Turn on" MUST ask for confirmation, then re-read the plugin configuration and write it back
  with only `EnforcementEnabled` changed, and confirm with a toast.
- **FR-007**: Each kid row MUST show an "App only" pill while the kid's Status says `Enforced: false`.

**Allowance and sitting limit**

- **FR-008**: The daily allowance MUST have an explicit "No daily limit" choice; with it, the profile's allowance is
  null; without it, the field MUST hold a whole number 0–1440, and an empty field blocks the save.
- **FR-009**: An allowance of 0 MUST be shown as "No screen time today" on the kid row, in the editor status and in the
  field's help; a null allowance as "No daily limit".
- **FR-010**: The sitting limit MUST have the same explicit "No sitting limit" choice (5–120 minutes otherwise).
- **FR-011**: The editor MUST show the household break next to the sitting limit, with a link to the Kids page, and
  the UI MUST use "sitting" for this concept everywhere ("Sitting limit", "This sitting", "Break between sittings").

**Live TV state**

- **FR-012**: The Kids page and the editor MUST show "Watches N of M channels" (N = picked channels that exist on the
  server, M = server channels) or "No live TV" when N is 0 or live TV access is off; without the server list,
  "Watches N channels".

**Save model and status**

- **FR-013**: Each settings form MUST save with one Save button and confirm success with a toast; validation errors
  stay inline; server and network errors are shown inline or in a dialog. Immediate actions confirm with a toast.
- **FR-014**: The editor and the Kids page screen-time form MUST show "Unsaved changes" while their values differ from
  what was loaded or last saved, and warn on page unload while they do.
- **FR-015**: After a successful editor save, the kid's Status MUST be read again and redrawn.
- **FR-016**: Times MUST be labelled as server time with the server's UTC offset; the Kids page MUST show the server's
  current time near "Day starts at"; `DeniedUntil` MUST be shown in server time.
- **FR-017**: The editor MUST show the kid's allowed hours (Jellyfin access schedules) read-only, with a Fix link to
  the Parental control tab.
- **FR-018**: Status, error, conflict, banner and unsaved messages MUST be in polite live regions; decorative icons
  MUST be hidden from assistive technology.
- **FR-019**: Markup MUST only rely on elements guaranteed in plugin pages (`emby-button`, `emby-linkbutton`,
  `emby-input`, `emby-checkbox`, `paper-icon-button-light`) and theme classes; select boxes MUST carry their final
  classes and label in the markup; collapsibles, if any, use `<details>`.
- **FR-020**: Every user-provided string MUST be inserted as text. The page HTML and scripts MUST hold no household
  data or secret. Every configuration write MUST re-read the configuration first.

**Reset today**

- **FR-021**: `POST /Kids/Users/{id}/WatchTime/Reset` MUST set the kid's used time and bonus for the current day to 0
  (stored and not-yet-stored time), clear the current sitting and its break, reload the kid's limits and answer 200
  with the new Status.
- **FR-022**: Reset MUST be allowed only to administrators, API keys and parent grant holders (the bonus rule); a kid's
  own token or anyone else gets 403; a non-kid 404. It MUST work outside the kid's allowed hours.
- **FR-023**: Reset MUST be logged with the kid, the day, who did it, when, and the minutes of usage and bonus cleared.
- **FR-024**: Reset MUST NOT touch other days, reported offline segment ids (so a repeated report still adds nothing),
  the profile or the allowed hours.
- **FR-025**: `GET /Kids/Info` MUST list the `watchTimeReset` capability; the API version stays 1.
- **FR-026**: The kid editor MUST offer "Reset today" with a confirmation naming the kid; on success the status is
  redrawn and a toast confirms.
- **FR-027**: No existing endpoint's behaviour may change.

**Release**

- **FR-028**: The plugin version MUST be 0.0.7.0.

### Key Entities

- **Health issue** (not stored): kind, message, Fix target (Jellyfin user editor tab or the kid editor), derived from
  the user policy, the profile's channel list and the server's channels.
- **Day reset** (not stored apart from the log): kid, day, caller, time, usage and bonus minutes cleared.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a freshly marked kid with Jellyfin's default policy, the Kids page shows 4 health lines (5 on a server with
  live TV channels), each with a working Fix link, without the parent opening any other page.
- **SC-002**: With enforcement off, the first screen of the Kids page at 1920 px and at 400 px shows the banner.
- **SC-003**: A parent can turn enforcement on from the banner in 2 clicks.
- **SC-004**: "No limit" and 0 are never displayed with the same words, on any screen.
- **SC-005**: A reset kid can play again within one request after the reset when refused for time or the sitting.
- **SC-006**: 100% of tested reset attempts by a kid token or a foreign-device grant are refused, and every successful
  reset is in the log.
- **SC-007**: Both pages render without layout overflow at 400 px and 1920 px, in the dark and light themes.

## Assumptions

- **Time zone (default)**: the page derives the server's UTC offset from Status (`NextDayStartsAt` is the configured
  day-start hour in server time) instead of adding a time zone name to the API, which the "no API change" constraint
  rules out. Around daylight-saving changes the offset shown is the one at the next day start.
- **What Reset clears (default)**: today's usage (stored and pending) and today's bonus rows, and the in-memory sitting
  and break. Reported segment ids, other days and the profile stay. A denial for allowed hours or for an allowance of 0
  stays, because no used time causes it.
- **Reset log (default)**: the server log at information level, like bonus minutes; no new table and no Jellyfin
  activity entry.
- **Health conditions (default)**: "unrated not blocked" means Movie or Series missing from `BlockUnratedItems`; the
  "no allowed hours" check from the report is left out (GOAL scope); allowed hours are shown in the editor instead.
- **Banner button (default)**: "Turn on" saves only `EnforcementEnabled` at once after a confirmation, because the goal
  is a one-click fix; the screen-time form keeps its own switch.
- **Health in the editor (default)**: the full list, not only the administrator line, since the editor already loads
  the user.
- **Unsaved warning**: the browser's unload prompt only covers reloads and closing the tab; Jellyfin's in-app
  navigation cannot be cancelled by plugin pages, so the visible note is the main signal.
- Out of scope (report P1/P2 not selected): header tabs, kid cards and one-click bonus presets, localisation, live
  "watching now", denial history, age presets, configuration validation endpoint, plugin icon.
