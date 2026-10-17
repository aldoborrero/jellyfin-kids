# Feature Specification: Kids plugin foundation

**Feature Branch**: `001-kids-plugin-foundation` (spec directory; no git branch created yet)

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "kids-plugin-foundation: esqueleto del plugin con almacenamiento SQLite en la carpeta de datos del plugin, GET /Kids/Info anónimo, perfiles de niño por usuario de Jellyfin (marcar y desmarcar, ajustes de Wholphin: búsqueda, autoplay y su límite, secciones de la home, colecciones/playlists elegidas) con revisiones y conflicto 409, limpieza al borrar un usuario, y página del panel de Jellyfin que lista usuarios, marca niños y avisa si un niño es administrador. Ver docs/plan.md §7 feature 1."

Research input: [docs/plan.md](../../docs/plan.md) §2 (leverage map), §4.1 (client contract) and §7 (feature 1).

## Clarifications

### Session 2026-10-17

- Q: Must a picked collection or playlist also be visible to that kid? → A: No. On save it must exist and be a
  collection or a playlist; visibility to the kid (for example sharing a playlist) is left to feature 5.
- Q: Does the Kids dashboard page also edit each kid's Wholphin settings? → A: Yes. The page includes a per-kid
  editor for search, autoplay and its limit, the order and visibility of home sections, and the picked items with
  a collection and playlist chooser.
- Q: What happens to unknown home sections? → A: They are rejected as invalid. Adding a section is a contract
  change that raises the API version.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A client detects the plugin (Priority: P1)

A household runs Jellyfin with the Kids plugin installed. When Wholphin (or any other client) connects to the
server, even before anyone signs in, it asks the server whether the plugin is present and which version of the
kids contract it speaks. If the plugin is there, the client uses the server-side kids configuration; if not, it
keeps working in its own local mode.

**Why this priority**: Every other capability depends on clients knowing that the plugin exists. On its own it
already lets Wholphin switch between local mode and plugin mode.

**Independent Test**: Install the plugin on the 12.2 dev server and ask for the plugin information without
credentials; then ask a server without the plugin.

**Acceptance Scenarios**:

1. **Given** a server with the plugin installed and enabled, **When** a client asks for the plugin information
   without signing in, **Then** it receives a success answer with the plugin version, the contract version (1) and
   the list of capabilities the plugin offers (in this feature: kid profiles only).
2. **Given** a server without the plugin, **When** a client asks for the plugin information, **Then** it receives a
   "not found" answer, which clients treat as local mode.
3. **Given** a household with kids and settings configured, **When** an anonymous caller reads the plugin
   information, **Then** the answer contains nothing specific to the household (no user ids, no kid names, no
   settings).

---

### User Story 2 - A parent marks and unmarks kids from the Jellyfin dashboard (Priority: P1)

An administrator opens the Kids page in the Jellyfin dashboard. The page lists every Jellyfin user and shows which
ones are kids. The administrator marks a user as a kid, or unmarks one. If a user marked as a kid is a Jellyfin
administrator, the page shows a clear warning, because administrators bypass every restriction.

**Why this priority**: It is the household's setup step. Without a way to say who the kids are, the server has
nothing to serve to clients.

**Independent Test**: Sign in to the dashboard as an administrator on the dev server, mark `alice` as a kid,
reload the page, check that `alice` is still marked; mark the admin user and see the warning; unmark `alice`.

**Acceptance Scenarios**:

1. **Given** an administrator on the Kids page, **When** the page loads, **Then** it lists every Jellyfin user with
   their name, whether they are a kid, and whether they are an administrator.
2. **Given** a user who is not a kid, **When** the administrator marks them as a kid, **Then** the user gets a kid
   profile with the default settings and appears as a kid in the list and to clients.
3. **Given** a kid, **When** the administrator unmarks them (after confirming), **Then** their kid profile and
   settings are removed and clients no longer treat them as a kid.
4. **Given** a kid who is also a Jellyfin administrator, **When** the page shows them, **Then** it displays a warning
   that administrators bypass all restrictions and that the kid should not be an administrator. Marking is still
   allowed.
5. **Given** the dashboard page is fetched without signing in, **When** its content is inspected, **Then** it
   contains no household data; all users and kid flags are loaded only after the administrator is authenticated.

---

### User Story 3 - Clients read and change a kid's settings, with conflict detection (Priority: P1)

Wholphin needs the kid's settings to build kids mode: whether search is allowed, whether autoplay is on and how
many episodes it may play in a row, which home sections are shown and in which order, and which collections or
playlists the grown-ups picked. A kid's device reads its own settings; a parent (an administrator, or later a
parent grant) changes them. Two parents may edit at once from different TVs or from a TV and the dashboard, so
every change states which version it was based on, and a change based on an outdated version is rejected with the
current settings so the client can merge or ask again.

