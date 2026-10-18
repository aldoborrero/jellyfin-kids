# Quickstart: validating the Kids parent lock

This shows end to end that feature 2 works. The API is in
[contracts/kids-api-v1-parent-lock.md](contracts/kids-api-v1-parent-lock.md), the page in
[contracts/dashboard-parent-lock.md](contracts/dashboard-parent-lock.md), and the rules in
[data-model.md](data-model.md).

## Prerequisites

- Everything from [feature 1's quickstart](../001-kids-plugin-foundation/quickstart.md): the devshell, and
  `JELLYFIN_KIDS_DEV=$PWD/.dev dev` with `plugin`, `jellyfin` and `provision` green on <http://127.0.0.1:8096>.
  Provisioning creates `admin`/`admin`, and kids `alice` and `bob` without passwords.
- Stop the dev loop when you are done. It must run only while testing.

## 1. Unit tests

```sh
dotnet test Jellyfin.Plugin.Kids.slnx
```

All tests pass, and `dotnet build` has 0 warnings. The new tests cover:

- **Verifier compatibility** (SC-001):
  - the RFC 7914 PBKDF2-HMAC-SHA256 vector;
  - verifiers produced by Wholphin's `PasscodeHasher` (digits and remote) that verify on the server;
  - server-made verifiers that check against an independent derivation;
  - malformed verifiers that never verify (Wholphin's own list).
- **Code and import validation**: lengths, alphabets, case, iteration, salt and hash bounds.
- **Throttle**, with a fake clock:
  - the 6th wrong code within 30 s is refused;
  - waits double and are capped at 15 min;
  - the per-user cap of 20 spans devices;
  - a right code resets the counts;
  - refused attempts are not counted.
- **Grants**:
  - grants are bound to user and device;
  - they expire at 5 min;
  - `RevokeAll` and `RevokeUser` end them;
  - an unknown or malformed grant is refused;
  - several uses work within the lifetime.
- **Access rules**: profile mutation is admin or grant; the verifier is admin or grant; import and remove are admin
  only; status is any signed-in user.
- **Store**:
  - migration 1 → 2 keeps the profiles;
  - set, replace, remove;
  - import only when no lock exists, including two concurrent imports (one wins).

## 2. Automated checks against the dev server

```sh
scripts/dev-check.sh
```

Feature 1's checks a–m still pass. Check a now also asserts that `Info` has `parentLock` and no `LockType`. New
checks:

- The script first sets a known lock as admin (a `REMOTE` code). It signs in `alice` twice, as device `kids-check-A`
  and `kids-check-B`, and `bob` once.
- It uses `bob` for the throttle checks, so `alice`'s grants are not affected.
- At the end it waits out `bob`'s block and verifies the right code, so a second run starts clean. That makes the run
  about 40 s longer.

| # | Check | Expected |
|---|---|---|
| n | alice `GET /Kids/Parent/Lock` | 200, `LockType` `REMOTE`, `ChangedAt` present, no `Verifier` |
| o | alice (A) `POST /Kids/Parent/Verify` with a wrong code | 200, `Valid` false, no `Grant`; never 401 |
| p | bob: 5 wrong codes, then the right code | five `Valid:false` without `RetryAfterSeconds`, then `Valid:false` with `RetryAfterSeconds` > 0 (SC-003) |
| q | bob: the right code again, with a different `DeviceId` in the `Authorization` header but the same token | still `RetryAfterSeconds` > 0 (the header is ignored; SC-009) |
| r | alice (A) right code | 200, `Valid` true, a 43-char `Grant`, `ExpiresAt` about 5 min ahead |
| s | alice (A) `PUT /Kids/Users/{alice}/Profile` with the current revision, with the grant; then the same without the grant | 200 with revision +1; then 403 |
| t | alice (B) `PUT` profile with A's grant | 403, unchanged (SC-004) |
| u | alice (A) token with the grant but the `Authorization` header claiming `DeviceId="kids-check-B"` | 200 (the grant follows the registered device, not the header) |
| v | alice without a grant: `GET /Kids/Parent/Verifier`, `PUT /Kids/Parent/Lock`, `DELETE /Kids/Parent/Lock` | 403, 403, 403 (SC-006) |
| w | alice (A) with the grant: `GET /Kids/Parent/Verifier` | 200, `Verifier` matches `^pbkdf2-sha256\$120000\$[0-9a-f]{32}\$[0-9a-f]{64}$` |
| x | alice (A) with the grant: `PUT /Kids/Parent/Lock` `{LockType:"NUMERIC",Code:"2468",Current:<wrong>}`, then with the right `Current` | 400 keyed `Current`; then 200, `LockType` `NUMERIC` |
| y | alice (A) reuses the old grant after x | 403 (lock change revokes grants) |
| z | admin `PUT /Kids/Parent/Lock` `{LockType:"REMOTE",Verifier:<any valid>}` while a lock is set | 409 with the status body |
| aa | admin `PUT` with codes `123` (too short), `12a4` (digits type), `UDLX` (remote type) | 400 keyed `Code` each time |
| ab | admin `DELETE /Kids/Parent/Lock`; then `GET /Kids/Parent/Lock`; then Verify as alice | 204; `{}`; `Valid:false`. Then admin imports a Wholphin verifier (fixture) → 200, and its code verifies |
| ac | alice with an access schedule that excludes now (as in check m): Verify the right code | 200, `Valid` true (FR-006) |
| ad | `GET /web/ConfigurationPage?name=Kids` with no token | the HTML contains no lock type, verifier or date |

The script ends by leaving a known lock set, so the manual steps below can use it.

## 3. Grants vanish on restart (manual)

1. As alice, get a grant (`Verify` with the right code).
2. Restart `jellyfin` in process-compose.
3. Repeat the grant-carrying `PUT Profile`: 403. Bob's throttle state is gone too: his next right code gives a grant.

## 4. Wholphin compatibility (manual, optional with a TV)

1. In Wholphin's local kids mode, set a remote code, then run its migration to the server, signed in as the admin
   profile.
2. On the TV, the gate unlocks with the same code online.
3. With the server stopped, it unlocks offline from the cached verifier.

## 5. Dashboard (manual or Playwright)

1. Dashboard → Kids. The "Parent lock" section shows the current status. If there is none, it shows the
   recommendation.
2. Choose Remote buttons, enter `UDLRrg` twice, and save. The status shows "Remote buttons" and the time. Time it: the
   whole step takes under 1 minute (SC-007).
3. Enter mismatched codes, then `UDLX`. Both show an inline message, and nothing is sent.
4. Remove the lock, after a confirmation. The status shows that no lock is set, and an existing grant stops working.
5. View the page source of `ConfigurationPage?name=Kids`. It has no lock data.

## 6. Logs

`dev-check.sh` prints the codes it used and the last grant and verifier hash. Search `.dev/log` with `rg -F` for each
of them, and for `pbkdf2-sha256$`: nothing matches (SC-008). There are only "Parent lock
set/replaced/imported/removed by …" and throttle warnings.

## Last run (2026-10-18, Jellyfin 12.2 dev loop started from the worktree)

| Step | Result |
|---|---|
| §1 Unit tests | `dotnet build` with 0 warnings; `dotnet test`: 176 passed (59 from feature 1 plus 117 new, theories included) |
| SC-001 vectors | The RFC 7914 vector and two Wholphin-format verifiers (`2468` and `UDLRrg12`, made with `openssl kdf` and also reproduced by a test port of `PasscodeHasher.derive`) verify on the server; server-made verifiers match the same port |
| §2 `scripts/dev-check.sh` | Checks a–m and n–ad all pass: 66 PASS lines, 0 FAIL. Run three times in a row (the last against the final build); each run passes, including the 30 s wait for bob |
| §3 Restart | A grant that read the verifier (200) before the restart gets 403 after it. The lock status (type and `ChangedAt`) is the same after the restart, and the code still verifies (FR-005). Bob, blocked before the restart, verifies right away after it |
| §5 Dashboard | Driven in Chromium (nixpkgs 146) through jellyfin-web 12.2 with a Playwright script kept outside the repo; 18/18 pass, no page errors. Covered: no-lock status with the recommendation, Remove hidden without a lock, set a remote code (status with type and time, fields cleared, the code verifies), mismatched, `UDLX` and `12a4` codes refused inline with nothing sent, Remove with confirmation, the old grant refused afterwards, page source without lock data. The run found and fixed one bug: the Remove button ignored the `hidden` attribute (emby-button styles), so it now uses `style.display` |
| SC-005 / SC-007 | Server side of verify, read and change with the grant: 369 ms. Setting a code in the page took under 1 s, scripted. Neither was timed with a person |
| §6 Logs | The codes (`UDLRrgyb`, `2468`, `UDLRrg12`), four grants, two verifier hashes and `pbkdf2-sha256$` appear nowhere in the server log or the process-compose output; `kids.db` does not contain the code. The log has only "Parent lock set/replaced/imported/removed by …" and "attempts blocked for "bob" (this device) for 30 s" |
| Artifacts | Plugin 0.0.2.0 (`build.yaml` and `Directory.Build.props`) installs as `Kids_0.0.2.0`, and `Info` reports `0.0.2.0` with `["profiles","parentLock"]` |
| 12.0 and 12.1 | Not checked (feature 1's accepted risk) |
