# Feature Specification: Kids allowance tracking

**Feature Branch**: `003-kids-allowance-tracking` (stacked on feature 2, commit `ebd505c`)

**Created**: 2026-10-18

**Status**: Draft

**Input**: User description: "Feature 3 kids-allowance-tracking (docs/plan.md §7 row 3, §3.1, §2.3, §4.1): count each
kid's daily screen time on the server from Jellyfin playback events across all clients (PlaybackTracker: wall-clock
deltas, paused time not counted, automated ticks after a dead client ignored), daily usage per kid per day with a
configurable "day starts at" hour in server local time, a daily allowance in the kid profile, bonus time added by a
parent (admin or grant), GET /Kids/Users/{id}/Status for clients, POST WatchTime/Report add-only and idempotent for
offline playback, allowance and today's usage in the dashboard, and a prune task. Acceptance: 10 min of playback from
jellyfin-web counts within ±30 s; paused time is not counted; two concurrent devices both count; a client report
cannot reduce usage; the day resets at the configured hour. Stopping playback is feature 4."

Research input: [docs/plan.md](../../docs/plan.md) §2.3 (sessions and playback events), §3.1 (counting), §4.1
(client contract: `Status`, `WatchTime/Report`, `WatchTime/Bonus`, `Admin/Overview`), §7 (feature 3) and §8 (open
questions 1, 5 and 6). Builds on [feature 1](../001-kids-plugin-foundation/spec.md) (kid profiles) and
[feature 2](../002-kids-parent-lock/spec.md) (parent grants).

## Clarifications

### Session 2026-10-18

- Q: Is one daily allowance per kid enough, or do weekdays need their own values? → A: One allowance for every day
  (0–1440 minutes, none means unlimited). Weekday-specific values may be added later as optional fields.
- Q: What happens when an offline report covers time the server also counted? → A: The client is trusted: every valid
  segment is added, and the contract tells clients to report only playback the server did not see.
- Q: Does playback count when the kid is an additional user in someone else's session? → A: No. Only playback in
  sessions the kid owns counts.
- Q: Does the status report allowed hours (`withinSchedule`, `nextAllowedAt`) now? → A: No; they come with feature 5.
- Q: Can a parent lower a kid's usage for today? → A: No. Parents add bonus time instead; usage is never lowered.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The server counts a kid's watching time on every client (Priority: P1)

A kid watches cartoons on the living-room TV with Wholphin, then on a tablet with the Jellyfin app, then in a browser.
The server adds up the time they spent actually watching, whichever app they used, so the household sees one honest
number for today. Time on pause does not count, and a client that crashed or lost its connection does not keep
"watching" for minutes after it went quiet.

**Why this priority**: Every other part of the feature (the allowance, the status clients show, the dashboard) depends
on a trustworthy count. It is also the value of doing this on the server: a limit that only one app counts is not a
limit (constitution II).

**Independent Test**: On the dev server, mark `alice` as a kid, play a video for 10 minutes in jellyfin-web signed in as
`alice`, pause for 3 minutes in the middle, and read her usage: it is 10 minutes within 30 seconds, with the pause not
counted.

**Acceptance Scenarios**:

1. **Given** a kid plays a video for 10 minutes in any client that reports playback to the server, **When** their usage
   for today is read, **Then** it is 10 minutes, within 30 seconds.
2. **Given** a kid pauses for 3 minutes during playback, **When** their usage is read, **Then** the paused time is not
   included.
3. **Given** a kid plays on two devices at the same time for 10 minutes, **When** their usage is read, **Then** both
   count: the usage is 20 minutes, within 1 minute.
4. **Given** a client stops reporting during playback (crash, network loss), **When** the server has had no report from
   it for more than a minute, **Then** no further time is counted for that playback, even though the server itself
   keeps the session alive for longer.
5. **Given** a user who is not a kid, **When** they watch, **Then** nothing is counted for them.
6. **Given** a kid added as an additional user to a grown-up's session, **When** that session plays, **Then** nothing
   is counted for the kid.