**Why this priority**: This is the data Wholphin's kids mode consumes; it is the reason for moving kid profiles to
the server.

**Independent Test**: With the dev server, read `alice`'s profile with her own token and with the admin token;
update it as admin; try to update it with `alice`'s token; send two updates based on the same version and check
the second one is rejected with the current settings.

**Acceptance Scenarios**:

1. **Given** a kid, **When** the kid (with their own credentials) or an administrator reads the kid's profile,
   **Then** they receive the settings and the current revision.
2. **Given** a kid, **When** another non-administrator user reads that kid's profile, **Then** the request is
   refused.
3. **Given** a user who is not a kid, **When** anyone authorized reads their profile, **Then** the answer is "not
   found".
4. **Given** a kid at revision N, **When** an administrator saves new settings based on revision N, **Then** the
   settings are stored and the answer carries revision N+1.
5. **Given** a kid at revision N+1, **When** an administrator saves settings based on revision N, **Then** the
   change is rejected as a conflict and the answer carries the current settings and revision N+1.
6. **Given** a kid, **When** the kid's own credentials (without administrator rights) are used to change, mark or
   unmark any profile, including their own, **Then** the request is refused and nothing changes.
7. **Given** a kid outside their allowed hours in Jellyfin, **When** their device reads their profile or the list
   of kids, **Then** the read still succeeds.
8. **Given** any signed-in user, **When** they ask for the list of kids, **Then** they receive only the kids' user
   ids and revisions, so a user switcher can tell who the kids are.
9. **Given** settings with an autoplay limit outside 1–10, unknown or duplicated home sections, or picked items
   that do not exist or are not collections or playlists, **When** they are saved, **Then** the change is rejected
   with a message naming the invalid field and nothing is stored.

---

### User Story 4 - A parent edits a kid's settings from the Jellyfin dashboard (Priority: P2)

From the Kids page, an administrator opens a kid and edits their Wholphin settings: allows or blocks search, turns
autoplay on or off and sets how many episodes it may play in a row, reorders the home sections and hides the ones
they do not want, and chooses the collections and playlists shown in "Picked by your grown-ups", in order, from a
list of the server's collections and playlists. Saving uses the same revision rule as clients, so a change made
meanwhile on a TV is not silently overwritten.

**Why this priority**: Parents can configure kids without needing Wholphin's parent menu or the API, and the
household has one place to see each kid's settings. It builds on Stories 2 and 3.

**Independent Test**: On the dev server, open `alice` in the Kids page, turn on autoplay with a limit of 2, hide
"New", move "Picked" to the top, pick one collection and one playlist, save; read `alice`'s profile as `alice`
and check every value. Then change `alice` through the API and save stale edits from the page to see the conflict.

**Acceptance Scenarios**:

1. **Given** a kid, **When** the administrator opens them in the Kids page, **Then** the editor shows their current
   search, autoplay, autoplay limit, home sections (in order, with visibility) and picked items (in order, by
   name and type).
2. **Given** the editor, **When** the administrator changes any setting and saves, **Then** the kid's profile holds
   exactly those values and its revision increases by one.
3. **Given** the editor, **When** the administrator adds picks, **Then** they choose from a list of the server's
   collections and playlists (searchable by name), can reorder and remove picks, and cannot add anything else.
4. **Given** the autoplay limit field, **When** the administrator enters a value outside 1–10, **Then** the editor
   shows the allowed range and does not save.
5. **Given** the editor was opened at revision N and the kid was changed elsewhere meanwhile, **When** the
   administrator saves, **Then** nothing is overwritten, the page explains that the settings changed elsewhere and
   offers to reload the current settings.
6. **Given** a picked item that has since been deleted from the server, **When** the editor opens, **Then** it shows
   the pick as missing and lets the administrator remove it.

---

### User Story 5 - Deleting a Jellyfin user cleans up their kid profile (Priority: P3)

When an administrator deletes a Jellyfin user who was a kid, the plugin removes that kid's profile and settings,
so no orphaned data is left behind and the list of kids stays accurate.

**Why this priority**: Prevents stale kids showing up in clients and the dashboard; small but needed for data
hygiene.

**Independent Test**: Mark `bob` as a kid, delete `bob` in the Jellyfin dashboard, and check that the list of
kids no longer contains him.

**Acceptance Scenarios**:

1. **Given** a kid, **When** the administrator deletes the Jellyfin user, **Then** the kid's profile is removed and
   no longer appears in the list of kids or on the Kids page.
2. **Given** a kid profile whose user was deleted while the plugin was disabled or not running, **When** the
   plugin next starts, **Then** that orphaned profile is removed.

---

### Edge Cases

- The plugin's data store is missing or empty on first start: it is created with no kids; the plugin information
  is still served.
