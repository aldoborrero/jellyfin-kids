# Contract: Kids dashboard pages (jellyfin-web 12.x)

These pages are served by Jellyfin from the plugin's embedded resources (`IHasWebPages.GetPages`). The HTML is
fetched anonymously (`GET /web/ConfigurationPage?name=`), so it is static and holds no household data (FR-022).
All data is loaded after the page shows, with the administrator's session (`window.ApiClient`). See research R10
for the mechanics and citations.

| Page name | Resource | Menu | URL |
|---|---|---|---|
| `Kids` | `Configuration/kids.html` | Yes: `EnableInMainMenu`, icon `child_care`, display name "Kids" | `#/configurationpage?name=Kids` |
| `KidsJs` | `Configuration/kids.js` | — | ES module for `Kids` |
| `KidsUser` | `Configuration/kidsUser.html` | No | `#/configurationpage?name=KidsUser&userId=<guid>` |
| `KidsUserJs` | `Configuration/kidsUser.js` | — | ES module for `KidsUser` |

Both modules are `export default function (view, params)` and load their data on `viewshow`.

## Kids (list)

- **Data**: `ApiClient.getUsers()` (Jellyfin) and `GET /Kids/Profiles`.
- **Rows**: one per Jellyfin user, sorted by name. Each row has the name, a "Kid" toggle and an "Administrator"
  badge. Kids also get an "Edit settings" link to `KidsUser`.
- **Warning** (FR-021): a kid who is an administrator shows a visible warning on their row: "Administrators bypass
  every restriction. Use a non-admin account for this kid."
- **Mark**: `PUT /Kids/Users/{id}/Profile` with `{"Revision":0}`.
  - 409 means already a kid: refresh the list.
  - 404 means the user was deleted: refresh.
- **Unmark**: `Dashboard.confirm` ("Remove kid settings for <name>?"), then `DELETE /Kids/Users/{id}/Profile`.
  A 404 is treated as already done.
- **Errors**: any other failure shows `Dashboard.alert` and leaves the toggle unchanged. A 500 on load shows an
  error in place of the list, never an empty list.

## KidsUser (editor)

- **Load**:
  - `GET /Kids/Users/{userId}/Profile` and `ApiClient.getUser(userId)` for the name.
  - If the profile has picks, also `GET /Kids/Admin/PickCandidates?ids=…` to show names and types.
  - A 404 shows "This user is not a kid" with a link back.
- **Fields** (FR-023):
  - Search allowed (checkbox).
  - Autoplay (checkbox).
  - Autoplay limit: a number from 1 to 10, enabled only while autoplay is on. An out-of-range value blocks saving
    with an inline message.
  - Home sections: an ordered list, each with a visibility checkbox and up and down buttons.
  - Picks (FR-024):
    - an ordered list showing name and type, with up, down and remove buttons;
    - a pick missing from the `ids` answer shows "Missing (deleted from the server)" and can be removed;
    - an "Add" search box (`emby-input`, debounced) lists `PickCandidates?search=…`, and choosing a result appends
      it unless it is already picked.
- **Save**: `PUT /Kids/Users/{userId}/Profile` with the loaded `Revision` and every field.
  - 200: store the returned profile (and its new revision), then show "Saved".
  - 400: show each message next to its field (FR-027).
  - 409: show "These settings were changed somewhere else" with a "Load current settings" action that replaces
    the form with the returned profile. Nothing is overwritten (FR-026).
  - 404: "This user is no longer a kid".
- **Controls**: everything is reachable with the keyboard; the up and down buttons are `paper-icon-button-light`.
