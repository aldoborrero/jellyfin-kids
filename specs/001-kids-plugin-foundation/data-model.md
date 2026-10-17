# Data model: Kids plugin foundation

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## KidProfile

A Jellyfin user marked as a kid; at most one per user. A row exists if and only if the user is a kid. This is the
same rule as Wholphin's `KidProfile` entity.

| Field | Type | Default | Rules |
|---|---|---|---|
| `UserId` | Guid | (key) | A Jellyfin user that exists when the profile is created (FR-006) |
| `Revision` | long | 1 on create | +1 on every successful update (FR-008). Never reused while the row exists |
| `SearchAllowed` | bool | false | |
| `AutoplayEnabled` | bool | false | |
| `AutoplayCap` | int | 3 | 1–10 inclusive (FR-007, FR-010) |
| `Sections` | list of `HomeSectionEntry` | all five, visible, default order | Normalised and validated, see below |
| `PickedItemIds` | ordered list of Guid | empty | No duplicates, at most 50 (see below) |
| `CreatedAt`, `UpdatedAt` | UTC timestamp | now | Stored for diagnostics, not exposed in the client DTO |

### HomeSectionEntry

| Field | Type | Rules |
|---|---|---|
| `Section` | enum `HomeSection`: `KeepWatching`, `Picked`, `Libraries`, `New`, `Favourites` | Unknown values are rejected (FR-011). Serialised as strings |
| `Visible` | bool | Defaults to true |

The order is the list order. The default order is the enum order above, matching Wholphin's `KidsHomeSection`.

### Validation (on create and update)

1. **AutoplayCap**: must be within 1–10, or the change is rejected with a 400 naming `AutoplayCap`.
2. **Sections**:
   - An unknown section is rejected with an error keyed `Sections[i]`. Requests carry the section as a **string**
     (case-insensitive name), which the validator converts to `HomeSection`. If it were an enum, an unknown value
     would fail JSON binding first, with a key like `$.Sections[0].Section` (research R6). Numbers are not accepted.
   - A duplicate section is rejected.
   - Missing sections are appended, visible, in the default order. This is the same normalisation as
     `KidsHomeSectionEntry.normalize` in Wholphin.
3. **PickedItemIds**:
   - Duplicates are rejected.
   - More than 50 entries are rejected. The cap keeps a row small; a kid's home row never shows more.
   - Each id that is **not already in the stored profile** must resolve through `IPickCatalog` to an existing
     BoxSet or Playlist (FR-010, Clarification Q1).
   - Ids that are already stored are kept even if the item has since been deleted (Edge Cases).
   - Whether the kid can see a pick is not checked.

All errors in one request are collected and returned together, keyed by field.

### State transitions

```text
                mark (create, revision 0 → 1)
 [not a kid] ─────────────────────────────────▶ [kid @ r1]
      ▲                                              │ update with base r → r+1
      │ unmark │ Jellyfin user deleted │             ▼
      │        │ orphan sweep          │        [kid @ r+1]
      └──────────────────────────────────────────────┘
```

- Mark when already a kid → conflict (409 with the current profile); nothing changes.
- Update with a stale base revision → conflict (409 with the current profile); nothing changes.
- Update or unmark when not a kid → 404.
- Unmark removes the row; marking again starts at revision 1 with defaults.

## PluginInfo (not stored)

`PluginVersion` (string), `ApiVersion` (int, 1), `Features` (string list, `["profiles"]` in this feature),
`LockType` (string or absent; absent until feature 2). Nothing in it is household-specific (FR-003).

## PickCandidate (not stored; read from Jellyfin)

`Id` (Guid), `Name` (string), `Type` (`BoxSet` or `Playlist`). It comes from `ILibraryManager` with no user
filter (research R9).

## Jellyfin user (owned by Jellyfin, read only)

The plugin reads the id and, through `IAuthorizationContext`, the caller's administrator permission and whether
the caller is an API key. The dashboard page reads names and `Policy.IsAdministrator` from Jellyfin's own
`GET /Users`.

## Storage layout (`kids.db` in the plugin data folder)

```sql
-- user_version 1
CREATE TABLE kid_profile (
  user_id    TEXT    PRIMARY KEY,      -- Guid "N" format
  revision   INTEGER NOT NULL CHECK (revision >= 1),
  settings   TEXT    NOT NULL,         -- JSON: SearchAllowed, AutoplayEnabled, AutoplayCap, Sections, PickedItemIds
  created_at TEXT    NOT NULL,         -- ISO 8601 UTC
  updated_at TEXT    NOT NULL
);
```

- **Migrations**: `PRAGMA user_version` is read when the database is first used (lazily, `EnsureInitialized`; the
  orphan sweep triggers it at startup), and steps `n+1 … latest` run inside one transaction,
  each idempotent. An unknown higher version (a downgraded plugin) is logged, and profile endpoints answer 500
  rather than risk the data (Edge Cases: store broken).
- **Settings JSON**: unknown properties are ignored on read and missing ones take defaults. This lets later
  features add fields (for example the allowance in feature 3) without a table migration.
- **Future tables** (`household`, `usage_day`, `bonus`) arrive with features 2 and 3 as `user_version` steps
  (docs/plan.md §2.6).
