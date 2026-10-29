# Research: A safe home for the kids data

Sources: the Jellyfin checkout at `v12.2` (`~/Dev/aldoborrero/jellyfin-fork`, commit `ca0f16e`, paths below relative to
it), the plugin clones in `.reference/` (Playback Reporting `814e507`, Intro Skipper `1bb2af7`, KodiSyncQueue
`a1cdd8d`, Streamyfin, the plugin template), and the Jellyfin docs page
<https://jellyfin.org/docs/general/administration/backup-and-restore/> (read 2026-10-29).

## R1. What `BasePlugin.DataFolderPath` is, and why the current folder is exactly that

- `BasePlugin<T>`'s constructor sets `dataFolderPath = Path.Combine(ApplicationPaths.PluginsPath,
  Path.GetFileNameWithoutExtension(assemblyFilePath))` (`MediaBrowser.Common/Plugins/BasePluginOfT.cs:50`). The
  `_<Version>` fallback (`:51-55`) runs only when `Version` is set, and `Version` is assigned by `SetAttributes` on the
  next line (`:57`), so it is always null there: the folder is always `<plugins>/Jellyfin.Plugin.Kids`. It is
  documented only as "where the plugin can store any miscellaneous files needed"
  (`MediaBrowser.Common/Plugins/BasePlugin.cs:45-49`).
- `PluginsPath` is `<ProgramDataPath>/plugins` (`Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs:58`).
- `KidsDatabase` rebuilds the same path from `IApplicationPaths.PluginsPath` because services are registered before
  the plugin instance exists (`src/Jellyfin.Plugin.Kids/Storage/KidsDatabase.cs`, `docs/plan.md` §2.1, §6.2).

**So yes: today's folder is just the default `DataFolderPath`.** The plugin itself installs as
`<plugins>/Kids_<version>/` (`Emby.Server.Implementations/Updates/InstallationManager.cs:538, 598`).

**Why it looks like junk**: the plugin manager treats *every* subfolder of `plugins/` as a plugin candidate
(`Emby.Server.Implementations/Plugins/PluginManager.cs:793-806`). A folder without `meta.json` is read as an old-style,
unversioned install, named after the folder (`:759-786`, "No metafile, so lets see if the folder is versioned. TODO:
Phase this support out"), and is then dropped only because it has no DLLs (`:870-871`). So
`plugins/Jellyfin.Plugin.Kids/` has exactly the shape of a pre-manifest plugin install, beside `Kids_0.0.9.0/`.
Jellyfin itself never deletes it (uninstall deletes only the install folder, `PluginManager.cs:702-707` via
`InstallationManager.cs:401-404`; old-version cleanup only deletes folders with the same manifest name,
`PluginManager.cs:808-866`), but a human tidying up stray installs would.

## R2. Where well-known plugins keep their databases

| Plugin | Location | Source |
|---|---|---|
| Playback Reporting (official) | `<DataPath>/playback_reporting.db` | `.reference/jellyfin-plugin-playbackreporting/Jellyfin.Plugin.PlaybackReporting/Data/ActivityRepository.cs:35` |
| Kodi Sync Queue (official) | `DbRepo(applicationPaths.DataPath)` | `.reference/jellyfin_jellyfin-plugin-kodisyncqueue/Jellyfin.Plugin.KodiSyncQueue/KodiSyncQueuePlugin.cs:26` |
| Intro Skipper | `<DataPath>/introskipper/introskipper-v2.db` (+ a cache db); it moved there from older files and imports the legacy db read-only | `.reference/intro-skipper_intro-skipper/IntroSkipper/Db/IntroSkipperDatabasePaths.cs:17-49` (GPL: studied, not copied) |
| Streamyfin | `<DataPath>/streamyfin_plugin.db` | `.reference/streamyfin_jellyfin-plugin-streamyfin/Jellyfin.Plugin.Streamyfin/Plugin.cs:27` |
| Plugin template (official) | No database; only the XML configuration | `.reference/jellyfin-plugin-template` (no `DataPath`/`DataFolderPath` use) |

None of them keep a database in `DataFolderPath`. The convention is the server's `DataPath`, and the most careful one
(Intro Skipper) uses its own subfolder there.

## R3. What `DataPath`, `PluginConfigurationsPath` and `CachePath` are for

- **`DataPath`** = `<ProgramDataPath>/data`, created at startup (`BaseApplicationPaths.cs:36`) and marked
  `.jellyfin-data` (`:92`). It holds the server's own state: `jellyfin.db`, `collections`, `playlists`,
  `ScheduledTasks`, `subtitles`, `attachments`, `keyframes`, `trickplay`, `backups`, `device.txt`
  (`BaseApplicationPaths.cs:79, 82`; `Emby.Server.Implementations/Collections/CollectionManager.cs:123`;
  `Emby.Server.Implementations/Library/PathManager.cs:39, 41`; `Emby.Server.Implementations/Devices/DeviceId.cs:38`;
  `Jellyfin.Server/Migrations/JellyfinMigrationService.cs:321`). Debian: `/var/lib/jellyfin/data`; Docker:
  `/config/data`; Windows: `%ProgramData%\Jellyfin\Server\data`. Nothing in the server enumerates or cleans unknown
  subfolders of it (every use is a fixed subpath).
- **`PluginConfigurationsPath`** = `<plugins>/configurations` (`BaseApplicationPaths.cs:61`): where
  `BasePlugin<T>.ConfigurationFilePath` writes each plugin's XML settings (`BasePluginOfT.cs:131`). It is for
  serialised configuration, still inside `plugins/`, and Jellyfin (and the plugin's own `POST
  /Plugins/{id}/Configuration`) rewrites files there.
- **`CachePath`** (`BaseApplicationPaths.cs:73`) is disposable: Jellyfin writes a `CACHEDIR.TAG` into it "so backup
  tools (e.g. Restic, Borg) … can exclude it from backups" (`:93`, `:103-121`), and admins and containers clear it
  freely. **Never** for the kids database.

## R4. Uninstall, reinstall and updates

- An update installs `Kids_<new>/` and deletes the older install folder (`PluginManager.cs:808-866`). Neither
  `DataFolderPath` nor `DataPath/kids` is touched: both survive updates.
- Uninstall calls `OnUninstalling()` (the plugin doesn't override it) and deletes only the install folder
  (`InstallationManager.cs:401-404`, `PluginManager.cs:702-707`). The data stays, and a reinstall finds the kids again:
  the intended behaviour for a parental tool (an uninstall to fix a broken update must not wipe the household's kids).
  Removing the data on purpose is a manual step, documented.

## R5. Jellyfin's backup and restore (10.11+ / 12.x)

- **Backup** (`Jellyfin.Server.Implementations/FullSystemBackup/BackupService.cs:407-443`) writes: the database (as
  JSON per table), top-level `*.xml`/`*.json` of the config dir, `config/users`, `config/ScheduledTasks`, the root
  folder, `data/collections`, `data/playlists`, `data/ScheduledTasks`, and optionally `data/subtitles`,
  `data/trickplay` and metadata. **No plugin data from any folder**: not `plugins/` (not even
  `plugins/configurations`), and no other subfolder of `data/`.
- **Restore** (`BackupService.cs:125-160`) extracts every `Data/…` entry of the archive into `DataPath`, overwriting
  files, and deletes nothing else. So a restore never removes `data/kids/`.
- **Docs**: the built-in backup covers "Database", "Metadata", "Subtitles" and "Trickplay"; plugins are not mentioned.
  For a manual backup they say "Copy your data and configuration directories", e.g. `/var/lib/jellyfin` and
  `/etc/jellyfin` on Debian.
- **Conclusion**: no location is included in the built-in backup, so it cannot decide the choice. `DataPath` is,
  however, the folder admins already treat as "the data" (it holds `jellyfin.db`), so any backup that keeps Jellyfin's
  own database keeps the kids data too. The documentation must say plainly that the built-in backup does not include
  it. (The constitution already requires export/import before a stable release.)

## R6. Decision: `<DataPath>/kids/kids.db`

- **Decision**: `Path.Combine(IApplicationPaths.DataPath, "kids", "kids.db")`, with a plugin-written `README.txt` in the
  folder.
- **Rationale**: conventional (the place the official Playback Reporting and Kodi Sync Queue plugins and Intro Skipper
  use, R2); beside `jellyfin.db`, so it reads as data, not an install (R1, R3); outside `plugins/`, so neither updates
  nor uninstall touch it and no "stray plugin" cleanup does (R4); kept by the manual backups Jellyfin documents, and
  never removed by a restore (R5). A subfolder rather than a bare file keeps the db, its `-wal`/`-shm` and the README
  together, as Intro Skipper does. The folder name `kids` matches the plugin's name and its `/Kids` API; the README
  removes any doubt about what it is.
- **Alternatives considered**:
  - Keep `DataFolderPath`: what the API offers, but it is the folder that was nearly deleted (R1).
  - `PluginConfigurationsPath`: still inside `plugins/`, mixes a live SQLite file with XML Jellyfin rewrites, and is
    not backed up either.
  - `CachePath`: disposable by design (R3).
  - A file directly in `DataPath` (`kids.db`): workable, but `-wal`/`-shm` and the README would sit loose among
    Jellyfin's files.
  - The config dir (`/etc/jellyfin`): only top-level `*.xml`/`*.json` there are backed up, and config dirs are often
    read-only or managed by configuration tools; a database does not belong there.

## R7. Copy method: SQLite online backup into a temporary file

- **Decision**: open the old database (read-write, never create: `Mode=ReadWrite`, no pooling, private cache) and use
  `SqliteConnection.BackupDatabase` into `<new>/kids.db.migrating`, before `KidsDatabase` opens any connection.
- **Rationale**: the backup API copies the database as SQLite sees it, *including committed frames still in the
  `-wal` file*, so the WAL case needs no special handling. Copying three files by hand risks a `-wal` from another
  generation, and a file copy of a database with an unmerged WAL is not a consistent snapshot. Opening read-write is
  needed because reading a WAL database needs its `-shm`. Closing the last connection checkpoints the WAL into the
  old file, which changes no data.
- **Alternatives**: `VACUUM INTO` (same consistency but rebuilds pages; fine too, but the backup API keeps the copy
  page-identical, which makes comparison trivial); raw file copy (rejected above).

## R8. Verification

- **Decision**: on the temporary copy, `PRAGMA integrity_check` must answer the single row `ok`; `PRAGMA user_version`
  must equal the old one's; and a *content fingerprint* must match: SHA-256 over every table (from `sqlite_master`,
  ordered by name), every row (`ORDER BY rowid`), every value with its type. Row counts per table are logged.
- **Rationale**: row counts (what the user asked for) are logged and implied by the fingerprint, which also catches a
  row with a changed value. The database is small (usage pruned at 90 days), so the full read is milliseconds.
  The same fingerprint decides R10's "same data" case.

## R9. The switch, crash safety and idempotence

Order of steps, each logged:

1. Detect: `old = <plugins>/Jellyfin.Plugin.Kids/kids.db`, `new = <data>/kids/kids.db`.
2. Remove a leftover `<data>/kids/kids.db.migrating` (and its `-journal`/`-wal`/`-shm`) from an earlier crash.
3. Back up `old` → `kids.db.migrating`; close both connections.
4. Verify `kids.db.migrating` against `old` (R8); on failure delete it and stop.
5. `File.Move(kids.db.migrating, kids.db)` without overwrite (atomic rename in one folder).
6. Rename `old` → `kids.db.migrated-<yyyyMMdd>` (and any `-wal`/`-shm` to the same name plus suffix, so SQLite still
   pairs them); write the old folder's README.txt.

Crash analysis:

| Crash after | State on next start | What happens |
|---|---|---|
| 2–4 | `old`, maybe a partial `.migrating`, no `new` | Step 2 removes it; migration reruns from the untouched `old` |
| 5 | `old` and `new`, same content | "Both" with equal fingerprints → skip to step 6 |
| 6 (part) | `new`, `old` renamed, maybe a stray `-wal`/`-shm` | No `old`: nothing to do (an orphan `-wal` is never applied to another file: SQLite checks its salt) |

`new` only ever appears under its final name as a verified, complete copy, and `old` is renamed only after that, so no
crash can leave the server without a database that has the data, and the plugin never creates an empty `new` while
`old` exists: `KidsDatabase` creates a database only when the migration reports that there is nothing to migrate.

## R10. Both databases present

- **Decision**:
  1. Same fingerprint and schema version → interrupted migration: finish step 6.
  2. Different, and one's last write (latest mtime of the file and its `-wal`) is more than 2 s after the other's →
     prefer it, **unless** it holds no kid profile and no parent lock while the other does (the typical "downgraded to
     0.0.9.0, which created an empty database at the old place" case): refuse. Preferring `new` renames `old` to
     `kids.db.older-<yyyyMMdd-HHmmss>`; preferring `old` renames `new` (with `-wal`/`-shm`) to
     `kids.db.older-<…>` in the new folder and then migrates `old` normally.
  3. Otherwise refuse: log an error naming both files, their sizes and last writes, and the fix ("keep the one you
     want, move the other out of its folder, restart"); `EnsureInitialized` is false; no file is touched.
- **Rationale**: matches "prefer the newer one, otherwise refuse and don't overwrite". mtimes are taken before either
  file is opened (opening checkpoints the WAL and changes the mtime). 2 s covers FAT/SMB timestamp rounding. Nothing is
  ever deleted: the loser keeps its data under a name that says what it is.

## R11. Locked or corrupt old database

- **Decision**: any `SqliteException` while opening, backing up or verifying the old database (`SQLITE_BUSY` from
  another program's exclusive lock, `SQLITE_NOTADB`/`SQLITE_CORRUPT`, an integrity check that is not `ok`) aborts the
  migration: the temporary file is deleted, `old` is untouched, an error names the path and the reason, and kids
  data is unavailable for this run. A short busy timeout (the default 30 s command timeout is too long at startup;
  use 5 s) bounds the wait on a lock.
- **Rationale**: this is today's behaviour for an unopenable database (`KidsDatabase.Initialize` logs and returns
  false). Starting an empty database instead would look like "settings gone" and the next write would make it real.
  `EnsureInitialized` is cached for the process (`Lazy`), so a restart retries; that matches how the plugin treats
  its database everywhere else.

## R12. README.txt in the folder

- Plain text, rewritten at startup when missing or different, so it tracks the plugin version that wrote it. It says:
  what the folder is, that deleting it loses every kid's settings, the files in it, how to back it up (stop Jellyfin,
  or `sqlite3 kids.db ".backup …"`), that Jellyfin's built-in backup does not include it, and that uninstalling the
  plugin keeps it. Failing to write it is a warning, never a reason to refuse the database.
- The old folder gets its own README.txt after a migration: "the data moved to …; `kids.db.migrated-*` is a copy of
  the old database kept for one release; you may delete this folder after checking the kids in the dashboard."

## R13. Showing the location on the dashboard

- **Decision**: `GET /Kids/Admin/Storage` (admins only, like `Admin/PickCandidates`) answers `{ Folder, DatabasePath,
  Available }`, and the Kids page shows it in a read-only Data section at the end.
- **Rationale**: the page HTML is served without authentication (constitution IV), so the path, a per-household
  detail, must come from an authenticated call. A dashboard-only admin route is not part of the client contract, so
  `ApiMinorVersion` stays 2 (as for `Admin/PickCandidates` and `Admin/LiveTvChannels`).

## R14. Constitution wording

The Technical Constraints say "Plugin state in a SQLite database under the plugin's data folder". "Plugin's data
folder" reads as `DataFolderPath`. Amend to "under the server's data folder (`<data dir>/data/kids`)": a PATCH
(clarification of a constraint, no principle changes), version 1.0.3, with a Sync Impact Report entry.
