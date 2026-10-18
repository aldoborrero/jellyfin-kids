# Feature Specification: Kids parent lock

**Feature Branch**: `002-kids-parent-lock` (stacked on feature 1, commit `5c7e393`)

**Created**: 2026-10-18

**Status**: Draft

**Input**: User description: "Feature 2 kids-parent-lock (docs/plan.md §7 row 2, §3.6, §4.1): household parent lock on
the server. Lock storage (NUMERIC or REMOTE type, PBKDF2 verifier in the app's pbkdf2-sha256$iter$saltHex$hashHex
format), verifier import during migration (only when none is set), POST /Kids/Parent/Verify issuing short-lived
in-memory device-bound grants with per (user, device) throttling and never 401, GET /Kids/Parent/Verifier for offline
use (admin or grant), PUT /Kids/Parent/Lock to set/change the code (admin, or grant plus current code), grants accepted
via X-Kids-Grant on the mutation endpoints from feature 1, lockType in GET /Kids/Info, and lock management in the
dashboard config page. Acceptance: an app verifier validates server-side unchanged; a wrong code returns 200
valid:false and never 401; the 6th attempt is throttled; a grant from device A is rejected on device B; grants vanish
on restart."

Research input: [docs/plan.md](../../docs/plan.md) §3.6 (parent lock), §4.1 (client contract), §7 (feature 2) and §8
(risks: offline verifier cache). Builds on [feature 1](../001-kids-plugin-foundation/spec.md).

## Clarifications

### Session 2026-10-18

- Q: Should the lock type be in the anonymous plugin information? → A: No. The plugin information only adds the
  `parentLock` capability; any signed-in user reads the lock type and last change time from an authenticated lock
  status request (constitution IV: nothing per-household in anonymous answers).
- Q: How should wrong-code throttling resist a kid who sends a different device id on every try? → A: The device is
  the one Jellyfin registered for the caller's sign-in (looked up from the credentials, not the request header), and
  each user also has a cap across all devices: 20 consecutive wrong codes, then the same doubling wait up to 15
  minutes.
- Q: Who may set the first lock or import Wholphin's verifier when none is set? → A: Administrators and API keys only.
- Q: Can the lock be removed, and by whom? → A: Yes, by administrators and API keys only; a grant can change the code
  but not remove it.
- Q: Is a grant bound to the same user and the same device? → A: Yes, both (the device as defined above).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A parent unlocks on the kid's TV and gets a short-lived grant (Priority: P1)

A kid's TV runs Wholphin signed in as the kid. A grown-up wants to change something (a setting, later more time), so
they open the parent gate and enter the household parent code. The TV sends the code to the server, which checks it
against the stored verifier and, if it matches, hands the TV a parent grant: a random token that lets this TV make
parent changes for a few minutes. A wrong code is simply "not valid" (the TV must not think its session expired), and
after a few wrong tries the server makes the TV wait before trying again.

**Why this priority**: It is the core of the feature. Without a server-checked code, a kid's token is the only
credential on the TV, and parents would need an administrator login on every TV to change anything.

**Independent Test**: On the dev server with a lock set by an administrator, verify the right code with a kid's
credentials and get a grant; verify a wrong code and get "not valid" without an authentication error; send six wrong
codes from one device and see the sixth refused with a wait time.

**Acceptance Scenarios**:

1. **Given** a household lock is set, **When** a signed-in user (kid or not) submits the right code, **Then** the
   answer says valid and carries a grant and its expiry time.
2. **Given** a household lock is set, **When** a signed-in user submits a wrong code, **Then** the answer is a
   success response saying not valid, never the "unauthenticated" answer.
3. **Given** a kid outside their allowed hours, **When** their TV submits the code, **Then** verification still
   works, so a parent can unlock at bedtime.
4. **Given** five wrong codes from the same user and device, **When** a sixth code (right or wrong) is submitted
   before the wait is over, **Then** it is not checked, the answer says not valid and tells how many seconds to wait.
   Sending a different device id in the request does not start a fresh count.
5. **Given** the wait is over, **When** another wrong code is submitted, **Then** the next wait is twice as long, up to
   15 minutes; **When** the right code is submitted, **Then** a grant is issued and the count of wrong tries resets.
6. **Given** no household lock is set, **When** a code is submitted, **Then** the answer says not valid and no grant
   is issued.
7. **Given** an anonymous caller, **When** they submit a code, **Then** the request is refused as unauthenticated (the
   normal answer for an unsigned request) and nothing is checked.
