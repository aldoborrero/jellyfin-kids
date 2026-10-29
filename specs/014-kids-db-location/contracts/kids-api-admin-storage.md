# Contract: `GET /Kids/Admin/Storage` (feature 14)

Dashboard-only, like `GET /Kids/Admin/PickCandidates`: not part of the client API, so `ApiVersion` 1 /
`ApiMinorVersion` 2 and `Features` in `GET /Kids/Info` are unchanged.

## Request

`GET /Kids/Admin/Storage` with a Jellyfin token (`Policies.IgnoreParentalControl`).

## Responses

| Status | When | Body |
|---|---|---|
| 200 | The caller is an administrator or an API key | `StorageInfo` |
| 401 | No or invalid token | Jellyfin's own |
| 403 | Any other user, including a kid with a parent grant (a grant unlocks profile changes, not dashboard data) | empty |

```json
{
  "Folder": "/var/lib/jellyfin/data/kids",
  "DatabasePath": "/var/lib/jellyfin/data/kids/kids.db",
  "Available": true
}
```

- `Folder`: the kids data folder (`<DataPath>/kids`), absolute.
- `DatabasePath`: the database file in it.
- `Available`: false when the database could not be opened or a migration was refused this run (the log says why).

camelCase with `profile="CamelCase"` as for every Kids endpoint.
