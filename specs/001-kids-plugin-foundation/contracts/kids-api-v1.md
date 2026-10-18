# Contract: Kids client API, version 1 (feature 1 subset)

Base path `/Kids` on the Jellyfin server.

**JSON shape.** Jellyfin's formatters apply (research R5):
- responses are PascalCase by default, and camelCase with `Accept: application/json; profile="CamelCase"`;
- enums are strings;
- null properties are omitted;
- request bodies are case-insensitive.

The examples below are PascalCase.

**Auth.** Calls carry the standard `Authorization: MediaBrowser … Token="…"` header, or `?ApiKey=`.

| Caller | Meaning |
|---|---|
| anonymous | no token |
| self | the token's user is the `{userId}` in the path |
| admin | a user with the administrator permission, or an API key |

Since feature 2, mutations accept "admin or grant": a parent grant in `X-Kids-Grant`, valid for the same user and
registered device. See [the feature 2 contract](../../002-kids-parent-lock/contracts/kids-api-v1-parent-lock.md).
`GET /Kids/Admin/PickCandidates` stays administrator only.

**Status codes**

| Code | When | Body |
|---|---|---|
| 401 | missing or invalid token | none |
| 403 | signed in but not allowed (FR-016) | none |
| 404 | the user is not a kid, or does not exist | ProblemDetails |
| 400 | validation failed | ValidationProblemDetails, `errors` keyed by field. ProblemDetails are always camelCase (`title`, `status`, `errors`), whatever the JSON profile; the keys inside `errors` are field names as listed below |
| 409 | revision conflict or already a kid | the current `KidProfile` |
| 500 | store failure; never an empty success | none |

Authenticated endpoints use Jellyfin's `IgnoreParentalControl` policy, so they work outside a kid's allowed hours
(FR-014).

## GET /Kids/Info

- **Auth**: anonymous.
- **200**: `{"PluginVersion":"0.0.1.0","ApiVersion":1,"Features":["profiles"]}`.
  - There is no `LockType`: feature 2 dropped it from `Info` (nothing per-household in anonymous answers). Clients
    read it from `GET /Kids/Parent/Lock` after signing in. Feature 2 adds `parentLock` to `Features`.
  - Clients detect capabilities by `Features`: 200 means plugin mode; 404 means no plugin (local mode).
  - The body contains nothing household-specific (FR-003).

## GET /Kids/Profiles

- **Auth**: any signed-in user.
- **200**: `[{"UserId":"<guid>","Revision":3}, …]`. Ids and revisions only (FR-012), unordered.

## GET /Kids/Users/{userId}/Profile

- **Auth**: self or admin. Any other user gets 403.
- **200**: `KidProfile`:

```json
{
  "UserId": "5f1c…",
  "Revision": 3,
  "SearchAllowed": false,
  "AutoplayEnabled": true,
  "AutoplayCap": 2,
  "Sections": [
    {"Section": "Picked", "Visible": true},
    {"Section": "KeepWatching", "Visible": true},
    {"Section": "Libraries", "Visible": true},
    {"Section": "New", "Visible": false},
    {"Section": "Favourites", "Visible": true}
  ],
  "PickedItemIds": ["a1b2…", "c3d4…"]
}
```

- **404**: the user is not a kid, or does not exist. A kid reading their own profile after being unmarked also
  gets 404.

## PUT /Kids/Users/{userId}/Profile

Marks a user as a kid (create) or replaces a kid's settings (update).

- **Auth**: admin. A kid's own token gets 403, even for `{userId}` = self (FR-015).
- **Body**: `KidProfileUpdate`:

```json
{
  "Revision": 0,
  "SearchAllowed": false,
  "AutoplayEnabled": true,
  "AutoplayCap": 2,
  "Sections": [{"Section": "Picked", "Visible": true}],
  "PickedItemIds": ["a1b2…"]
}
```

- `Revision` is required:
  - a missing `Revision` gets 400 with `errors.Revision`; it is never treated as "create";
  - `0` means create (mark);
  - `N ≥ 1` means update based on revision N.
- The settings fields are optional. Omitted ones take their **defaults** (see data-model). This is a full
  replacement, so clients send the whole profile when updating. `{"Revision":0}` marks a kid with the defaults.
- `Sections[].Section` is a section name string, matched case-insensitively. An unknown name gets 400 with
  `errors["Sections[i]"]`.
- **Responses**:
  - **200**: the stored `KidProfile` (revision 1 after a create, N+1 after an update).
  - **400**: validation failed (data-model "Validation"). Example: `{"title":"One or more validation errors occurred.","status":400,"errors":{"AutoplayCap":["Must be between 1 and 10."],"PickedItemIds[1]":["Not a collection or playlist."]}}`.
  - **404**:
    - on create, the Jellyfin user does not exist;
    - on update, the user is not a kid (any more).
  - **409**: the current `KidProfile`, in two cases:
    - on create, the user is already a kid;
    - on update, `Revision` is not the current revision.

    Nothing is changed. The client merges or asks the parent, then retries with the returned revision.
- **Order of checks**:
  1. A missing `Revision` gets 400.
  2. The caller must be admin, or gets 403.
  3. Existence and revision are checked: 404, or 409.
  4. Only then are the settings validated: 400.

  So a request with a stale revision **and** invalid settings gets **409**, not 400: the client has to reload
  anyway, and validates against the current profile when it retries.

## DELETE /Kids/Users/{userId}/Profile

Unmarks a kid and deletes their settings.

- **Auth**: admin.
- **Responses**:
  - **204**: deleted.
  - **404**: not a kid.

## GET /Kids/Admin/PickCandidates

Lists the server's collections and playlists for the dashboard's picks chooser (FR-025).

- **Auth**: admin.
- **Query**:
  - `search` (optional, name contains, case-insensitive);
  - `ids` (optional, comma-separated: resolve exactly these ids, used to show stored picks);
  - `limit` (default 50, max 200).
- **200**: `[{"Id":"a1b2…","Name":"Bluey","Type":"BoxSet"},{"Id":"c3d4…","Name":"Bedtime songs","Type":"Playlist"}]`,
  ordered by name. With `ids`, ids that no longer exist or are not a collection or playlist are simply absent; the
  editor shows them as missing.
- Covers all collections and playlists on the server, whoever owns them (research R9).

## Compatibility rules

- New endpoints, new optional request fields and new response fields do not change `ApiVersion`. Clients ignore
  unknown response fields.
- Adding a home section value is a breaking change (Clarification Q3). It raises `ApiVersion`, and the plugin
  keeps refusing unknown values for clients that speak version 1.