- The data store cannot be opened or is corrupt: the plugin logs the failure, the plugin information is still
  served, and profile requests fail with a server error rather than returning empty data that clients would read as
  "no kids". The Kids page shows an error instead of an empty list.
- Marking a user who is already a kid: refused as a conflict, returning the existing profile (no silent reset of
  settings).
- Unmarking a user who is not a kid: answered as "not found"; nothing changes.
- Reading or changing the profile of a user id that does not exist in Jellyfin: "not found".
- A picked collection or playlist is deleted after being picked: the profile keeps the id; clients skip items they
  cannot load, and the editor shows it as missing (Story 4.6). Validation applies only to items added in a save;
  saving a profile that still contains an already stored missing pick is allowed.
- Saved settings that omit some home sections: the missing sections are appended, visible, in the default order,
  matching Wholphin's normalization.
- A server with no collections or playlists: the picks chooser says so; the rest of the editor works.
- A server with many collections and playlists (hundreds): the chooser stays usable through search by name.
- An API key (script) changes a profile: treated like an administrator.
- A Jellyfin administrator is marked as a kid: allowed, with a warning on the Kids page (Story 2.4).
- Two administrators mark the same user at the same time: one succeeds, the other gets a conflict.
- A kid is unmarked while their editor is open in another tab: saving reports that the user is no longer a kid.

## Requirements *(mandatory)*

### Functional Requirements

**Plugin and detection**

- **FR-001**: The plugin MUST load and run on every Jellyfin 12.x release from 12.0 onward.
- **FR-002**: The plugin MUST answer an anonymous request for plugin information with the plugin version, the
  client contract version (1), the list of capabilities it offers (this feature: `profiles`), and the household
  lock type, which is empty until the parent lock feature exists.
- **FR-003**: The plugin information MUST NOT contain any user, kid or household-specific data.
- **FR-004**: The plugin MUST keep kid profiles in its own data store inside the plugin's data folder on the
  server, separate from the plugin's dashboard settings, so that saving dashboard settings never overwrites profile
  changes made by clients.
- **FR-005**: The data store MUST survive server restarts and plugin updates, and MUST be able to evolve its layout
  in later features without losing existing profiles.

**Kid profiles**

- **FR-006**: A Jellyfin user MUST be either a kid (has a kid profile) or not; marking creates the profile with
  default settings, unmarking removes it.
- **FR-007**: A kid profile MUST hold: whether search is allowed (default off), whether autoplay is on (default
  off), the autoplay limit in episodes in a row (1–10, default 3), the ordered list of home sections with a
  visibility flag each (sections: keep watching, picked by your grown-ups, libraries, new, favourites; default all
  visible in that order), and the ordered list of picked collections or playlists (default empty).
- **FR-008**: Each kid profile MUST carry a revision that increases by one on every change.
- **FR-009**: A change to a kid profile MUST state the revision it was based on; if it does not match the current
  revision, the change MUST be rejected as a conflict and the answer MUST include the current profile.
- **FR-010**: The system MUST validate settings on save and reject invalid ones, naming the invalid field:
  autoplay limit within 1–10; home sections only from the known set and without duplicates (missing ones are
  appended as visible in default order); each newly added pick must exist on the server and be a collection or a
  playlist, without duplicates. Whether the kid can see a pick is not checked.
- **FR-011**: Unknown home sections MUST be rejected as invalid; adding a section is a contract change that raises
  the contract version.
- **FR-012**: Any signed-in user MUST be able to list the kids as user ids with their revisions only.
- **FR-013**: A kid MUST be able to read their own profile; administrators (and API keys) MUST be able to read any
  profile; other users MUST be refused.
- **FR-014**: Reads of profiles and of the list of kids MUST work outside the kid's allowed hours.
- **FR-015**: Marking, unmarking and changing a kid profile MUST require an administrator or an API key. A kid's own
  credentials MUST never be enough, including for their own profile. (Parent grants are added by the parent lock
  feature.)
- **FR-016**: Signed-in callers who lack rights MUST be refused as "forbidden", never with the "unauthenticated"
  answer that clients treat as an expired session.

**Cleanup**

- **FR-017**: When a Jellyfin user is deleted, the plugin MUST remove their kid profile.
- **FR-018**: On startup, the plugin MUST remove kid profiles whose Jellyfin user no longer exists.

**Dashboard page: users and kids**

- **FR-019**: The plugin MUST provide a Kids page in the Jellyfin dashboard, available to administrators only and
  reachable from the dashboard menu.
- **FR-020**: The page MUST list every Jellyfin user with their name, kid status and administrator status, and MUST
  let the administrator mark and unmark kids; unmarking MUST ask for confirmation because it deletes the kid's
  settings.