8. **Given** a user with 20 consecutive wrong codes spread over several devices, **When** they submit another code
   from any device, **Then** it is refused with a wait, even if that device has fewer than 5 wrong tries.

---

### User Story 2 - A grant lets the kid's TV make parent changes, only on that TV and only for a while (Priority: P1)

With a grant, the kid's TV can do what feature 1 reserved for administrators: mark and unmark kids and change kid
settings, while still signed in as the kid. The grant only works on the TV that obtained it, expires after a few
minutes, and disappears when the server restarts. The kid's credentials alone remain useless for changes.

**Why this priority**: The grant is what makes the parent gate useful on a shared TV; together with Story 1 it is the
minimum that replaces Wholphin's local-only gate.

**Independent Test**: Obtain a grant on device A with a kid's token; change a kid's profile from device A with the
grant; repeat from device B with the same grant and see it refused; wait past the expiry and see it refused on A;
restart the server and see a fresh grant refused.

**Acceptance Scenarios**:

1. **Given** a grant obtained on device A, **When** device A sends a profile change with that grant, **Then** the
   change is accepted exactly as if an administrator had made it (including revision checks).
2. **Given** a grant obtained on device A, **When** device B sends a change with that grant, **Then** the change is
   refused as forbidden and nothing changes.
3. **Given** a grant obtained by one user on device A, **When** another user signed in on device A sends a change
   with it, **Then** the change is refused as forbidden.
4. **Given** a grant, **When** its lifetime (5 minutes) has passed, **Then** changes with it are refused as forbidden.
5. **Given** a grant, **When** the server restarts, **Then** changes with it are refused as forbidden.
6. **Given** a grant, **When** the household code is changed, **Then** every grant issued before the change stops
   working.
7. **Given** a kid's credentials without a grant, or with a malformed or made-up grant, **When** a change is sent,
   **Then** it is refused as forbidden, never as unauthenticated.
8. **Given** an administrator or an API key, **When** they send a change without a grant, **Then** it is accepted as
   in feature 1.

---

### User Story 3 - A parent sets and changes the household code (Priority: P1)