7. **Given** the server restarts during a kid's playback, **When** usage is read afterwards, **Then** the time counted
   before the restart is kept, losing at most one minute.

---

### User Story 2 - A kid's device shows how much time is left today (Priority: P1)

Wholphin (or any client) asks the server how a kid is doing today: how much they have watched, how much their daily
allowance is, any bonus time a grown-up added, how much is left, and whether time is up. The client polls about once a
minute while kids mode is open, and shows "10 minutes left" warnings before the end.

**Why this priority**: This is what clients consume. Without it, the count only lives on the server. Together with
Story 1 it is the minimum that replaces Wholphin's device-only timer for the daily limit.

**Independent Test**: Give `alice` a 30-minute allowance, let her watch 25 minutes, and read her status as `alice`: 5
minutes left, not time up. Watch 5 more minutes: 0 left, time up.

**Acceptance Scenarios**:

1. **Given** a kid with a daily allowance, **When** the kid (with their own credentials) or an administrator reads the
   kid's status, **Then** it shows today's date, the time used, the allowance, the bonus added today, the time
   remaining, whether time is up, the server's current time, and when the next day starts.
2. **Given** a kid without an allowance (unlimited), **When** their status is read, **Then** it shows the time used and
   no allowance, no remaining time, and never "time up".
3. **Given** a kid whose used time reaches allowance plus bonus, **When** their status is read, **Then** remaining is 0
   and time is up.
4. **Given** another non-administrator user, **When** they read a kid's status, **Then** the request is refused.
5. **Given** a kid outside their allowed hours in Jellyfin, **When** their device reads their status, **Then** the read
   still succeeds.
6. **Given** a user who is not a kid, **When** anyone reads their status, **Then** the answer is "not found".

---

### User Story 3 - A parent sets each kid's daily allowance (Priority: P1)

A grown-up decides that a kid may watch 60 minutes a day. They set it from the Jellyfin dashboard's kid editor, or from
the parent menu on a TV (with the parent lock). Setting no allowance means unlimited, which is the default, so existing
kids are not suddenly limited when the plugin is updated.

**Why this priority**: Without an allowance there is nothing to reach; the count would only be informational.

**Independent Test**: Set `alice`'s allowance to 45 minutes in the dashboard editor; read her profile and status as
`alice` and see 45 minutes. Clear it and see unlimited.

**Acceptance Scenarios**:

1. **Given** a kid, **When** an administrator or a caller with a parent grant saves an allowance of N minutes,
   **Then** the profile holds N and the status uses it from the next read.
2. **Given** the allowance field, **When** a value outside 0–1440 minutes is saved, **Then** it is rejected naming the
   field.
3. **Given** an allowance of 0, **When** the kid's status is read, **Then** time is up from the start of the day (until
   a bonus is added).
