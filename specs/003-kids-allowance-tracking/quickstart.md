# Quickstart: validating the Kids allowance tracking

This proves feature 3 end to end. The API is described in
[contracts/kids-api-v1-allowance.md](contracts/kids-api-v1-allowance.md), the pages in
[contracts/dashboard-allowance.md](contracts/dashboard-allowance.md) and the rules in [data-model.md](data-model.md).

## Prerequisites

- Everything from features 1 and 2: the devshell, and `JELLYFIN_KIDS_DEV=$PWD/.dev dev` on <http://127.0.0.1:8096>.
  Run it only while testing, then stop it.
- Provisioning now also creates a "Kids" movie library from `.dev/media/kids`. If the clip is missing, it first
  generates `Test Pattern (2026).mkv`, a 12-minute 320×240 clip made with jellyfin-ffmpeg's `testsrc` and a sine
  tone. (Not `test-clip.mkv`: Jellyfin takes files ending in `-clip`, `-trailer` and the like for extras and hides
  them.)

## 1. Unit tests

```sh
dotnet test Jellyfin.Plugin.Kids.slnx
```

All tests pass, and `dotnet build` has 0 warnings. The new tests cover:

- **PlaybackClock**:
  - playing time counts with a cap of 30 s per gap;
  - pauses do not count;
  - synthetic ticks stop counting 60 s after the last real report;
  - concurrent playbacks add up;
  - events out of order add nothing;
  - stop and unknown-key progress are handled;
  - records are swept after 6 minutes.
- **DayClock** in `Europe/Madrid`:
  - start hours H = 0, 2 and 4;
  - the spring-forward gap (H = 2 is invalid that day) and the fall-back overlap;
  - splitting an interval across a boundary.
- **StatusCalculator**: unlimited, at the allowance, over the allowance, allowance 0 with a bonus, and remaining never
  negative.
- **Usage store**:
  - migration 2 → 3 keeps profiles and the lock;
  - add-only upsert;
  - report idempotency (the same id twice adds once);
  - an invalid request adds nothing;
  - purge;
  - prune by day.
- **Validation**: allowance bounds, bonus bounds, and every segment rule.
- **Access**: status is self or admin; bonus is admin or grant; report is self only.

## 2. Automated checks against the dev server

```sh
scripts/dev-check.sh
```

Feature 1 and 2 checks a–ad still pass, and check a now asserts `allowance` in `Features`. The new checks drive
playback through Jellyfin's own playstate API as alice, on the generated clip. They add about 4 minutes.

| # | Check | Expected |
|---|---|---|
| ae | alice `GET /Kids/Users/{alice}/Status` with no allowance | 200, `UsedSeconds` ≥ 0, no `AllowanceSeconds`, `TimeUp` false, `NextDayStartsAt` in the future, `WarnAtSeconds` `[300,60]` |
| af | bob reads alice's status; admin reads bob after `DELETE` (not a kid) | 403; 404 |
| ag | alice starts playback, reports progress every 10 s for 60 s, then stops | used grows by 60 ± 5 s |
| ah | alice plays 10 s, pauses for 30 s (pause report, then paused progress), resumes for 10 s, stops | used grows by 20 ± 5 s |
| ai | alice plays on two sessions (devices `kids-check-A` and `kids-check-B`) for 30 s at once | used grows by 60 ± 6 s |
| aj | alice starts, reports once, then goes quiet for 120 s (no stop) | used grows by at most 75 s (60 s cut-off plus the first gap), then stops growing |
| ak | admin starts a session, adds alice as an additional user (`POST /Sessions/{id}/User/{alice}`), and plays 30 s | alice's used does not grow |
| al | admin `PUT` alice's profile with `DailyAllowanceMinutes` 1441, then 1 | 400 keyed `DailyAllowanceMinutes`; then 200, and the status has `AllowanceSeconds` 60, and `TimeUp` once used ≥ 60 |
| am | alice adds a bonus without a grant; admin adds 0 and 241; admin adds 5; alice with a grant adds 5 | 403; 400 keyed `Minutes` twice; 200 with `BonusSeconds` 300; 200 with `BonusSeconds` 600 |
| an | alice reports a new 120 s segment from an hour ago; the same again; one with `Seconds` 0 plus a valid one | +120 s; +0 s; 400 and +0 s |
| ao | admin reports a segment for alice; bob reports for alice | 403; 403 |
| ap | admin `DELETE` alice's profile (no purge), marks her again, reads her status | today's `UsedSeconds` unchanged; then `DELETE ?purge=true`, mark again → 0 |
| aq | `GET /web/ConfigurationPage?name=Kids` and `KidsUser` with no token | no usage numbers or status JSON |

The script restores alice's profile (no allowance) at the end.

## 3. Real client: 10 minutes in jellyfin-web (SC-001, SC-002)

A Playwright script, kept outside the repo, signs in to jellyfin-web as alice and plays the test clip. Note
`UsedSeconds` before and after:

