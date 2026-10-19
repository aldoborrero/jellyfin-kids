# Research: Kids session limit

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## R1. How the field is stored

- **Decision**: in the profile settings JSON, like every other setting, with schema v4 as a data migration:
  `UPDATE kid_profile SET settings = json_insert(settings, '$.SessionLimitMinutes', NULL)`. `json_insert` only adds the
  key when it is missing, so it never overwrites a value.
- **Rationale**: the request asked for a schema migration with old rows null. Doing it inside the JSON keeps one place
  for profile settings (`KidProfileStore` reads and writes the whole settings object) and makes every stored row carry
  the key, as rows written by this version do (System.Text.Json writes nulls by default).
- **Alternatives considered**:
  - A `session_limit_minutes` column with a `CHECK (BETWEEN 5 AND 120)`: a database-level check, but the store would
    have to read and write the setting outside the settings object, the only one to do so.
  - No migration (feature 3's approach for `DailyAllowanceMinutes`): missing keys already deserialise as null and a
    downgrade keeps working, but it is not what was asked.
- **Consequence**: a 0.0.3 plugin refuses a v4 database (feature 1's newer-schema rule) and kids data is unavailable
  until 0.0.4 is reinstalled. Recorded in plan.md Risks.

## R2. Why a capability flag

- **Decision**: `GET /Kids/Info` `Features` gains `"sessionLimit"`.
- **Rationale**: Jellyfin's JSON options omit null properties (`DefaultIgnoreCondition = WhenWritingNull`, as feature
  3's contract relies on for `AllowanceSeconds`). A kid without a limit and a plugin that does not know the field look
  the same, so a client could otherwise drop a limit set elsewhere. Clients check the capability first.
- **Alternatives considered**: raising `ApiVersion` (not a breaking change, so the constitution does not allow it);
  always writing the field (would differ from every other optional field).

## R3. The editor control

- **Decision**: a number field "Session limit (minutes)", `min=5 max=120 step=1`, empty meaning "No limit", checked on
  input and before saving, with an error slot keyed `SessionLimitMinutes` that the existing ProblemDetails mapping
  (`showErrors`) fills on a 400.
- **Rationale**: the same pattern as the daily allowance in feature 3, so the page has one way of entering minutes and
  the server message lands next to the field with no new code path.
- **Alternatives considered**: a select with fixed steps (5, 10, 15 … 120): fewer typing errors, but it hides valid
  values such as 25 and would not show the server's range message for anything typed elsewhere.

## R4. Who counts the sitting

- **Decision**: the device. The server stores the limit only; the contract says so.
- **Rationale**: plan §1 ("Per-session timer: device by default"). A sitting is a UX notion on one TV (start of kids
  mode to a break), and the daily allowance already covers the cross-device rule on the server.
- **Alternatives considered**: a server "session cap" enforced like the allowance (plan §7 feature 6), which needs a
  definition of a sitting across devices and its own enforcement; kept for later.
