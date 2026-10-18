# Implementation Plan: Kids parent lock

**Branch**: `002-kids-parent-lock` (stacked on feature 1, `5c7e393`) | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-kids-parent-lock/spec.md` (clarified 2026-10-18, Q1–Q5)

## Summary

This feature adds a household parent lock that the server checks.

- **Storage**: the lock (`NUMERIC` or `REMOTE`) is one row in `kids.db`, holding only a verifier in Wholphin's
  `pbkdf2-sha256$…` format, so verifiers move both ways unchanged.
- **Verify**: `POST /Kids/Parent/Verify` checks a code in constant time and always answers 200. A right code gets
  an in-memory grant of 256 random bits, bound to (user, registered device) and valid for 5 minutes.
- **Throttle**: wrong codes are limited per (user, registered device), 5 tries then 30 s doubling up to 15 min, and
  per user across devices (20 tries).
- **Grants** in `X-Kids-Grant` unlock feature 1's profile changes, changing the code (with the current one), and
  reading the verifier for offline unlock.
- **Admin only**: setting the first lock, importing a verifier, and removing the lock.
- **Lock type**: `Info` loses its (always null) `LockType` and gains the `parentLock` feature. The lock type moves
  to an authenticated `GET /Kids/Parent/Lock`.
- **Dashboard**: the Kids page gains a "Parent lock" section.

**Key finding** (research R2): Jellyfin takes `DeviceId` from the request header and does not check it against the
token. The plugin therefore resolves the device from the token through `IDeviceManager`, which is why clarification
Q2 was needed.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`); the dashboard page is a plain ES module and HTML (jellyfin-web 12.x)

**Primary Dependencies**: unchanged from feature 1. `Jellyfin.Controller` and `Jellyfin.Model` 12.0.0 with
`ExcludeAssets=runtime`, `Microsoft.Data.Sqlite.Core` with runtime excluded, and `Microsoft.AspNetCore.App`. Crypto
comes from the BCL: `Rfc2898DeriveBytes.Pbkdf2`, `CryptographicOperations.FixedTimeEquals`, `RandomNumberGenerator`
and `SHA256`. Time comes from `TimeProvider`. **No new packages.**

**Storage**: `kids.db` schema version 2 adds a single-row `parent_lock` table (research R6). Grants and throttle
state live in memory only.

**Testing**:
- xUnit v3 in `tests/Jellyfin.Plugin.Kids.Tests`, with plugin-owned fakes (no mocking library) and a hand-written
  `FakeTimeProvider` subclass.
- `scripts/dev-check.sh` gets checks n–ad.
- Manual steps cover restart and the dashboard ([quickstart.md](quickstart.md)).

**Target Platform**: Jellyfin server 12.0–12.2 (runtime checks on 12.2, as in feature 1)

**Project Type**: server plugin (class library) with an embedded dashboard page

**Performance Goals**:
- A verify costs one PBKDF2 at 120 000 iterations, about 30–60 ms on x86 and up to roughly 200 ms on a Pi-class
  CPU. That is acceptable at gate frequency, and the throttle bounds abuse.
- Other new endpoints cost a single-row read, plus one indexed device lookup.

**Constraints**:
- Never 401 for a wrong code or a missing grant (403 or 200 `Valid:false`).
- Nothing about the lock in anonymous responses or the page HTML.
- Codes, grants and verifiers are never logged.
- `TreatWarningsAsErrors` with the configured analyzers.

**Scale/Scope**: one household lock; at most tens of devices; grants and throttle records number in the tens. There
are 5 new endpoints, 1 changed endpoint (`Info`), 2 feature-1 endpoints gaining grants, and 1 page section.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (below).*

