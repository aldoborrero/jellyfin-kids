---

description: "Task list for the Kids parent lock"
---

# Tasks: Kids parent lock

**Input**: Design documents from `specs/002-kids-parent-lock/`

**Prerequisites**: plan.md, spec.md (clarified Q1–Q5), research.md (R1–R11), data-model.md,
contracts/kids-api-v1-parent-lock.md, contracts/dashboard-parent-lock.md, quickstart.md

**Tests**: included. Constitution VI requires unit tests for verifier compatibility, grants and throttling, and
access decisions. It also requires dev-server checks for behaviour that depends on Jellyfin. Write each phase's unit
tests first and see them fail.

**Organization**: tasks are grouped by user story, with story numbers from spec.md:

| Story | Title | Priority |
|---|---|---|
| US1 | Unlock on the kid's TV and get a grant | P1 |
| US2 | A grant authorizes parent changes on that TV only | P1 |
| US3 | Set and change the household code | P1 |
| US4 | Import Wholphin's verifier | P2 |
| US5 | Offline verifier for TVs | P2 |
| US6 | Lock management on the dashboard | P2 |

**Dependency order for the P1 stories is US3 → US1 → US2.** Verification needs a lock that an administrator has
set, and grants need verification. The grant + `Current` path of US3 (Story 3.2/3.3) needs grants, so it is
implemented in the US2 phase.

## Conventions for every task

- **Projects**: the plugin is in `src/Jellyfin.Plugin.Kids/` (namespaces `Jellyfin.Plugin.Kids.*`) and the tests
  in `tests/Jellyfin.Plugin.Kids.Tests/`. Match the style of `Api/KidsController.cs`, `Storage/KidsDatabase.cs`
  and the existing tests.
- **Build gate**: `TreatWarningsAsErrors` with StyleCop, SerilogAnalyzer and MultithreadingAnalyzer. Every public
  member has XML docs.
- **No new NuGet packages**:
  - crypto comes from the BCL: `Rfc2898DeriveBytes.Pbkdf2`, `CryptographicOperations.FixedTimeEquals`,
    `RandomNumberGenerator`, `SHA256`;
  - time comes from `TimeProvider`.
- **No mocking library**: fakes go in `tests/Jellyfin.Plugin.Kids.Tests/Fakes/`.
- **JSON**: Jellyfin's formatters, PascalCase in C#. The lock type is a **string** (`"NUMERIC"`/`"REMOTE"`) in DTOs,
  not a C# enum.
- **Policies**: every new endpoint uses `[Authorize(Policy = Policies.IgnoreParentalControl)]`. A refusal is
  `Forbid()` (403), never 401.
- **Secrets**: codes, grants, verifiers and token hashes are never logged (research R10). Request DTOs that carry
  `Code`, `Current` or `Verifier` override `ToString()` to redact them.
- **Unit tests**: `dotnet test Jellyfin.Plugin.Kids.slnx`.
- **Server checks**:
  - Start `JELLYFIN_KIDS_DEV=$PWD/.dev dev` (port 8096) only while testing, and stop it afterwards.
  - Run `scripts/dev-check.sh`.
  - Checks are named after quickstart.md §2.

---

## Phase 1: Setup

**Purpose**: test fakes and the dev-check helpers that later phases use.

- [X] T001 [P] Add `tests/Jellyfin.Plugin.Kids.Tests/Fakes/FakeTimeProvider.cs`: a `TimeProvider` subclass whose `GetUtcNow()` returns a settable instant, with an `Advance(TimeSpan)` method. Add `tests/Jellyfin.Plugin.Kids.Tests/Fakes/FakeDeviceDirectory.cs`, which implements `IDeviceDirectory` (created in T006) over a token → deviceId dictionary.
- [X] T002 [P] Extend `scripts/dev-check.sh` with helpers (no checks yet):
  - `login_as <user> <deviceId> [password]`: signs in with an `Authorization` header that uses that `DeviceId` and
    prints the token. Use it for alice A (`kids-check-A`), alice B (`kids-check-B`) and bob (`kids-check-bob`).
  - `req_dev <token> <deviceId> <method> <path> [json]`: like `req`, but with a chosen `DeviceId` header (for the
    header-spoof checks).
  - `req_grant <token> <grant> <method> <path> [json]`: adds `X-Kids-Grant`.

---

## Phase 2: Foundational (blocking)

**Purpose**: the verifier format, code rules, schema v2 and lock store, the device seam and the extended caller
context. Every story uses them.

### Tests first

