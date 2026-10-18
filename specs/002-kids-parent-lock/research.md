# Research: Kids parent lock

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-18

Citations are relative to `.reference/` (Jellyfin at tag `v12.2`) unless stated otherwise. "12.0.0 package" means the
NuGet `Jellyfin.*` 12.0.0 packages the plugin compiles against (checked in `~/.nuget/packages/jellyfin.*/12.0.0`, XML
docs). Wholphin paths are in `Wholphin/.worktree/kids-mode/app/src`. Feature 1's research (R1–R14) still applies;
this file only adds to it.

## R1. Verifier format: Wholphin's `PasscodeHasher`, byte for byte

- **Decision**: verifiers are `pbkdf2-sha256$<iterations>$<saltHex>$<hashHex>`. The hash is
  `Rfc2898DeriveBytes.Pbkdf2(UTF8(code), salt, iterations, HashAlgorithmName.SHA256, 32)`. It is compared with
  `CryptographicOperations.FixedTimeEquals`. New verifiers use 120 000 iterations, a 16-byte salt from
  `RandomNumberGenerator`, and lowercase hex.
  - **Parsing on verify** (as Wholphin does): exactly 4 `$`-separated parts, the prefix, iterations > 0, and even-length
    hex. Anything else never verifies.
  - **Hex**: uppercase is accepted on parse (`Convert.FromHexString`). Wholphin parses with `toInt(16)`, which is also
    case-insensitive.
  - **Import validation is stricter** (FR-017): iterations 10 000–1 000 000, a salt of 16–64 bytes, and a hash of exactly
    32 bytes.
- **Rationale**:
  - `PasscodeHasher.kt:13-70` derives a single PBKDF2 block, `U1 = HMAC(code, salt || 00000001)` followed by XOR
    rounds, with a 32-byte output. That is exactly RFC 8018 PBKDF2 with dkLen = hLen = 32, which
    `Rfc2898DeriveBytes.Pbkdf2` implements.
  - Wholphin's own test checks it against the JDK's `PBKDF2WithHmacSHA256` (`PasscodeHasherTest.kt:29-38`). The JDK
    takes the password from the chars' UTF-8 bytes, as Wholphin does. The codes are ASCII, so there is no encoding
    ambiguity.
- **Cross-compatibility evidence (SC-001)**: the unit tests use fixed vectors.
  - (a) The RFC 7914 §11 PBKDF2-HMAC-SHA256 vector (`P="passwd"`, `S="salt"`, `c=1`), truncated to the first 32
    bytes. It checks the primitive and the verify path, which accepts any positive iteration count.
  - (b) Verifiers produced by Wholphin's `PasscodeHasher.hash` for one digits code and one remote code, pasted into
    the tests. They are generated once with a throwaway Wholphin unit test, or the same derivation with
    `openssl kdf -keylen 32 -kdfopt digest:SHA256 -kdfopt pass:<code> -kdfopt hexsalt:<salt> -kdfopt iter:120000 PBKDF2`.
  - (c) The reverse direction: a verifier made by the plugin checks against an independent derivation in the test,
    using the instance `Rfc2898DeriveBytes` API.
- **Alternatives considered**:
  - A server-only format such as Argon2: it breaks offline unlock and migration (FR-002), and needs a library.
  - Re-hashing imported verifiers: impossible, because the code is unknown.

## R2. Which device a request comes from: the token's registered device, not the header

- **Finding**: `AuthorizationContext.GetAuthorizationInfoFromDictionary`
  (`Jellyfin.Server.Implementations/Security/AuthorizationContext.cs:113-147`) builds `DeviceId` from the request's
  `Authorization` header. It looks up the token's `Device` row (`DeviceQuery { AccessToken }`), but uses
  `device.DeviceId` **only when the header has none** (:144-147). So a valid kid token can claim any `DeviceId` on each
  request, and `AuthorizationInfo.DeviceId` is not trustworthy for throttling or for grant binding.
