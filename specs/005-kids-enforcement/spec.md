# Feature Specification: Kids enforcement on every client

**Feature Branch**: `008-kids-enforcement` (from `main`, plugin 0.0.4.0)

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "Feature 5 kids-enforcement (spec 005): kids enforcement on every client. Today only
Wholphin stops kids when time is up; the server only counts. Make the server enforce it for jellyfin-web and every
other client. Scope (docs/review-playback-authorization-hook.md §F): (1) a blocking spike first: does aborting in-flight
direct-play responses plus refusing the next range request stop jellyfin-web and ExoPlayer, and how fast; recorded in
research.md. (2) a filter attached only to the matched playback and stream actions: PlaybackInfo GET and POST, HLS
master, variant and segment routes, Videos/Audio stream, UniversalAudio, Items/{id}/File, Items/{id}/Download,
LiveStreams/Open and live TV files; PlaybackInfo answered with ErrorCode NotAllowed, the others with 403 and
X-Application-Error-Code PlaybackNotAllowed (never ParentalControl). (3) an in-memory deny set driven by the playback
tracker and usage service on the 1 s progress ticks. (4) the enforcement chain: a warning before time's up, a time's-up
alert, Stop, the transcode kill, abort, repeated while the client keeps playing. (5) the same when the allowed-hours
window closes, with the same rule as Jellyfin's parental schedule check. (6) the per-kid SessionLimitMinutes enforced
server-side for any client. (7) a startup self-check that the matched actions exist, reported in /Kids/Info. (8) behind
a config flag, off by default; admins and API keys never blocked; disabling the plugin removes all blocking. No
dashboard page edits (a UI redesign is pending elsewhere). Version 0.0.5.0."

Research input: [docs/review-playback-authorization-hook.md](../../docs/review-playback-authorization-hook.md) §A, §D,
§F and §G; [docs/plan.md](../../docs/plan.md) §3.2–3.4 and §7 row 4;
[docs/research-jellyfin-kids-landscape.md](../../docs/research-jellyfin-kids-landscape.md) §3;
[docs/native-core-design.md](../../docs/native-core-design.md) §4 and §7;
[docs/repro-schedule-playback.md](../../docs/repro-schedule-playback.md) (stock 12.2 does not stop running playback at
the end of allowed hours; measurements of Stop, the transcode kill and a socket-less direct-play client). Builds on
[feature 3](../003-kids-allowance-tracking/spec.md) (counting, status, bonus) and
[feature 4](../004-kids-session-limit/spec.md) (the stored session limit).

## Clarifications

### Session 2026-10-19

Answers from the user, relayed by the `wholphin-kids` session.

- Q: What counts as one "sitting" when the server enforces the session limit? → A: Per kid across all devices: the
  kid's playing time since their last break, a break being a stretch with nothing playing on any device.
- Q: How long must the break be, and what resets a sitting? → A: A household setting in the plugin configuration,
  default 15 minutes: a sitting resets after that long with nothing playing; after reaching the limit the kid is refused
  until the break has passed. The dashboard control is a follow-up.
- Q: Should Wholphin sessions also get the server's messages and Stop? → A: Yes, every client gets them; Wholphin hides
  duplicates using the denial reason in Status.
- Q: Should kids be warned before their allowed hours end? → A: Yes, at 5 and 1 minutes before the window closes.
- Q: When time runs out mid-item, stop at once or let it finish? → A: Stop at once, after the time's-up message and
  about 5 seconds.
- Follow-up (same relay): Status exposes the sitting state (used, remaining, break remaining) so Wholphin can show it
  and stop counting sittings locally; it is an addition to API v1, with a new `sitting` capability in Info.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Time's up stops playback on any client (Priority: P1)

A kid has a 60-minute daily allowance and watches on the family laptop in a browser (jellyfin-web), not on Wholphin.
Five minutes before the end, a short notice appears. When the allowance is used up, a "Time's up" message appears and
the video stops, whichever client plays it. If the client ignores the stop request, the server cuts the stream it is
delivering, and keeps stopping it for as long as the client reports playback.

**Why this priority**: It is the point of the feature. Without it, a kid bypasses the allowance by switching from
Wholphin to the browser.

**Independent Test**: Give `alice` an allowance she has nearly used, enable enforcement, play a video in jellyfin-web
as `alice` and wait: the warning, then the time's-up message, then the player stops within the target time.

**Acceptance Scenarios**:

1. **Given** enforcement is on and a kid has 5 minutes or less left, **When** they are playing, **Then** each of their
   playing sessions receives one short notice at 5 minutes and one at 1 minute left (only the ones still ahead). The
   current item is not allowed to finish: when time is up, playback stops at once, after the message.
