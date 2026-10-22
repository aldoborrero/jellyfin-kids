# Research: Kids dashboard UX and "Reset today"

Citations are against `.reference/jellyfin-web` and `.reference/jellyfin` at tag `v12.2`, and the plugin source at
0.0.6.0. The UX findings come from the report "UX del dashboard del plugin Kids" (Wholphin repo, `reports/`) and its
research notes; this file keeps only what the design depends on.

## R1. Fix links: Jellyfin's user editor routes

- **Decision**: `#/dashboard/users/{userId}/{tab}` with `tab` = `profile`, `access` or `parentalcontrol`.
- **Findings**: the dashboard route is `users/:userId/:tab` (`apps/dashboard/routes/_asyncRoutes.ts:32`), tab values
  come from `UserTab` (`features/users/constants/userTab.ts`). The settings sit on these tabs:
  - Profile (`Profile.tsx:183-239`): `IsAdministrator`, `EnableLiveTvAccess`, `EnableContentDownloading`;
  - Access (`Access.tsx:52-64`): `EnableAllFolders`, `EnabledFolders`;
  - Parental control (`ParentalControl.tsx:47-56`): `MaxParentalRating`, `BlockUnratedItems`, `AccessSchedules`.
- **Alternatives**: injecting a Kids tab into the user editor (startup filter or File Transformation) — fragile,
  jellyfin-web only, and File Transformation is GPL; rejected by the report.

## R2. Health conditions from `UserDto.Policy`

- **Decision** (all from `ApiClient.getUsers()` on the Kids page and `ApiClient.getUser(id)` in the editor):

  | Issue | Condition | Fix |
  |---|---|---|
  | admin | `Policy.IsAdministrator` | profile |
  | every library | `Policy.EnableAllFolders` | access |
  | no rating cap | `Policy.MaxParentalRating` null or absent | parentalcontrol |
  | unrated shown | `BlockUnratedItems` lacks `Movie` or `Series` | parentalcontrol |
  | live TV off, channels picked | `EnableLiveTvAccess === false` and the profile lists ≥ 1 channel | profile |
  | live TV on, none picked | `EnableLiveTvAccess !== false`, the profile lists none, the server has ≥ 1 channel | kid editor |
  | downloads | `Policy.EnableContentDownloading` | profile |

- **Rationale**: `UserPolicy`'s constructor defaults are `EnableAllFolders = true`, `EnableLiveTvAccess = true`,
  `EnableContentDownloading = true`, `MaxParentalRating = null` (`MediaBrowser.Model/Users/UserPolicy.cs`), so a
  freshly marked kid has four or five issues. Unrated movies and shows are the most cited leak through a rating cap;
  blocking unrated `LiveTvChannel` is already a separate warning in the editor (feature 6), so it is not required.
- **Order**: admin first, then the order above.

## R3. Guaranteed elements and the select box

- **Decision**: write `<label class="selectLabel" for="id">…</label>` and
  `<select is="emby-select" id="id" class="emby-select-withcolor emby-select">` plus the arrow container by hand.
- **Findings**: `emby-select` is not in jellyfin-web's static import graph for plugin pages; it is loaded by accident
  through SyncPlay's settings editor. Its `attachedCallback` returns early when the element already has the
  `emby-select` class (`elements/emby-select/emby-select.js:110-128`), so the hand-written label and arrow are not
  duplicated when it does load, and the select looks the same when it does not. `emby-toggle` and `emby-collapse` are
  not loaded; this feature uses `emby-checkbox` and no collapsibles.

## R4. Toasts and confirmations

- **Decision**: success → `Dashboard.alert('…')` (string = toast, `utils/dashboard.js:174-185`); confirmations →
  `Dashboard.confirm({ title, text, confirmText, primary: 'delete' }, '', callback)`: the options object goes through to
  `components/confirm/confirm.ts:19-52`, which honours `confirmText` and `primary`. Errors stay inline or use
  `Dashboard.alert({ title, message })` (modal).
- **Replaces**: the inline "Saved." of the lock form and the configuration toast of `processPluginConfigurationUpdateResult`
  (kept as the same toast text for the screen-time form, written by the page).

## R5. The server's time and UTC offset without an API change

