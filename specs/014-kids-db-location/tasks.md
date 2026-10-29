# Tasks: A safe home for the kids data

**Input**: Design documents from `specs/014-kids-db-location/` (plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md)

**Tests**: required by the spec (FR-017) and constitution VI: xUnit with real SQLite files for every migration path;
`scripts/dev-check.sh` section `g` against the dev server; a manual migration from a 0.0.9.0 dev database
(quickstart §3).

## Phase 1: Setup

- [X] T001 Bump the version to 0.0.10.0 in `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`) and `build.yaml` (`version`; changelog: the database moves to `<data dir>/data/kids/kids.db` with a verified, crash-safe move from the old folder, which keeps a renamed copy; README.txt in the folder; the Data section on the Kids page; how to downgrade)

## Phase 2: Foundational

- [X] T002 `DatabaseSnapshot` in `src/Jellyfin.Plugin.Kids/Storage/DatabaseSnapshot.cs` (data-model "Database snapshot"): `Read(SqliteConnection)` → `IntegrityOk` ("`PRAGMA integrity_check` is exactly one row `ok`"), `UserVersion`, `RowCounts` (tables from `sqlite_master` type `table`, by name), `Fingerprint` ("SHA-256 over each table name and each row of `SELECT * … ORDER BY rowid`, each value tagged with its type"), `HasKidsData` ("at least one row in `kid_profile` or `parent_lock` (tables missing count as none)"); `SameDataAs(other)` = same version and fingerprint
- [X] T003 `MigrationOutcome` (`NothingToDo`, `Migrated`, `Finished`, `KeptNew`, `Refused`, `Failed`) in `src/Jellyfin.Plugin.Kids/Storage/MigrationOutcome.cs`

## Phase 3: User Story 1 - Updating keeps every kid's settings (P1)

**Independent test**: an old-only folder migrates; fresh creates only the new file; a second run changes nothing.

- [X] T004 [US1] `LegacyDataMigration` in `src/Jellyfin.Plugin.Kids/Storage/LegacyDataMigration.cs` (research R7–R9, data-model "Migration states"): `Run()` for old only: discard a stray `.migrating`; `BackupDatabase` from the old file (`Mode=ReadWrite`, no pooling, private cache, 5 s busy timeout) into `kids.db.migrating`; close; verify with `DatabaseSnapshot`; `File.Move` without overwrite; rename old + `-wal`/`-shm` to `kids.db.migrated-<yyyyMMdd>` (`-2`, `-3`… added when taken); old-folder README; log lines of `contracts/data-folder.md`; an internal step hook for crash tests
- [X] T005 [US1] `DataFolderNotes` in `src/Jellyfin.Plugin.Kids/Storage/DataFolderNotes.cs`: the two README texts of `contracts/data-folder.md`, `Write(path, text, logger)` only when missing or different, warning on failure
- [X] T006 [US1] `KidsDatabase` in `src/Jellyfin.Plugin.Kids/Storage/KidsDatabase.cs`: public ctor uses `Path.Combine(DataPath, "kids")` and legacy `Path.Combine(PluginsPath, "Jellyfin.Plugin.Kids")`; `Folder` property; `Initialize` runs the migration first (`Refused`/`Failed` → false, nothing created), then creates and migrates the schema, then writes README.txt; doc comments updated
- [X] T007 [US1] Tests in `tests/Jellyfin.Plugin.Kids.Tests/LegacyDataMigrationTests.cs`: fresh install, old only (rows, lock, bonus ids kept; renamed file; README; log lines), new only, idempotent second run, unmerged WAL (rows only in `-wal` arrive), `migrated-` name taken; `KidsDatabaseTests` for the README and fresh install not touching the legacy folder

## Phase 4: User Story 2 - Interrupted or blocked migrations lose nothing (P1)

**Independent test**: unit tests for each crash point, lock, corruption and "both" case.

- [X] T008 [US2] "Both" handling in `LegacyDataMigration` (research R10): mtimes before opening; same data → `Finished`; newer by > 2 s → `KeptNew` (rename old to `kids.db.older-<yyyyMMdd-HHmmss>`) or rename new aside and migrate old; "the later one has no kid and no lock, the other has some" → `Refused`; within 2 s → `Refused`; error text per contract
- [X] T009 [US2] Failure handling (research R11): any `SqliteException`, `IOException`, `UnauthorizedAccessException` → delete the temp file, `Failed`, error with the reason; old untouched
- [X] T010 [US2] Tests in `LegacyDataMigrationTests.cs`: crash mid-copy (partial `.migrating` garbage, and a fault after the copy), crash after the switch, both (same, new newer, old newer, ambiguous, newer but empty), locked old database (another connection in exclusive locking mode), corrupt old database (garbage bytes; a damaged page failing `integrity_check`); in every failure: no new `kids.db`, the old file byte-identical

## Phase 5: User Story 3 - Admins know where the data is (P2)

**Independent test**: `GET /Kids/Admin/Storage` as admin, kid, anonymous; the Data section on the Kids page.

- [X] T011 [US3] `StorageInfoDto(Folder, DatabasePath, Available)` in `src/Jellyfin.Plugin.Kids/Api/StorageInfoDto.cs`; `GET Admin/Storage` in `src/Jellyfin.Plugin.Kids/Api/KidsController.cs` (admins only, 403 otherwise) per `contracts/kids-api-admin-storage.md`; `ApiDtoTests` for the DTO
- [X] T012 [US3] Kids page `src/Jellyfin.Plugin.Kids/Configuration/kids.html` and `kids.js`: Data section per `contracts/dashboard-data.md`, text only, loaded on `viewshow`; header comment updated
- [X] T013 [US3] `scripts/dev-check.sh` section `g` (quickstart §2: `ga`–`ge`)
- [X] T014 [P] [US3] Docs: README (storage line in How it works, a "Your data" note under Install), `docs/install.md` "Your data" section (paths for Debian, Docker, Windows; not in Jellyfin's backup; what to back up; do not delete; uninstall keeps it; downgrade), `docs/plan.md` §2.1, §2.6, §6.2 and the module list
- [X] T015 [P] Constitution PATCH 1.0.3 in `.specify/memory/constitution.md` (storage line, Sync Impact Report)

## Phase 6: Polish

- [X] T016 `dotnet build -c Release` with 0 warnings and `dotnet test` green
- [X] T017 Dev loop on a free port: start with a 0.0.9.0 (`main`) build and populate, then this branch on the same state (quickstart §3); `scripts/dev-check.sh` green; restart for idempotence; stop the dev loop. Done on port 8496: 0.0.9.0 (HEAD, exported with `git archive`) provisioned and populated, plus one usage row and one bonus added by hand; then this branch on the same state: the move was logged step by step, `sqlite3 .dump` of the new database identical to the old one, the API answers unchanged, `scripts/dev-check.sh` 291 checks passed (`ga`–`ge` included), and a restart moved nothing again. A fresh state created only `data/kids/`. Browser check not run (it needs a jellyfin-web sign-in); the page was checked with `node --check`, its anonymous HTML holds no path, and `Admin/Storage` is covered by dev-check
- [X] T018 Review the diff, mark tasks done, set the spec status

## Dependencies

- T002, T003 → T004 → T006 → T007. T005 → T004, T006. T008–T009 extend T004 → T010.
- T011 → T012 → T013. T014, T015 any time. Polish last.

## Parallel examples

- T002 with T003 and T005; T011 with T008; T014 and T015 any time.

## Implementation strategy

MVP: US1 and US2 together (a migration without its failure handling is not shippable), then the storage call,
dashboard and docs, then validation on the dev server.
