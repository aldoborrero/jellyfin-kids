# Feature Specification: Live TV for kids

**Feature Branch**: `010-kids-live-tv` (from `main`, plugin 0.0.5.0)

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "kids-live-tv (spec 006): live TV for kids. Per-kid allowed live TV channel list in the
kid profile (empty/null = no live TV); server-side gating for kids only (admins/API keys exempt) of live TV playback
(PlaybackInfo for a channel, LiveStreams/Open, live TV stream and file routes) with NotAllowed/403 and X-Kids-Reason
liveTvChannel; recordings and timers routes refused for kids; live TV time counts toward allowance and sitting (005
chain); kid-readable endpoints for allowed channels (name, number, image) and their guide for a time window via
ILiveTvManager; feature flag liveTv in /Kids/Info; dashboard picker in the kid editor with a warning when
EnableLiveTvAccess is off. Decisions: kids may see the guide (now/next and upcoming) of allowed channels; no
recordings/DVR for kids; channels chosen per kid."

Context: the household uses ErsatzTV as a live TV tuner in Jellyfin, with two kids channels, PekeTV and PekeTV
Classics, next to grown-up channels. Jellyfin has no per-user channel permission: the user policy's live TV access is
all or nothing. Builds on [feature 1](../001-kids-plugin-foundation/spec.md) (profiles, revisions, validation),
[feature 3](../003-kids-allowance-tracking/spec.md) (counting) and [feature 5](../005-kids-enforcement/spec.md) (the
playback gate, the sitting and the stop chain).

## Clarifications

### Session 2026-10-19

Answers from the user, relayed by the `wholphin-kids` session.

- Q: Does the channel gate (and the recordings refusal) apply always, or only while the household's enforcement switch
  is on? → A: Always on, whatever `EnforcementEnabled` says: it is a content rule, not a time rule. Upgrading therefore
  stops live TV for every kid until a parent picks their channels; the release note and README say so, and the kid
  editor shows "No live TV channels allowed" prominently for a kid with an empty list.
- Q: Should Jellyfin's own live TV browse routes be limited for kids? → A: Yes: the channel and guide lists
  (`LiveTv/Channels`, `LiveTv/Programs` GET and POST, `LiveTv/Programs/Recommended`) only show the kid's effective
  channels, and a single disallowed channel or program (`LiveTv/Channels/{id}`, `LiveTv/Programs/{id}`) answers 404.
