# Data model: Kids parent lock

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

Feature 1's `KidProfile` is unchanged. This feature adds one stored entity, two in-memory ones, and changes to
`CallerContext` and `PluginInfo`.

## ParentLock (stored, `kids.db` schema version 2)

At most one per server (FR-001). "No lock" means there is no row.

| Field | Type | Rules |
|---|---|---|
| `LockType` | `NUMERIC` or `REMOTE` | The names match Wholphin's `ParentLockType`. `CHALLENGE` is never stored (spec assumption) |
| `Verifier` | string | `pbkdf2-sha256$<iterations>$<saltHex>$<hashHex>` (research R1). Never returned except by `GET /Kids/Parent/Verifier` to an admin or a grant holder |
| `ChangedAt` | UTC timestamp | Set on every set, replace or import |

Table `parent_lock(id INTEGER PRIMARY KEY CHECK (id = 1), lock_type TEXT NOT NULL CHECK (lock_type IN
('NUMERIC','REMOTE')), verifier TEXT NOT NULL, changed_at TEXT NOT NULL)`. Migration step 2 creates it, and existing
profiles are untouched (research R6).

### Validation

- **Code** (when setting from a code, FR-004): 4–32 chars. `NUMERIC` allows `0-9` only. `REMOTE` allows `U D L R`,
  `0-9` and `r g y b p w f + - i`. The check is ordinal and case-sensitive. The error key is `Code`.
- **Verifier** (import, FR-017): 4 parts; prefix `pbkdf2-sha256`; iterations an integer in 10 000–1 000 000; salt
  even-length hex of 16–64 bytes; hash hex of exactly 32 bytes. The error key is `Verifier`.
- **LockType**: required, `NUMERIC` or `REMOTE`, case-insensitive on input and stored uppercase. The error key is
  `LockType`.
- A request sends exactly one of `Code` or `Verifier`. Sending both or neither is a 400 keyed `Code`.
- When the caller uses a grant, `Current` is required and must verify against the stored lock. A wrong `Current`
  counts as a wrong try (FR-016).

### State transitions

```text
              set (admin) │ import (admin, only from here)
 [no lock] ─────────────────────────────────────────▶ [lock: type, verifier, changedAt]
     ▲                                                  │   ▲
     │ remove (admin)                                   │   │ replace: admin (no current)
     └──────────────────────────────────────────────────┘   │          or grant + current
                                                        └───┘
```

Every transition calls `GrantService.RevokeAll()` (FR-019). An import while a lock exists is a 409, and nothing
changes.

## Grant (in memory only)

| Field | Type | Rules |
|---|---|---|
| key | SHA-256 of the grant value | The value is 32 random bytes, sent to the client as base64url (43 chars). The server keeps only the hash |
| `UserId` | Guid | The signed-in user who verified. `Guid.Empty` for API keys |
| `DeviceKey` | string | The registered device id of the token, or `apikey:` + the hash of the key (research R2) |
| `ExpiresAt` | UTC timestamp | Issue time + 5 minutes. Not extended by use |

A grant is valid when all of these hold: the hash is known, `UserId` and `DeviceKey` equal the request's, and
`now < ExpiresAt`. It is removed by `RevokeAll` (lock change), `RevokeUser` (user deleted), expiry (pruned on access)
or a restart.

## ThrottleRecord (in memory only)

Kept per `(UserKey, DeviceKey)` (threshold 5) and per `UserKey` (threshold 20). `UserKey` is the user id, or the
API key hash.

| Field | Type | Rules |
|---|---|---|
| `Failures` | int | Consecutive wrong codes. Reset to 0 by a right code (both records of that user) |
| `BlockedUntil` | UTC timestamp or none | After a wrong code with `Failures ≥ threshold`: now + min(30 s × 2^(Failures − threshold), 15 min) |
| `LastSeen` | UTC timestamp | For pruning idle records (30 min after the block ends) |

Attempts while either record is blocked are refused unchecked and not counted. `RetryAfterSeconds` is the larger
remaining wait, rounded up.

## CallerContext (changed)

Feature 1's `UserId`, `IsAdmin` and `IsApiKey`, plus:

- `DeviceKey`: string or null. It is resolved only when needed (a Verify, a lock change, or a request carrying
  `X-Kids-Grant`).
- `HasGrant`: bool. True only for a valid grant for this user and device.

## LockStatus (not stored, the response to `GET /Kids/Parent/Lock`)

`LockType` (`NUMERIC`, `REMOTE` or absent) and `ChangedAt` (absent when there is no lock). Nothing else.

## PluginInfo (changed)

`PluginVersion`, `ApiVersion` (still 1) and `Features` (`["profiles","parentLock"]`). **`LockType` is removed**
(clarification Q1). It was always null in feature 1 and omitted from the JSON, so no client relied on it.
