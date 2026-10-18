# Contract: Kids client API, version 1 (feature 2 additions)

This extends [feature 1's contract](../../001-kids-plugin-foundation/contracts/kids-api-v1.md): base path, JSON
shape (PascalCase by default, camelCase with `profile="CamelCase"`, nulls omitted, case-insensitive bodies), status
code table and compatibility rules all still apply. The examples are PascalCase. On implementation, that file
receives the `Info` change and the "admin or grant" rule below, and links here.

## Callers, extended

| Caller | Meaning |
|---|---|
| grant | a signed-in user whose request carries `X-Kids-Grant: <grant>`, valid for that user and device (below) |
| admin or grant | either |

- **Device**: the device Jellyfin registered for the caller's access token, **not** the `DeviceId` in the request's
  `Authorization` header (Jellyfin does not check it against the token; research R2). Changing the header changes
  nothing. A new sign-in with another `DeviceId` is another device.
- **Grant**: valid for 5 minutes from issue, for the same user and device, any number of times. Changing or removing
  the lock, deleting the user, or restarting the server ends it. An invalid, expired or foreign grant is ignored: the
  request is treated as having no grant (403 for non-admins, never 401).
- **Where grants apply**: `PUT` and `DELETE /Kids/Users/{userId}/Profile` (feature 1, now "admin or grant"),
  `GET /Kids/Parent/Verifier`, and `PUT /Kids/Parent/Lock` (with `Current`). `GET /Kids/Admin/PickCandidates` stays
  admin only.
- Grants are accepted only in the header, never in the query string.
- All endpoints below use Jellyfin's `IgnoreParentalControl` policy, so they work outside a kid's allowed hours.
  Anonymous calls get the framework's 401.

## GET /Kids/Info (changed)

- **200**: `{"PluginVersion":"0.0.2.0","ApiVersion":1,"Features":["profiles","parentLock"]}`.
- **`LockType` is no longer part of `Info`** (clarification Q1). In feature 1 it was always null and omitted, so no
  client received it. Clients read the lock type from `GET /Kids/Parent/Lock` after signing in.

## GET /Kids/Parent/Lock

- **Auth**: any signed-in user (kids included).
- **200**: `{"LockType":"REMOTE","ChangedAt":"2026-10-18T09:12:00.0000000Z"}`, or `{}` when no lock is set. Nothing
  else about the lock.
- **500**: the store is unavailable. It is never reported as "no lock".

## POST /Kids/Parent/Verify

- **Auth**: any signed-in user (kids included).
- **Body**: `{"Code":"UDLR12"}`. Strings longer than 256 chars are answered not valid without checking.
- **200 always** for signed-in callers:
  - right code: `{"Valid":true,"Grant":"q5Zr…43 chars…","ExpiresAt":"2026-10-18T09:17:00.0000000Z"}`;
  - wrong code: `{"Valid":false}`;
  - throttled: `{"Valid":false,"RetryAfterSeconds":30}`. The code was not checked;
  - no lock set, or the store is unavailable: `{"Valid":false}`.
- **Throttling**:
  - Per user and device: after 5 consecutive wrong codes, attempts are refused for 30 s. Each wrong code after a wait
    doubles the wait, up to 15 minutes.
  - Per user across all devices: the same rule after 20 consecutive wrong codes.
  - A right code resets both. Refused attempts are not counted.
  - State is in memory and resets on restart. API keys are throttled per key.
- **Never 401** for a wrong code (FR-007). A 400 is only for a body that cannot be read.

## PUT /Kids/Parent/Lock

Sets, replaces or imports the household lock.

- **Body**: `{"LockType":"NUMERIC"|"REMOTE", "Code"?: string, "Verifier"?: string, "Current"?: string}`. Exactly one
  of `Code` or `Verifier` must be present.

| Request | Who | Effect |
|---|---|---|
| `{LockType, Code}` | admin | sets or replaces the lock; `Current` is not needed |
| `{LockType, Code, Current}` | grant (not admin) | replaces the lock if `Current` verifies. A wrong `Current` counts as a wrong try |
| `{LockType, Verifier}` | admin | imports a verifier, **only when no lock is set** |

- **Responses**:
  - **200**: the new lock status, `{"LockType":"NUMERIC","ChangedAt":"…"}`. Every outstanding grant, including the
    caller's, is revoked; a grant holder verifies again with the new code.
  - **400**: ValidationProblemDetails keyed by field:
    - `LockType`: missing or unknown;
    - `Code`: fewer than 4 or more than 32 symbols, or symbols outside the type's set; also both or neither of
      `Code` and `Verifier`;
    - `Verifier`: bad format, iterations outside 10 000–1 000 000, salt outside 16–64 bytes, or hash not 32 bytes;
    - `Current`: missing (grant caller), or wrong.
  - **403**: a kid or other user without a grant; a grant caller trying to import (`Verifier`); a grant caller when
    no lock is set.
  - **409**: import while a lock is already set. The body is the current lock status. Nothing changes.
  - **429**: grant caller while throttled. `Retry-After` header and `{"RetryAfterSeconds":n}`. `Current` was not
    checked.
- **Order of checks**:
  1. Body shape and `LockType` (400).
  2. Caller rights for the request kind (403).
  3. Import conflict (409).
  4. Throttle (429).
  5. `Current` (400).
  6. `Code` or `Verifier` format (400).
- **Code sets**: `NUMERIC` is `0-9`. `REMOTE` is `U D L R` (D-pad), `0-9`, `r g y b` (colour buttons), `p`
  (play/pause), `w` (rewind), `f` (fast-forward), `+` and `-` (channel), `i` (info). Case-sensitive. These are the
  same as Wholphin's `ParentLockKeys`.

## DELETE /Kids/Parent/Lock

- **Auth**: admin only. A grant is not enough (clarification Q4).
- **204**: removed, and every outstanding grant is revoked.
- **404**: no lock is set.

## GET /Kids/Parent/Verifier

For a TV to cache, so it can unlock offline.

- **Auth**: admin or grant. Kid tokens without a grant get 403.
- **200**: `{"LockType":"NUMERIC","Verifier":"pbkdf2-sha256$120000$<32 hex>$<64 hex>"}`.
- **404**: no lock is set.
- **Format**: `pbkdf2-sha256$<iterations>$<saltHex>$<hashHex>`. The hash is PBKDF2-HMAC-SHA256 over the UTF-8 code
  with a 32-byte output. Lowercase hex. The server creates verifiers with 120 000 iterations and a 16-byte salt. This
  is Wholphin's `PasscodeHasher` format, so either side's verifier checks on the other.

## Known limits (constitution II)

- A cached verifier lets someone who extracts it from a TV guess a short code offline. A 4-digit code at 120 000
  iterations falls in seconds. The server never gives the verifier to a kid's token alone, and the dashboard
  recommends longer remote codes.
- A kid who knows their own Jellyfin password can sign in again as new devices. The per-user cap of 20 consecutive
  wrong codes still bounds the guesses.