- The specify-time defaults (live TV access off means no channels, stored ids kept, 24 h / 7 day guide window,
  Jellyfin's item shape, content-restriction warning) are accepted as written.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A parent chooses the channels each kid may watch (Priority: P1)

In the kid editor on the dashboard, a parent sees every live TV channel of the server (name, number, logo) with a
checkbox, ticks PekeTV and PekeTV Classics for Alice, and saves. Bob gets only PekeTV Classics. A kid with nothing
ticked has no live TV.

**Why this priority**: Without a per-kid list there is nothing to enforce or show; it is the data the rest builds on.

**Independent Test**: Open Alice's editor, tick two channels, save, reload: the same two are ticked, and the profile
read through the API lists exactly those two channel ids.

**Acceptance Scenarios**:

1. **Given** a server with live TV channels, **When** a parent opens a kid's editor, **Then** the channels are listed
   with name, number and logo, the kid's allowed ones ticked.
2. **Given** the editor, **When** the parent ticks channels and saves, **Then** the profile stores exactly those
   channels, under the same revision and conflict rules as every other profile field.
3. **Given** a profile update sent without the channel list, **When** it is saved, **Then** the kid has no allowed
   channels (a full replacement, like the other fields).
4. **Given** an update that lists an id that is not a live TV channel, or lists one twice, **When** it is saved,
   **Then** it is refused with a field error naming the position of the bad id, and nothing is changed.
5. **Given** a kid whose Jellyfin account has live TV access turned off, **When** the parent opens the editor, **Then**
   a warning says live TV will not work for this kid until live TV access is turned on in the user's settings.
6. **Given** a server with no live TV channels, **When** the parent opens the editor, **Then** the section says there
   are no live TV channels and offers nothing to tick.

---

### User Story 2 - Kids can play only their channels, on any client (Priority: P1)

Alice opens live TV in jellyfin-web (or any other client) and plays PekeTV: it works. She tries the grown-up news
channel: it is refused with a clear "not allowed" answer, whatever route the client uses to start or continue the
stream. Bob, who has no channels, is refused on every channel.

**Why this priority**: It is the safety promise. Without the gate, a kid with live TV access can watch any channel.

**Independent Test**: As Alice, ask the server how to play PekeTV (works) and the news channel (refused); open a live
stream for each; request their HLS playlists and stream files: everything for the news channel is refused, everything
for PekeTV works. As an administrator, everything works.

**Acceptance Scenarios**:

1. **Given** a kid allowed PekeTV, **When** a client asks how to play PekeTV, opens its live stream and streams it,
   **Then** it plays as today.
2. **Given** the same kid, **When** a client asks how to play a channel not in their list, **Then** the answer is a
   normal answer that says playback is not allowed, with the reason `liveTvChannel`.
3. **Given** the same kid, **When** a client opens a live stream, requests a playlist, a segment, a stream or a live
   stream file for a channel not in their list, **Then** it is refused as forbidden with the error code
   `PlaybackNotAllowed` and the reason `liveTvChannel`, never with the code that signs jellyfin-web out.
4. **Given** a kid with no allowed channels, or whose Jellyfin live TV access is off, **When** they try any channel,
   **Then** it is refused.
5. **Given** an administrator, an API key or a user who is not a kid, **When** they play any channel, **Then** nothing
   is refused.
6. **Given** a kid watching a channel, **When** a parent removes it from the kid's list, **Then** the next request of
   that stream is refused, so the playback stops within about a minute at most for clients that keep requesting
   playlists or segments.
7. **Given** a kid playing ordinary library content, **When** they play it, **Then** nothing changes from feature 5.
8. **Given** a kid in jellyfin-web or any client using Jellyfin's own live TV pages, **When** they open the channel list
   or the guide, **Then** they see only their allowed channels and those channels' programs.
9. **Given** enforcement switched off for the household, **When** a kid tries a disallowed channel, **Then** it is
   still refused.

---

### User Story 3 - Live TV counts as screen time (Priority: P1)

Alice watches PekeTV for 20 minutes; her used time grows by 20 minutes and her sitting by the same. When her
allowance, her sitting or her allowed hours run out while she watches live TV, the feature 5 warnings, message and stop
happen exactly as for a film.

**Why this priority**: Live TV is endless; without counting it, it is the easiest way around the allowance.

**Independent Test**: Play a channel as Alice for a few minutes and read her Status before and after: used time grows
by the watched time (±30 s). With enforcement on and her time nearly used, the stop chain ends the live playback.

**Acceptance Scenarios**:

1. **Given** a kid watching an allowed channel, **When** time passes, **Then** it is counted toward their daily usage
   and their sitting like any other playback.
2. **Given** enforcement on and a kid whose limit runs out during live TV, **When** it runs out, **Then** they get the
   feature 5 message and stop, the live stream is closed and further requests are refused with the feature 5 reason
   (`timeUp`, `sessionLimit` or `outsideSchedule`).

---

### User Story 4 - A kids client shows the kid's channels and guide (Priority: P2)

Wholphin's kids mode shows a "TV" row with Alice's two channels, each with its logo, number, what is on now and what
comes next, and a guide of the coming hours, using only Alice's own sign-in. It never sees the other channels.

**Why this priority**: The decision is that kids may see the guide of their channels; clients need a way to get it
without live TV management rights and without listing the other channels.

**Independent Test**: As Alice, read her channels: exactly the two allowed ones, with name, number and image, and the
current program. Read her guide for the next 6 hours: programs of those two channels only. As Bob, read Alice's
channels: refused.

**Acceptance Scenarios**:

1. **Given** a kid with allowed channels, **When** the kid reads their channels, **Then** they get those channels, in
   channel number order, each with name, number, image and the program on now.
2. **Given** the same kid, **When** they read their guide for a time window, **Then** they get the programs of their
   allowed channels that overlap the window, ordered by start time; without a window, from now for the next 24 hours.
3. **Given** a guide request naming channels, **When** some are not allowed, **Then** only the allowed ones are
   included.
4. **Given** another kid or a user who is not a kid, **When** they read this kid's channels or guide, **Then** it is
   refused; an administrator can read them.
5. **Given** a kid with no allowed channels or with live TV access off, **When** they read their channels or guide,
   **Then** the lists are empty.
6. **Given** a client, **When** it reads the plugin information, **Then** the `liveTv` capability tells it the
   channel list, the endpoints and the gate exist.

---

### User Story 5 - No recordings for kids (Priority: P2)

Kids cannot list, read, schedule, change or delete live TV recordings and recording timers, and cannot stream an
in-progress recording, whatever client they use.

**Why this priority**: The decision is no DVR for kids; recordings could contain any channel.

**Independent Test**: As Alice, list recordings, list timers, create a timer and stream a live recording: each is
refused as forbidden. As an administrator, each works as before.

**Acceptance Scenarios**:

1. **Given** a kid, **When** they call any live TV recording, timer or series timer route, **Then** it is refused as
   forbidden with the reason `liveTvRecording`.
2. **Given** an administrator or a user who is not a kid, **When** they use those routes, **Then** nothing changes.

---

### Edge Cases

- A channel in a kid's list is removed from the server (tuner removed, channel deleted): the stored id is kept, so
  other fields can still be saved; it is shown as missing in the editor, it plays nothing and the kid's endpoints leave
  it out. Adding a new unknown id is refused.
- Jellyfin's own content restrictions (parental rating, blocked unrated live TV channels or programs) still apply: a
  channel or program Jellyfin hides from the kid is neither listed nor playable, even if allowed here.
- A stream request without a user (server-internal transcoding fetching a live stream file, anonymous links) is not
  refused by the channel rule unless it can be attributed to a kid, as in feature 5.
- A kid uses an open live stream id that was not opened through an allowed channel by the plugin's knowledge (for
  example after a plugin restart while the stream stays open): refused as an unknown live stream.
