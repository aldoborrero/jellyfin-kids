# Feature Specification: Kid avatars, hidden live TV channels and the Peke client

**Feature Branch**: `012-kids-avatar` (from `main` at v0.0.7.0, plugin 0.0.7.0 → 0.0.8.0; spec directory
`012-kids-avatar`, named after the branch as requested)

**Created**: 2026-10-24

**Status**: Implemented (not committed)

**Input**: User description: "Feature 012: (1) kid avatar id in the profile settings (leo, max, nico, lucy, sara, mia;
null = Jellyfin profile image), readable by kid/admin via profile endpoints, settable by admin or parent grant, exposed
in /Kids/Profiles refs, dashboard picker with names/coloured initials only (no Peke artwork, GPLv2), client API minor
version bump and docs; (2) dashboard health warning when a picked live TV channel is hidden from the kid by Jellyfin's
parental control (BlockUnratedItems incl. LiveTvChannel, or max rating the channel exceeds/lacks), naming kid, channel
and fix; (3) build.yaml/README name Peke for Jellyfin as the client instead of Wholphin, plugin stays client-agnostic."

Builds on [feature 1](../001-kids-plugin-foundation/spec.md) (profiles, `/Kids/Profiles`, `/Kids/Info`),
[feature 6](../006-kids-live-tv/spec.md) (per-kid live TV channels, the kid's effective channels) and
[feature 7](../007-kids-dashboard-ux/spec.md) (health lines with Fix links on both dashboard pages).

## Clarifications

### Session 2026-10-24

No question was sent to the household: the coordinator asked for the recommended defaults, recorded here and in
Assumptions.

- **API version**: `GET /Kids/Info` has only a major `ApiVersion` (1), and earlier additive features left it alone.
  "Bump the client API minor version" is read as: add `ApiMinorVersion`, starting at **1** (API 1.1) with this
  feature, raised on every later additive change. `ApiVersion` stays 1 (nothing breaks) and the `avatar` capability
  is added to `Features`, which stays what clients check per field.
- **Avatar values**: lowercase ids on the wire; input is matched case-insensitively and stored lowercase; a blank
  string reads as none; anything else is a 400.
- **Full replacement**: like every other profile setting, a `PUT` without `Avatar` clears it. Clients send it on every
  update.
- **`/Kids/Profiles`**: each entry gains `Avatar` (omitted when none). The list was already open to any signed-in user
  so a TV can tell kids apart; an avatar id is a cosmetic choice, not household data worth hiding.
- **Picker artwork**: the plugin shows the names and its own coloured initials (its own palette); none of Peke's
  pictures, colours or SVGs are copied or linked.
- **Hidden channel check**: "hidden from that kid" is what Jellyfin itself hides: a picked channel that exists on the
  server but that Jellyfin does not list for the kid (the content rules behind the kid's effective channels, feature 6),
  with the kid's live TV access on. The reason
  shown comes from the channel's rating and the kid's policy.
- **Client name**: "Peke for Jellyfin" is named as the kids client in `build.yaml` and the README; the plugin's
  contract stays client-agnostic. The Kids page banner said "Limits only apply in Wholphin"; it now says "in the kids
  app" so it matches the README. Historical references (the verifier format, research notes, older specs) keep
  "Wholphin" because they describe where formats came from.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A parent gives each kid an avatar a TV can show (Priority: P1)

In the kid editor a parent chooses the kid's avatar from Leo, Max, Nico, Lucy, Sara and Mia, or "Jellyfin profile
image". The choice is saved with the other settings. The kid's TV reads it from the kid's profile, and a "Who's
watching" screen reads every kid's avatar from the list of kids without opening each profile.

**Why this priority**: kids who can't read yet pick their profile by picture; the household client needs the server
to hold the choice so every TV shows the same one.

**Independent Test**: as admin set Alice's avatar to `lucy`; `GET /Kids/Users/{alice}/Profile` (as Alice and as admin)
and `GET /Kids/Profiles` show `Avatar: "lucy"`; a `PUT` with `Avatar: null` removes it from both.

**Acceptance Scenarios**:

1. **Given** a kid without an avatar, **When** an administrator or a parent grant holder saves `Avatar: "mia"`,
   **Then** the profile and the list of kids show `"mia"`, and the revision goes up by one.
2. **Given** the kid's own token, **When** it tries to change the avatar, **Then** 403 and nothing changes.
3. **Given** `Avatar: "Lucy"`, **When** saved, **Then** it is stored and answered as `"lucy"`.
4. **Given** `Avatar: "bluey"` (not one of the six), **When** saved, **Then** 400 with an error on `Avatar` listing the
   allowed ids, and nothing changes.
5. **Given** a kid with an avatar, **When** a `PUT` omits `Avatar` or sends null or a blank string, **Then** the kid
   has no avatar and clients fall back to the Jellyfin profile image.
6. **Given** `GET /Kids/Info`, **Then** `ApiVersion` is 1, `ApiMinorVersion` is 1 and `Features` lists `avatar`.

---

### User Story 2 - The dashboard shows and edits the avatar without Peke's artwork (Priority: P2)

The kid editor has an "Avatar" select (Jellyfin profile image, Leo, Max, Nico, Lucy, Sara, Mia) with a small coloured
circle showing the initial of the chosen name. On the Kids page a kid with an avatar shows that coloured initial in
place of their Jellyfin picture, labelled with the avatar's name.

**Why this priority**: parents set the avatar where they set everything else; the TV is where the artwork is shown.

**Independent Test**: choose Sara in the editor: the preview shows an "S" circle, "Unsaved changes" appears; Save: a
toast; the Kids page row shows the "S" circle; choose "Jellyfin profile image" and save: the row shows the Jellyfin
picture (or the person icon) again.

**Acceptance Scenarios**:

1. **Given** a kid without an avatar, **When** the editor loads, **Then** "Jellyfin profile image" is selected and no
   initial is shown.
2. **Given** the select changes, **Then** the preview follows and "Unsaved changes" is shown until Save or undo.
3. **Given** a stored avatar id that this page does not know (a later plugin's), **When** the editor loads, **Then** it
   is kept and shown as that id, so saving other settings does not erase it.
4. **Given** the page assets, **Then** they contain no image, colour set or text taken from the Peke app.

---

### User Story 3 - A parent learns that Jellyfin hides a picked channel from a kid, and how to fix it (Priority: P1)

On the Kids page and in the kid editor, when a channel picked for a kid is hidden from that kid by Jellyfin's own
parental control, a health line names the channel and the reason, and a Fix link opens the kid's Parental control tab:

- "PekeTV is hidden from Alice: unrated live TV channels are blocked. Untick Live TV under "Block items with no or
  unrecognized rating information", or give the channel a rating."
- "PekeTV is hidden from Alice: it is rated TV-14, above the maximum allowed rating. Raise the maximum or change the
  channel's rating."
- "PekeTV is hidden from Alice by Jellyfin's parental control (for example a blocked or missing tag). Check the
  Parental control tab."

**Why this priority**: this happened at home: PekeTV vanished for a kid because unrated channels were blocked, and
nothing on the dashboard said why.

**Independent Test**: on the dev server pick PekeTV for Alice, tick Live TV under "Block items with no or
unrecognized rating information" on Alice's Parental control tab: both pages show the unrated line with a Fix link; untick it: the
line goes.

**Acceptance Scenarios**:

1. **Given** a picked channel without a rating and a kid whose unrated blocks include live TV channels, **When** the
   page loads, **Then** the unrated line is shown for that channel and kid, with Fix → Parental control tab.
2. **Given** a picked channel rated above the kid's maximum rating, **Then** the rating line names the rating.
3. **Given** a picked channel hidden for another reason, **Then** the generic line is shown.
4. **Given** several hidden channels, **Then** one line per channel.
5. **Given** the kid's live TV access is off in Jellyfin, **Then** only feature 7's "Live TV access is off" line is
   shown, not one line per channel.
6. **Given** a picked channel that no longer exists on the server, **Then** no hidden line (it is not a parental
   control problem).
7. **Given** the hidden flags cannot be read, **Then** no hidden line is shown and nothing claims the
   channels are fine.
8. **Given** channel and kid names with HTML or `${…}`, **Then** they are shown as text.

---

### User Story 4 - Docs and the plugin catalog name the household client (Priority: P3)

The plugin catalog entry (`build.yaml`) and the README describe the plugin as the server side of kids mode for any
Jellyfin client, naming Peke for Jellyfin as the kids client, instead of Wholphin.

**Why this priority**: wording only, but the catalog is what a household reads before installing.

**Independent Test**: `build.yaml` description and the README's note, feature table and Clients section name Peke for
Jellyfin and say any client can use the API; no current-client claim names Wholphin.

**Acceptance Scenarios**:

1. **Given** the catalog description, **Then** it names Peke for Jellyfin and says other clients can use the same API.
2. **Given** the README, **Then** the note, the enforcement and dashboard lines and the Clients section name Peke for
   Jellyfin; the API section links this feature's contract.

### Edge Cases

- A kid marked before this feature: no `Avatar` in their stored settings: they have none.
- An older client that does not know `Avatar` saves the profile: the avatar is cleared (full replacement, documented
  since feature 1; the dashboard always sends it).
- A stored id outside the six (written by a later version and read after a downgrade): answered as stored; the next
  save must send a known id or none, otherwise 400.
- A channel with a rating Jellyfin does not recognise and unrated items blocked: Jellyfin treats it as unrated; the
  rating line names the rating and says Jellyfin does not recognise it.
- The server's channel list cannot be read: no hidden lines (nothing to compare against).
- A channel visible to the kid but restricted by enforcement (time's up): not hidden; enforcement is not parental
  control.

## Requirements *(mandatory)*

### Functional Requirements

**Avatar**

- **FR-001**: A kid profile MUST hold an optional avatar id, one of `leo`, `max`, `nico`, `lucy`, `sara`, `mia`; none
  means "use the Jellyfin profile image".
- **FR-002**: `GET /Kids/Users/{id}/Profile` MUST answer the avatar (`Avatar`, omitted when none) to the kid and to
  administrators, like the other settings.
- **FR-003**: `PUT /Kids/Users/{id}/Profile` MUST accept `Avatar`, matched case-insensitively and stored lowercase;
  null, omitted or blank means none; any other value MUST be refused with 400 `errors.Avatar`. Only administrators,
  API keys and parent grant holders may change it (the existing rule).
- **FR-004**: `GET /Kids/Profiles` MUST include each kid's `Avatar` (omitted when none).
- **FR-005**: `GET /Kids/Info` MUST report `ApiVersion` 1, `ApiMinorVersion` 1 and list `avatar` in `Features`, after
  `watchTimeReset`.
- **FR-006**: The kid editor MUST offer an "Avatar" select with "Jellyfin profile image" and the six names, a coloured
  initial preview, and save it with the other settings; an unknown stored id MUST be kept.
- **FR-007**: The Kids page MUST show a kid's coloured initial in place of their Jellyfin picture when an avatar is set.
- **FR-008**: The plugin MUST NOT ship, copy or link Peke's artwork; it shows names and its own initials only.

**Hidden live TV channels**

- **FR-009**: For each kid with live TV access on, both dashboard pages MUST show one health line per picked channel
  that exists on the server but that Jellyfin hides from the kid, naming the kid, the channel, the reason
  and the fix, with a Fix link to the kid's Parental control tab.
- **FR-010**: The reason MUST be "unrated" when the channel has no rating and the kid blocks unrated live TV channels;
  "rating" when it has a rating and the kid has a maximum rating; "unrecognised" when it has a rating, no maximum is
  set and unrated live TV channels are blocked (Jellyfin treats a rating it does not know as unrated); otherwise
  generic.
- **FR-011**: The administrator channel list (`GET /Kids/Admin/LiveTvChannels`) MUST include each channel's rating
  (`Rating`: the custom rating, else the official one, as Jellyfin compares them; omitted when none) and, when asked
  for a user (`?userId=`), whether Jellyfin hides the channel from that user (`HiddenFromUser`), so the dashboard
  names the channel and the rating without reimplementing Jellyfin's rules.
- **FR-012**: When the channel list with the hidden flags cannot be read, no hidden line is shown.

**Client naming**

- **FR-013**: `build.yaml`'s description and the README MUST name Peke for Jellyfin as the kids client and keep the
  plugin client-agnostic; the Kids page banner MUST not name a single app.

**Release**

- **FR-014**: The plugin version MUST be 0.0.8.0 in `build.yaml` and `Directory.Build.props`, with a changelog; nothing
  is published.
- **FR-015**: No existing endpoint's behaviour changes other than the additions above.

### Key Entities

- **Avatar**: an id from a fixed set of six, stored in the kid's profile settings; none by default.
- **Hidden channel** (dashboard, not stored): kid, channel, reason (`unrated`, `rating`, `other`) and the fix.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A TV can show every kid's avatar from one request to the list of kids.
- **SC-002**: 100% of avatar changes attempted with a kid's own token are refused.
- **SC-003**: With unrated live TV channels blocked for a kid who has an unrated channel picked, the parent sees the
  channel, the kid and the fix on the Kids page without opening another page.
- **SC-004**: Setting an avatar in the editor takes 2 actions (choose, Save).
- **SC-005**: Both pages render without overflow at 400 px and 1920 px.

## Assumptions

- **API minor version (default)**: a new `ApiMinorVersion` field, 1 for this feature (see Clarifications).
- **Avatar ids (default)**: lowercase on the wire, case-insensitive on input, blank as none.
- **Avatar initials (default)**: the first letter of the name in a circle, each of the six with its own colour from
  the plugin's palette, white text; the name is the accessible label.
- **Hidden channel detection (default)**: the server asks Jellyfin for the channels it lists for the kid (the same
  query the kid's own channel list uses) and flags the others, independent of the plugin's allowed list, so a channel
  ticked a moment ago is judged right away; so the plugin does not reimplement Jellyfin's rating rules
  (constitution I). The reason is derived from the channel's rating and the kid's policy only to word the fix.
- **Fix wording (default)**: Jellyfin 12.2 labels the setting "Block items with no or unrecognized rating
  information" with a "Live TV" box (`ParentalControl.tsx`, `UnratedItem.LiveTvChannel`); the Fix link opens `#/dashboard/users/{id}/parentalcontrol`.
- **Banner (default)**: "Limits only apply in the kids app" replaces "in Wholphin".
- Out of scope: avatar artwork in the dashboard, avatars for non-kid users, a kid choosing their own avatar, the
  constitution's "Wholphin as the reference client" wording in principle V (left for a constitution amendment).
