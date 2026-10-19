# Quickstart: validating kids enforcement

The behaviour is in [spec.md](spec.md), the API in [contracts/kids-api-v1-enforcement.md](contracts/kids-api-v1-enforcement.md),
the rules in [data-model.md](data-model.md) and the decisions in [research.md](research.md).

## Prerequisites

- The devshell. Run the dev loop on a free port, and stop it afterwards:

  ```sh
  export JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=8196
  dev -p 8197 -t=false        # process-compose API on 8197, no TUI
  ```

- Provisioning creates `admin`, the kids `alice` and `bob`, and the "Kids" library with the 12-minute
  `Test Pattern (2026).mkv` (H.264/AAC in MKV: the browser direct-streams or remuxes it). The browser runs use a
  long MP4 test clip generated outside the repo (see §4).
- For §4: Chromium and Playwright from nixpkgs (`nix shell nixpkgs#chromium nixpkgs#playwright-driver …`), scripts kept
  outside the repo (job scratch directory).

## 1. Unit tests

```sh
dotnet build Jellyfin.Plugin.Kids.slnx    # 0 warnings
dotnet test Jellyfin.Plugin.Kids.slnx
```

New tests cover: the budget (load, add, new day, unlimited, bonus reload), the sitting (restart after a break,
concurrency, limit and break, lazy reset), `ScheduleClock` (days, fractional hours, inclusive ends, `Everyday` /
`Weekday` / `Weekend`), the decision order and `Until`, warnings once per threshold and instance, the enforcer
(order message → Stop → kill → close → abort → end; once per denial; real reports escalate at most every 10 s;
automated ticks never escalate), the convention (matches by name and by attribute, exclusions, coverage), and the
filter (disabled, admin, API key, non-kid, kid allowed, denied PlaybackInfo 200 `NotAllowed`, denied stream 403 with
the headers, tokenless request attributed by `deviceId`, delivery tracked and released, exceptions fail open).

## 2. Automated checks against the dev server

```sh
JELLYFIN_URL=http://127.0.0.1:8196 scripts/dev-check.sh
```

The enforcement section:

1. `/Kids/Info` lists `enforcement` and `sitting`, and `Enforcement.Gate` is `ok` with no `Missing`.
2. With `EnforcementEnabled=false`: `alice` with an allowance of 0 still gets PlaybackInfo with media sources, and her
   Status says `Enforced: false` with no `DeniedReason`.
3. Turn it on (`POST /Plugins/{id}/Configuration` as admin). `alice` (allowance 0): PlaybackInfo GET and POST answer 200
   with `ErrorCode: NotAllowed`; `master.m3u8`, `main.m3u8`, a `hls1` segment, `Videos/{id}/stream?static=true&ApiKey=`,
   `Items/{id}/File`, `Items/{id}/Download` and `Audio/{id}/universal` answer 403 with
   `X-Application-Error-Code: PlaybackNotAllowed` and `X-Kids-Reason: timeUp`; `Users/Me`, `Items`, `Sessions/Playing`
   and `/Kids/...` still answer as before. Status says `DeniedReason: timeUp`.
4. The admin and an API key get media for the same requests.
5. A tokenless `Videos/{id}/stream?static=true&deviceId=<alice's device>` while alice has a playing session is 403.
6. A 5-minute bonus for alice: the next PlaybackInfo has media sources (within 2 s).
7. Live cutoff with curl: alice with 1 minute left starts a `Sessions/Playing` session and holds a direct-play download
   open; within about 70 s the download is cut (connection reset), later range requests answer 403, and admin's
   `/Sessions` shows alice's session with no `NowPlayingItem`.
8. Allowed hours: alice gets a schedule ending in 90 s; her running playback is stopped at the boundary and her
   tokenless/ApiKey stream requests answer 403 with `X-Kids-Reason: outsideSchedule`. The schedule is restored.
9. Sitting: alice with a 5-minute sitting limit and a 1-minute break (`SittingBreakMinutes=1`) is denied with
   `sessionLimit` after 5 minutes of reported playback, and allowed again a minute later. (Long: behind
   `DEV_CHECK_SLOW=1`.)
10. The server log has one information line per denial and per stopped session, not one per tick.
11. Enforcement off again: every refusal is gone at once.

## 3. The spike (research R1)

Before the stop chain relies on the abort, run the spike harness (outside the repo) and record the numbers in
research.md R1:

- curl client: one long direct-play download plus 1 MiB range requests every 10 s with alice's `ApiKey`; deny alice;
  record the time to the reset and the status of each later request.
- jellyfin-web in headless Chromium: alice direct-plays the MP4 clip, the page's `Playstate` handling is disabled
  (so Stop is ignored); deny alice; sample `video.currentTime` every second; record when it stops advancing, what the
  page shows, and whether it signs out.

## 4. Browser validation (jellyfin-web 12.2, headless Chromium)

With enforcement on and a normal page (Stop not disabled):

1. alice with 6 minutes left plays the MP4: the 5-minute and 1-minute toasts appear; at time's up the modal "Time's up"
   appears and the player stops within 10 s; the page is not signed out.
2. Pressing play again shows "Playback of this media is not allowed."
3. An HLS (transcoded) item: same, and no ffmpeg is left running (`pgrep -af hls_segment_filename`).
4. Bedtime: a schedule that ends in 2 minutes; warning, then "Bedtime" and stop at the boundary.

## 5. Clean up

Stop the dev loop (`process-compose down -p 8197`, or Ctrl-C), and leave `.dev/` in place or `dev-reset` it.