2. **Given** a kid playing on any client, **When** their allowance plus today's bonus is used up, **Then** within a
   few seconds every session they own receives a time's-up message that stays until dismissed, followed by a stop
   request.
3. **Given** the stop was sent, **When** the playback was being transcoded, **Then** the transcode ends and cannot be
   restarted by the client.
4. **Given** the stop was sent, **When** the playback is a direct stream of the original file, **Then** the stream in
   progress is cut and the next request for more of it is refused (see the spike, User Story 6).
5. **Given** a client that ignores the stop request and keeps reporting playback, **When** it reports again, **Then**
   the server repeats the stop, the transcode end and the stream cut, at most once every 10 seconds.
6. **Given** a kid whose time is up, **When** a grown-up adds bonus time, **Then** the kid can play again at once,
   without restarting anything.
7. **Given** a kid whose time is up, **When** the next day starts, **Then** the kid can play again.

---

### User Story 2 - New playback is refused while time is up (Priority: P1)

Once time is up, pressing play on any client, or opening any media link directly, is refused with a clear reason
instead of a generic error or a forced sign-out.

**Why this priority**: Stopping alone is useless if the kid can press play again straight away.

**Independent Test**: With `alice` out of time, ask the server for playback information, an HLS playlist, a segment,
a direct stream and the original file as `alice`: each is refused. Do the same as an administrator: each works.

**Acceptance Scenarios**:

1. **Given** a kid whose time is up, **When** a client asks how to play an item, **Then** the answer is a normal
   answer that says playback is not allowed, so jellyfin-web shows "Playback of this media is not allowed".
2. **Given** a kid whose time is up, **When** a client requests a playlist, a segment, a stream, the original file, a
   download, a live stream opening or a live TV stream, **Then** it is refused as forbidden with the error code
   `PlaybackNotAllowed`, and never with the code that makes jellyfin-web sign the user out.
3. **Given** a kid whose time is up, **When** they use any other part of Jellyfin (browse, search, images, subtitles,
   playback reports, the `/Kids` endpoints), **Then** it works as before.
4. **Given** an administrator or an API key, **When** they play anything at any time, **Then** nothing is refused,
   even if the administrator account is marked as a kid.
5. **Given** a kid with time left, a user who is not a kid, or enforcement turned off, **When** they play, **Then**
   nothing changes from today.
6. **Given** a stream request that carries no user identity, **When** the device it names is currently used by a
   denied kid, **Then** it is refused too (best effort).

---

### User Story 3 - Bedtime stops running playback (Priority: P2)

A kid's allowed hours end at 20:00 (Jellyfin's own schedule for the user). Jellyfin already refuses most new requests
after 20:00, but a video that is already streaming keeps going. With enforcement on, the server stops it at 20:00 the
same way it stops for time's up, with a message that says bedtime rather than time's up. As for the allowance, it
warns 5 and 1 minutes before the window closes.

**Why this priority**: It closes a known gap in Jellyfin itself, at little extra cost, but the allowance comes first.

**Independent Test**: Give `alice` a schedule that ends two minutes from now, start a direct-play video in
jellyfin-web, and wait: at the boundary the message appears and the player stops.

**Acceptance Scenarios**:

1. **Given** a kid with allowed hours, **When** the window closes while they are playing, **Then** within a few seconds
   they get a message and a stop, followed by the same transcode end, stream cut and repeats as in User Story 1.
2. **Given** a kid outside their allowed hours, **When** they ask to play, **Then** the request is refused like in
   User Story 2, including stream requests that Jellyfin itself does not check.
3. **Given** a kid with no schedule, **When** they play at any hour, **Then** nothing changes.
4. **Given** the schedule, **When** the plugin decides whether a kid is inside it, **Then** it reaches the same answer
   as Jellyfin's own check at that moment (same days, hours, inclusive ends and server time zone).

---

### User Story 4 - The sitting limit holds on every client (Priority: P2)

A kid's profile has a 30-minute sitting limit (feature 4). Wholphin already counts it on the TV; with enforcement on,
the server also enforces it for any client: after 30 minutes of watching in one sitting the kid gets the same warning,
message and stop, and cannot continue until they have taken a break.

**Why this priority**: The daily allowance is the main limit; the sitting limit is a refinement that until now only
Wholphin honoured.

**Independent Test**: Set `alice`'s sitting limit to 5 minutes, play in jellyfin-web for 5 minutes: she is stopped.
After the break, she can play again.

**Acceptance Scenarios**:

