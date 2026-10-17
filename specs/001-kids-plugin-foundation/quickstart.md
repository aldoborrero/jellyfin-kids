# Quickstart: validating the Kids plugin foundation

This is how to prove that feature 1 works end to end. The API is described in
[contracts/kids-api-v1.md](contracts/kids-api-v1.md), the pages in
[contracts/dashboard-pages.md](contracts/dashboard-pages.md), and the rules in [data-model.md](data-model.md).

## Prerequisites

- The devshell (`direnv allow` or `nix develop`).
- `dev` running: `plugin`, `jellyfin` and `provision` are all green, and the server is on <http://127.0.0.1:8096>.
  `provision` creates the admin `admin`/`admin` and the kids `alice` and `bob` without passwords, and writes the
  admin token to `.dev/admin.token`.
- For the user-deletion scenario, create a throwaway user first (step 6).

## 1. Unit tests

```sh
dotnet test Jellyfin.Plugin.Kids.slnx
```

Expected: everything passes. The tests cover:
- validation (cap range, unknown, duplicate and missing sections, duplicate picks, existing and missing picks);
- revision create, update and conflict, including two concurrent updates on the same revision;
- the access decision (self, admin, API key, other user);
- store migrations from an empty file;
- the orphan sweep (it skips when the user list is empty).

`dotnet build` stays free of warnings (`TreatWarningsAsErrors`).

## 2. Automated checks against the dev server

```sh
scripts/dev-check.sh
```

The script signs in as `admin` and as `alice` (empty password) and checks, printing PASS or FAIL for each:

| # | Check | Expected |
|---|---|---|
| a | `GET /Kids/Info` with no token | 200, `ApiVersion` 1, `Features` contains `profiles`, no user ids in the body |
| b | admin `PUT /Kids/Users/{alice}/Profile` `{"Revision":0}` (after a cleanup `DELETE`) | 200, `Revision` 1, defaults (cap 3, five sections) |
| c | alice `GET` her own profile | 200 |
| d | alice `GET` bob's profile (bob marked as a kid first) | 403 |
| e | alice `PUT` her own profile `{"Revision":1,"SearchAllowed":true}` | 403, and the profile is unchanged |
| f | admin `PUT` `{"Revision":1,"AutoplayEnabled":true,"AutoplayCap":2}`, then again with `Revision` 1 | 200 with `Revision` 2, then 409 whose body has `Revision` 2 and `AutoplayCap` 2 |
| g | admin `PUT` with `AutoplayCap` 11; with `Sections:[{"Section":"Shorts"}]`; with no `Revision` | 400 with `Errors` keyed `AutoplayCap`, `Sections[0]` and `Revision` respectively |
| h | admin `PUT` with a random GUID in `PickedItemIds` | 400 naming `PickedItemIds[0]` |
| i | any signed-in user `GET /Kids/Profiles` | 200, only `UserId` and `Revision` per kid |
| j | `GET /web/ConfigurationPage?name=Kids` with no token | 200, and the HTML contains no user names or GUIDs |
| k | create user `tmpkid`, mark them, delete them through `DELETE /Users/{id}` | the next `GET /Kids/Profiles` has no `tmpkid` |
| l | `GET /Kids/Admin/PickCandidates` as alice | 403 |
| m | admin gives `alice` an access schedule that excludes the current hour (the full policy is saved and always restored on exit); then `alice` reads her profile and `/Kids/Profiles`, and calls `GET /Users/Me` | 200, 200, and refused for `/Users/Me` (the schedule is active); the policy is restored afterwards |

## 3. Allowed hours (automated by check m; manual run optional)

Check m covers FR-014. To see it by hand: in the dashboard, give `alice` an access schedule that excludes now. As `alice`:
- `GET /Kids/Users/{alice}/Profile` and `GET /Kids/Profiles` still answer 200 (FR-014);
- a plain Jellyfin call such as `GET /Users/Me` is refused, which shows the schedule is active.

Remove the schedule afterwards.

## 4. Orphan sweep on startup (manual, once)

1. Mark `bob`.
2. Stop `jellyfin` in process-compose.
3. Delete `bob` directly in the database. The quicker alternative: disable the plugin, delete `bob` in the
   dashboard, then re-enable it.
4. Start `jellyfin` again.

Expected: `GET /Kids/Profiles` no longer lists `bob`, and the log has an "Removed 1 orphaned kid profile" line.

## 5. Dashboard pages (manual)

1. Open <http://127.0.0.1:8096>, sign in as `admin`, then go to Dashboard → Kids (menu entry).
2. The list shows `admin`, `alice` and `bob` with their kid toggles. Mark `admin`: their row shows the administrator
   warning. Unmark `admin` (a confirmation is asked first).
3. Mark `alice`, then use "Edit settings":
   - turn on autoplay with a limit of 2;
   - hide "New";
   - move "Picked" to the top;
   - add one collection and one playlist (create them first in the library if the dev media has none);
   - save.

   Time this: under 2 minutes (SC-003).
4. `GET /Kids/Users/{alice}/Profile` as `alice` matches every value.
5. Keep the editor open. Change `alice` with curl (admin `PUT` with the current revision), then save in the
   editor. It reports that the settings changed elsewhere and offers to load them. Nothing is overwritten
   (FR-026).
6. Delete the picked playlist in Jellyfin and reopen the editor. The pick shows as missing and can be removed.

## 6. Restart persistence

Restart `jellyfin` (touch a file under `src/`, or restart the process). `GET /Kids/Profiles` returns the same kids
and revisions (SC-008).

## 7. Jellyfin 12.0 and 12.1 (not checked)

These checks run only on 12.2, the dev loop's version. Loading on 12.0 and 12.1 relies on compiling against the
12.0.0 packages and is not verified at runtime. This is an accepted risk (plan.md "Decisions", Q2).