The household has one parent code, of one of two types: digits (typed on a remote's number keys or a keyboard) or a
remote sequence (D-pad, digits and extra remote buttons). An administrator sets it the first time. Later, a grown-up
can change it from a TV by unlocking with the current code and entering the new one; an administrator can always
replace it without knowing the current one, for example when it was forgotten. The server stores only a verifier, in
the same format Wholphin uses, never the code.

**Why this priority**: Stories 1 and 2 need a stored lock; without a way to set it the feature cannot be used.

**Independent Test**: As administrator, set a digits code; verify it; with a grant plus the current code, change it to
a remote sequence; verify the old code fails and the new one works; as administrator, replace it without the current
code.

**Acceptance Scenarios**:

1. **Given** no lock is set, **When** an administrator sets a code with a type, **Then** the lock is stored, the code
   verifies, and signed-in clients asking for the lock status get its type and change time.
2. **Given** a lock is set, **When** a caller with a grant sends the current code and a new code, **Then** the lock is
   replaced and only the new code verifies.
3. **Given** a lock is set, **When** a caller with a grant sends a wrong current code, **Then** the change is refused
   and the wrong current code counts as a wrong try for throttling.
4. **Given** a lock is set, **When** an administrator sends a new code without the current one, **Then** the lock is
   replaced.
5. **Given** a code shorter than 4 symbols, or with symbols not allowed for its type (digits only for the digits
   type; D-pad, digits and the extra remote symbols for the remote type), **When** it is set, **Then** it is rejected
   naming the invalid field and nothing changes.
6. **Given** a kid's credentials without a grant, **When** they try to set or change the code, **Then** the request is
   refused as forbidden.
7. **Given** a stored lock, **When** the stored data or any response is inspected, **Then** the code itself appears
   nowhere; only the verifier is stored.
8. **Given** any signed-in user, kid included, **When** they ask for the lock status, **Then** they receive the lock
   type (or none) and the last change time, and nothing else about the lock; anonymous callers are refused.

---

### User Story 4 - Wholphin moves its existing local lock to the server unchanged (Priority: P2)

A household already uses Wholphin's local kids mode, with a parent code stored on the TV as a verifier. When the
server gains the plugin, Wholphin offers to move the lock to the server: it sends its stored verifier and type, so
parents keep the same code without re-entering it. This is only allowed when the server has no lock yet, so one TV
cannot overwrite a lock the household already uses.

**Why this priority**: It makes the switch to plugin mode painless for existing households, but the household can
also just set a new code (Story 3).

**Independent Test**: Hash a code with Wholphin's hasher (or the same algorithm and parameters); import the verifier
on a server with no lock; verify the original code on the server; try a second import and see it refused.

**Acceptance Scenarios**:

1. **Given** no lock is set and a verifier produced by Wholphin, **When** an administrator imports it with its type,
   **Then** the original code verifies on the server, without the server ever seeing the code.
2. **Given** a lock is already set, **When** a verifier is imported, **Then** the import is refused as a conflict and
   the existing lock is unchanged.
3. **Given** a verifier that is not in the expected format (wrong prefix, iterations out of range, salt or hash not
   hex or of the wrong size), **When** it is imported, **Then** it is rejected naming the invalid field.
4. **Given** a verifier created by the server (Story 3), **When** Wholphin checks a code against it locally, **Then**
   the result matches the server's (the same format and parameters).

---

### User Story 5 - A TV keeps a copy of the verifier to unlock while offline (Priority: P2)

Kids mode must keep working when the server is unreachable, including the parent gate. After a parent unlocks, the
TV fetches the household verifier and type and caches them, so it can check the code locally when offline. Kids'
credentials alone never get the verifier, because anyone holding it can guess a short code offline.

**Why this priority**: Needed for offline parity with local mode, but the online flow (Stories 1–3) works without it.

**Independent Test**: With a grant, fetch the verifier and check the code against it with Wholphin's algorithm;
without a grant, with a kid's token, see the request refused.

**Acceptance Scenarios**:

1. **Given** a lock is set, **When** an administrator or a caller with a valid grant asks for the verifier, **Then**
   they receive the lock type and the verifier.
2. **Given** a lock is set, **When** a kid's credentials without a grant ask for the verifier, **Then** the request
   is refused as forbidden.
3. **Given** no lock is set, **When** an authorized caller asks for the verifier, **Then** the answer is "not found".

---

### User Story 6 - An administrator manages the lock from the Jellyfin dashboard (Priority: P2)

On the Kids page, an administrator sees whether the household has a parent lock, its type and when it was last
changed. They can set or replace the code (choosing the type and entering the code twice) and remove the lock. The
page explains that a code is needed for parents to unlock from TVs when kids are managed on the server, and never
shows the code or the verifier.

**Why this priority**: Gives the household a place to set the first lock without a script; the API covers it
otherwise.

**Independent Test**: On the dev server, open the Kids page as administrator, set a remote sequence code, reload and
see the lock reported with its type and date; verify the code through the API; replace it; remove it.

**Acceptance Scenarios**:

1. **Given** no lock is set, **When** the administrator opens the Kids page, **Then** it says there is no parent lock
   and recommends setting one.
2. **Given** the administrator enters a code and its confirmation, **When** they differ or the code is invalid for the
   chosen type, **Then** the page explains why and does not save.
3. **Given** a valid code, **When** the administrator saves, **Then** the page reports the lock as set, with its type
   and the date of the change.
4. **Given** a lock is set, **When** the administrator removes it (after confirming), **Then** no lock is set, clients
   see no lock type, and every outstanding grant stops working.
5. **Given** the page is fetched without signing in, **When** its content is inspected, **Then** it contains no lock
   information; everything is loaded through authenticated requests.

---

### Edge Cases

- A request carries a grant and also comes from an administrator: it is accepted as administrator; the grant is not
  needed.
- A grant is sent on a request that does not accept grants (for example the administrator-only picks list): it has
  no effect and the request is refused as for any non-administrator.
- The same grant is used for several changes within its lifetime: all are accepted (a grant is not single-use); its
  lifetime is not extended by use.
- Two codes are verified at the same moment from the same device, one wrong: the wrong one counts; the right one still
  gets a grant unless the device is already waiting.
- The wait after wrong tries applies to the user and device pair; another device signed in as the same kid has its
  own count, but all of that user's devices together are capped (FR-008).
- A request whose device id header differs from the device registered for its credentials: the registered device is
  used for throttling and grant binding; the header is ignored.
- Throttling state, like grants, is kept only in memory and resets on restart.
- An API key verifies a code: it is checked like any caller (throttled per key); API keys never need a grant.
- Verifying while no lock is set does not count as a wrong try.
- The stored verifier cannot be read on startup (data store unavailable): verification answers not valid, setting
  the lock fails with a server error, and the lock status request fails with a server error rather than reporting
  no lock.
- An imported verifier with a very high iteration count would make every check slow: iteration counts outside the
  accepted range are rejected.
- The code is never written to logs; neither are grants or verifiers.
- A grant is issued and the issuing user is then deleted: the grant stops working.

## Requirements *(mandatory)*

### Functional Requirements

**Lock storage**

- **FR-001**: The household MUST have at most one parent lock, made of a type (digits or remote sequence), a
  verifier and the time it was last changed. There is no per-kid lock in this feature.
- **FR-002**: The lock MUST be stored only as a verifier in Wholphin's format (`pbkdf2-sha256$<iterations>$<saltHex>$<hashHex>`:
  PBKDF2-HMAC-SHA256, 32-byte hash), so a verifier made by Wholphin verifies on the server and one made by the server
  verifies in Wholphin. The code itself MUST never be stored, logged or returned.