- A kid asks how to play a guide program instead of the channel: judged by the program's channel.
- The kid editor is open while a channel is added on the server: it appears after reloading the editor.
- A guide window longer than 7 days is cut to 7 days; an end before the start is refused.

## Requirements *(mandatory)*

### Functional Requirements

**Profile**

- **FR-001**: A kid profile MUST hold an optional list of allowed live TV channels, read and written with the rest of
  the profile; absent, null or empty means the kid has no live TV.
- **FR-002**: An update MUST be a full replacement: omitting the list clears it.
- **FR-003**: Each newly listed id MUST be an existing live TV channel and appear once; otherwise the update is refused
  with a field error keyed by position (`LiveTvChannelIds[i]`), alongside any other field errors, and the revision is
  checked first as before. Ids already stored are kept even if the channel no longer exists. At most 200 channels.
- **FR-004**: Profiles stored before this feature MUST read as having no allowed channels; no data migration may lose
  or change other fields.

**Gate**

- **FR-005**: For a kid caller, the server MUST refuse playback of a live TV channel not in the kid's effective list:
  PlaybackInfo (GET and POST) answered as "not allowed" with reason `liveTvChannel`; live stream opening, HLS
  playlists and segments, video and audio streams and live stream files answered 403 with error code
  `PlaybackNotAllowed` and reason `liveTvChannel`.
- **FR-006**: The kid's effective list MUST be empty when their Jellyfin live TV access is off, whatever the profile
  holds.