- **Run 1**: play for 10 minutes without pausing. The increase is 600 ± 30 s.
- **Run 2**: play 5 min, pause 3 min, play 5 min. The increase is 600 ± 30 s, so the pause did not count.

## 4. Day boundary (SC-006)

1. In the Kids page, set "day starts at" to the next hour (or stop the server and edit
   `.dev/data/plugins/configurations/Jellyfin.Plugin.Kids.xml`).
2. Wait for the hour.
3. alice's status shows the new `Date` and `UsedSeconds` 0, with no bonus.
4. Set the hour back to 00:00.

## 5. Restart during playback (SC-009)

1. Start alice playing through the playstate API with progress every 10 s.
2. After 90 s, restart `jellyfin`.
3. The status shows at least 60 s counted from before the restart.

## 6. Dashboard (manual or Playwright)

1. On the Kids page, alice's row shows today's usage. Set "Day starts at", reload, and the value is kept.
2. Add 10 bonus minutes. The row's remaining time grows by 10 minutes.
3. In alice's editor, set the daily allowance to 45, save, and reload. It shows 45. Clear it and save: "No limit".
   Time the whole step: under 1 minute (SC-008).
4. Enter 2000. The editor blocks saving with the allowed range.

## 7. Retention task

In Dashboard → Scheduled Tasks, "Prune kids screen time" is listed under Kids. Run it with a planted row older than
90 days, and the row is gone.

## Last run (2026-10-18, Jellyfin 12.2 dev loop started from the worktree)

| Step | Result |
|---|---|
| §1 Unit tests | `dotnet build` with 0 warnings; `dotnet test`: 291 passed, 0 skipped (176 from features 1 and 2, 115 new, theories included). `DayClockTests` ran with the real `Europe/Madrid` zone |
| §2 `scripts/dev-check.sh` | 106 PASS lines, 0 FAIL, on two runs in a row against the final build (and two earlier runs before the review fixes). New checks: ag +60 s for 60 s of playing; ah +20 s for 10 + 10 s around a 30 s pause; ai +60 s for two devices × 30 s; aj +60 s after the last real report, then +0 for 30 s more; ak +0 for alice as an additional user of the admin's session; al, am, an (+120, then +0 for the same id, then 400 and +0), ao, ap (kept 321 s after unmark, 0 after purge), aq. The first run found one failure, check j: `kids.js` held the plugin GUID; it now looks the id up by name |
| §3 Real client | jellyfin-web 12.2 in headless Chromium 155 (Playwright script outside the repo), alice playing the generated clip (HLS transcode). The clock starts when the server lists the playback. **Run 1** (10 min): 601 s counted for 600 s of reported playback. **Run 2** (5 min, pause 3 min, 5 min; 785 s of wall time): 602 s counted, so the pause added 2 s. Both within ±30 s (SC-001) and ≤ 5 s per pause (SC-002). In both runs the video element advanced 565 s: about 35 s of HLS buffering, which jellyfin-web reports as playing, so the server counts it. Headless HLS start-up took about 2 minutes before the client reported playback; an earlier harness that started its clock at the video element (not at the server) reported 504 / 600 s, a harness error |
| §4 Day boundary | At 18:52, "day starts at" set to 19 through the plugin configuration API: alice's date went to `2026-10-17` (Story 6.3, no recalculation), then 61 s of playback and a 5-minute bonus. At 19:00:33 her status was `2026-10-18`, 0 used, 0 bonus, `NextDayStartsAt` the next day at 19:00 local (SC-006). Hour set back to 00:00 |
| §5 Restart | 90 s of playstate playback, 92 s counted; `jellyfin` restarted through process-compose; afterwards 102 s counted (the shutdown flush kept everything, including the ticks after the read). Nothing lost (SC-009) |
| §6 Dashboard | 14/14 in Chromium, no page errors: alice's row "Today: 0 min (no limit)"; "day starts at" 04:00 kept after a reload, then back to 00:00; allowance 45 saved and kept after a reload, the row then reads "Today: 0 of 45 min (… min left)"; a 0-minute bonus refused inline; a 10-minute bonus grows "left" by 10; 2000 blocked inline with nothing sent; cleared → "No limit". Setting the allowance and seeing usage against it took 15–22 s, scripted (SC-008). The run found one bug: the bonus field's native `min` validation blocked the submit silently, so the inline message never showed (`noValidate` now) |
| §7 Retention | The task is listed as "Prune kids screen time" under Kids, daily at 03:30. With planted rows 91 and 90 days old, a run removed the 91-day usage and bonus rows ("Pruned 2 … rows") and kept the 90-day one |
| Artifacts | Plugin 0.0.3.0 (`build.yaml` and `Directory.Build.props`) installs as `Kids_0.0.3.0`; `Info` reports `0.0.3.0` with `["profiles","parentLock","allowance"]` |
| 12.0 and 12.1 | Not checked (feature 1's accepted risk) |
