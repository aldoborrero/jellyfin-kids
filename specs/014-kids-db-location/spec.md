# Feature Specification: A safe home for the kids data

**Feature Branch**: `014-kids-db-location` (from `main` at v0.0.9.0, plugin 0.0.9.0 → 0.0.10.0; spec directory
`014-kids-db-location`)

**Created**: 2026-10-29

**Status**: Implemented (not committed)

**Input**: User description: "Feature 014: the plugin's SQLite database kids.db (kid profiles, the parent code verifier,
usage, bonuses, channels and picks) lives in `/var/lib/jellyfin/plugins/Jellyfin.Plugin.Kids/`, next to the code
folder `plugins/Kids_<version>/`. That folder looks like a stray old install, and an admin (and an AI agent) nearly
deleted it; losing it loses every kid's settings. (1) Research from Jellyfin's source where plugin data belongs and
pick a location that is conventional, survives plugin updates and uninstall/reinstall as intended, is in Jellyfin's
backups if possible and doesn't look like junk. (2) Migrate from the old path on startup safely: copy or SQLite backup
with no connection open, verify (integrity check, row counts), switch, leave the old file renamed
(`kids.db.migrated-<date>`) for one release, log every step; idempotent and crash-safe, never losing data or starting
an empty database; if both exist prefer the newer one, otherwise refuse with a clear error and overwrite nothing.
(3) README and docs/install.md say where the data lives, what to back up and not to delete it; a README.txt written
by the plugin next to the database. (4) The data location shown read-only on the plugin's settings page.
(5) Version 0.0.10.0 with a changelog, not published; unit tests for fresh install, old only, new only, both, a crash
mid-copy, a locked or corrupt old database and WAL files present; dev-check passes, including a migration from an
old-path database."

Builds on [feature 1](../001-kids-plugin-foundation/spec.md) (`kids.db`, schema migrations) and every later feature that
stores data in it ([2](../002-kids-parent-lock/spec.md), [3](../003-kids-allowance-tracking/spec.md),
[6](../006-kids-live-tv/spec.md), [12](../012-kids-avatar/spec.md), [13](../013-bonus-undo-break/spec.md)).

## Clarifications

### Session 2026-10-29

No question was sent to the household: the coordinator asked for the recommended defaults, recorded here and in
Assumptions. The research behind them is in [research.md](research.md).

- **New location: `<data dir>/data/kids/`**, the server's own data folder (`IApplicationPaths.DataPath`; on Debian
  `/var/lib/jellyfin/data/kids/kids.db`, in the official Docker image `/config/data/kids/kids.db`), where
  `jellyfin.db` and the databases of Playback Reporting, Intro Skipper and others live. It is not inside `plugins/`,
  so nobody mistakes it for an install, and Jellyfin never cleans it up (R1–R4).
- **Jellyfin's built-in backup cannot include it**: the 10.11+/12.x backup copies a fixed list of folders and no
  plugin's data, wherever it lives (R5). The documentation says so and tells admins to back up the folder, or the
  whole data directory as Jellyfin's own docs advise for manual backups. Export and import stay on the roadmap.
- **Copy method**: SQLite's online backup from the old database (which reads its WAL) into a temporary file next to
  the new one, before any other connection is opened; verified with `integrity_check`, the schema version and the
  same rows in every table; then renamed into place. A copy is never visible under the final name until it is
  verified.
- **The old file is renamed, not deleted**: `kids.db.migrated-<yyyyMMdd>` (with its `-wal`/`-shm` if any are left),
  plus a README.txt in the old folder that says where the data went. Removing the leftovers is for a later release.
- **Both databases present**: if they hold the same data (an interrupted migration), finish the migration. If they
  differ and one was written clearly later (more than 2 seconds), use that one and rename the other aside
  (`kids.db.older-<yyyyMMdd-HHmmss>`), never deleting it, except when the later one holds no kid and no parent lock
  while the other does (typically an empty database made by a downgrade): then refuse. Otherwise refuse too. Refusing
  means: an error in the log naming both files and what to do, kids data unavailable until a restart, no file
  touched.
- **Locked or corrupt old database**: no migration, nothing created at the new place, an error in the log; kids data
  is unavailable this run, exactly as when the database cannot be opened today. The next start tries again.
- **README.txt** next to the database, rewritten at startup when missing or outdated, in plain English.
- **Dashboard**: a read-only "Data" section at the end of the Kids page with the folder, the database file and what
  to back up; read through a new admin-only call, `GET /Kids/Admin/Storage`, never in the page HTML (served without
  authentication).
