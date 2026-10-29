# Quickstart: validating the new data location and its migration

## 1. Build and unit tests

```sh
dotnet build Jellyfin.Plugin.Kids.slnx -c Release     # 0 warnings (TreatWarningsAsErrors)
dotnet test Jellyfin.Plugin.Kids.slnx                 # LegacyDataMigrationTests cover every migration path
```

## 2. Dev loop and dev-check

From the devshell, on a free port (never against the household server):

```sh
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> dev
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> scripts/dev-check.sh
```

The dev server runs with `--datadir .dev/data`, so `DataPath` is `.dev/data/data` and the database is
`.dev/data/data/kids/kids.db`.

New checks, section `g` ([contracts](contracts/)):

| Check | Expected |
|---|---|
| `ga` admin asks for the storage information | 200, `Folder` ends in `/data/kids`, `DatabasePath` is `Folder/kids.db`, `Available` true |
| `gb` alice asks; nobody signed in asks | 403; 401 |
| `gc` the files | `kids.db` and `README.txt` in the folder; no `kids.db` in `.dev/data/plugins/Jellyfin.Plugin.Kids/` |
| `gd` README.txt | says "DO NOT DELETE THIS FOLDER" and names the folder |
| `ge` when this dev state was migrated (a `kids.db.migrated-*` exists) | the log has the found, copied, verified, now-in and renamed lines |

## 3. Migration from a 0.0.9.0 database on the dev server

1. Build and run 0.0.9.0 (the `main` branch) with a fresh `.dev`, let it provision, run `scripts/dev-check.sh` so the
   database has kids, a lock, usage and bonuses. Note `GET /Kids/Profiles` and `GET /Kids/Parent/Lock`.
2. Stop the dev loop. `.dev/data/plugins/Jellyfin.Plugin.Kids/kids.db` exists.
3. Start the dev loop from this branch on the same `.dev`. Expected:
   - `.dev/data/data/kids/kids.db` and `README.txt` exist; the old folder holds `kids.db.migrated-<today>` and its
     `README.txt`, and no `kids.db`;
   - the log has every migration line of [data-folder.md](contracts/data-folder.md), in order;
   - `GET /Kids/Profiles` and `GET /Kids/Parent/Lock` answer what step 1 noted;
   - `scripts/dev-check.sh` passes, `ge` included.
4. Restart once more: no migration line in the new log (idempotent).

Notes from running it:

- The devshell sets `JELLYFIN_KIDS_DEV=$PRJ_ROOT/.dev`, which overrides a value exported before `nix develop`. To run
  two trees on the same state, set it inside: `nix develop -c env JELLYFIN_KIDS_DEV=<state> JELLYFIN_KIDS_PORT=<port>
  process-compose up --tui=false -p <port + 1>`.
- 0.0.9.0 can be built without touching git state: `git archive <commit> | tar -x -C <dir>`.
- Don't run `dotnet build` in the tree the dev loop watches while dev-check runs: the build writes `.cs` files under
  `src/…/obj/`, the watch rebuilds and restarts Jellyfin, provisioning signs in again and the old admin token stops
  working (every later check answers 401).

## 4. Browser (jellyfin-web 12.2)

Dashboard → Kids: the last section, Data, shows the folder and the database, read-only, with the backup advice;
narrow window: paths wrap.
