# Feature Specification: Undo a bonus and end a break

**Feature Branch**: `013-bonus-undo-break` (from `main` at v0.0.8.0, plugin 0.0.8.0 → 0.0.9.0; spec directory
`013-bonus-undo-break`)

**Created**: 2026-10-26

**Status**: Implemented (not committed)

**Input**: User description: "Feature 013, for the Peke TV app's new "Add time" widget: (1) undo the last bonus of
today: a grown-up who gave the wrong amount can undo it within a short window; prefer a bonus id returned by
`POST .../Bonus` and `DELETE .../Bonus/{bonusId}` so two TVs don't remove each other's bonus; only today's bonuses,
only by an administrator or a parent grant holder, logged like bonuses, returns the new status (time's up may come
back); decide a window so undo can't become a hidden way to take time away. (2) End the current break after the
sitting limit: `POST .../WatchTime/Break/End`; used time and bonus stay; same access rules, logged, returns the new
status; decide and document what the next sitting counts from. (3) Capabilities in `GET /Kids/Info`, API minor
version 2, contract in contracts/. (4) Dashboard: today's bonuses with Undo inside the window, and End break while a
kid is in a break, consistent with the 0.0.7.0 and 0.0.8.0 style. Version 0.0.9.0 with a changelog, not published."

Builds on [feature 3](../003-kids-allowance-tracking/spec.md) (bonus minutes, status),
[feature 4](../004-kids-session-limit/spec.md) and [feature 5](../005-kids-enforcement/spec.md) (sittings, the break,
denials), [feature 7](../007-kids-dashboard-ux/spec.md) (Reset today, the kid editor's Screen time section) and
[feature 12](../012-kids-avatar/spec.md) (`ApiMinorVersion`).

## Clarifications

### Session 2026-10-26

No question was sent to the household: the coordinator asked for the recommended defaults, recorded here and in
Assumptions.

- **Undo by id, not "last"**: `POST .../WatchTime/Bonus` answers the new bonus's id; undo is
  `DELETE .../WatchTime/Bonus/{bonusId}`. A "delete the last bonus" call would let TV B undo the bonus TV A just gave
  when both act at once, and a retried request could undo two bonuses. With an id, each widget undoes exactly the
  bonus it gave, and a repeated undo of the same id is answered 404 and changes nothing. Ids are never reused, so a
  late retry cannot hit a newer bonus.
- **Undo window: 10 minutes**, counted from when the bonus was given, and never past the end of the kid's day.
  Ten minutes covers "I tapped 60 instead of 15" noticed on the spot, and limits what undo can take away to a bonus
  the kid has had for minutes. After the window a bonus stays; the explicit, logged "Reset today" (feature 7) remains
  the only way to clear a day.
- **Who may undo**: any administrator, API key or parent grant holder, not only whoever gave it (a parent may correct
  the other parent's tap from the dashboard). The log names both.
- **Today's bonuses can be listed**: `GET .../WatchTime/Bonus` answers today's bonuses with their id, minutes, who gave
  them, when, until when they can be undone and whether they still can. The kid themselves and administrators may read
  it, like the status, so the TV can show Undo after a page reload.
- **"With a parent grant"**: a bonus given through a parent grant is recorded as "{kid} with a parent grant" (as Reset
  today logs it), so the list does not claim the kid gave themselves time. Bonuses stored before this feature keep
  what they had.
- **End break**: ends the break that follows the sitting limit, at once. Used time, bonus and the day's allowance are
  untouched. When the kid is not in a break the call changes nothing and answers the status (200), so two TVs pressing
  End break at once both succeed and only one break is ended and logged.
- **The next sitting** counts from the kid's next playback, from zero, with the full sitting limit, exactly as after a
  break that ran out. Playing time from before End break never counts toward it.
- **Capability names**: `bonusUndo` and `breakEnd`, camelCase like the existing names (`watchTimeReset`).
  `ApiMinorVersion` becomes 2; `ApiVersion` stays 1.
- **Dashboard placement**: the kid editor's Screen time section, next to Add and Reset today (where bonus minutes
  already live). The Kids page is unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A grown-up undoes a bonus given by mistake (Priority: P1)

On the kid's TV a grown-up unlocks "Add time", taps 60 minutes instead of 15, and sees "Undo" next to the bonus they
just gave. They tap Undo: the bonus is gone, the kid's time left goes back to what it was, and they give 15.

**Why this priority**: the widget's buttons are big and close together; a wrong amount is easy to give and today can
only be fixed by resetting the whole day.

**Independent Test**: as admin give alice 30 minutes; the answer carries a bonus id; undo it: the status shows the
bonus 30 minutes lower; undo again: 404.

**Acceptance Scenarios**:

1. **Given** a kid, **When** a grown-up gives a bonus, **Then** the answer is the new status plus the bonus's id and
   until when it can be undone.
2. **Given** a bonus given less than 10 minutes ago today, **When** an administrator or a parent grant holder undoes it
   by id, **Then** it is removed, the answer is the kid's new status, and the server log names the bonus, its minutes,
   who gave it and who undid it.
3. **Given** two bonuses given on two TVs, **When** each TV undoes its own bonus, **Then** each removes only its own.
4. **Given** a bonus undone already, **When** it is undone again, **Then** 404 and nothing changes.
5. **Given** a bonus given more than 10 minutes ago, or on an earlier day, **When** someone tries to undo it, **Then**
   409 "too late" and nothing changes.
6. **Given** the kid's own token without a grant, **When** it tries to undo, **Then** 403 and nothing changes.
7. **Given** the kid has already used part of the bonus, **When** it is undone, **Then** the answer may say time is up,
   and (with enforcement on) the kid is refused as for any other time's up.
8. **Given** a bonus id that belongs to another kid, **When** it is undone through this kid, **Then** 404.

---

### User Story 2 - A grown-up ends a break so the kid can keep watching (Priority: P1)

The kid reached their sitting limit and is in the break. A grown-up decides to let them finish the episode, unlocks the
widget on the TV and taps "End break". The kid can play again straight away; today's used time and bonus are what they
were, and the next sitting starts fresh.

**Why this priority**: today the only way to lift a break early is Reset today, which also wipes the day's time and
bonus: far more than the grown-up meant.

**Independent Test**: with a 5-minute sitting limit and a 1-minute break, play 5 minutes; while in the break, end it as
admin: the status has no break, no denial, the same used time and bonus; playback is allowed at once.

**Acceptance Scenarios**:

1. **Given** a kid in the break after the sitting limit, **When** an administrator or a parent grant holder ends it,
   **Then** the break is over, the kid may play at once, used time and bonus are unchanged, the answer is the new
   status, and the server log names the kid, who ended it and how long was left.
2. **Given** the break was ended, **When** the kid plays again, **Then** a new sitting starts at zero with the full
   sitting limit.
3. **Given** a kid who is not in a break (no sitting limit, still within it, or the break already over), **When**
   someone ends the break, **Then** 200 with the status, nothing changes and nothing is logged.
4. **Given** the kid's time for the day is up, **When** the break is ended, **Then** the kid is still refused for time;
   ending a break gives no screen time.
5. **Given** the kid's own token without a grant, **Then** 403; a user who is not a kid, **Then** 404.

---

### User Story 3 - Clients discover the new actions (Priority: P2)

A client reads `GET /Kids/Info` and shows Undo and End break only when the server offers them.

**Independent Test**: `GET /Kids/Info` shows `ApiVersion` 1, `ApiMinorVersion` 2 and `bonusUndo` and `breakEnd` in
`Features`.

**Acceptance Scenarios**:

1. **Given** this plugin version, **Then** `Features` ends with `bonusUndo`, `breakEnd` and `ApiMinorVersion` is 2.
2. **Given** an older plugin, **Then** these names are missing and clients hide the actions.

---

### User Story 4 - The dashboard shows today's bonuses and the break (Priority: P2)

In the kid editor's Screen time section a parent sees today's bonuses ("15 min at 18:02 by mum") with an Undo button
on those still inside the window, and an "End break" button while the kid is in a break.

**Independent Test**: give a bonus in the editor: it appears in the list with Undo; Undo: confirmation toast, the line
goes and the usage line drops; put the kid in a break: End break shows; press it: the sitting line no longer says
"on a break".

**Acceptance Scenarios**:

1. **Given** today's bonuses, **When** the editor loads or a bonus is added, **Then** each is listed with minutes, time
   (server clock) and who gave it, newest first.
2. **Given** a bonus inside the window, **Then** it has Undo; outside it, no button and the text says it can no longer
   be undone.
3. **Given** Undo is pressed, **Then** the bonus is removed, a toast confirms it and the usage line follows; if it
   became too late meanwhile, a message says so and the list refreshes.
4. **Given** the kid is in a break, **Then** End break is shown next to Reset today; pressing it ends the break, a toast
   confirms it and the status refreshes. Otherwise End break is hidden.
5. **Given** names with HTML, **Then** they are shown as text.

### Edge Cases

- A bonus given at 23:58 with the day starting at midnight: undo is possible until the day ends, not 10 minutes.
- Reset today removed the day's bonuses: undoing one of them answers 404.
- Two grown-ups undo the same bonus at once: one gets 200, the other 404.
- The kid stopped for the sitting limit with enforcement off: End break still ends the break the kids app shows.
- The server restarted during a break: sittings live in memory, so there is no break to end (200, nothing changes).
- A playback that kept going during the break (direct play that could not be stopped): after End break its next
  counted interval starts the new sitting.
- A bonus id that is not a number: 400 from the route; a number that is no bonus: 404.

## Requirements *(mandatory)*

### Functional Requirements

**Undo a bonus**

- **FR-001**: Each bonus MUST have an id that is unique and never reused on the server.
- **FR-002**: `POST /Kids/Users/{id}/WatchTime/Bonus` MUST answer, with the status it already returns, the new bonus's
  id (`BonusId`) and until when it can be undone (`BonusUndoUntil`).
- **FR-003**: `GET /Kids/Users/{id}/WatchTime/Bonus` MUST list the kid's bonuses of the current day (id, minutes, who
  gave them, when, until when and whether they can be undone), newest first; readable by the kid and administrators.