- **FR-003**: Verifiers created by the server MUST use a fresh random 16-byte salt and 120 000 iterations, matching
  Wholphin.
- **FR-004**: Codes MUST be at least 4 symbols and at most 32 symbols long; digits-type codes MUST use only `0-9`;
  remote-type codes MUST use only `U D L R`, `0-9` and `r g y b p w f + - i`. Codes are compared exactly
  (case-sensitive).
- **FR-005**: The lock MUST survive server restarts and plugin updates, in the plugin's own data store.

**Verification**

- **FR-006**: Any signed-in user, including kids and including outside their allowed hours, MUST be able to submit a
  code for verification. Anonymous callers MUST NOT.
- **FR-007**: Verification MUST compare in constant time and MUST answer with a success response in every case: valid
  with a grant and its expiry time, or not valid (optionally with the seconds to wait). It MUST NOT answer
  "unauthenticated" for a wrong code.
- **FR-008**: Wrong codes MUST be counted per signed-in user and device, where the device is the one Jellyfin
  registered for the caller's credentials, not a device id the request claims. After 5 consecutive wrong codes,
  further attempts from that pair MUST be refused without checking for 30 seconds; each wrong code after a wait
  doubles the wait, up to 15 minutes. In addition, after 20 consecutive wrong codes by the same user across all
  devices, every attempt by that user MUST be refused with the same doubling wait. A right code resets both counts
  for that user.
- **FR-009**: When no lock is set, verification MUST answer not valid without counting a wrong try.

**Grants**

- **FR-010**: A grant MUST be a random value of at least 256 bits, valid for 5 minutes from issue, bound to the user and
  device (as defined in FR-008) that obtained it, usable any number of times within its lifetime, and kept only in the server's memory.
- **FR-011**: A grant MUST be accepted only when presented by the same user on the same device that obtained it, before
  its expiry, and while the lock it was issued under is unchanged. Changing or removing the lock, a server restart, or
  deletion of the issuing user MUST invalidate it.
- **FR-012**: Clients MUST present the grant alongside their normal credentials in a dedicated request header
  (`X-Kids-Grant`); the grant never replaces the user's own sign-in.
- **FR-013**: A valid grant MUST authorize the changes that feature 1 reserved for administrators on kid profiles:
  marking, unmarking and changing a kid's settings. Administrator-only dashboard data (such as the picks list) MUST
  stay administrator-only.
- **FR-014**: Requests with a missing, malformed, expired or foreign grant MUST be treated as having no grant: refused
  as forbidden for non-administrators, never answered as unauthenticated.

**Managing the lock**

- **FR-015**: An administrator (or API key) MUST be able to set or replace the lock with a type and a code, without the
  current code.
- **FR-016**: A caller with a valid grant MUST be able to replace the lock by sending the current code and the new type
  and code. A wrong current code MUST be refused and MUST count as a wrong try (FR-008).
- **FR-017**: An administrator (or API key) MUST be able to import a verifier with its type, only when no lock is set;
  when one is set the import MUST be refused as a conflict. The verifier MUST be validated: the expected prefix,
  iterations between 10 000 and 1 000 000, a hex salt of 16 to 64 bytes and a hex hash of 32 bytes.
- **FR-018**: An administrator (or API key) MUST be able to remove the lock.
- **FR-019**: Setting, replacing, importing or removing the lock MUST invalidate every outstanding grant.
- **FR-020**: Kids' credentials without a grant MUST be refused as forbidden for every lock change.

**Offline verifier**

- **FR-021**: An administrator, an API key, or a caller with a valid grant MUST be able to read the lock type and
  verifier; anyone else MUST be refused as forbidden. When no lock is set the answer MUST be "not found".

**Plugin information**

- **FR-022**: The anonymous plugin information MUST report the `parentLock` capability in addition to `profiles`, and
  MUST NOT include the lock type, the verifier or anything else about the lock.