- **FR-021**: The page MUST warn, next to the user, when a kid is a Jellyfin administrator.
- **FR-022**: The page content served by Jellyfin MUST NOT include household data; everything MUST be loaded through
  authenticated requests.

**Dashboard page: per-kid editor**

- **FR-023**: For each kid, the page MUST offer an editor for every setting in FR-007: search allowed, autoplay on
  or off, autoplay limit, order and visibility of the home sections, and the ordered picks.
- **FR-024**: The picks editor MUST let the administrator choose only from the server's collections and playlists,
  find them by name, show each pick's name and type, reorder picks and remove them, and MUST show picks that no
  longer exist as missing.
- **FR-025**: The plugin MUST provide administrators with the list of candidate collections and playlists (id, name,
  type) for the chooser.
- **FR-026**: The editor MUST save through the same revision rule as clients (FR-009): on conflict it MUST NOT
  overwrite, MUST tell the administrator the settings changed elsewhere, and MUST offer to reload the current
  settings.
- **FR-027**: The editor MUST show validation errors next to the field concerned and MUST NOT report success
  unless the save was stored.

### Key Entities

- **Kid profile**: one per Jellyfin user who is a kid. Identified by the Jellyfin user id. Holds the revision,
  search allowed, autoplay enabled, autoplay limit, home sections (ordered, each with visibility) and picked items
  (ordered collection or playlist ids). Removed when the kid is unmarked or the Jellyfin user is deleted.
- **Home section entry**: a section of Wholphin's kids home (keep watching, picked, libraries, new, favourites)
  with a visible flag; its position in the list is its order.
- **Pick candidate**: a collection or playlist on the server that the editor can offer, with id, name and type.
  Owned by Jellyfin; the plugin only lists it.
- **Plugin information**: what anonymous clients may learn: plugin version, contract version, capabilities, lock
  type.
- **Jellyfin user** (existing, owned by Jellyfin): referenced by id; the plugin reads name and administrator status
  and never changes it in this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The plugin builds against the Jellyfin 12.0 packages and loads without errors on Jellyfin 12.2,
  where the anonymous plugin information answers successfully. Runtime loading on 12.0 and 12.1 is not verified
  (accepted risk, plan.md "Decisions").
- **SC-002**: An administrator can mark a user as a kid from the dashboard in under 30 seconds from opening the
  Kids page, and the change is visible to clients on their next read.
- **SC-003**: An administrator can set all of a kid's settings (search, autoplay and limit, section order and
  visibility, three picks) from the Kids page in under 2 minutes, and a read of the kid's profile afterwards
  matches every value entered.
- **SC-004**: 100% of attempts to change, mark or unmark profiles with a kid's own credentials are refused, and
  none of them change stored data.
- **SC-005**: When two changes are based on the same revision (from clients or the dashboard editor), exactly one
  succeeds and the other receives a conflict carrying the current settings, in 100% of attempts; the editor never
  silently overwrites.
- **SC-006**: After a kid's Jellyfin user is deleted, the kid no longer appears in the list of kids or on the Kids
  page (immediately, or on the next start if the plugin was not running).
- **SC-007**: Every kid who is a Jellyfin administrator is flagged with a warning on the Kids page.
- **SC-008**: Profiles and revisions are unchanged after a server restart.

## Assumptions

- Mutations in this feature are admin or API key only; the parent grant (`X-Kids-Grant`) arrives with feature 2
  (`kids-parent-lock`) and will extend FR-015 without changing the contract.
- The list of kids is visible to every signed-in user (ids and revisions only), as in plan §4.1, so Wholphin's user
  switcher works before a parent unlocks.
- Marking an administrator as a kid is allowed with a warning, not blocked.
- Marking an existing kid is a conflict returning the current profile; unmarking a non-kid is "not found".
- The plugin information reports the `profiles` capability and an empty lock type in this feature.
- The settings, defaults and limits mirror Wholphin's current local kid profile (`KidProfile.kt`: autoplay limit
  1–10, default 3; five home sections, all visible by default) so the app's one-time migration is lossless.
- The picks chooser lists all of the server's collections and playlists, not only those the kid can see; sharing a
  picked playlist with the kid and visibility warnings belong to feature 5.
- Unmarking removes the profile immediately; there is no usage data to purge yet (allowance tracking is feature 3).
- Profile fields for later features (daily allowance, session default, strict lock) are not part of this feature;
  they will be added by those features.
- Content restrictions, allowed hours and recommended policies are out of scope (Jellyfin's own policy; feature 5).
- Export and import of plugin data are out of scope (feature 6), although Jellyfin's backups do not include it.
- Size: with the per-kid editor this feature has five stories (three P1). It stays one feature; if planning shows
  it is too large, the editor (Story 4) is the natural split point, to be decided by the user.