- **FR-004**: `DELETE /Kids/Users/{id}/WatchTime/Bonus/{bonusId}` MUST remove that bonus when it belongs to the kid,
  is of the current day and was given at most 10 minutes ago, and answer the kid's new status.
- **FR-005**: Only administrators, API keys and parent grant holders may undo (403 otherwise); a user who is not a kid
  or a bonus not found for that kid is 404; a bonus past its window or day is 409. Checks run in that order.
- **FR-006**: Each undo MUST be logged with the kid, the bonus id and minutes, who gave it and who undid it.
- **FR-007**: After an undo the kid's limits MUST follow at once (time's up may come back).
- **FR-008**: A bonus given through a parent grant MUST be recorded as "{name} with a parent grant".

**End a break**

- **FR-009**: `POST /Kids/Users/{id}/WatchTime/Break/End` MUST end the kid's current break after the sitting limit and
  answer the new status; used time, bonus and allowance MUST NOT change.
- **FR-010**: The next sitting MUST start at the kid's next playback, from zero.
- **FR-011**: When the kid is not in a break, the call MUST change nothing and answer 200 with the status.
- **FR-012**: Same access rules as undo (403, then 404); an ended break MUST be logged with the kid, who ended it and
  the time that was left.

**Discovery and release**

- **FR-013**: `GET /Kids/Info` MUST report `ApiMinorVersion` 2 and list `bonusUndo` and `breakEnd` after `avatar`.
- **FR-014**: The contract MUST be documented in `specs/013-bonus-undo-break/contracts/`.
- **FR-015**: The plugin version MUST be 0.0.9.0 with a changelog; nothing is published or tagged.
- **FR-016**: No existing endpoint changes behaviour other than the additions above.

