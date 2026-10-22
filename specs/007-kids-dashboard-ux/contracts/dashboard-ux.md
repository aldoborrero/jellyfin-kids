# Contract: Kids dashboard pages (feature 7)

Supersedes the dashboard parts of features 1, 3, 4, 5 and 6 where they differ. Both pages stay static HTML served
anonymously; everything is loaded through authenticated calls. Every user-provided string is inserted as text.

## Shared rules

- **Elements**: `emby-button`/`emby-linkbutton`, `emby-input`, `emby-checkbox`, `paper-icon-button-light`; selects
  written as `<label class="selectLabel" for="…">` + `<select is="emby-select" class="emby-select-withcolor
  emby-select">` + the arrow container (research R3). No `emby-toggle`, `emby-collapse`, `emby-textarea`,
  `emby-radio`, `button-alt`, `button-cancel`, `data-require`.
- **Save model**: each form has one Save (`raised button-submit block`). Success → toast (`Dashboard.alert(string)`).
  Validation errors inline next to the field (nothing sent). Server or network errors inline in the form's error line
  or, for actions outside a form, a dialog. Immediate actions (Kid checkbox, bonus, Reset today, Remove lock, Turn on)
  confirm with a toast.
- **Unsaved**: "Unsaved changes" next to the form's Save while it differs from what was loaded or saved
  (`role="status"`); `beforeunload` asks while any form is dirty.
- **Live regions**: banner, errors, lock message, conflict, usage lines and the unsaved note are `role="status"`
  (`aria-live="polite"`); material icons are `aria-hidden`.
- **Words**: "Daily allowance", "No daily limit", "No screen time today"; "Sitting limit", "No sitting limit",
  "This sitting", "Break between sittings"; "server time (UTC±hh:mm)".
- **Server time**: computed from a Status (research R5) and formatted as the server's wall clock with its offset.

## Kids page (`kids.html`, `kids.js`)

### Banner (top of the page, above "Users")

| Condition | Text |
|---|---|
| `EnforcementEnabled` false | **Limits only apply in Wholphin.** Other apps keep playing when a kid's time is up, the sitting limit is reached or allowed hours end. + button **Turn on** |
| `Info.Enforcement.Gate == "degraded"` | **Some playback routes can't be guarded on this Jellyfin version**: {Missing, comma-separated}. Kids may still play through them. Check for a plugin update. |
| `Info.LiveTv.Gate == "degraded"` | **Some live TV routes can't be guarded on this Jellyfin version**: {Missing}. |

"Turn on" → `Dashboard.confirm({ title: 'Turn on enforcement', text: 'The server will stop playback and refuse new
playback on every app when a kid's limits run out.', confirmText: 'Turn on' })` → re-read the configuration, set
`EnforcementEnabled = true`, write it, toast "Enforcement is on", reload the page data. The banner is hidden when no
condition holds. The screen-time form's checkbox and gate line stay where they are.

### Kid row

```text
[avatar] Alice                                                     [Edit settings] [x] Kid
         Kid · 60 min a day                       (or "No daily limit", "No screen time today")
         Today: 12 of 60 min · 48 min left        [App only]
         Watches 1 of 2 channels                  (or "No live TV")
         ⚠ Sees every library                                       Fix
         ⚠ No maximum parental rating                               Fix
```

- "App only" pill: `Status.Enforced === false`; title "Limits only apply in Wholphin while enforcement is off".
- Usage line with an allowance of 0: "Today: {used} min · No screen time today".
- Health lines (research R2), each `listItemBodyText secondary kidsWarning` with a `warning` icon and a Fix
  `emby-linkbutton` (`button-flat`, aria-label "Fix: {text} for {name}"):

| Kind | Text | Fix |
|---|---|---|
| admin | Administrator: bypasses every restriction. Use a non-admin account for this kid. | `#/dashboard/users/{id}/profile` |
| allFolders | Sees every library | `…/access` |
| noRating | No maximum parental rating | `…/parentalcontrol` |
| unrated | Unrated movies and shows are not blocked | `…/parentalcontrol` |
| liveTvOff | Live TV access is off in Jellyfin, so the picked channels don't play | `…/profile` |
| liveTvNone | Live TV access is on but no channels are picked | `#/configurationpage?name=KidsUser&userId={id}` ("Pick") |
| downloads | Downloads are allowed | `…/profile` |

- When the user policy is missing, no health line is shown and nothing claims the kid is fine; when the profile or the
  status cannot be read, their lines say "unavailable".

### Screen time form

- "Day starts at" select, with the line "Server time: 14:32 (UTC+02:00). Each kid's screen time starts again at this
  hour." when a Status gave the offset.
- "Enforce on every app" checkbox and its gate line (unchanged meaning).
- "Break between sittings (minutes)", 1–240.
- Save → toast "Screen time settings saved"; the unsaved note.

### Parent lock

- Save → toast "Parent lock saved" (was inline "Saved."), Remove → toast "Parent lock removed".

## Kid editor (`kidsUser.html`, `kidsUser.js`)

Top to bottom:

1. Back link, title "Kid settings: {name}".
2. Health lines (same table), the admin one first.
3. **Screen time** status block (`role="status"`):
   - "Today: 12 of 60 min · 48 min left" / "Today: 12 min · No daily limit" / "Today: 0 min · No screen time today";
   - "This sitting: 10 of 30 min · 20 min left" / "… · on a break, 5 min to go";
   - "Playback refused until 18:20 (server time): {reason}." (denial end in server time);
   - "App only: limits only apply in Wholphin while enforcement is off." when `Enforced` is false;
   - "Server time: 14:32 (UTC+02:00)";
   - **Reset today** button (`raised button-delete`): `Dashboard.confirm({ title: 'Reset today', text: 'Set
     {name}'s screen time and bonus for today back to 0? If they were stopped for time or the sitting limit, they can
     watch again at once.', confirmText: 'Reset', primary: 'delete' })` → `POST …/WatchTime/Reset` → redraw the status,
     toast "Today's screen time for {name} was reset".
4. **Daily allowance**: checkbox "No daily limit"; number field "Daily allowance (minutes)" 0–1440, disabled while
   the checkbox is ticked; help "Screen time a day, counted on every app. 0 means no screen time today."; while the
   value is 0 the help reads "0 minutes: No screen time today."; error "Between 0 and 1440 minutes.".
5. **Sitting limit**: checkbox "No sitting limit"; number field "Sitting limit (minutes)" 5–120; help "The longest one
   sitting can last, across the kid's devices. A sitting ends after a break of {break} min with nothing playing;
   whichever limit comes first applies."; line "Break between sittings: {break} min, set for the household on the
   [Kids page]."; error "Between 5 and 120 minutes.".
6. Bonus (unchanged field; toast "Added {n} min for today").
7. **Allowed hours** (read-only): "Every day 08:00–20:00", "Weekdays …", "Weekends …", "Monday …"; with none, "Any
   time: no allowed hours are set in Jellyfin."; link "Edit in Jellyfin" → `…/parentalcontrol`. Hours use the
   server's clock (Jellyfin applies them in server time).
8. Browsing, Home sections, Picks (unchanged).
9. Live TV channels: the state line ("Watches 1 of 2 channels" / "No live TV") above the list; the feature 6 notices
   stay.
10. Form error line, the unsaved note, Save.

Save → toast "Saved", the snapshot is retaken, the Status is read again. 409 keeps the conflict banner; 400 errors go
next to their fields.
