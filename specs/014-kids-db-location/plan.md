# Implementation Plan: A safe home for the kids data

**Branch**: `014-kids-db-location` (spec directory `014-kids-db-location`) | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/014-kids-db-location/spec.md`

## Summary

- **Location**: `kids.db` moves from `<plugins>/Jellyfin.Plugin.Kids/` (the default `DataFolderPath`, which looks like a
  pre-manifest plugin install) to `<DataPath>/kids/`, beside `jellyfin.db`, where the official Playback Reporting and
  Kodi Sync Queue plugins and Intro Skipper keep theirs (research R1–R6).
- **Migration** (`Storage/LegacyDataMigration.cs`), run by `KidsDatabase.Initialize` before its first connection: SQLite
  online backup of the old database (WAL included) into `kids.db.migrating`, verified by `integrity_check`, schema
  version and a content fingerprint, renamed into place, then the old file renamed `kids.db.migrated-<yyyyMMdd>`
  (R7–R9). Both present: same data → finish; one clearly newer → use it, rename the other `kids.db.older-<stamp>`;
  otherwise refuse (R10). Locked or corrupt → no migration, data unavailable this run, nothing created (R11).
- **Notes**: `README.txt` beside the database (rewritten when outdated) and in the old folder after migrating (R12).
- **Dashboard**: `GET /Kids/Admin/Storage` (admins) and a read-only Data section on the Kids page (R13).
- **Docs and release**: README, docs/install.md, docs/plan.md, the constitution's storage line (PATCH 1.0.3, R14);
  0.0.10.0 in `build.yaml` (changelog) and `Directory.Build.props`.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); dashboard pages in plain ES modules (no build step).

**Primary Dependencies**: `Jellyfin.Controller` / `Jellyfin.Model` 12.0.0 (compile only): `IApplicationPaths.DataPath`
and `PluginsPath`. `Microsoft.Data.Sqlite` (shipped with the server): `SqliteConnection.BackupDatabase`.

**Storage**: `kids.db` schema unchanged (5); only its folder changes.

**Testing**: xUnit with real files in temp folders: fresh, old only, new only, both (same, old newer, new newer,
ambiguous, newer but empty), crash during the copy (partial `.migrating`) and after the switch (fault hook),
locked old database (exclusive locking mode), corrupt old database (garbage bytes, and a page-damaged file failing
`integrity_check`), unmerged WAL, a taken `migrated-` name, README contents. `scripts/dev-check.sh` gains section `g`
(storage call, layout, README, migration log when the dev state was migrated). A manual migration run on the dev
server from a 0.0.9.0 database (quickstart §3).

**Target Platform**: Jellyfin 12.0–12.2 plugin (`targetAbi 12.0.0.0`); the dev loop runs 12.2.

**Project Type**: server plugin (one project plus tests).

**Performance Goals**: the migration reads the small database three times (copy, verify, compare) once per server;
later starts only check that two files exist.

**Constraints**: never delete data; `TreatWarningsAsErrors`; the path never in anonymous responses or page HTML; no git
writes, no household server.

**Scale/Scope**: one file of a few hundred KB.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How the design complies |
|---|---|
| I. Leverage Jellyfin | Uses Jellyfin's own `DataPath`, the folder the server and other plugins use for databases (R2, R3), instead of inventing a location; nothing reimplemented. |
| II. The server decides | No rule changes. Usage, bonuses and the lock move row for row (fingerprint-verified); nothing can lower usage. |
| III. Parents never locked out | Admins and API keys are untouched. A refused or failed migration makes kids data unavailable, exactly as an unreadable database does today: the plugin fails open for parents (no kids, no blocking), and the log says what to do. No admin-owned Jellyfin state is touched. |
| IV. Secrets stay secret | The verifier moves inside the database, never logged; the README holds no household data. The folder path is shown only through an admin-only call, never in the anonymous page HTML or `/Kids/Info`. |
| V. Stable contract | No client API change; `Admin/Storage` is a dashboard-only admin route like `Admin/PickCandidates`. `ApiMinorVersion` stays 2. The plugin still loads on 12.0 (`DataPath` and `PluginsPath` exist on `IApplicationPaths` since 10.x). |
| VI. Tested where it matters | Every migration path unit-tested with real SQLite files; dev-check and a manual migration on the dev server; 0 warnings. |
| Technical constraints | "Under the plugin's data folder" becomes "under the server's data folder (`<data dir>/data/kids`)": PATCH amendment 1.0.3 (R14). The note that plugin data is not in Jellyfin's backups stays true (R5). |

No violations after the amendment.

## Project Structure

### Documentation (this feature)

```text
specs/014-kids-db-location/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── kids-api-admin-storage.md    # GET /Kids/Admin/Storage
│   ├── data-folder.md               # the folders, file names, README.txt texts, log lines
│   └── dashboard-data.md            # Kids page Data section
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── Storage/KidsDatabase.cs           # new folder, legacy folder, runs the migration, writes README.txt
├── Storage/LegacyDataMigration.cs    # new: detect, copy, verify, switch, rename, both, refuse
├── Storage/MigrationOutcome.cs       # new: NothingToDo, Migrated, Finished, KeptNew, Refused, Failed
├── Storage/DatabaseSnapshot.cs       # new: integrity check, version, row counts, fingerprint, kid/lock presence
├── Storage/DataFolderNotes.cs        # new: README.txt texts
├── Api/StorageInfoDto.cs             # new
├── Api/KidsController.cs             # GET Admin/Storage
└── Configuration/kids.html, kids.js  # Data section

tests/Jellyfin.Plugin.Kids.Tests/     # LegacyDataMigrationTests, KidsDatabaseTests (README, wiring), ApiDtoTests
scripts/dev-check.sh                  # section g
README.md, docs/install.md, docs/plan.md, build.yaml, Directory.Build.props, .specify/memory/constitution.md
```

**Structure Decision**: migration and snapshot helpers beside `KidsDatabase` in `Storage/`; one type per file as the
analyzers require.

## Phases

- **Phase 0**: [research.md](research.md) R1–R14.
- **Phase 1**: [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md).
- **Phase 2 ([tasks.md](tasks.md))**: snapshot → migration → database wiring and notes → storage call → dashboard →
  docs and release → build, tests, dev-check, manual migration.

## Complexity Tracking

None.