4. **Given** a kid's own credentials without a grant, **When** they try to change their allowance, **Then** it is
   refused (feature 1's rule).
5. **Given** the allowance is lowered below what the kid has already used today, **When** the status is read,
   **Then** remaining is 0 and time is up; usage is not changed.

---

### User Story 4 - A parent adds bonus time for today (Priority: P2)

The kid finished their homework, so a grown-up gives them 20 more minutes today. They do it from the TV's parent menu
(after unlocking) or from the dashboard. The bonus only applies to the current day.

**Why this priority**: Very common in practice and part of plan §4.1, but the allowance works without it.

**Independent Test**: With `alice` at time up, add 15 minutes of bonus with a parent grant from her TV; her status shows
15 minutes remaining. The next day the bonus is gone.

**Acceptance Scenarios**:

1. **Given** a kid, **When** an administrator or a grant holder adds a bonus of N minutes (1–240), **Then** today's
   bonus grows by N and the answer is the kid's new status.
2. **Given** several bonuses on the same day, **When** the status is read, **Then** they add up.
3. **Given** a bonus added today, **When** the next day starts, **Then** it no longer applies.
4. **Given** a kid's own credentials without a grant, **When** they try to add bonus time, **Then** it is refused and
   nothing changes.
5. **Given** a bonus outside 1–240 minutes, **When** it is added, **Then** it is rejected naming the field.

---

### User Story 5 - Playback that the server never saw can still be counted (Priority: P2)

A kid watched downloaded episodes on a tablet in the car, with no connection to the server. When the tablet is back
online, Wholphin reports those watching segments, and the server adds them to the right days. A report can only add
time: a client can never lower a kid's usage, and sending the same report twice counts it once.

**Why this priority**: It closes the offline gap, but only Wholphin with downloads needs it today.

**Independent Test**: As `alice`, report a 10-minute segment from earlier today with a new id: today's usage grows by 10
minutes. Send the same report again: no change. Report a negative or oversized segment: rejected.

**Acceptance Scenarios**:

1. **Given** a kid, **When** their own device reports a segment (an id, a start time and a duration), **Then** the
   duration is added to the usage of the day the segment started in.
2. **Given** a segment id already reported, **When** it is reported again, **Then** nothing more is added.
3. **Given** a segment with a negative or zero duration, a duration over 12 hours, or a start time more than 5 minutes
   in the future, **When** it is reported, **Then** it is rejected and nothing from that request is added.
4. **Given** an administrator or another user, **When** they report segments for a kid, **Then** the request is
   refused: only the kid's own device reports its own offline watching.
5. **Given** any report, **When** it is processed, **Then** the kid's usage for any day is never lower than before.

---

### User Story 6 - The day resets at the household's chosen hour (Priority: P2)

Some families count the day from midnight; others from 4 a.m. so a late film does not eat into tomorrow. The
administrator sets the hour the day starts, in the server's local time. Each kid's usage starts again from zero at that
hour, and bonuses expire.

**Why this priority**: Decided in plan.md (Decisions, 2026-10-17); midnight is a working default without it.

**Independent Test**: Set "day starts at" to the next hour; at that hour `alice`'s status shows a new date with 0 used.

**Acceptance Scenarios**:

1. **Given** "day starts at" is H (0–23, default 0), **When** the server's local time passes H:00, **Then** a new day
   begins for every kid: used time and bonus start at 0.
2. **Given** playback that spans the start of a day, **When** usage is read, **Then** the time before H:00 counts for
   the old day and the time after for the new one.
3. **Given** the administrator changes H, **When** the status is read, **Then** the new boundary applies from then on;
   already recorded days are not recalculated.

---

### User Story 7 - The dashboard shows allowances and today's usage (Priority: P2)

On the Kids page an administrator sees, for each kid, today's usage against their allowance, and can add bonus time.
In the kid editor they set the daily allowance. The Kids page also holds the "day starts at" setting.

**Why this priority**: Parents need one place to see and set this without a TV; the API covers it otherwise.

**Independent Test**: Open the Kids page: `alice` shows "12 of 45 minutes today"; add 10 minutes of bonus; the row shows
the new remaining time. Edit `alice`, set 60 minutes, save; the row shows 60.

**Acceptance Scenarios**:

1. **Given** kids with and without allowances, **When** the Kids page loads, **Then** each kid row shows today's usage,
   and the allowance and remaining time when there is one.
2. **Given** a kid row, **When** the administrator adds bonus minutes, **Then** the row shows the new remaining time.
3. **Given** the kid editor, **When** the administrator sets, changes or clears the daily allowance and saves, **Then**
   the profile holds it, under the same revision rules as feature 1.
4. **Given** the "day starts at" setting, **When** the administrator changes it and saves, **Then** it applies to the
   next status reads.
5. **Given** the page is fetched without signing in, **When** its content is inspected, **Then** it contains no usage
   data.

---

### Edge Cases

- A kid is unmarked: their usage history is kept unless the parent asks to purge it (plan §4.1 `?purge=true`), so
  marking them again does not reset today's count. Deleting the Jellyfin user removes their usage.
- A user is marked as a kid while already watching: counting starts from the next playback report.
- Playback by a kid who is an additional user in someone else's session (a shared session): not counted for the kid;
  only the session's owner is counted, and only if the owner is a kid.
- The server clock jumps (time zone or daylight-saving change): no interval longer than the expected report cadence is
  ever counted from a single gap, so a jump does not add or remove hours. On the daylight-saving day the day is 23 or
  25 hours long.
- Two playback reports arrive out of order or very close together: the same wall-clock time is never counted twice for
  one playback.
- A playback session lasts many hours (a film marathon): counted continuously; usage can exceed the allowance (this
  feature does not stop playback; feature 4 does).
- The data store is unavailable: the counter keeps time in memory and retries; the status answers with a server
  error rather than "0 used".
- Old usage: days older than the retention period (90 days) are removed by a scheduled task.
- Music and other audio played by a kid: counted like video, since it is playback the server sees.
- An allowance set on a kid who is a Jellyfin administrator: counted and reported, but feature 4's blocking will never
  apply to administrators (constitution III); the Kids page already warns about admin kids.

## Requirements *(mandatory)*

### Functional Requirements

**Counting**

- **FR-001**: The server MUST count watching time for every kid from the playback reports Jellyfin receives from any
  client, per kid and per day.
- **FR-002**: Only playing, non-paused time MUST count. The time between two reports of the same playback MUST be
  counted only when the playback was playing in between, and never more than about twice the normal report interval
  from a single gap.
- **FR-003**: Time synthesised by the server after a client stopped reporting MUST stop counting no later than 60
  seconds after the client's last real report.
- **FR-004**: Simultaneous playback on several devices MUST all count (summed), each playback counted once.
- **FR-005**: Only the user who owns a playback session MUST be counted. A kid listed as an additional user of
  another user's session MUST NOT be counted for that playback.
- **FR-006**: Users who are not kids MUST NOT be counted.
- **FR-007**: Counted time MUST be stored durably at least every 60 seconds and when playback stops, so a restart
  loses at most about a minute.
- **FR-008**: A day MUST run from "day starts at" H:00 in the server's local time to the next H:00. Playback spanning a
  boundary MUST be split between the two days. H is an administrator setting (0–23, default 0).

**Allowance and bonus**

- **FR-009**: A kid profile MUST hold an optional daily allowance in minutes (0–1440). None means unlimited and is the
  default; existing profiles get none.
- **FR-010**: Changing the allowance MUST follow feature 1's profile rules: administrator or parent grant, revision
  check, validation naming the field.
- **FR-011**: An administrator or a parent-grant holder MUST be able to add bonus minutes (1–240 per request) to a
  kid's current day. Bonuses on the same day add up; they apply only to that day.
- **FR-012**: A kid's own credentials without a grant MUST NOT change the allowance or add bonus time.
- **FR-012a**: No one, administrators included, MUST be able to lower a day's recorded usage. Bonus time is the way to
  give time back.

**Status**

- **FR-013**: The kid themselves and administrators MUST be able to read a kid's status: the day's date, time used,
  allowance (if any), bonus today, time remaining (if limited), whether time is up, the server's current time, when
  the next day starts, and the warning thresholds clients should use (5 and 1 minutes).
- **FR-014**: Time is up when an allowance exists and used ≥ allowance + bonus; remaining is never negative.
- **FR-015**: Status reads MUST work outside the kid's allowed hours; other non-administrator users MUST be refused as
  forbidden; non-kids are "not found".
- **FR-016**: When the data store cannot be read, the status MUST fail with a server error, never report 0 used.

**Offline reports**

- **FR-017**: A kid's own credentials MUST be able to report offline segments (id, start time, duration). Each segment
  adds its duration to the day it started in.
- **FR-018**: Reports MUST only add usage: no segment can lower any day's usage. A segment id MUST count at most once
  per kid.
- **FR-019**: Segments with a duration ≤ 0 or > 12 hours, a start time more than 5 minutes in the future (allowed
  clock skew), or a start older than the retention period MUST be rejected; one invalid segment rejects the whole
  request and nothing is added. At most 100 segments per request.
- **FR-020**: Only the kid themselves may report their segments; administrators and other users MUST be refused.

**Retention and cleanup**

- **FR-021**: Usage, bonus and reported-segment records older than 90 days MUST be removed by a scheduled task that
  administrators can also run on demand.
- **FR-022**: Deleting a Jellyfin user MUST remove their usage records. Unmarking a kid MUST keep them unless the
  parent asks to purge.

**Plugin information**

- **FR-023**: The anonymous plugin information MUST add the `allowance` capability.

**Dashboard**

- **FR-024**: The Kids page MUST show, per kid, today's usage and, if limited, the allowance and remaining time; it
  MUST let an administrator add bonus minutes per kid.
- **FR-025**: The kid editor MUST let an administrator set, change or clear the daily allowance, with the same
  validation and conflict handling as the other settings.
- **FR-026**: The Kids page MUST let an administrator set "day starts at" (0–23).
- **FR-027**: The page content served by Jellyfin MUST NOT include usage data.

### Key Entities

- **Daily usage**: per kid and per day (the day's date under the "day starts at" rule): seconds used. Only grows.
- **Bonus**: per kid and per day: minutes added, who added them (user or "API key"), when.
- **Reported segment**: per kid: the client's segment id, start time and duration, kept to make reports idempotent.
- **Kid profile** (from feature 1): gains the optional daily allowance in minutes.
- **Kid status** (not stored): date, used, allowance, bonus, remaining, time up, server time, next day start, warning
  thresholds.
- **Household settings** (administrator): "day starts at" hour.
- **Plugin information** (from feature 1): gains the `allowance` capability.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 10 minutes of playback in jellyfin-web is counted as 10 minutes within ±30 seconds, in 100% of runs.
- **SC-002**: Paused time adds at most 5 seconds to usage per pause.
- **SC-003**: Two devices playing for 10 minutes at the same time count 20 minutes within ±1 minute.
- **SC-004**: A client that stops reporting adds at most 60 seconds after its last report.
- **SC-005**: No report from a kid's device ever lowers any day's usage, and a repeated segment never counts twice, in
  100% of attempts.
- **SC-006**: At the configured "day starts at" hour, every kid's status shows a new date with 0 used and no bonus,
  within 1 minute.
- **SC-007**: A parent can add bonus time from a TV (with the parent lock) and the kid's status reflects it on the next
  read.
- **SC-008**: An administrator can set a kid's allowance and see their usage against it from the dashboard in under 1
  minute.
- **SC-009**: After a server restart during playback, at most 1 minute of counted time is lost.

## Assumptions

- Only watching counts; browsing time is not screen time in v1 (plan §8 question 1, proposal "no").
- One allowance per kid for every day of the week (plan §8 question 5); weekday-specific allowances are out of scope.
- Only the session owner is counted (clarification Q3). This differs from plan §3.1 as first written, which has been
  updated: a kid added to a grown-up's session for watching together is not charged.
- Concurrent playback is summed, not merged into one timeline (plan §7 acceptance "two concurrent devices both count").
- Offline reports are trusted to describe only playback the server did not see; the server does not cross-check them
  against its own counting. A misbehaving client can only add time against itself.
- The allowance range 0–1440 and bonus range 1–240 follow plan §4.1; the 12-hour segment cap, 100 segments per
  request and 90-day retention are new defaults.
- "Day starts at" is a household-wide administrator setting in the plugin's dashboard settings, in server local time;
  per-kid time zones are out of scope (plan Decisions).
- Status reads are self or administrator; a parent on a kid's TV reads it as the kid. Bonus and allowance changes use
  feature 2's grants.
- This feature counts and reports only. Warnings, stopping playback and refusing new playback are feature 4; the
  status's time-up flag is what feature 4 and clients act on.
- Allowed hours (`withinSchedule`, `nextAllowedAt` in plan §4.1's status) belong with schedule editing in feature 5 and
  are not part of this feature's status (clarification Q4). Adding them later is an additive change.
- The per-sitting device timer stays in Wholphin (plan §1 table); the profile's `sessionDefaultMinutes` is out of scope.
- The dashboard list keeps using Jellyfin's user list plus a per-kid status read; a separate administrator overview
  endpoint is added only if planning shows the list needs it.