- **FR-007**: A request MUST be judged by every channel it names: the item (a channel, or a guide program's channel),
  a live stream opening token, and a live stream id; a live stream the plugin cannot attribute to an allowed channel of
  this kid MUST be refused.
- **FR-008**: For a kid caller, every live TV recording, timer and series timer route, and the live recording stream,
  MUST be refused with 403 and reason `liveTvRecording`.
- **FR-009**: Administrators, API keys and users who are not kids MUST never be refused by these rules.
- **FR-010**: The channel, recording and browse rules MUST apply always, whether or not the household's enforcement
  switch is on (a content rule, not a time rule). The release note and README MUST say that upgrading stops kids' live
  TV until a parent picks their channels.
- **FR-011**: A failure in the plugin's own logic MUST let the request through and be logged, never refuse (fail open,
  as in feature 5); disabling or removing the plugin removes all refusals.
- **FR-012**: The gate MUST NOT add noticeable delay to playback requests of non-kids.

**Counting and limits**

- **FR-013**: Live TV playback by a kid MUST count toward the daily usage and the sitting, and the feature 5 warnings,
  stop chain and refusals MUST apply to it, closing the live stream on stop.

**Client API**

- **FR-014**: A kid (or an administrator) MUST be able to read the kid's effective allowed channels, each with id,
  name, number, image and the program on now, in Jellyfin's usual item shape.
- **FR-015**: A kid (or an administrator) MUST be able to read the programs of the kid's effective channels for a time
  window (default now to now + 24 h, at most 7 days), optionally narrowed to some of those channels, in Jellyfin's
  usual item shape, ordered by start time.
- **FR-016**: Those reads MUST be refused (403) to anyone else and MUST work outside the kid's allowed hours.
- **FR-017**: `GET /Kids/Info` MUST list the `liveTv` capability.
- **FR-018**: For a kid caller, Jellyfin's own live TV channel and guide lists (`LiveTv/Channels`, `LiveTv/Programs`
  GET and POST, `LiveTv/Programs/Recommended`) MUST contain only the kid's effective channels and their programs;
  `LiveTv/Channels/{id}` and `LiveTv/Programs/{id}` for anything else MUST answer 404.

**Dashboard**

- **FR-019**: The kid editor MUST list the server's live TV channels (administrator call) with checkboxes, show stored
  channels that no longer exist as missing, and save the ticked ones with the profile.
- **FR-020**: The kid editor MUST show "No live TV channels allowed" prominently when the kid's list is empty, and
  MUST warn when the kid's Jellyfin live TV access is off, and when the kid's content
  restrictions block unrated live TV channels or programs.
- **FR-021**: Dashboard changes MUST stay in the kid editor page; the HTML stays static and anonymous.

### Key Entities

- **Allowed channel list**: per kid, an ordered list of live TV channel ids; part of the kid profile and its revision.
- **Effective channels**: the allowed list, minus everything when the kid's live TV access is off, minus channels
  Jellyfin hides from the kid or that no longer exist.
- **Live stream attribution**: which channel an open live stream plays, as learnt when the plugin let a kid open it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A parent can give a kid two channels in under one minute from the kid editor.
- **SC-002**: 100% of tested play attempts of a disallowed channel by a kid are refused, across PlaybackInfo, live
  stream opening, HLS and stream routes, and in jellyfin-web the player shows a "not allowed" message.
- **SC-003**: 100% of tested play attempts of an allowed channel by a kid, and of any channel by an administrator,
  succeed.
- **SC-004**: Ten minutes of live TV by a kid are counted within ±30 s.
- **SC-005**: A kid's channel and guide reads return only their channels, every time.
- **SC-006**: No profile, usage or lock data changes on upgrade.

## Assumptions

- The kid's Jellyfin account must have live TV access on for live TV to work; the plugin does not change the policy
  (constitution III), it only warns.
- Jellyfin's content restrictions still apply on top of the list (constitution I).
- Recordings already stored as library items are governed by Jellyfin's library access, not by this feature.
- The plugin learns which channel a live stream plays when it lets a kid open it; that knowledge is in memory and lost
  on restart, which also ends Jellyfin's open live streams.
- Texts are English, like feature 5.
- The ErsatzTV setup is a normal M3U tuner with an XMLTV guide; the dev loop reproduces it with a local stream.
- Plugin version 0.0.6.0; `ApiVersion` stays 1 (all additions are optional).
