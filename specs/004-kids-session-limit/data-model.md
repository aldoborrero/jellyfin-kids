# Data model: Kids session limit

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

`kids.db` goes from schema version 3 to 4. No table changes: the kid profile gains one setting in its settings JSON.

## KidProfileSettings (changed)

| Field | Type | Default | Rules |
|---|---|---|---|
| `SessionLimitMinutes` | int or null | null (no sitting limit) | 5–120 inclusive, error key `SessionLimitMinutes` (FR-001, FR-004) |

- A PUT is a full replacement, so omitting the field clears it.
- The server only stores it (FR-005). It has no effect on counting, status or playback.

## Migration 3 → 4

```sql
UPDATE kid_profile SET settings = json_insert(settings, '$.SessionLimitMinutes', NULL);
```

- Existing rows read the field as null and keep every other setting (FR-002).
- `json_insert` adds the key only when missing, so running on a row that already has it changes nothing.
- The other tables (`parent_lock`, `usage_day`, `bonus`, `reported_segment`) are untouched.

## KidsInfo (changed)

`Features`: `["profiles", "parentLock", "allowance", "sessionLimit"]`.