- [X] T003 [P] Write `tests/Jellyfin.Plugin.Kids.Tests/VerifierTests.cs` (research R1):
  - **(a)** The RFC 7914 §11 vector. `P="passwd"`, `S="salt"`, `c=1`, first 32 bytes
    `55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc`. Built into
    `pbkdf2-sha256$1$73616c74$<hash>`, it verifies `passwd` and rejects `passwd2`.
  - **(b)** Two fixed verifiers produced by Wholphin's `PasscodeHasher.hash`: one for the digits code `2468` and one
    for the remote code `UDLRrg12`, both at 120 000 iterations. Each verifies its code and rejects a one-symbol change.
    - Produce them once, either with a throwaway Wholphin unit test at
      `Wholphin/.worktree/kids-mode/app/src/test/.../PasscodeHasherTest.kt` (do not commit there), or with
      `openssl kdf -keylen 32 -kdfopt digest:SHA256 -kdfopt pass:<code> -kdfopt hexsalt:<salt> -kdfopt iter:120000 PBKDF2`.
      The local openssl reproduces the RFC vector in (a), so either source is valid.
    - Record how they were produced in a comment.
  - **(c)** `Verifier.Create(code)` has the form `^pbkdf2-sha256\$120000\$[0-9a-f]{32}\$[0-9a-f]{64}$`. Two creates of
    the same code differ. The created verifier checks against an independent derivation with the instance
    `new Rfc2898DeriveBytes(code, salt, iterations, HashAlgorithmName.SHA256).GetBytes(32)`.
  - **(d)** Wholphin's malformed list never verifies: `""`, `"garbage"`, `"pbkdf2-sha256$0$00$00"`,
    `"pbkdf2-sha256$10$zz$00"`, `"md5$1$00$00"`.
  - **(e)** `Verifier.ValidateForImport` rejects these, with the error keyed `Verifier`:
    - iterations `9999` and `1000001`;
    - a salt of 15 or 65 bytes;
    - a hash of 31 or 33 bytes;
    - a wrong prefix;
    - 3 or 5 parts.

    It accepts iterations `10000` and `1000000` and salts of 16 and 64 bytes.
- [X] T004 [P] Write `tests/Jellyfin.Plugin.Kids.Tests/ParentCodeTests.cs` (data-model "Validation", research R8):
  - **Length**: "4–32 chars": `123` is rejected, and a 33-symbol code is rejected.
  - **`NUMERIC`**: allows "`0-9` only", so `0000` is accepted and `12a4` is rejected.
  - **`REMOTE`**: allows "`U D L R`, `0-9` and `r g y b p w f + - i`":
    - `UDLR`, `rgyb`, `pwf+-i09` are accepted;
    - `UDLX`, `udlr` (case-sensitive) and `R G` (space) are rejected.
  - **`LockType` parse**: "case-insensitive on input and stored uppercase", so `remote` → `REMOTE`. `CHALLENGE` and
    an empty value are rejected, with the error keyed `LockType`.
- [X] T005 [P] Extend `tests/Jellyfin.Plugin.Kids.Tests/KidsDatabaseTests.cs` and add
  `tests/Jellyfin.Plugin.Kids.Tests/ParentLockStoreTests.cs`:
  - **Migration**: a v1 database with two profiles migrates to `user_version` 2, and the profiles are unchanged.
  - **Store**:
    - `GetAsync` returns null when empty;
    - `UpsertAsync` sets and then replaces, updating `ChangedAt`;
    - `InsertIfAbsentAsync` returns false when a row exists;
    - **two concurrent `InsertIfAbsentAsync`** → exactly one true;
    - `ReplaceIfVerifierMatchesAsync(expectedVerifier, …)` returns false when the stored verifier changed meanwhile;
    - `DeleteAsync` returns false when empty;
    - the table refuses a second row (`CHECK (id = 1)`) and a `lock_type` outside `('NUMERIC','REMOTE')`.

### Implementation

- [X] T006 [P] Create `src/Jellyfin.Plugin.Kids/ParentLock/ParentLockType.cs`: the string constants `Numeric = "NUMERIC"` and `Remote = "REMOTE"`, plus `TryParse(string?, out string)`, which is case-insensitive and returns the uppercase form. Create `src/Jellyfin.Plugin.Kids/ParentLock/ParentCode.cs`: `Validate(string lockType, string? code)` returns the errors keyed `Code`, with min 4 and max 32, "ordinal and case-sensitive", and the alphabets from T004 (cite `ParentLockKeys.kt:31-37`). Create `src/Jellyfin.Plugin.Kids/ParentLock/IDeviceDirectory.cs` with `string? GetRegisteredDeviceId(string token)`.
- [X] T007 [P] Create `src/Jellyfin.Plugin.Kids/ParentLock/Verifier.cs` (research R1):
  - `Create(string code)`: 120 000 iterations, a 16-byte `RandomNumberGenerator` salt, a 32-byte hash, lowercase hex.
  - `Verify(string code, string verifier)`: exactly 4 parts, prefix `pbkdf2-sha256`, iterations > 0, and hex parsed
    with `Convert.FromHexString` (accepts either case). Any parse failure returns false. The hash comes from
    `Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, iterations, HashAlgorithmName.SHA256, expected.Length)`
    and is compared with `CryptographicOperations.FixedTimeEquals`.
  - `ValidateForImport(string?)`: "iterations an integer in 10 000–1 000 000; salt even-length hex of 16–64 bytes;
    hash hex of exactly 32 bytes".
  - Make T003 pass.