- **FR-022a**: Any signed-in user, including kids and outside their allowed hours, MUST be able to read the lock
  status: the lock type (digits, remote sequence, or none) and the last change time. Anonymous callers MUST NOT.

**Dashboard page**

- **FR-023**: The Kids page MUST show whether a lock is set, its type and when it was last changed, and MUST let an
  administrator set or replace the code (type, code and confirmation) and remove the lock (after confirming).
- **FR-024**: The page MUST validate the code against FR-004 before saving, MUST explain that a code is needed for
  parents to unlock from TVs, and MUST NOT show the code or the verifier after saving.
- **FR-025**: The page content served by Jellyfin MUST NOT include lock data; it MUST be loaded through authenticated
  requests.

### Key Entities

- **Household parent lock**: at most one per server. Type (digits or remote sequence), verifier (salted PBKDF2 hash in
  Wholphin's format), last changed time. A change counter (or equivalent) lets grants know whether the lock changed
  since they were issued.
- **Parent grant**: in memory only. Random value, issuing user, device, expiry time, and the lock version it was issued
  under.
- **Throttle record**: in memory only, per user and device. Consecutive wrong tries, the end of the current wait, and
  the length of the last wait.
- **Plugin information** (from feature 1): gains the `parentLock` capability; its lock type field is removed.
- **Lock status**: what any signed-in user may learn: the lock type (or none) and the last change time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A verifier produced by Wholphin's hasher verifies its code on the server unchanged, and one produced by
  the server verifies in Wholphin's algorithm, for 100% of codes tested (digits and remote sequences).
- **SC-002**: 100% of wrong-code answers are success responses saying not valid; none is an "unauthenticated" answer.
- **SC-003**: From one user and device, the sixth consecutive wrong attempt within 30 seconds is refused with a wait
  time, in 100% of runs.
- **SC-004**: A grant obtained on one device is refused on any other device, by any other user, after 5 minutes, after
  a lock change and after a server restart, in 100% of attempts.
- **SC-005**: A parent can unlock on a kid's TV and make a profile change in under 30 seconds, including outside the
  kid's allowed hours.
- **SC-006**: 100% of lock changes, verifier reads and profile changes attempted with kids' credentials and no valid
  grant are refused, and none changes stored data.
- **SC-007**: An administrator can set the first household code from the Kids page in under 1 minute.
- **SC-008**: Neither the code nor any grant appears in the data store, the server logs, the dashboard page source or
  the anonymous plugin information, and the anonymous plugin information says nothing about the lock beyond the
  `parentLock` capability.
- **SC-009**: Changing the device id in the request never resets or bypasses the wrong-code wait, and no user gets more
  than 20 consecutive wrong guesses across all devices before waiting, in 100% of runs.

## Assumptions

- Defaults come from docs/plan.md §3.6 and §4.1: grant lifetime 5 minutes; throttle after 5 wrong tries, 30 seconds
  doubling to 15 minutes; verifier format and parameters from Wholphin's `PasscodeHasher` (minimum code length 4,
  120 000 iterations, 16-byte salt). The 32-symbol maximum and the accepted import ranges are new, chosen to bound
  the cost of a check.
- The first lock is set or imported by an administrator (dashboard, script or a Wholphin administrator profile); with
  no lock there is no grant, so no one else can authorize it.
- The "challenge" lock type (an arithmetic question) stores no secret and stays device-only; the server never stores
  or reports it. A household using it on its TVs has no server lock, and server changes need an administrator.
- Grants are bound to both the user and the device, as in plan §4 (`GrantService`): a grant obtained while the kid is
  signed in on a TV only works for that kid's session on that TV.
- The device is the one Jellyfin registered for the caller's credentials. Jellyfin does not check the request's device
  id header against the token (`AuthorizationContext.cs:144-147`), so the plugin does not trust it and adds no device
  identity of its own. API keys have no registered device and are throttled per key.
- Grants extend only feature 1's profile changes and this feature's lock endpoints. Later features (bonus time,
  schedules, recommended policy) will accept the same grant.
- The lock type stays out of the anonymous plugin information (constitution IV). Feature 1's `lockType` field in the
  plugin information is removed; API version 1 is not released yet, so no client breaks.
- Removing the lock is administrator-only; with a grant a parent can change the code but not remove it.
- A per-kid lock override, remote unlock of a device and the device lock itself are out of scope (plan: feature 6 and
  "device lock stays local").
- Export and import of plugin data, including the lock, are out of scope (feature 6).
