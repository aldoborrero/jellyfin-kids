# Contract: Kids dashboard pages (feature 12 additions)

Extends [feature 7's dashboard contract](../../007-kids-dashboard-ux/contracts/dashboard-ux.md); its shared rules
(elements, save model, unsaved note, live regions, text-only names) apply.

## Kid editor (`kidsUser.html`, `kidsUser.js`)

New block at the top of **Browsing**, before "Allow search":

```text
Avatar  [Jellyfin profile image ▾]   (L)   ← coloured initial, hidden for "Jellyfin profile image"
        Shown on kids apps that support avatars, such as Peke for Jellyfin.
```

- `<label class="selectLabel" for="kidsAvatar">Avatar</label>` + `<select is="emby-select" id="kidsAvatar"
  class="emby-select-withcolor emby-select kidsAvatar">` with options `""` "Jellyfin profile image", `leo` "Leo",
  `max` "Max", `nico` "Nico", `lucy` "Lucy", `sara` "Sara", `mia` "Mia", and the arrow container.
- A stored id not in the list is added as an extra option labelled with the id, selected, so Save sends it back.
- The preview `span.kidsAvatarInitial` (`aria-hidden`) follows the select; colours in data-model.md.
- `Avatar` is part of what Save sends (null for "Jellyfin profile image") and of the unsaved-changes snapshot. A 400
  `errors.Avatar` shows next to the select.

Health lines: after feature 7's lines, one per hidden picked channel (below), and the editor's "unrated live TV may be
hidden" notice under Live TV channels stays.

## Kids page (`kids.html`, `kids.js`)

- A kid row whose profile has an avatar shows `span.kidsAvatarInitial` instead of the Jellyfin picture or the person
  icon, with `title`/`aria-label` "Avatar: {Name of the avatar}". Until the profile loads, the Jellyfin picture is
  shown.
- Health lines: as in the editor, from the kid's profile and `GET /Kids/Admin/LiveTvChannels?userId={id}`.

## Hidden channel lines (both pages)

For each picked channel (Jellyfin's channel order) listed with `HiddenFromUser: true`, when the kid's
`Policy.EnableLiveTvAccess` is not false:

| Reason (research R5) | Text |
|---|---|
| unrated | {Channel} is hidden from {Kid}: unrated live TV channels are blocked. Untick Live TV under "Block items with no or unrecognized rating information", or give the channel a rating. |
| rating | {Channel} is hidden from {Kid}: it is rated {Rating}, above the maximum allowed rating. Raise the maximum or change the channel's rating. |
| unrecognised | {Channel} is hidden from {Kid}: Jellyfin does not recognise its rating {Rating}, and unrated live TV channels are blocked. Untick Live TV under "Block items with no or unrecognized rating information", or change the channel's rating. |
| other | {Channel} is hidden from {Kid} by Jellyfin's parental control, for example a blocked or missing tag. Check the Parental control tab. |

- Fix → `#/dashboard/users/{id}/parentalcontrol`, aria-label "Fix: {text}".
- No line when the flagged list could not be read, when live TV access is off (feature 7's line covers it) or for a
  picked channel that is not on the server.

## Wording

- Kids page banner: "**Limits only apply in the kids app.** Other apps keep playing when …" (was "in Wholphin").
- "App only" pill title and the editor's App only line: "Limits only apply in the kids app while enforcement is off".