**Dashboard**

- **FR-017**: The kid editor MUST list today's bonuses with Undo inside the window, and show End break while the kid is
  in a break, in the existing Screen time section's style; every name is inserted as text.

### Key Entities

- **Bonus**: id, kid, day, minutes, given by, given at; can be undone until min(given at + 10 minutes, start of next
  day).
- **Break** (in memory, not stored): the wait after the sitting limit; ending it forgets the current sitting.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A wrong bonus is undone in 1 action on the device that gave it, within 10 minutes.
- **SC-002**: When two TVs undo their own bonuses at the same moment, 100% of the time each removes only its own.
- **SC-003**: 0 undos succeed more than 10 minutes after the bonus, or on a later day.
- **SC-004**: After End break the kid can play at once, and the day's used time and bonus are identical before and
  after.
- **SC-005**: 100% of undo and End break attempts with a kid's own token are refused.

## Assumptions

- **Window (default)**: 10 minutes, fixed (not a setting), and capped at the end of the kid's day.
- **Undo removes the bonus** (no "undone" record kept); the log keeps the history.
- **Ids (default)**: integers, made never-reused by a database change; existing bonuses keep their ids.
- **End break when not in a break (default)**: 200, idempotent, not logged.
- **Next sitting (default)**: from the next playback, from zero (see Clarifications).
- **Capabilities (default)**: `bonusUndo`, `breakEnd`; `ApiMinorVersion` 2.
- **Dashboard (default)**: kid editor only; Undo needs no confirmation (it is itself the correction, inside a short
  window); End break needs none either (it only gives).
- **Constitution II**: undo lowers a bonus, not a day's usage, and only within 10 minutes of giving it; ending a break
  lowers nothing. No amendment is needed.
- Out of scope: undo outside the window, editing a bonus's minutes, ending a break for "outside the allowed hours"
  (Jellyfin's schedule), a configurable window, the Kids page.