- **Decision**: the plugin resolves the device itself:
  `IDeviceManager.GetDevices(new DeviceQuery { AccessToken = info.Token }).Items.FirstOrDefault()?.DeviceId`.
  - This goes behind a plugin-owned seam, `IDeviceDirectory.GetRegisteredDeviceId(token)`, so the logic can be
    tested without Jellyfin.
  - API keys have no `Device` row (:191-200), so their device key is the key itself: a SHA-256 of the token, never
    the raw token.
  - A user token without a device row (which should not happen) is treated as "no device": Verify answers not valid,
    and grants are never accepted.
- **12.0.0 check**: `IDeviceManager.GetDevices(Jellyfin.Data.Queries.DeviceQuery)`, `DeviceQuery.AccessToken`,
  `Device.DeviceId` and `AuthorizationInfo.Token` all exist in the 12.0.0 packages (`MediaBrowser.Controller.xml`,
  `Jellyfin.Data.xml`, `Jellyfin.Database.Implementations.xml`).
- **Cost**: one indexed query per Verify, lock change, verifier read and grant-carrying mutation. These calls are rare.
  The query is skipped entirely for requests that carry no grant and are not Verify.
- **Residual risk**: a kid who knows their own password (kids often have none) can sign in again with new device ids.
  Each new sign-in is a new registered device, so the per-user cap (R3) bounds the total guesses. This is documented
  in the contract.
- **Alternatives considered**:
  - Trusting the header: rejected, because it gives unlimited guesses (clarification Q2).
  - Binding to the token itself: equivalent for grants, but a re-login on the same TV would lose the throttle
    history, so the registered `DeviceId` is better for throttling.

## R3. Throttling: per (user, device) plus a per-user cap, in memory

- **Decision**: a `ThrottleService` keeps two counters, `(userKey, deviceKey)` and `userKey` (API keys use the key
  hash for both).
  - **Each record**: consecutive failures and `blockedUntil`.
  - **On a wrong code**: increment both records. A record with failures `f ≥ threshold` gets
    `blockedUntil = now + min(30 s × 2^(f − threshold), 15 min)`, with threshold 5 for the pair and 20 for the user.
  - **On an attempt**: if either record is blocked, refuse without checking the code. The answer carries
    `retryAfterSeconds`, the larger remaining wait, rounded up. A refused attempt is not counted.
  - **On a right code**: clear the pair and the user records.
  - **Time** comes from an injected `TimeProvider`.
  - **Records** sit in a `ConcurrentDictionary`. A record is pruned when it is idle for more than 30 minutes past its
    block, on access and on a cheap sweep at each Verify.
- **Matches the spec**: five wrong codes are answered normally, the sixth inside 30 s is refused (SC-003), each wrong
  code after a wait doubles the wait, and the wait is capped at 15 min.
- **Concurrency**: the check and the update for one key run under a per-record lock. Two simultaneous wrong codes
  both count, and a right code racing a wrong one still succeeds unless the pair was already blocked (spec edge case).
- **A wrong `current` code in PUT Lock** goes through the same throttle (FR-016).
- **Restart** clears everything, like grants (spec edge case).
- **Alternatives considered**:
  - Persisting throttle state: it would survive restarts, but a restart already needs an admin. It adds writes for
    no real gain.
  - A global cap across users: one kid could then lock every other device in the house out of the gate.

## R4. Grants: random, hashed in memory, bound to (user, device)

- **Decision**: a `GrantService` singleton.
  - A grant is 32 bytes from `RandomNumberGenerator`, sent as base64url without padding (43 chars).
  - The server keeps only `SHA-256(grant)` as the dictionary key, with `{userId, deviceKey, expiresAt}`. A memory dump
    or a log line therefore never holds a usable grant, and the lookup is a hash-table hit, not a string compare.
  - **TTL**: 5 min from issue, with no sliding.
  - **Accepted** when the hash is present, the request's `(userId, deviceKey)` (R2) equals the stored pair, and
    `now < expiresAt`.
  - **Invalidated**: `RevokeAll()` on any lock change (set, replace, import, remove: FR-019); `RevokeUser(userId)` from
    the existing `UserDeletedConsumer`; and a restart.