1. **Given** a kid with a sitting limit and time left today, **When** a sitting reaches the limit, **Then** they get
   the warning before, then the message, stop and refusal, as for time's up.
2. **Given** a kid stopped by the sitting limit, **When** the break has passed, **Then** they can play again, if time
   is left today.
3. **Given** a kid without a sitting limit, **When** they play, **Then** only the daily allowance and schedule apply.
4. **Given** a kid watching on two devices, one after the other with less than the break in between, **When** the
   plugin counts the sitting, **Then** both count towards the same sitting (a sitting is per kid, across devices).
5. **Given** a kid who has had nothing playing anywhere for the break length (household setting, default 15 minutes),
   **When** they play again, **Then** a new sitting starts from zero.
6. **Given** a kid with a sitting limit, **When** a client reads the kid's status, **Then** it sees the sitting time
   used and remaining, and the break time remaining when they are on a break after reaching the limit.

---

### User Story 5 - Parents stay in control of enforcement (Priority: P1)

Enforcement is off until an administrator turns it on, so updating the plugin changes nothing on its own. Parents and
API keys are never blocked. Turning enforcement off, disabling the plugin or removing it ends every refusal at once.
Clients can see whether enforcement is on and working.

**Why this priority**: A parental tool that can lock a household out, or that changes behaviour silently on update,
will be uninstalled (constitution III).

**Independent Test**: Update the plugin: no kid is refused. Turn enforcement on: an out-of-time kid is refused. Turn it
off: the same request works. Read the plugin information and see the enforcement state.

**Acceptance Scenarios**:

1. **Given** the plugin is updated to this version, **When** nothing is configured, **Then** enforcement is off and no
   request is refused or stopped.
2. **Given** enforcement is on, **When** an administrator turns it off, **Then** every refusal ends at once and no more
   stops are sent.
3. **Given** enforcement is on, **When** the plugin is disabled or removed and the server restarted, **Then** no
   request is refused.
4. **Given** the server starts, **When** the plugin checks that every playback route it must guard exists, **Then** the
   plugin information reports whether the guard is complete and which routes are missing, a warning is logged for each
   missing one, and the kid status says whether enforcement is on.
5. **Given** a failure inside the enforcement logic, **When** a request is checked, **Then** the request is allowed and
   the failure is logged, rate limited (fail open).

---

### User Story 6 - Proof that cutting a direct stream works (Priority: P1, blocking)

Before any feature depends on it, the team proves on the dev server, with real players, whether cutting a direct-play
stream in progress and refusing its next request actually stops jellyfin-web (browser) and an ExoPlayer-like client,
and how long they keep playing from their buffer.

**Why this priority**: The design depends on it (constitution VI: risky mechanisms are proven by a spike first). If it
does not work, the stop for direct play falls back to the client honouring Stop, and the limit is documented.

**Independent Test**: The spike result in `research.md` names the clients tried, the media, the measured time from cut
to the picture stopping, and the decision.

**Acceptance Scenarios**:

1. **Given** a kid direct-playing a file in a headless browser running jellyfin-web 12.2, **When** the server cuts the
   stream and refuses further requests while the page ignores the stop request, **Then** the time until playback
   stops is measured and recorded.
2. **Given** the same for an ExoPlayer-like range-request client (or its closest available stand-in), **Then** the
   result is measured and recorded, or the gap is named.
3. **Given** a client with no live connection to the server (so no stop request reaches it) that holds one long
   direct-play download open and makes range requests with the kid's token, **When** the kid is denied, **Then** the
   open download is cut and every later range request is refused; the time to the cut is recorded (the repro measured
   that stock 12.2 keeps serving such a client at full rate).
4. **Given** the results, **Then** research.md says whether the cut is part of the chain, with its expected delay.

### Edge Cases

- **Two devices at once**: the allowance runs out sooner; the cutoff follows the counted time, so both are stopped
  together.
- **Paused playback** uses no time and is not stopped by the allowance; it is stopped if it resumes after time is up.
- **Playing as an additional user** in someone else's session is not counted (feature 3 Q3) and not stopped; only the
  session owner is.
- **Bonus added while denied**: the refusal lifts at once; sessions already stopped stay stopped.
- **Allowance or sitting limit changed while playing**: the new value applies within a few seconds.
- **Day start while denied**: the refusal lifts at the next day start without waiting for an event.
- **Restart while a kid is out of time**: refusals are restored at startup, before kids can play.
- **The kids database cannot be read**: nothing is refused (fail open), and a warning is logged.
- **A kid who is also an administrator**: never blocked; the dashboard already warns about admin kids.
- **A stream URL without a token** and with no or another device's id cannot be tied to a kid: it is served (a
  documented limit, constitution II).
