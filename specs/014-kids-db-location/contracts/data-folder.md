# Contract: the kids data folder (feature 14)

Paths and names are in [data-model.md](../data-model.md). This file fixes the notes the plugin writes and the log
lines admins and dev-check rely on.

## `<DataPath>/kids/README.txt`

Written at each start when missing or different (UTF-8, LF line endings). `{folder}` is the absolute folder.

```text
Jellyfin Kids plugin data
=========================

This folder holds the Jellyfin Kids plugin's data: kid profiles, the parent code
(stored only as a verifier), screen time used, bonuses, live TV channels and picks.

DO NOT DELETE THIS FOLDER. It is not a leftover: deleting it loses every kid's
settings. Uninstalling or updating the plugin keeps it on purpose.

Files:
  kids.db          the database (SQLite)
  kids.db-wal,
  kids.db-shm      SQLite's working files while Jellyfin runs; keep them with kids.db
  README.txt       this note, rewritten by the plugin

Backing up:
  Jellyfin's own backup (Dashboard > Backups) does not include plugin data.
  Back up this folder with Jellyfin stopped, or, with sqlite3 installed, copy the
  database while Jellyfin runs:
    sqlite3 "{folder}/kids.db" ".backup kids-backup.db"
  Restore by stopping Jellyfin and putting kids.db back here (without old -wal/-shm
  files).

To remove the plugin's data for good, uninstall the plugin, stop Jellyfin and delete
this folder.

Folder: {folder}
More: https://github.com/aldoborrero/jellyfin-kids/blob/main/docs/install.md#your-data
```

## `<plugins>/Jellyfin.Plugin.Kids/README.txt`

Written after a migration (or when the old database is renamed aside).

```text
Jellyfin Kids: old data folder
==============================

Up to version 0.0.9.0 the Jellyfin Kids plugin kept its database here. Since
0.0.10.0 it lives in:

  {folder}

{file} is the old database, renamed and kept as a backup for one release.
After checking that your kids and their settings are in Dashboard > Kids, you
may delete this folder. A later version of the plugin removes it.
```

## Log lines

Category `Jellyfin.Plugin.Kids.Storage.KidsDatabase` (the migration logs through the database's logger). Values in
`{}` are structured properties.

| Level | Message |
|---|---|
| Information | `Kids data found at the old location {OldPath}; moving it to {NewPath}` |
| Information | `Copied {OldPath} to {TempPath} ({Rows} rows: {Tables})` |
| Information | `Verified the copy {TempPath}: integrity ok, schema version {Version}, same rows as {OldPath}` |
| Information | `Kids data now in {NewPath}` |
| Information | `Renamed the old database {OldPath} to {RenamedPath}; it is kept as a backup for one release` |
| Information | `Kids data is in {NewPath}, and {OldPath} holds the same data: finishing an interrupted move` |
| Warning | `Kids data is in both {NewPath} (last written {NewWrite}) and {OldPath} (last written {OldWrite}); using the newer {UsedPath} and keeping the other as {RenamedPath}` |
| Warning | `Discarded an unfinished copy {TempPath} from an earlier start` |
| Error | `Kids data is in both {NewPath} … and {OldPath} …, and the plugin cannot tell which to keep; kids data is unavailable. Keep the one you want, move the other out of its folder, and restart Jellyfin` |
| Error | `Cannot move the kids data from {OldPath} to {NewPath}: {Reason}; the old database is untouched and kids data is unavailable until this is fixed and Jellyfin restarts` |
| Warning | `Cannot write {Path}` (README; never blocks the database) |
