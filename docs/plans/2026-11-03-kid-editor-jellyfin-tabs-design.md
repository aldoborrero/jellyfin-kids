# Kid editor with Jellyfin tabs

Setting up a kid today means visiting five places: Jellyfin's Users pages (Profile, Access, Parental control) and
Dashboard → Kids. The kid editor already reads the kid's Jellyfin policy and lists what still leaves them unprotected,
but each Fix link sends the parent out to a Jellyfin tab. This change lets the kid editor edit the parts of that policy
that matter for a kid, so a kid is configured from one page.

Mockup: https://claude.ai/artifact/DsccZaYuVanoJd5H5JDYeD

## Scope

In: editing an existing kid's Jellyfin policy from the kid editor.

Out: creating users, passwords, allowed and blocked tags, devices, age presets, any new server endpoint.

## Layout

The kid editor gets tabs, mirroring Jellyfin's user editor:

| Tab | Fields | Saved with |
|---|---|---|
| Kids (today's editor) | Screen time, sitting limit, bonus, avatar, home sections, picks, live TV channels | The plugin API, as today |
| Profile | Manage the server (shown only while the kid is an administrator, so it can be turned off), Allow Live TV access, Allow media downloading | `updateUserPolicy` |
| Access | Enable access to all libraries, or a checkbox per library | `updateUserPolicy` |
| Parental control | Maximum parental rating, block unrated items per type, allowed hours (add in a dialog, remove per row) | `updateUserPolicy` |

The Jellyfin tabs say their settings are the kid's Jellyfin account settings and link to Jellyfin's user page for the
rest. The unprotected warnings stay above the tabs; each Fix opens the editor tab that fixes it instead of leaving the
page, and the warnings are recomputed from the saved policy after each save.

## Saving, as jellyfin-web does

Each Jellyfin tab is its own form with its own Save, like `Access.tsx` and `ParentalControl.tsx` in jellyfin-web:

1. Re-read the user with `ApiClient.getUser(id)`, so the write starts from the current policy.
2. Change only the tab's fields:
   - Profile: `IsAdministrator`, `EnableLiveTvAccess`, `EnableContentDownloading`.
   - Access: `EnableAllFolders`, `EnabledFolders` (empty when all libraries are on).
   - Parental control: `MaxParentalRating`, `MaxParentalSubRating`, `BlockUnratedItems`, `AccessSchedules`.
3. `ApiClient.updateUserPolicy(id, policy)`; every field the page doesn't show is sent back as it came.
4. Toast "Settings saved", recompute the warnings.

On load the editor also fetches `ApiClient.getParentalRatings()` (grouped by score and sub score, as jellyfin-web
does) and the media folders.

## Errors

- A failed `updateUserPolicy` shows the error inline in the tab and keeps the form's values for a retry.
- Turning off administrator on the last administrator is refused by Jellyfin; its message is shown as is.
- The editor's "Unsaved changes" state is per tab; switching tabs keeps unsaved values and marks the tab.
- The dashboard already requires an administrator, so the Jellyfin tabs never meet a 403 in normal use; one is shown
  as a save error.

## Testing

Front end only (`kidsUser.html`, `kidsUser.js`); the server is unchanged. The merge of a tab's fields into the policy
and the rating grouping are small pure functions in `kidsUser.js`. No JavaScript test framework is added.

A checklist in the quickstart, on the dev server with alice and bob:

- Each Fix opens the right tab, and its warning goes away after saving.
- Profile, Access and Parental control save their fields, and Dashboard → Users → Alice shows the same values.
- Nothing is lost: tags and devices set in Jellyfin first survive a save of every Kids tab.
- An allowed-hours slot can be added and removed, and the plugin's bedtime enforcement follows it.
- A policy change from another browser tab survives a save here, except for this tab's fields.
- The last-administrator error shows inline.
- Light theme, dark theme and phone width.

New screenshots of the editor in `docs/assets/screenshots/`, and the README's Dashboard pages row says a kid is set up
from their editor. CI is unchanged.
