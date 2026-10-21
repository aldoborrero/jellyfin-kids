# Contract: live TV on the kid editor

This extends the [KidsUser editor](../../001-kids-plugin-foundation/contracts/dashboard-pages.md). Only
`kidsUser.html` and `kidsUser.js` change; the Kids page (`kids.html`, `kids.js`) is untouched. The HTML stays static
and anonymous.

## "Live TV channels" section

- Placed after "Picked by your grown-ups", with Jellyfin's native layout (`verticalSection`, `sectionTitle`,
  `paperList`, `listItem`, `emby-checkbox`), like the rest of the page.
- Lists every channel from `GET /Kids/Admin/LiveTvChannels`: a checkbox, the logo
  (`ApiClient.getScaledImageUrl(id, {type: 'Primary', tag, maxHeight: 40})`) or a `live_tv` icon, the name, and the
  number as the secondary line. Allowed channels are ticked.
- Stored ids that are not on the server any more are listed first as "Missing (removed from the server)" with their
  id, ticked; unticking drops them.
- Empty list: a prominent notice (warning border, like the conflict banner) "No live TV channels allowed. This kid
  cannot watch live TV on any device until you tick channels below." It updates as boxes are ticked.
- Warnings, shown above the list when they apply (from `GET /Users/{id}` policy):
  - `EnableLiveTvAccess` false: "Live TV access is off for this user in Jellyfin, so the channels below will not play.
    Turn on 'Allow Live TV access' in the user's profile." with a link to `#/dashboard/users/{id}/profile`;
  - `BlockUnratedItems` contains `LiveTvChannel` or `LiveTvProgram`: "This user's content restrictions block unrated
    live TV channels or programs, so some channels or guide entries may be hidden."
- No channels on the server: "This server has no live TV channels." and no list.
- Saving sends `LiveTvChannelIds` (the ticked ids, in list order) with the rest of the profile, under the same revision
  and conflict handling. A 400 keyed `LiveTvChannelIds` or `LiveTvChannelIds[i]` shows next to the section.