- [X] T008 Add migration step 2 to `src/Jellyfin.Plugin.Kids/Storage/KidsDatabase.cs` and set `SchemaVersion = 2`:

  ```sql
  CREATE TABLE parent_lock (
    id         INTEGER PRIMARY KEY CHECK (id = 1),
    lock_type  TEXT    NOT NULL CHECK (lock_type IN ('NUMERIC','REMOTE')),
    verifier   TEXT    NOT NULL,
    changed_at TEXT    NOT NULL
  );
  ```

  Update the log messages that say "kid profiles are unavailable" to say "kids data is unavailable".
- [X] T009 Create `src/Jellyfin.Plugin.Kids/Storage/ParentLockStore.cs` and the record
  `src/Jellyfin.Plugin.Kids/ParentLock/StoredLock.cs` (`LockType`, `Verifier`, `ChangedAt`), following
  `KidProfileStore` (it uses `KidsDatabase.WriteAsync` and throws if `EnsureInitialized()` is false):
  - `GetAsync`;
  - `UpsertAsync`;
  - `InsertIfAbsentAsync`: `INSERT … ON CONFLICT(id) DO NOTHING`, then the change count;
  - `ReplaceIfVerifierMatchesAsync(expected, new…)`: `UPDATE … WHERE id = 1 AND verifier = $expected`;
  - `DeleteAsync`.

  Make T005 pass.
- [X] T010 [P] Create `src/Jellyfin.Plugin.Kids/ParentLock/JellyfinDeviceDirectory.cs`. It returns
  `IDeviceManager.GetDevices(new DeviceQuery { AccessToken = token }).Items.FirstOrDefault()?.DeviceId` (research R2;
  cite `AuthorizationContext.cs:133-147`). Add a remark that the header `DeviceId` is deliberately not used.
- [X] T011 Extend `src/Jellyfin.Plugin.Kids/Auth/CallerContext.cs` with `string? DeviceKey` and `bool HasGrant`
  (default null and false; keep `FromAsync` working for existing callers). Create
  `src/Jellyfin.Plugin.Kids/Auth/CallerResolver.cs`:
  - It is scoped, built on `IAuthorizationContext`, `IDeviceDirectory`, `GrantService` (T020) and the request.
  - `ResolveAsync(HttpContext, bool needDevice)`:
    - `DeviceKey` is the registered device id for user tokens, or `"apikey:" + hex(SHA256(token))` for API keys;
    - it is computed only when `needDevice` is true or an `X-Kids-Grant` header is present;
    - `HasGrant = GrantService.IsValid(grant, userId, deviceKey)`.
  - Read the header only from `Request.Headers["X-Kids-Grant"]`, never from the query string.
  - Until T020 exists, stub `HasGrant` as false. This task and T020 are in different phases, so leave a
    `// T020` marker.
- [X] T012 Register in `src/Jellyfin.Plugin.Kids/PluginServiceRegistrator.cs`:
  - `serviceCollection.TryAddSingleton(TimeProvider.System)`;
  - `IDeviceDirectory → JellyfinDeviceDirectory` (singleton);
  - `ParentLockStore` (singleton);
  - `CallerResolver` (scoped).

  Later phases add `ThrottleService`, `GrantService` and `ParentLockService` (singletons) here. Keep the "declare
  types only" rule from the class remarks.

**Checkpoint**: `dotnet build` has 0 warnings, and T003–T005 pass.

---

## Phase 3: User Story 3 – Set and change the household code (Priority: P1), admin part

**Goal**: an administrator sets or replaces the lock, and any signed-in user reads the lock status. `Info` reports
`parentLock` and no longer has `LockType`.

**Independent Test**: as admin, `PUT /Kids/Parent/Lock {"LockType":"NUMERIC","Code":"2468"}` → 200. As alice,
`GET /Kids/Parent/Lock` → `NUMERIC`. `GET /Kids/Info` has `parentLock` and no `LockType`. Invalid codes → 400 keyed
`Code`.

### Tests first

