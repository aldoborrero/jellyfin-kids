# Contract: Kid editor (feature 13 additions)

Extends [feature 7's dashboard contract](../../007-kids-dashboard-ux/contracts/dashboard-ux.md) and
[feature 12's](../../012-kids-avatar/contracts/dashboard-avatar.md); their shared rules (elements, toasts, live
regions, text-only names) apply. The Kids page is unchanged.

## Screen time section (`kidsUser.html`, `kidsUser.js`)

```text
Today: 32 of 60 min + 15 min bonus · 43 min left
This sitting: 30 of 30 min · on a break, 12 min to go
[Reset today] [End break]                ← End break only while BreakRemainingSeconds > 0
…
Bonus for today (minutes) [    ] [Add]
Extra minutes on top of today's allowance, added straight away.
Today's bonuses                           ← hidden when there are none
  15 min at 18:02 by mum  Undo            ← Undo while CanUndo
  30 min at 16:00 by alice with a parent grant · can no longer be undone
```

- **End break**: `<button is="emby-button" class="raised kidsEndBreak">End break</button>` in `.kidsStatusActions`
  after Reset today, `hidden` unless the last status has `BreakRemainingSeconds > 0`. Click: disable, `POST
  …/WatchTime/Break/End`, render the answer, toast "{Kid}'s break was ended", re-enable. Failure: `Dashboard.alert`
  "Could not end the break."
- **Today's bonuses**: `div.kidsBonusList` (`role="status"`, `aria-live="polite"`) under the bonus help text, with a
  `p.fieldDescription` heading "Today's bonuses" and one `p.fieldDescription.kidsBonusItem` per bonus: "{Minutes} min
  at {HH:mm} by {GrantedBy}", times on the server clock when known. While `CanUndo`: an
  `<button is="emby-button" class="button-link kidsBonusUndo">Undo</button>` with aria-label "Undo {Minutes} min
  bonus". Otherwise the text ends " · can no longer be undone". Empty list or read failure: the block is hidden.
- **Undo**: disable the button, `DELETE …/WatchTime/Bonus/{Id}`, render the status, toast "Undid the {Minutes} min
  bonus", reload the list. 409: `Dashboard.alert` "This bonus can no longer be undone." and reload; 404: reload
  silently (someone else undid it); other failures: "Could not undo the bonus."
- The list reloads after the status loads, after Add, Undo, Reset today and End break. Undo turns into the "can no
  longer be undone" text at the next reload (the page does not run a timer).
- Names (`GrantedBy`, the kid's name) are text nodes; dialogs escape them.
