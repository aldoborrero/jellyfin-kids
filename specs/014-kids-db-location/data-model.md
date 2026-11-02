# Data Model: A safe home for the kids data

The database schema does not change (version 5, see [feature 13](../013-bonus-undo-break/data-model.md)). This feature
changes where the file lives, and adds files around it.

## Folders and files

| Path | Written by | Meaning |
|---|---|---|
| `<DataPath>/kids/kids.db` | `KidsDatabase` | The live database (`-wal`, `-shm` beside it while open). |
| `<DataPath>/kids/README.txt` | `KidsDatabase`, each start when missing or different | What the folder is; do not delete; how to back it up. |
| `<DataPath>/kids/kids.db.migrating` | the migration, only while it runs | Unverified copy. Never opened as the database; deleted at the next start if left by a crash. |
| `<DataPath>/kids/kids.db.older-<yyyyMMdd-HHmmss>` | the migration, "both" case | The database at the new place that lost to a newer old one. Kept. |
| `<plugins>/Jellyfin.Plugin.Kids/kids.db` | plugin 0.0.9.0 and earlier | The legacy database. Read-only to this feature, then renamed. |
| `<plugins>/Jellyfin.Plugin.Kids/kids.db.migrated-<yyyyMMdd>` (`-2`, `-3`… added if taken; also for `older-`) | the migration | The legacy database after a successful migration, kept for one release. `-wal`/`-shm` keep their suffix after the new name. |
| `<plugins>/Jellyfin.Plugin.Kids/kids.db.older-<yyyyMMdd-HHmmss>` | the migration, "both" case | A legacy database that lost to a newer new one. Kept. |
| `<plugins>/Jellyfin.Plugin.Kids/README.txt` | the migration | Where the data moved. |

`<DataPath>` is `IApplicationPaths.DataPath` (Debian `/var/lib/jellyfin/data`, Docker `/config/data`); `<plugins>` is
`IApplicationPaths.PluginsPath`.

## Migration states

Evaluated in this order at the first `EnsureInitialized`, before any connection to `kids.db` is opened.

| Old `kids.db` | New `kids.db` | Outcome | Action |
|---|---|---|---|
| no | no | `NothingToDo` (fresh) | Discard a stray `.migrating` with a warning; `KidsDatabase` creates the new database |
| no | yes | `NothingToDo` | None |
| yes | no | `Migrated` or `Failed` | Copy → verify → switch → rename old |
| yes | yes, same fingerprint and version | `Finished` | Rename old to `migrated-` (resumes a crash after the switch) |
| yes | yes, new later by > 2 s | `KeptNew` | Rename old to `older-` |
| yes | yes, old later by > 2 s | `Migrated` | Rename new (+wal/shm) to `older-`, then migrate old |
| yes | yes, the later one has no kid and no lock, the other has some | `Refused` | Nothing touched |
| yes | yes, within 2 s | `Refused` | Nothing touched |

`Refused` and `Failed` make `EnsureInitialized` false for the process (kids data unavailable), and nothing is created at
the new place. "Last write" = latest of the file's and its `-wal`'s modification time, read before opening either.

## Database snapshot (verification)

Read from one open connection:

- `IntegrityOk`: `PRAGMA integrity_check` is exactly one row `ok`.
- `UserVersion`: `PRAGMA user_version`.
- `RowCounts`: rows per table (`sqlite_master` type `table`, by name).
- `Fingerprint`: SHA-256 over each table name and each row of `SELECT * … ORDER BY rowid`, each value tagged with its
  type (null, integer, real, text, blob).
- `HasKidsData`: at least one row in `kid_profile` or `parent_lock` (tables missing count as none).

A copy is verified when `IntegrityOk` and its `UserVersion` and `Fingerprint` equal the source's.

## Storage information (API)

`StorageInfoDto(string Folder, string DatabasePath, bool Available)`: the folder, the database file, and whether the
database could be opened (`EnsureInitialized`).