- **The header**: `X-Kids-Grant`, read by a small helper the controller calls. Grants are never accepted from a query
  string, so they don't end up in access logs or referrers.
- **Alternatives considered**:
  - Signed stateless tokens (HMAC with a per-process key): equivalent lifetime semantics, but `RevokeAll` would need a
    key rotation. The dictionary is simpler at household scale.
  - Single-use grants: they break the parent menu flow, which saves several things in a row (spec edge case).

## R5. Who may do what (access rules)

- **Decision**: extend `CallerContext` with `DeviceKey` (R2) and `HasGrant` (R4). It is built by a scoped
  `CallerResolver` from `IAuthorizationContext`, `IDeviceDirectory`, `GrantService` and the `X-Kids-Grant` header.
  The rules stay pure functions in `Auth/`:
  - `ProfileAccess.CanMutate = IsAdmin || HasGrant`. This changes feature 1 as promised (its FR-015 note and
    `ProfileAccess` remark).
  - `LockAccess.CanReadStatus = signed in`.
  - `CanReadVerifier = IsAdmin || HasGrant`.
  - `CanSetWithoutCurrent = IsAdmin`.
  - `CanSetWithCurrent = HasGrant`.
  - `CanImport = IsAdmin` (only when there is no lock).
  - `CanRemove = IsAdmin`.
  - `Admin/PickCandidates` keeps `IsAdmin` only (FR-013).
- **Device lookup**: the resolver does it only when a grant header is present, or for Verify and lock changes. The
  hot read paths (`Profiles`, `Profile` GET) pay nothing new.
- **Policies**: every new endpoint uses `[Authorize(Policy = Policies.IgnoreParentalControl)]`, so they work at
  bedtime (FR-006, FR-022a) and refusals are 403. The framework still answers anonymous calls with its 401, which is
  correct for unsigned requests (Story 1.7).

## R6. Storage: schema version 2, one-row `parent_lock` table

- **Decision**: migration step 2 (`PRAGMA user_version` 1 → 2) adds:

  ```sql
  CREATE TABLE parent_lock (
    id         INTEGER PRIMARY KEY CHECK (id = 1),
    lock_type  TEXT    NOT NULL CHECK (lock_type IN ('NUMERIC','REMOTE')),
    verifier   TEXT    NOT NULL,
    changed_at TEXT    NOT NULL
  );
  ```

  - "No lock" means no row.
  - **Import-only-if-none** is `INSERT … ON CONFLICT DO NOTHING`, then checking the change count. That is atomic, so
    two TVs that migrate at once give one success and one 409.
  - **Set or replace** is an upsert.
  - **Remove** is `DELETE`.
  - **Replace with the current code** is check-then-write in one write transaction, comparing the verifier read in
    that transaction. A concurrent change cannot slip between the check and the write.
- **Rationale**: a single row with a `CHECK` enforces "at most one household lock" in the schema. Feature 6's per-kid
  override can add its own table. `KidsDatabase.SchemaVersion` goes to 2, and feature 1's migration test pattern
  covers 1 → 2 on a database that holds profiles.
- **Alternatives considered**:
  - The plan §2.6 `household(key, value)` table: it is generic, but it loses the type check and makes the atomic
    "insert if absent" clumsier.
  - Plugin XML: a dashboard save would race with it, and it would sit in a world-readable config file. Rejected by
    the constitution.

## R7. Lock status replaces `LockType` in `Info` (clarification Q1)