- [X] T013 [P] [US3] Write `tests/Jellyfin.Plugin.Kids.Tests/LockAccessTests.cs` for the pure rules in
  `Auth/LockAccess.cs` (research R5), covering admin, API key, kid without a grant, kid with a grant, and another
  user:
  - `CanReadStatus`: anyone signed in;
  - `CanSetWithoutCurrent`: admin or API key;
  - `CanSetWithCurrent`: `HasGrant`;
  - `CanImport`: admin only;
  - `CanRemove`: admin only;
  - `CanReadVerifier`: admin or `HasGrant`.

  Also write the admin-path cases of `tests/Jellyfin.Plugin.Kids.Tests/ParentLockServiceTests.cs`, using a temp-dir
  `KidsDatabase` with `TempFolder` and `FakeTimeProvider`:
  - set when empty → status with `ChangedAt` = now;
  - replace without `Current` as admin;
  - an invalid code → errors keyed `Code`, and nothing is stored;
  - both or neither of `Code` and `Verifier` → error `Code`;
  - an unknown `LockType` → error `LockType`;
  - status when empty → no type;
  - admin remove → `Saved` then status empty; remove when empty → `NoLock`; a non-admin remove → `Forbidden`.

### Implementation

- [X] T014 [US3] Create `src/Jellyfin.Plugin.Kids/Auth/LockAccess.cs` (pure, as in T013) and
  `src/Jellyfin.Plugin.Kids/ParentLock/ParentLockService.cs`, with `GetStatusAsync`, `SetAsync` and `RemoveAsync`.
  `RemoveAsync` is admin only (`LockAccess.CanRemove`) and returns `Forbidden`, `NoLock` or `Saved` (FR-018). It is
  done here so check ab (T029) and the dashboard (T034) can use it.
  - **`SetAsync(CallerContext, LockUpdateInput)`** returns a `LockChangeResult` outcome: `Saved`, `Invalid` (errors),
    `Forbidden`, `Conflict` (with the status), `Throttled` (with retry seconds) or `NoLock`.
  - It owns the contract's **order of checks**: "body shape and `LockType` (400) → caller rights (403) → import
    conflict (409) → throttle (429) → `Current` (400) → `Code`/`Verifier` format (400)". This task implements only
    the admin code path. Leave the grant path and the import path returning `Forbidden` until T025 and T028.
  - On every successful change, call `GrantService.RevokeAll()` (FR-019). Until T020, it is a `// T020` marker.
  - Log at Information "Parent lock {Action} by {Caller}", with the user name or "API key", and never the code.
- [X] T015 [US3] Create the DTOs in `src/Jellyfin.Plugin.Kids/Api/`:
  - `LockStatusDto(string? LockType, DateTime? ChangedAt)`;
  - `LockUpdate { LockType, Code, Verifier, Current }`, with a `ToString()` that redacts `Code`, `Current` and
    `Verifier`.

  Create `src/Jellyfin.Plugin.Kids/Api/ParentLockController.cs`, routed `[Route("Kids/Parent")]` with the same
  `[Produces]` as `KidsController`:
  - `GET Lock` → 200 `LockStatusDto`, or 500 if the store is unavailable (never "no lock");
  - `PUT Lock` maps the `LockChangeResult` outcomes to 200, 400 (`ValidationProblemDetails` keyed by field), 403,
    409 (status body) and 429 (`Retry-After` header plus `{"RetryAfterSeconds":n}`).
    A store failure is not an outcome: the store exception propagates and Jellyfin answers 500 (spec edge case:
    "setting the lock fails with a server error");
  - `DELETE Lock` → 204, 403 (including grant holders), or 404 ProblemDetails "No parent lock is set.".

  Register `ParentLockService` in `PluginServiceRegistrator.cs`.
- [X] T016 [US3] Change `src/Jellyfin.Plugin.Kids/Api/KidsInfo.cs` and `KidsController.GetInfo`: remove `LockType`
  (record parameter and XML doc), and set `Features: ["profiles", "parentLock"]` (FR-022, research R7). Update every
  usage and test that constructs `KidsInfo`.
- [X] T017 [US3] Update `scripts/dev-check.sh`:
  - check a: also asserts `(.Features | index("parentLock")) != null and (has("LockType") | not)`;
  - add a setup step in which admin sets a known `REMOTE` code through `PUT /Kids/Parent/Lock`;
  - check n: alice `GET /Kids/Parent/Lock` → 200 with `.LockType == "REMOTE" and has("ChangedAt") and (has("Verifier") | not)`;
  - check aa: admin `PUT` with codes `123`, `12a4` (`NUMERIC`) and `UDLX` (`REMOTE`) → 400 with `errors | has("Code")`.

**Checkpoint**: unit tests pass. With the dev loop up, checks a, n and aa pass.

---

## Phase 4: User Story 1 – Unlock on the kid's TV and get a grant (Priority: P1) 🎯 MVP

**Goal**: `POST /Kids/Parent/Verify` checks the code and issues a device-bound grant, with throttling per registered
device and per user. It never answers 401.