- **DLNA renderers and offline downloads** are out of reach (documented limits).
- **Clients without a live connection to the server** never receive messages or stop requests; refusals and stream
  cuts still apply.
- **A newer Jellyfin 12.x renames a guarded route**: the self-check reports it missing and enforcement reports itself
  degraded; matching by kind of response still catches renamed media routes where possible.
- **After the allowed hours end**, Jellyfin itself refuses the client's own "playback stopped" report and its request
  to end the transcode, so the client cannot clean up; the server-side end of playback (FR-013) does it instead
  (the repro measured ffmpeg lingering about 10 minutes on stock 12.2).
- **jellyfin-web on HLS** freezes on the last frame with no message when a segment is refused; the message and stop
  sent before the refusal are what explain it to the kid.
- **Wholphin** keeps its own time's-up screen; the server's message and stop arrive as well (see assumptions).

## Requirements *(mandatory)*

### Functional Requirements

**Spike**

- **FR-001**: Before the stream cut is relied on, the team MUST measure on the dev server whether cutting an in-flight
  direct stream and refusing its next request stops jellyfin-web 12.2 in a browser and an ExoPlayer-like client, and how
  quickly, and record the result and decision in research.md.

**Who is enforced**

- **FR-002**: Enforcement MUST apply only to kids (feature 1's kid list), only while the household's enforcement
  setting is on, and never to administrators or API keys.
- **FR-003**: The enforcement setting MUST default to off, and turning it off MUST end every refusal and stop
  immediately. Disabling or removing the plugin MUST remove all blocking.
- **FR-004**: A kid MUST be denied while any of these holds: today's allowance plus bonus is used up; they are outside
  their Jellyfin allowed hours; their current sitting has reached their sitting limit or they are within the break
  after it.

**Refusing playback**

- **FR-005**: For a denied kid, requests that negotiate playback MUST be answered with a normal playback answer that
  states "not allowed", with no media to play.
- **FR-006**: For a denied kid, requests that deliver or open media (HLS master, variant and live playlists and
  segments, video and audio streams, universal audio, the original file, downloads, live stream opening, live TV
  stream files and recordings, and the legacy HLS routes) MUST be refused as forbidden with the error code
  `PlaybackNotAllowed` and a reason (`timeUp`, `outsideSchedule` or `sessionLimit`). The code that makes jellyfin-web
  sign the user out MUST never be used.
- **FR-007**: All other requests (browsing, images, subtitles, trickplay, lyrics, playback reports, `/Kids`) MUST NOT
  be checked or slowed.
- **FR-008**: A request that names no user MUST be attributed to a kid only through the device it names, when that
  device is currently used by a kid; otherwise it is served.
- **FR-009**: Checking a guarded request MUST NOT read the database; the decision MUST come from memory.

**Deciding and stopping**

- **FR-010**: The denial MUST be decided from the playing time already counted by feature 3, within about one second of
  the limit being reached, with no timers armed per kid.
- **FR-011**: Each playing session of a kid MUST get a short notice at 5 and at 1 minute before the allowance, the
  sitting limit or the allowed hours end, each at most once per limit and threshold.
- **FR-012**: When a kid becomes denied, each session they own that is playing MUST receive, in order: a message that
  stays until dismissed and names the reason, a stop request a few seconds later, the end of its transcodes, the end of
  any live stream it opened, and the cut of the direct streams being delivered to it.
- **FR-013**: The stop request MUST be sent once per denial and session. After it, the server MUST end the session's
  playback on its own side (as Jellyfin's idle sweep does), so the server stops treating the session as playing, its
  transcode is no longer kept alive, and no further stop is triggered by the server's own progress ticks. Only a
  **real** new playback report from a denied kid's session (the client ignored the stop) MUST trigger the escalation
  again (stop, transcode end, stream cut), at most once every 10 seconds per session; a periodic sweep MUST catch
  sessions that are still shown as playing.
- **FR-013a**: Enforcement MUST log one information line per denial and per session stopped; repeats and refusals of
  individual requests MUST be logged at debug level only, so a client that keeps retrying cannot flood the log.
- **FR-014**: The allowed-hours rule MUST give the same answer as Jellyfin's own schedule check and MUST NOT copy or
  change the user's schedule.
- **FR-015**: A bonus, an allowance change, a sitting-limit change, unmarking a kid or the next day start MUST lift a
  denial that no longer applies, at once.
- **FR-016**: At startup, the denials of kids who are already out of time or outside their hours MUST be restored before
  their requests are checked.
- **FR-017**: Any failure in the enforcement logic MUST let the request through and be logged without flooding the
  log.

**Self-check and contract**

- **FR-018**: At startup the plugin MUST check that each playback route it must guard was found and guarded, log a
  warning for each missing one, and report the result in the anonymous plugin information as an `enforcement`
  object: whether the guard is `ok` or `degraded`, and the missing routes. Whether the household turned enforcement on
  is household data (constitution IV), so it is reported only in the signed-in kid status (FR-020), not here.
- **FR-019**: The plugin information MUST add the `enforcement` capability; the API version stays 1, since this only
  adds.
- **FR-020**: The kid status read by clients MUST say whether enforcement is on, and whether the kid is currently
  denied, why and until when, so clients such as
  Wholphin can show their own screen and hide the server's duplicate message.
- **FR-020a**: When the kid has a sitting limit, the status MUST include the sitting state: sitting time used and
  remaining, and the break time remaining after the limit is reached. These are optional fields added to API v1, and
  the plugin information MUST add the `sitting` capability.
- **FR-020b**: A sitting is per kid across all devices: playing time counted since the kid last had nothing playing
  anywhere for the break length. The break length is a household setting (default 15 minutes, 1–240), stored in the
  plugin configuration; after reaching the limit the kid is denied (`sessionLimit`) until the break has passed.
- **FR-021**: The setting MUST be stored in the plugin configuration and settable by an administrator through the
  plugin configuration API; dashboard controls are a follow-up (the page redesign is pending), and no dashboard page
  file is changed by this feature.

### Key Entities

- **Enforcement setting**: one household flag, off by default.
- **Denial**: per kid, in memory: the reason (`timeUp`, `outsideSchedule`, `sessionLimit`) and until when it holds.
- **Kid budget**: per kid, in memory: today's limit (allowance plus bonus, or unlimited), time used, and the sitting
  state; kept in step with feature 3's counting.
- **Sitting**: per kid, in memory: playing time since the last break, when anything last played, and, after the limit,
  when the break ends. Lost on restart (a restart counts as a break).
- **Break setting**: one household value in minutes, default 15.
- **Guard coverage**: which playback routes were found and guarded at startup, and which are missing.
- **In-flight delivery**: a direct stream being delivered to a kid's device, kept only while it streams, so it can be
  cut.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With enforcement on, a kid playing in jellyfin-web is stopped within 10 seconds of their allowance
  running out, for transcoded playback, and within the time measured by the spike for direct play.
- **SC-002**: With enforcement on, 100% of the guarded playback requests from an out-of-time kid are refused, and 0% of
  requests from administrators, API keys, non-kids and kids with time left are refused.
- **SC-003**: After a bonus is added, the kid's next play attempt succeeds within 2 seconds.
- **SC-004**: With enforcement off, or the plugin disabled, no request is refused.
- **SC-005**: A playing session that ignores stop requests receives a repeat at least every 10–15 seconds until it
  stops reporting.
- **SC-006**: Requests that are not guarded show no measurable added latency, and guarded requests from non-kids add
  under a millisecond.
- **SC-007**: The plugin information reports every guarded route as present on Jellyfin 12.2.

## Assumptions

- The server's own one-second progress tick for each playing session (plan §2.3) drives the decisions; no
  per-kid timers.
- Warnings at 5 and 1 minutes come from feature 3's `WarnAtSeconds`; notices disappear on their own after a few
  seconds, while the time's-up message stays until dismissed.
- The wait between the time's-up message and the stop is about 5 seconds, so the message can be read.
- Wholphin keeps its time's-up screen and also receives the server's message and stop (clarification Q3). Avoiding a
  duplicate message is a client concern (it reads the denial from Status), never done by client name on the server.
  Wholphin may stop counting sittings locally once it reads the server's sitting state; that is a later client change.
- The sitting state is kept in memory only: a server restart counts as a break.
- One enforcement setting covers the allowance, the schedule and the sitting limit; finer switches are out of scope.
- Stream requests that carry no token are served unless their device is a kid's: anonymous media routes are a
  deliberate Jellyfin compromise (review §A.3) and remain a documented limit.
- The opt-in "strict lock" that rewrites allowed hours (plan §3.3 C) is out of scope.
- The dashboard controls for the enforcement switch and the break length are a follow-up. The redesign merged on main
  as 0.0.4.1; this branch stays on 0.0.4.0 (a fast-forward was not permitted here) and leaves the page files
  untouched, so the merge only conflicts on the version number.
- The plugin version becomes 0.0.5.0.