- **Decision**:
  - Remove `LockType` from `KidsInfo`. Feature 1 always returned null for it, and Jellyfin omits nulls, so no client
    ever saw it.
  - `Features` becomes `["profiles","parentLock"]`.
  - Add `GET /Kids/Parent/Lock`, `IgnoreParentalControl`, for any signed-in user. It returns
    `{LockType: "NUMERIC"|"REMOTE"|null, ChangedAt?}`.
  - The type names are strings that match Wholphin's `ParentLockType` names.
  - `ApiVersion` stays 1: v1 is unreleased, and dropping an always-absent field breaks nobody.
  - The feature 1 contract and docs/plan.md §4.1 were updated (§4.1 in this step; the contract file with the
    implementation, see plan).
- **Store failure**: lock status answers 500, never "no lock" (spec edge case). Verify answers `Valid:false` and logs
  an error.

## R8. Code validation (FR-004)

- **Decision**: a pure `ParentCode.Validate(type, code)`:
  - 4 to 32 chars;
  - `NUMERIC`: `0-9` only;
  - `REMOTE`: only `UDLR`, `0-9` and `rgybpwf+-i`.
  - The check is ordinal and case-sensitive (`u` is not `U`; `r` is red, not right).
  - Errors are keyed `Code` (or `Current` / `Verifier` / `LockType`).
- **Rationale**: these are the sets in `ParentLockKeys.kt:31-37` and the minimum in `PasscodeHasher.kt:14`. The max
  of 32 bounds the work (spec assumption).
- **Note**: Verify does not validate the submitted code's alphabet. Any string up to 256 chars is hashed and compared,
  so the answer doesn't reveal the lock type. Longer input is refused as not valid without hashing.

## R9. Dashboard: a "Parent lock" section on the existing Kids page

- **Decision**: add a section to `kids.html` and `kids.js`, not a new page.
  - It shows the status from `GET /Kids/Parent/Lock`.
  - The form has a type select (Digits / Remote buttons), a code and a confirmation field (`type="password"`,
    `autocomplete="off"`), client-side validation that mirrors R8, Save (`PUT /Kids/Parent/Lock {LockType, Code}`) and
    Remove (`DELETE`, after a confirm).
  - A legend lists the remote symbols (U D L R for the D-pad, r g y b for the colour buttons, p play/pause, w rewind,
    f fast-forward, + and − channel, i info).
  - The page is static HTML, and data loads through `ApiClient` with the admin's token (FR-025). The admin needs no
    grant.
- **Rationale**: one dashboard entry, which the household already uses for kids. The lock is household-wide, so it
  belongs on the overview page and not in the per-kid editor.

## R10. Logging and secrets

- **Decision**: logs never contain codes, grants, verifiers or token hashes.
  - **Information**: lock set, replaced, imported or removed, with the acting user's name or "API key".
  - **Warning**: a pair or a user is blocked, with the user name and the wait. The device id is logged, but the API key
    hash is not.
  - Verify failures are not logged one by one.
  - Request DTOs that carry `Code`, `Current` or `Verifier` override `ToString()` to redact them, so model-binding
    diagnostics can't echo them.

## R11. Dev-server verification (constitution VI)

- **Server-dependent behaviour** is checked by new `scripts/dev-check.sh` cases against the process-compose loop:
  - Verify never answers 401 for a wrong code;
  - the 6th wrong code is throttled;
  - a spoofed `DeviceId` header neither resets the throttle nor moves a grant;
  - a grant from device A is refused on device B, which signs in as the same kid with another `DeviceId`;
  - a kid token without a grant gets 403 on lock, verifier and profile changes;
  - a grant enables `PUT Profile`;
  - `Info` has no `LockType`.
- **Restart behaviour** (grants vanish) is a manual quickstart step, because the script doesn't restart the server.
- **No spike is needed**. The only Jellyfin mechanism this feature relies on beyond feature 1 is
  `IDeviceManager.GetDevices` by token, which the server itself uses on every request (R2). The header-spoof
  dev-check proves it end to end.