- **Constitution**: its Technical Constraints say the database is "under the plugin's data folder". The wording is
  amended (PATCH 1.0.3) to "under the server's data folder (`<data dir>/data/kids`)".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Updating keeps every kid's settings, in a place that looks like data (Priority: P1)

An admin updates the plugin to 0.0.10.0 and restarts Jellyfin. The kids, the parent code, today's screen time and
bonuses, channels and picks are all still there. The database now lives in Jellyfin's data folder, next to Jellyfin's
own database, with a README.txt that says what it is. The plugins folder holds only the plugin itself, the
configurations folder and a renamed copy of the old database with a note.

**Why this priority**: it is the whole point: the data must survive, and must stop looking like junk.

**Independent Test**: on a server with a 0.0.9.0 database in the old folder, start 0.0.10.0: the kids and the lock are
unchanged through the API; the new file exists with README.txt; the old one is renamed; the log shows each step.

**Acceptance Scenarios**:

1. **Given** a database only at the old place, **When** the server starts, **Then** it is copied to the new place,
   verified, used, and the old file is renamed `kids.db.migrated-<date>`; every step is logged.
2. **Given** the old database has unmerged write-ahead log files, **When** it is migrated, **Then** the rows in them
   are in the new database.
3. **Given** a fresh install (no database anywhere), **When** the server starts, **Then** an empty database is created
   at the new place only, and nothing is written in the plugins folder.
4. **Given** the migration already happened, **When** the server starts again, **Then** nothing is copied or renamed
   (idempotent).

---

### User Story 2 - A migration interrupted or blocked never loses data (Priority: P1)

The server loses power while copying, or the old database is locked by another program or damaged. After the next
start the kids data is either fully migrated or still entirely at the old place; never an empty database in use.

**Why this priority**: a half-done migration that starts an empty database looks like "all settings gone" and the next
write makes it real.

**Independent Test**: unit tests for each case, with real files.

**Acceptance Scenarios**:

1. **Given** a crash during the copy (a partial temporary file), **When** the server starts, **Then** the partial file
   is discarded and the migration runs again from the untouched old database.
2. **Given** a crash after the new database was put in place but before the old one was renamed, **When** the server
   starts, **Then** it sees the same data in both, uses the new one and renames the old one.
3. **Given** the old database cannot be read (locked, corrupt, failing its integrity check), **When** the server
   starts, **Then** nothing is created at the new place, the old file is left as it was, an error is logged and kids
   data is unavailable until the problem is fixed and the server restarted.
4. **Given** both databases exist with different data, **When** one was written clearly later, **Then** it is used and
   the other is renamed aside, not deleted; **when** it is unclear which is right, **Then** the server refuses with an
   error naming both files and touches neither.

---

### User Story 3 - Admins know where the data is and what to back up (Priority: P2)

An admin opens Dashboard → Kids and, at the bottom, sees "Data: /var/lib/jellyfin/data/kids/kids.db. Back up this
folder; deleting it loses every kid's settings." The README, the install guide and the README.txt in the folder say the
same, and that Jellyfin's own backup does not include it.

**Independent Test**: the Kids page shows the folder from `GET /Kids/Admin/Storage`; a kid's token gets 403; the page
HTML holds no path.

**Acceptance Scenarios**:

1. **Given** an admin, **When** the Kids page loads, **Then** a read-only Data section shows the folder and the
   database file, and says to back it up and not delete it.
2. **Given** the database is unavailable (refused migration, unreadable file), **Then** the section says so and points
   to the server log.
3. **Given** a kid or an anonymous caller, **When** they ask for the storage information, **Then** 403 or 401.
4. **Given** the data folder, **Then** it holds a README.txt saying what the folder is, that it must not be deleted and
   how to back it up.

### Edge Cases

- The old folder holds only `kids.db.migrated-*` files and the README: no migration (nothing to migrate).
- A temporary copy is left from a crash but the old database is gone (moved by hand): the temporary file is never used
  as the database. It is discarded with a warning, and with no database anywhere the start is a fresh install. The
  migration never renames the old database while a temporary copy exists, so this happens only by hand.
- A `kids.db.migrated-<date>` of the same day already exists (a second migration on the same day after a manual
  restore): `-2` (then `-3`…) is added to the name; nothing is overwritten.
- The old database has a newer schema than this plugin (a downgrade): it is copied as is, and the existing "newer
  schema" refusal applies to the new file.