| Principle | Check | Status |
|---|---|---|
| I. Leverage Jellyfin | No content or hours logic. Built on: `Policies.IgnoreParentalControl` (`MediaBrowser.Common/Api/Policies.cs:26`); `IAuthorizationContext` and `AuthorizationInfo.Token` (`MediaBrowser.Controller/Net/AuthorizationInfo.cs`); `IDeviceManager.GetDevices(DeviceQuery{AccessToken})`, the same lookup the server makes per request (`Jellyfin.Server.Implementations/Security/AuthorizationContext.cs:133-134`); `IEventConsumer<UserDeletedEventArgs>` (the existing consumer, extended to revoke grants); and BCL crypto. The only new mechanisms are those the constitution names: the household lock, grants and throttling. | PASS |
| II. The Server Decides | A kid token alone never changes a profile or the lock, or reads the verifier (FR-020, FR-021). Grants are server-issued, bound to the server-known device (not a client claim, R2), and checked in pure rules (`ProfileAccess`, `LockAccess`) with unit tests. Known limits are documented in the contract: offline verifier brute force, and re-sign-in with new devices bounded by the per-user cap. | PASS |
| III. Parents Are Never Locked Out | Admins and API keys pass every rule without a grant. They can always replace a forgotten code without `Current` (FR-015) and remove the lock (FR-018). Verify, lock status and grant use all run under `IgnoreParentalControl`, so they work at bedtime (check ac). Throttling only limits the gate, never admin actions. No user policy or schedule is touched. Disabling the plugin removes the gate entirely (there is no blocking). | PASS |
| IV. Secrets Stay Secret | Only a PBKDF2 verifier is stored, in Wholphin's format, and compared with `FixedTimeEquals` (R1). Wrong codes are throttled per user and device, with a per-user cap, and never answered with 401. Grants are 256-bit random, 5-minute, device-bound and in memory, kept as hashes (R4). `Info` no longer carries `LockType` (Q1), and the page HTML is static. The verifier goes only to admins or grant holders. No telemetry. | PASS |
| V. Stable, client-agnostic contract | All under `/Kids`. `ApiVersion` stays 1: the new endpoints and the `parentLock` feature are additive, and removing `Info.LockType` affects no client because it was always null and omitted, and v1 is unreleased (R7). This is recorded in the contract. Every new API used exists in the 12.0.0 packages: `IDeviceManager.GetDevices`, `DeviceQuery.AccessToken`, `Device.DeviceId` and `AuthorizationInfo.Token` (R2). No internal controller or claim names are used, so no startup self-check is needed. | PASS (feature 1's accepted 12.0 runtime risk carries over unchanged) |
| VI. Tested where it matters | Unit tests cover verifier compatibility (RFC vector plus Wholphin vectors both ways), grants and throttling with a fake clock, the access rules and the store migration. Server-dependent behaviour (no 401, the 6th try throttled, the header spoof ignored, device A against device B, grants enabling profile changes, bedtime) is covered by `dev-check.sh`, and restart by a manual step. No spike is needed (R11). Analyzers stay at 0 warnings. | PASS |
| Technical constraints | net10.0, the 12.0.0 packages, and state in `kids.db` (plugin XML untouched). Nothing is copied from GPL plugins; only Wholphin's own format (same author) is mirrored. Export and import of the lock remain owed to feature 6. | PASS |
| Development workflow | spec-kit flow. docs/plan.md §4.1 was updated for Q1 and Q2; see "docs/plan.md updates". | PASS |

**Post-design re-check (after Phase 1)**: the design adds two endpoints that were not in docs/plan.md §4.1 before
this feature, `GET /Kids/Parent/Lock` (Q1) and `DELETE /Kids/Parent/Lock` (Q4). Both are now in §4.1. It uses 429
on `PUT Lock` for a throttled grant caller. That is not 401, so it is consistent with principle IV. All gates still
pass, and Complexity Tracking has no new entries.

## Project Structure

### Documentation (this feature)

```text
specs/002-kids-parent-lock/
├── spec.md
├── plan.md                              # this file
├── research.md                          # Phase 0 (R1–R11)
├── data-model.md                        # Phase 1
├── quickstart.md                        # Phase 1
├── contracts/
│   ├── kids-api-v1-parent-lock.md       # additions to feature 1's API contract
│   └── dashboard-parent-lock.md         # Parent lock section on the Kids page
├── checklists/requirements.md
└── tasks.md                             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Jellyfin.Plugin.Kids/
├── PluginServiceRegistrator.cs          # + TimeProvider.System (TryAdd), IDeviceDirectory, ParentLockStore, ParentLockService, GrantService, ThrottleService, CallerResolver (scoped)
├── Api/
│   ├── KidsController.cs                # Info: drop LockType, add "parentLock"; Profile PUT/DELETE use the resolver (admin or grant)
│   ├── ParentLockController.cs          # new, route Kids/Parent: GET Lock, PUT Lock, DELETE Lock, POST Verify, GET Verifier
│   ├── KidsInfo.cs                      # LockType removed
│   ├── LockStatusDto.cs                 # LockType?, ChangedAt?
│   ├── LockUpdate.cs                    # PUT body: LockType, Code?, Verifier?, Current? (ToString redacts)
│   ├── VerifyRequest.cs / VerifyResult.cs
│   └── VerifierDto.cs
├── Auth/
│   ├── CallerContext.cs                 # + DeviceKey, HasGrant
│   ├── CallerResolver.cs                # new: IAuthorizationContext + IDeviceDirectory + GrantService + X-Kids-Grant header
│   ├── ProfileAccess.cs                 # CanMutate = IsAdmin || HasGrant
│   └── LockAccess.cs                    # new: pure rules for status, verifier, set, import, remove
├── ParentLock/
│   ├── ParentLockType.cs                # NUMERIC, REMOTE (string constants and parse)
│   ├── ParentCode.cs                    # alphabet and length validation (R8)
│   ├── Verifier.cs                      # create, parse, verify, validate for import (R1)
│   ├── StoredLock.cs                    # the stored row: LockType, Verifier, ChangedAt
│   ├── LockChangeResult.cs              # outcomes: Saved, Invalid, Forbidden, Conflict, Throttled, NoLock
│   ├── ParentLockService.cs             # set, replace with current, import, remove, status, verify; calls Grant and Throttle services
│   ├── GrantService.cs                  # issue, validate, RevokeAll, RevokeUser (R4)
│   ├── ThrottleService.cs               # per-pair and per-user records (R3)
│   ├── IDeviceDirectory.cs              # seam: GetRegisteredDeviceId(token)
│   └── JellyfinDeviceDirectory.cs       # IDeviceManager-backed
├── Storage/
│   ├── KidsDatabase.cs                  # SchemaVersion 2, migration step 2 (parent_lock)
│   └── ParentLockStore.cs               # new: get, upsert, insert-if-absent, replace-if-verifier-matches, delete
├── Events/UserDeletedConsumer.cs        # + GrantService.RevokeUser
└── Configuration/kids.html / kids.js    # + Parent lock section

tests/Jellyfin.Plugin.Kids.Tests/
├── Fakes/ (FakeTimeProvider, FakeDeviceDirectory)
├── VerifierTests.cs                     # RFC vector, Wholphin vectors, round trip, malformed, import bounds
├── ParentCodeTests.cs
├── ThrottleServiceTests.cs
├── GrantServiceTests.cs
├── ParentLockServiceTests.cs            # flows with a real temp-dir SQLite: set, replace with current, import conflict, remove, revoke on change
├── ParentLockStoreTests.cs              # concurrent insert-if-absent
├── LockAccessTests.cs, ProfileAccessTests.cs (updated)
└── KidsDatabaseTests.cs (updated: 1 → 2 keeps profiles)

scripts/dev-check.sh                     # + checks n–ad; a second device sign-in helper; prints the values to grep the logs for
specs/001-kids-plugin-foundation/contracts/kids-api-v1.md   # Info LockType note replaced by a link to the feature 2 contract
```

**Structure decision**:
- Same single project and test project as feature 1.
- The lock domain lives in `ParentLock/` and depends on one new plugin-owned seam (`IDeviceDirectory`) plus the
  existing store pattern, so constitution VI's pure logic is tested without a server.
- A separate `ParentLockController` keeps `KidsController` focused on profiles. Both share `CallerResolver`.
- `ParentLockService` owns the order of checks in the contract (rights, conflict, throttle, current, format) and the
  "revoke all grants on any change" rule, so the controller only maps outcomes to status codes.

## Delivery order and size

| Slice | Stories | Contents |
|---|---|---|
| A. Verifier and store | US3 (part), US4 | `Verifier`, `ParentCode`, schema v2, `ParentLockStore`, unit tests (SC-001) |
| B. Verify, throttle, grants | US1, US2 | `ThrottleService`, `GrantService`, `IDeviceDirectory`, `CallerResolver`, Verify endpoint, `ProfileAccess` with grants, dev-check o–u |
| C. Lock management | US3, US4, US5 | `PUT`/`DELETE`/`GET Lock`, `GET Verifier`, `Info` change, dev-check n, v–ac |
| D. Dashboard | US6 | Parent lock section, dev-check ad, manual §5 |

**Size**: about 25–30 tasks, a normal single feature with no split needed. A and B carry the risk (format
compatibility and device binding), so they come first.

## docs/plan.md updates

- **Done in this step (clarifications)**:
  - §4.1 `Info` has no `lockType`;
  - added `GET /Kids/Parent/Lock` and `DELETE /Kids/Parent/Lock`;
  - throttling adds the per-user cap of 20, and the device is the token's registered device;
  - grants are bound to `(userId, deviceId)`.
- **With the implementation**:
  - §3.6: the lock is stored in `parent_lock` (schema v2), not `household(key, value)`;
  - §2.6: same;
  - §4: the tree gains `ParentLockController` and `IDeviceDirectory`;
  - §8 risks: add "Jellyfin does not bind the header DeviceId to the token" with the mitigation.

## Risks

- **Verifier compatibility** is the acceptance criterion with the most at stake (SC-001). It is mitigated by fixed
  Wholphin-produced vectors in the unit tests. If someone cannot run Wholphin's hasher, an `openssl kdf` derivation
  with the same parameters is an equivalent source (R1).
- **The device lookup per grant-carrying request** depends on `IDeviceManager.GetDevices` by token keeping its
  12.0–12.2 behaviour. The server itself relies on it for every request, and dev-check u proves it on 12.2.
- **A kid can lock the gate for a parent**, by burning the per-user cap or a device's throttle with wrong codes, so
  the parent must wait. This is by design (abuse is bounded, not prevented). An admin sign-in is never throttled, and
  a restart clears the state.
- **Long dev-check runtime**: the throttle check waits about 30 s to leave a clean state.

## Complexity Tracking

No violations. Feature 1's accepted risk (no runtime check on 12.0 and 12.1) carries over unchanged.