**Independent Test**: with a lock set (US3), verify the right code as alice to get a grant, and a wrong code to get
200 `Valid:false`. As bob, five wrong codes, then the sixth is refused with `RetryAfterSeconds`. Changing the
`DeviceId` header does not reset that.

### Tests first

- [X] T018 [P] [US1] Write `tests/Jellyfin.Plugin.Kids.Tests/ThrottleServiceTests.cs` with `FakeTimeProvider`
  (research R3, data-model `ThrottleRecord`):
  - 5 wrong codes are answered normally, and the 6th within 30 s is blocked with retry ≤ 30;
  - after 30 s, one more wrong code blocks for 60 s, then 120 s, and the wait is capped at 900 s ("min(30 s ×
    2^(Failures − threshold), 15 min)");
  - attempts while blocked are not counted;
  - a right code clears the pair and the user records;
  - **the per-user cap**: 4 wrong codes on each of 5 devices (20 total) → every device is blocked;
  - another user is unaffected;
  - idle records are pruned 30 min after their block ends.

  Write `tests/Jellyfin.Plugin.Kids.Tests/GrantServiceTests.cs`:
  - the grant is 43 base64url chars;
  - `IsValid` is true for the same user and device;
  - it is false for another device, another user, a malformed value, or an unknown value;
  - it is false at 5 min + 1 tick and true at 4:59;
  - it can be used repeatedly within the lifetime;
  - `RevokeAll` and `RevokeUser(userId)` invalidate it (only that user's grants for `RevokeUser`);
  - the internal dictionary does not contain the raw grant value (check through an `internal` count or keys accessor).
- [X] T019 [P] [US1] Add the Verify cases to `tests/Jellyfin.Plugin.Kids.Tests/ParentLockServiceTests.cs`:
  - the right code → `Valid`, a grant, and `ExpiresAt` = now + 5 min;
  - a wrong code → not valid;
  - no lock → not valid, and the throttle count is unchanged (FR-009);
  - throttled → not valid with retry seconds, and the code is not checked;
  - a code longer than 256 chars → not valid without hashing;
  - a store failure → not valid;
  - a caller with no `DeviceKey` → not valid.

### Implementation

- [X] T020 [US1] Create `src/Jellyfin.Plugin.Kids/ParentLock/ThrottleService.cs` (research R3):
  - **Keys**: `(userKey, deviceKey)` with threshold 5, and `userKey` with threshold 20. `userKey` is the user id, or
    the API key's `deviceKey` for API keys.
  - **Storage**: `ConcurrentDictionary` with a per-record lock.
  - **API**: `TryBegin(userKey, deviceKey, out retryAfterSeconds)`, `RecordFailure(...)` and `RecordSuccess(...)`.
  - **Logging**: Warning "Parent lock attempts blocked for {User} on {Device} for {Seconds} s". Never log an API key
    hash: log "API key" instead.

  Create `src/Jellyfin.Plugin.Kids/ParentLock/GrantService.cs` (research R4):
  - `Issue(userId, deviceKey)` → `(grant, expiresAt)`;
  - `IsValid(grant, userId, deviceKey)`;
  - `RevokeAll()`;
  - `RevokeUser(Guid)`.

  Keys are the base64url of `SHA256(grantBytes)`, the TTL is 5 min, and expired entries are pruned on access. Both
  take `TimeProvider`. Register both as singletons. Resolve the `// T020` markers in `CallerResolver` and
  `ParentLockService`. Make T018 pass.
- [X] T021 [US1] Add `VerifyAsync(CallerContext, string code)` to `ParentLockService`, and the DTOs
  `src/Jellyfin.Plugin.Kids/Api/VerifyRequest.cs` (`Code`, redacting `ToString`) and
  `src/Jellyfin.Plugin.Kids/Api/VerifyResult.cs` (`Valid`, `Grant?`, `ExpiresAt?`, `RetryAfterSeconds?`).
  - **The flow**, in this order:
    1. A missing `DeviceKey` → not valid.
    2. A code longer than 256 chars → not valid.
    3. Throttle `TryBegin`.
    4. Read the lock. No lock → not valid, not counted.
    5. `Verifier.Verify`.
    6. Record the success or failure.
    7. Issue the grant.
  - Add `POST Verify` to `ParentLockController`. It always returns 200 for signed-in callers (FR-007); 400 only for
    an unreadable body. It calls `CallerResolver.ResolveAsync(…, needDevice: true)`.
  - Make T019 pass.
- [X] T022 [US1] Add dev-check checks o, p, q, r to `scripts/dev-check.sh` (quickstart §2):
  - **o**: a wrong code as alice A → 200 `.Valid == false and (has("Grant") | not)`.
  - **p**: bob, 5 wrong codes, then the right code → the 6th answer has `.RetryAfterSeconds > 0`.
  - **q**: bob's same token with `req_dev` claiming `DeviceId="spoofed"` → still `.RetryAfterSeconds > 0`.
  - **r**: alice A, the right code → `.Valid and (.Grant | length) == 43`.
  - At the end of the script, wait until bob's block ends (sleep the reported `RetryAfterSeconds` + 1) and verify the
    right code once, so a second run starts clean. Keep this block last: later tasks insert their checks before it,
    not after.

**Checkpoint**: unit tests pass, and dev-check o–r pass. This is the MVP: a parent can unlock from a kid's TV.

---

## Phase 5: User Story 2 – A grant authorizes parent changes on that TV only (Priority: P1)

**Goal**: `X-Kids-Grant` unlocks profile changes and the grant + `Current` code change. It works only for the same
user and registered device, within 5 min. It is revoked on any lock change, on user deletion and on restart.

**Independent Test**: a grant from alice A enables `PUT Profile` from A, is refused from B, and keeps working on A
even when the header claims B. After a code change, the old grant is refused.

### Tests first

- [X] T023 [P] [US2] Update `tests/Jellyfin.Plugin.Kids.Tests/ProfileAccessTests.cs`: `CanMutate` is true for admin,
  API key and a kid with `HasGrant`, and false for self or another user without a grant.
  Add to `ParentLockServiceTests.cs`:
  - a grant caller replaces the lock with the right `Current` → saved;
  - a wrong `Current` → error keyed `Current`, and it counts as a wrong try (throttle failures +1);
  - a missing `Current` → error `Current`;
  - while throttled → `Throttled`;
  - a grant caller sending `Verifier` → `Forbidden`;
  - after any change, `GrantService.IsValid` is false for the old grant;
  - **concurrency**: if the stored verifier changes between the check and the write, the result is a reload and
    retry, or a conflict. It never overwrites (uses `ReplaceIfVerifierMatchesAsync`).

### Implementation

- [X] T024 [US2] Change `src/Jellyfin.Plugin.Kids/Auth/ProfileAccess.cs`: `CanMutate => caller.IsAdmin || caller.HasGrant`,
  and update the remarks (the feature 2 promise is fulfilled). In `src/Jellyfin.Plugin.Kids/Api/KidsController.cs`:
  - `PutProfile` and `DeleteProfile` resolve the caller through `CallerResolver`, so grants are seen;
  - **`GetPickCandidates` must check `caller.IsAdmin` directly, not `ProfileAccess.CanMutate`**, so a grant does not
    open the admin-only chooser (FR-013).
- [X] T025 [US2] Implement the grant path of `ParentLockService.SetAsync` (Story 3.2/3.3, FR-016):
  - A grant caller (not admin) needs `Current`, which goes through the throttle like Verify. A wrong `Current` is
    recorded as a failure and returns `Invalid` keyed `Current`; throttled returns `Throttled`. A right `Current`
    calls `RecordSuccess`, which clears both records, as Verify does.
  - The write uses `ReplaceIfVerifierMatchesAsync(<verifier just checked>, …)`. On a mismatch, return `Invalid`
    keyed `Current` ("the code was changed meanwhile").
  - `RevokeAll` follows on success.
  - Also extend `src/Jellyfin.Plugin.Kids/Events/UserDeletedConsumer.cs` to call `GrantService.RevokeUser(user.Id)`
    (FR-011).
- [X] T026 [US2] Add dev-check checks s, t, u, x, y to `scripts/dev-check.sh`:
  - **s**: alice A `PUT /Kids/Users/{alice}/Profile` (current revision, with the grant) → 200 and revision +1; the
    same without the grant → 403.
  - **t**: alice B with A's grant → 403, and the profile is unchanged.
  - **u**: alice A's token with A's grant, but `req_dev` claiming `DeviceId="kids-check-B"` → 200.
  - **x**: alice A with the grant, `PUT /Kids/Parent/Lock` `{"LockType":"NUMERIC","Code":"2468","Current":"<wrong>"}`
    → 400 `errors | has("Current")`. Then with the right `Current` → 200 `.LockType == "NUMERIC"`.
  - **y**: the old grant on `PUT Profile` → 403.
  - **Grant scope** (FR-013, spec edge case): alice A with a fresh grant, `GET /Kids/Admin/PickCandidates` → 403.
  - **Restore**: admin sets the known `REMOTE` code again for later checks.

**Checkpoint**: dev-check s–y pass. The P1 stories are complete.

---

## Phase 6: User Story 4 – Import Wholphin's verifier (Priority: P2)

**Goal**: an admin imports a Wholphin verifier only when no lock is set. Otherwise the result is 409.

**Independent Test**: on a server with no lock, import a Wholphin fixture verifier, then verify its code. A second
import → 409, and nothing changes.

- [X] T027 [P] [US4] Add import cases to `ParentLockServiceTests.cs`:
  - admin import when empty → saved, and the Wholphin fixture code from T003 verifies;
  - import when a lock exists → `Conflict` with the current status, unchanged;
  - an invalid verifier → errors keyed `Verifier`;
  - two concurrent imports → one `Saved` and one `Conflict`;
  - a non-admin import → `Forbidden`.
- [X] T028 [US4] Implement the import path of `ParentLockService.SetAsync`. It is admin only. `ValidateForImport`
  runs in the contract's order (the conflict comes before the format check), then `InsertIfAbsentAsync`. False →
  `Conflict` with the status. Success → `RevokeAll`, and log "imported".
- [X] T029 [US4] Add dev-check checks z and ab to `scripts/dev-check.sh`:
  - **z**: admin import while a lock is set → 409 with a status body.
  - **ab**: admin `DELETE /Kids/Parent/Lock` → 204.
    `GET /Kids/Parent/Lock` → `{}`. Alice Verify → `Valid:false`. Admin imports the Wholphin `REMOTE` fixture
    verifier → 200. Copy the fixture into the script as a constant: it is the same string as T003(b)'s `UDLRrg12`
    vector. Alice verifies `UDLRrg12` → `Valid:true`. Finally admin sets the known code again.

---

## Phase 7: User Story 5 – Offline verifier for TVs (Priority: P2)

**Goal**: an admin or grant holder reads `{LockType, Verifier}`. Kids without a grant get 403.

**Independent Test**: with a grant, `GET /Kids/Parent/Verifier` returns a verifier that checks with Wholphin's
algorithm. Without a grant → 403. With no lock → 404.

- [X] T030 [US5] Add `GetVerifierAsync` to `ParentLockService`, `src/Jellyfin.Plugin.Kids/Api/VerifierDto.cs`
  (`LockType`, `Verifier`) and `GET Verifier` to `ParentLockController`:
  - `LockAccess.CanReadVerifier` false → 403;
  - no lock → 404 ProblemDetails "No parent lock is set.";
  - otherwise 200.

  Add unit cases to `ParentLockServiceTests.cs`.
- [X] T031 [US5] Add to `ParentLockServiceTests.cs`, now that grants exist:
  - removing the lock revokes every grant (FR-019);
  - a grant holder's remove → `Forbidden` (clarification Q4);
  - `GetVerifierAsync` is refused after a remove (404).
- [X] T032 [US5] Add dev-check checks v and w to `scripts/dev-check.sh`:
  - **v**: alice without a grant: `GET /Kids/Parent/Verifier`, `PUT /Kids/Parent/Lock` and `DELETE /Kids/Parent/Lock`
    → 403, 403, 403.
  - **w**: alice A with a fresh grant: `GET /Kids/Parent/Verifier` → 200, and
    `.Verifier | test("^pbkdf2-sha256\\$120000\\$[0-9a-f]{32}\\$[0-9a-f]{64}$")`.

---

## Phase 8: User Story 6 – Lock management on the dashboard (Priority: P2)

**Goal**: a "Parent lock" section on the Kids page (contracts/dashboard-parent-lock.md).

**Independent Test**: quickstart §5. Set a remote code from the page, see the status, reject mismatched or invalid
codes inline, remove the lock, and check that the page HTML has no lock data.

- [X] T033 [P] [US6] Add the static markup to `src/Jellyfin.Plugin.Kids/Configuration/kids.html`. It holds no data,
  and the existing "served without authentication" comment applies. The section has:
  - a status line;
  - the type select (Digits / Remote buttons);
  - code and confirmation inputs (`type="password"`, `autocomplete="off"`, `maxlength="32"`);
  - the remote symbol legend and the recommendation text, verbatim from the contract;
  - Save and Remove buttons.
- [X] T034 [US6] Implement the section in `src/Jellyfin.Plugin.Kids/Configuration/kids.js`:
  - **Load** on `viewshow` with `GET /Kids/Parent/Lock`. A 500 shows an error, never "no lock".
  - **Client-side validation** mirrors `ParentCode`: 4–32 symbols, the type's alphabet, case-sensitive, both fields
    equal.
  - **Save**: `PUT {LockType, Code}` → clear the fields, show the status and "Saved". A 400 shows `errors.Code` or
    `errors.LockType` inline.
  - **Remove**: `Dashboard.confirm` with the contract text, then `DELETE`. Both 204 and 404 show "No parent lock is
    set."
  - **Never** request the verifier, and never echo the code.
- [X] T035 [US6] Add dev-check check ad to `scripts/dev-check.sh`: `GET /web/ConfigurationPage?name=Kids` with no
  token. The status element is empty in the served HTML (no "Parent lock:" text), and the HTML contains neither
  `pbkdf2` nor an ISO date (`[0-9]{4}-[0-9]{2}-[0-9]{2}T`). The static `<option value="NUMERIC">` and
  `<option value="REMOTE">` are allowed, because they are not household data. Then run quickstart §5 by hand (or the out-of-repo Playwright script from feature 1).

---

## Phase 9: Polish and cross-cutting

- [X] T036 [P] Add dev-check check ac to `scripts/dev-check.sh`: reuse check m's schedule that excludes now, and
  verify the right code as alice inside that window → 200 `.Valid == true` (FR-006). Restore the policy as check m
  does.
- [X] T037 [P] Update `specs/001-kids-plugin-foundation/contracts/kids-api-v1.md`:
  - remove the `LockType` bullet under `GET /Kids/Info` and say it was dropped (link
    `../../002-kids-parent-lock/contracts/kids-api-v1-parent-lock.md`);
  - replace "Feature 2 adds…" with "admin or grant, see feature 2".

  Update `docs/plan.md`:
  - §2.6 and §3.6: the lock lives in `parent_lock` (schema v2) instead of `household`;
  - §4 tree: add `ParentLockController`, `IDeviceDirectory`, `CallerResolver`;
  - §8 risks: "Jellyfin does not bind the header DeviceId to the token", with the mitigation (research R2).
- [X] T038 [P] Bump `version` in `build.yaml` to `0.0.2.0` and add a changelog line: "Household parent lock:
  verify, grants, throttling, verifier import and offline copy, dashboard section."
- [X] T039 Run the full quickstart:
  - **§1**: `dotnet build` with 0 warnings, and `dotnet test`.
  - **§2**: start the dev loop on :8096, run `scripts/dev-check.sh` twice in a row; both runs pass.
  - **§3**: the restart step. Also check that the lock survives the restart (FR-005): `GET /Kids/Parent/Lock`
    shows the same type and `ChangedAt`, and the right code still verifies.
  - **§5**: the dashboard, timing the first code set (SC-007, under 1 min). Also time the full gate flow as a parent
    would: verify, then a profile change with the grant (SC-005, under 30 s).
  - **§6**: grep the logs for the printed code, grant and verifier hash, and for `pbkdf2-sha256$` → no matches.
  - Stop the dev loop.
  - Record the results in a "Last run" table at the end of `specs/002-kids-parent-lock/quickstart.md`, as feature 1
    did.

---

## Dependencies and execution order

```text
Phase 1 Setup (T001–T002)
   └─▶ Phase 2 Foundational (T003–T012)
          └─▶ Phase 3 US3 admin (T013–T017)
                 └─▶ Phase 4 US1 Verify + grants (T018–T022)  🎯 MVP
                        └─▶ Phase 5 US2 grant use (T023–T026)
                               ├─▶ Phase 6 US4 import (T027–T029)
                               ├─▶ Phase 7 US5 verifier (T030–T032)
                               └─▶ Phase 8 US6 dashboard (T033–T035)
                                      └─▶ Phase 9 Polish (T036–T039)
```

- **Inside a phase**: tests first (they fail), then the implementation, then dev-check.
- `ParentLockService.cs`, `ParentLockController.cs`, `ParentLockServiceTests.cs` and `scripts/dev-check.sh` are each
  edited by several tasks. Those tasks are sequential, never [P] with each other.
- **US4, US5 and US6 are independent of each other** after US2. The `DELETE` endpoint they use is in Phase 3
  (T014–T015).

## Parallel opportunities

- **Phase 1**: T001 ∥ T002.
- **Phase 2**:
  - tests T003 ∥ T004 ∥ T005;
  - then T006 ∥ T007 ∥ T010 (different files);
  - then T008 → T009 → T011 → T012.
- **Phase 4**: T018 ∥ T019 (different test files, except that T019 appends to `ParentLockServiceTests.cs`, so run it
  after T013).
- **Phases 6–8**: T033 (HTML) ∥ T027–T032 (server and tests).
- **Phase 9**: T036 ∥ T037 ∥ T038.

## Implementation strategy

1. **MVP = Phases 1–4.** A parent can set a lock as admin and unlock from a kid's TV to get a grant. It is proven
   by unit tests (SC-001, SC-003) and dev-check a, n, o–r, aa.
2. **Phase 5** makes the grant useful: profile changes, and code changes with `Current`. Together with the MVP,
   this covers all P1 acceptance criteria (§7 row 2): the app verifier validates server-side unchanged, a wrong code
   gets 200 `valid:false` and never 401, the 6th attempt is throttled, a device A grant is rejected on device B, and
   grants vanish on restart (T039 §3).
3. **Phases 6–8** add the P2 stories, each shippable on its own.
4. **Phase 9** syncs the docs and records the evidence.
