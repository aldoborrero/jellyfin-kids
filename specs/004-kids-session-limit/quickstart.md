# Quickstart: validating the Kids session limit

This proves feature 4 end to end. The API is described in
[contracts/kids-api-v1-session-limit.md](contracts/kids-api-v1-session-limit.md), the editor in
[contracts/dashboard-session-limit.md](contracts/dashboard-session-limit.md) and the rules in
[data-model.md](data-model.md).

## Prerequisites

- Everything from features 1–3: the devshell, and `JELLYFIN_KIDS_DEV=$PWD/.dev dev` on <http://127.0.0.1:8096>. Run it
  only while testing, then stop it.

## 1. Unit tests

```sh
dotnet build
dotnet test Jellyfin.Plugin.Kids.slnx
```

All tests pass, and the build has 0 warnings. The new tests cover:

- **Validation**: 5 and 120 kept, null kept, 4, 0, -1 and 121 rejected with `SessionLimitMinutes`, reported together
  with other field errors.
- **Storage**: the value round-trips through insert and update; a stored row without the key reads null.
- **Migration 3 → 4**: every profile gets `"SessionLimitMinutes": null` and keeps its other settings, a row that
  already has the key is unchanged, and the lock and usage tables are untouched.
- **Service**: a PUT without the field clears it (full replacement).
- **Info**: the features list includes `sessionLimit` after the earlier ones.

## 2. Automated checks against the dev server

```sh
scripts/dev-check.sh
```

Checks a–aq still pass. The new ones:

| # | Check | Expected |
|---|---|---|
| ar | anonymous `GET /Kids/Info` | 200, `Features` includes `sessionLimit` |
| as | admin `PUT` alice's profile with `SessionLimitMinutes` 4, then 121 | 400 keyed `SessionLimitMinutes` both times; alice's revision is unchanged |
| at | admin `PUT` with `SessionLimitMinutes` 30 and `DailyAllowanceMinutes` 60; alice reads her profile; alice `PUT`s 120 | 200 with 30; 200 with 30 and 60; 403 |
| au | admin `PUT` without the field | 200 and the answer has no `SessionLimitMinutes` key; alice's read has none either |

The script leaves alice with no session limit.

## 3. Migration on a real database

1. Stop the dev loop with a 0.0.3 `kids.db` that holds alice and bob (any run of feature 3 leaves one).
2. Start the 0.0.4 build. The server log has "Kids database … migrated from schema version 3 to 4".
3. alice's and bob's profiles read as before, with no `SessionLimitMinutes`.

## 4. Dashboard (manual or Playwright)

1. In alice's editor, the "Session limit (minutes)" field is empty ("No limit").
2. Set 45 and save; reload: it shows 45. Clear it and save; reload: empty.
3. Enter 200: the editor shows "Between 5 and 120 minutes, or empty for no limit." next to the field and sends
   nothing. Time the whole step: under 1 minute (SC-004).

## Last run (2026-10-19, Jellyfin 12.2 dev loop started from the worktree)

| Step | Result |
|---|---|
| §1 Unit tests | `dotnet build` (also `--no-incremental`) with 0 warnings; `dotnet test`: 307 passed, 0 skipped (291 from features 1–3, 16 new, theories included) |
| §2 `scripts/dev-check.sh` | 115 PASS lines, 0 FAIL, on two runs in a row (feature 3 ended at 106). New: ar (`Features` ends with `sessionLimit`), as (4 and 121 → 400 keyed `SessionLimitMinutes`, revision unchanged), at (30 with allowance 60 round-trips, alice reads it, alice's PUT → 403), au (a PUT without the field clears it and the answer has no key, nor does alice's read). `Info` reported `0.0.4.0` |
| §3 Migration | Fresh `.dev` logged "migrated from schema version 0 to 4". Then, with `jellyfin` stopped, the database was rolled back to the v3 shape by hand (`SessionLimitMinutes` removed from both profiles, one given `DailyAllowanceMinutes` 45, `user_version` 3); on restart the log had "migrated from schema version 3 to 4", both profiles read as before (revisions 1 and 3, allowance 45 kept) with no `SessionLimitMinutes`. The allowance was cleared afterwards |
| §4 Dashboard | 12/12 in Chromium 155 (Playwright script outside the repo, signed in as the dev admin), no page errors: empty field with "No limit"; 45 saved (200) and kept after a reload, within 60 s of sign-in (SC-004); 200 shows "Between 5 and 120 minutes, or empty for no limit." on input and blocks the save with no request sent (native validation turned off to reach the page's own check); a save rewritten in flight to 121 got the server's 400 "Must be between 5 and 120." next to the field and stored nothing; cleared and saved → empty after a reload; every PUT carried `SessionLimitMinutes` |
| Artifacts | Plugin 0.0.4.0 (`build.yaml` and `Directory.Build.props`) |
| 12.0 and 12.1 | Not checked (feature 1's accepted risk) |