- The data folder is read-only: the migration fails, logged; the old database is untouched.
- Downgrading to 0.0.9.0 after migrating: 0.0.9.0 creates an empty database at the old place. The changelog says how
  to go back (copy the database back first). Updating again then finds both: the later one is used unless it holds
  no kid and no lock while the other does, in which case the server refuses.

## Requirements *(mandatory)*

### Functional Requirements

**Location**

- **FR-001**: The database MUST live at `<server data folder>/kids/kids.db` (`IApplicationPaths.DataPath`).
- **FR-002**: Nothing MUST be created in `plugins/` on a fresh install.

**Migration**

- **FR-003**: Before the first connection, the plugin MUST detect a database at the old place
  (`<plugins>/Jellyfin.Plugin.Kids/kids.db`).
- **FR-004**: It MUST copy it, including any write-ahead log content, into a temporary file next to the new place, with
  no other connection open on either file.
- **FR-005**: It MUST verify the copy: integrity check "ok", the same schema version and the same rows in every table
  as the old database; any mismatch aborts, deletes the temporary file and leaves the old database as it was.
- **FR-006**: Only a verified copy MUST be renamed to the final name; then the old database (and any `-wal`/`-shm`) is
  renamed `kids.db.migrated-<yyyyMMdd>` (with `-2`, `-3`… added when that name is taken). Nothing is deleted except
  the plugin's own temporary file.
- **FR-007**: Every step MUST be logged (detected, copied with row counts, verified, switched, renamed), and every
  failure as an error with the paths involved.
- **FR-008**: The migration MUST be idempotent and resume after a crash at any step without losing data.
- **FR-009**: When both databases exist: the same data → finish the migration; different data and one clearly newer
  (last write more than 2 s later) → use it and rename the other `kids.db.older-<yyyyMMdd-HHmmss>`, unless the newer
  has no kid profile and no parent lock while the older has one, which is refused; anything else → refuse.
- **FR-010**: A refusal or failure MUST leave kids data unavailable for the run (as today when the database cannot be
  opened) and MUST NOT create an empty database at the new place.
- **FR-011**: The old folder MUST get a README.txt saying where the data moved and that the renamed file is a backup
  kept for one release.

**Documentation and the folder note**

- **FR-012**: The plugin MUST write a README.txt next to the database explaining what the folder holds, not to delete
  it, and what to back up, rewriting it when missing or different.
- **FR-013**: README.md and docs/install.md MUST say where the data lives on common installs, that Jellyfin's backup
  does not include it, what to back up and that the folder must not be deleted.

**Dashboard**

- **FR-014**: `GET /Kids/Admin/Storage` (administrators only) MUST answer the data folder, the database file and
  whether the database is available.
- **FR-015**: The Kids page MUST show this read-only in a Data section, as text, with the backup advice.

**Release**

- **FR-016**: Version 0.0.10.0 with a changelog; nothing published or tagged. The client API version is unchanged (the
  storage call is dashboard-only, like `Admin/PickCandidates`).
- **FR-017**: Unit tests MUST cover: fresh install, old only, new only, both (same, newer old, newer new, ambiguous,
  newer but empty), a crash mid-copy, a crash after the switch, a locked and a corrupt old database, and WAL files.
- **FR-018**: dev-check MUST pass, including a migration from an old-path database on the dev server.

### Key Entities

- **Kids data folder**: `<data>/kids/` with `kids.db` (+ `-wal`, `-shm` while open) and `README.txt`.
- **Legacy folder**: `<plugins>/Jellyfin.Plugin.Kids/` with, after migration, `kids.db.migrated-<date>` and
  `README.txt`.
- **Temporary copy**: `<data>/kids/kids.db.migrating`, only during a migration.
- **Storage information**: folder, database path, whether available.

## Success Criteria *(mandatory)*

- **SC-001**: After updating, 100% of kid profiles, the parent lock, usage and bonuses are present, row for row.
- **SC-002**: No interruption at any step of the migration leaves the server using an empty or partial database.
- **SC-003**: The plugins folder holds no live data after migrating; an admin can tell from the dashboard or the
  README.txt where the data is in under a minute.
- **SC-004**: A second start after migrating changes no file.

## Assumptions

- One Jellyfin process uses the data folder at a time; the old plugin version is not running during the migration.
- The new and old folders are on the same filesystem as each other's temporary files (renames are within one folder).
- Folder modification times are reliable to 2 seconds (FAT and some network shares round to 2 s).
- Usage is pruned to 90 days, so the database is small (a full copy and comparison take well under a second).
- Old leftovers (`kids.db.migrated-*`, the old README.txt) are removed by a later release, not this one.