- **Decision**: from a Status: `NextDayStartsAt` (UTC) is the household's day-start hour `H` in the server's zone, so
  `offsetMinutes = ((H × 60 − minutesOfDayUtc(NextDayStartsAt)) mod 1440)`, normalised to −720…+840. Server time =
  `ServerTime + offset`, formatted as `HH:mm` in UTC after shifting, labelled "server time (UTC+02:00)". The editor
  reads `H` from the plugin configuration (admin call), the Kids page already has it.
- **Rationale**: Status has no zone name and the constraints forbid changing it; the shift-then-format-in-UTC trick
  shows the server's wall clock in any browser zone.
- **Limits**: around a daylight-saving change the offset is the one at the next day start; with no kid there is no
  Status, and the line is left out. A day start inside a spring-forward gap is shifted by Jellyfin's `DayClock` and
  would show an offset one hour off for that night.
- **Alternatives**: a `TimeZone` field in Status or Info (cleaner, but an API change); the browser's zone (wrong when
  the parent is elsewhere, which is what the report found).

## R6. Reset: what is cleared and how

- **Decision**:
  1. `UsageStore.ResetDayAsync(userId, day)`: in one write transaction, read the day's `used_ms` and bonus sum, then
     `DELETE FROM usage_day WHERE user_id = $id AND day = $day` and `DELETE FROM bonus WHERE user_id = $id AND
     day = $day`; returns what was cleared. `reported_segment` rows are kept (FR-024).
  2. `UsageService.ResetDayAsync(caller, userId)`: not a kid → NotAKid; otherwise, under the flush lock (as
     `PurgeAsync`), drop the pending amount for (kid, today), call the store, then raise `UsageChanged`. Returns the
     cleared amounts and the new Status.
  3. `EnforcementState.ResetSitting(kid)`: `Sitting.Reset()` forgets the sitting (no playing time, no break).
  4. The controller reloads the kid (`EnforcementService.TryLoadAsync`), as bonus does, then answers the Status.
- **Rationale**: serialising with flushes means a flush in progress cannot write the cleared time back. Deleting rows
  instead of writing 0 keeps `usage_day`'s "only add" write path unchanged; the next counted time recreates the row.
  Deleting bonus rows makes "today's bonus is 0" true for every reader.
- **In-flight time**: `PlaybackClock` adds at most one interval (≤ 30 s) after the reset that started before it;
  accepted and documented (spec edge cases).
- **Denials**: time's up is lifted by the reload (used 0 < allowance, unless the allowance is 0); the sitting limit by
  the sitting reset; allowed hours are untouched (constitution I).

## R7. Reset authorization and log

- **Decision**: `[Authorize(Policy = Policies.IgnoreParentalControl)]` + `ProfileAccess.CanMutate` (administrators,
  API keys, grant holders), the bonus rule; 403 otherwise, 404 for a non-kid. Log at information level:
  `Screen time of {UserId} reset for {Day} by {Caller}: {UsedMinutes} min used and {BonusMinutes} min bonus cleared`.
  `caller.LogName` is the user name or "API key", as for bonuses; `{Caller}` ends in " with a parent grant" when the caller is
  not an administrator or API key; the log line's timestamp is the "when".
- **Body**: none (an empty JSON object is accepted too); a `Consumes` restriction is not set, so a bodyless POST works.

## R8. Capability

- **Decision**: append `watchTimeReset` to `KidsInfo.SupportedFeatures`; the API version stays 1 (additive).

## R9. Unsaved changes

- **Decision**: a snapshot of the form's values (`JSON.stringify` of what Save would send, without the revision),
  taken after load and after each successful save; compare on every `input`/`change` and after list edits; show
  "Unsaved changes" next to Save in a polite live region; `beforeunload` asks while dirty. The Kids page does the same
  for the screen-time form.
- **Limit**: jellyfin-web's in-app navigation cannot be cancelled by a plugin (`viewbeforehide` is not cancellable,
  `viewManager.js:8-16`); `beforeunload` covers reloads and tab closes.

## R10. Browser validation

- **Decision**: Playwright (Python) with nixpkgs Chromium, kept outside the repo (as in features 1, 2, 4, 5 and 6),
  against the dev server. The dashboard theme is a server-side display preference (`CustomPrefs.dashboardTheme`,
  `scripts/settings/userSettings.js:105-140, 426-431`): set it to `dark` and then `light` for the admin through
  `POST /DisplayPreferences/usersettings?userId=…&client=emby` before loading the pages. Viewports 1920×1080 and
  400×900. Screenshots go to `.dev/screenshots/007/` (ignored by git) and their paths are reported.
