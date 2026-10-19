# Repro: AccessSchedules do not stop playback that is already running (Jellyfin 12.2)

Run on 2026-10-19, 15:07–15:34 CEST. This records evidence only. It is not issue text.

**Verdict: confirmed.** On stock 12.2, a non-admin user's running playback is not stopped when their access schedule
ends. Direct play keeps playing at full rate. HLS stalls at the next segment, but its ffmpeg job and the server-side
session stay alive until the idle-session sweep, about 10 minutes later. The prototype fork
(`ParentalScheduleHost`) stops both web clients and kills both transcodes less than 1 s after the boundary. A client
without a WebSocket that direct-plays through `Videos/{id}/stream?ApiKey=` is still served, as
`native-core-design.md` §2.2 predicts.

Tags used below: **[O]** observed in this run, **[I]** inferred from the observations and the source.

## 1. Environment

| Item | Value |
|---|---|
| Host | WSL2, Linux 6.18.33.2-microsoft-standard-WSL2, 32 cores, TZ `Europe/Madrid` (CEST, +02:00) |
| nixpkgs | `55bfb4d7437bd4e09ab7cba58a43bd68eb310044` (from the plugin repo's `flake.lock`) |
| Stock server | `jellyfin-12.2` from the plugin devshell (`/nix/store/wpc6ga77…-jellyfin-12.2`), reports `12.2.0`; runtime `dotnet-aspnetcore-runtime 10.0.12` |
| Fork server | `/home/aldo/Dev/aldoborrero/jellyfin-fork`, branch `kids-native`, HEAD `ca0f16e` ("Bump version to 12.2") plus the uncommitted prototype (26 files, +1679/−3; sha256 of `git diff HEAD` is `9858c0547ef25a1f…`). Ran the existing Debug build `Jellyfin.Server/bin/Debug/net10.0/jellyfin.dll` (built 14:20:16, after the last non-test source edit at 14:20:05), with the stock wrapper's environment, `--ffmpeg` and `--webdir` |
| Web client | jellyfin-web 12.2 (`/nix/store/s67vpvip…-jellyfin-web-12.2`), served by each server |
| ffmpeg | jellyfin-ffmpeg 8.1.2-5 |
| Browser | nixpkgs `chromium` 155.0.8059.79, headless, driven by Playwright 1.63.0 (Python 3.14). Plays H.264/AAC MP4; no HEVC (`canPlayType` empty), so HEVC goes to HLS |
| API client | curl, bash |
| Server config | Stock defaults after the wizard. Bound to 127.0.0.1, so all clients are "local". Transcode throttling and segment deletion off (defaults) |
| Users | `admin`/`admin` (administrator); `alice`, non-admin, no password, default policy |
| Schedule | `AccessSchedules = [{DayOfWeek: "Monday", StartHour: 0, EndHour: <window end as a fractional hour>}]`, set with `POST /Users/{id}/Policy`. Stock run: `EndHour 15.205556` (15:12:20). Fork run: `EndHour 15.476111` (15:28:34) |

Test media (generated with jellyfin-ffmpeg, in one `homevideos` library called Kids):

| File | Format | Path in the web client |
|---|---|---|
| `Direct Play Clip (H264).mp4` | 1280x720 30 fps H.264 High, about 4 Mb/s (testsrc + noise), AAC 128k, `+faststart`, 12 min, 363 MB | Direct play: `Videos/{id}/stream.mp4?Static=true&…&ApiKey=…` |
| `Transcode Clip (HEVC).mkv` | 1920x1080 30 fps HEVC (x265 ultrafast), AAC, 120 min (a 10-min encode concatenated 12 times with `-c copy`) | HLS transcode to H.264 in fMP4 segments (`hls1/main/N.mp4`) |

The HEVC clip is 2 hours long on purpose. Jellyfin's ffmpeg ran at about 5.4x here, so a 12-minute clip would have
finished transcoding before the boundary. That would have hidden whether the job is kept alive.

## 2. Step-by-step repro (manual)

1. Start a fresh Jellyfin 12.2 and finish the wizard (admin user). Create a non-admin user `alice`.
2. Create the two clips and add them to a library:

   ```sh
   ffmpeg -f lavfi -i "testsrc=size=1280x720:rate=30,noise=alls=12:allf=t" -f lavfi -i "sine=frequency=440:sample_rate=48000" \
     -t 720 -c:v libx264 -preset veryfast -b:v 4M -maxrate 4M -bufsize 8M -pix_fmt yuv420p -c:a aac -b:a 128k \
     -movflags +faststart "Direct Play Clip (H264).mp4"
   ffmpeg -f lavfi -i "testsrc=size=1920x1080:rate=30" -f lavfi -i "sine=frequency=660:sample_rate=48000" \
     -t 600 -c:v libx265 -preset ultrafast -b:v 4M -pix_fmt yuv420p -c:a aac part.mkv
   for i in $(seq 12); do echo "file 'part.mkv'"; done > list.txt
   ffmpeg -f concat -safe 0 -i list.txt -c copy "Transcode Clip (HEVC).mkv"
   ```

   Any long HEVC file a browser can't decode works for the second clip. It only needs to keep ffmpeg busy past the
   boundary.
3. Check the server's local time zone (the schedule is evaluated in it: `UserEntityExtensions.IsParentalScheduleAllowed`
   uses `DateTime.UtcNow.ToLocalTime()`).
4. As admin, give alice a window that ends about 3 minutes from now, for today's weekday. The dashboard's schedule
   editor uses coarse hour steps, but the API takes any fraction:

   ```sh
   end=$(( $(date +%s) + 180 ))
   h=$(date -d @$end +'%-H %-M %-S' | awk '{printf "%.6f", $1 + $2/60 + $3/3600}')
   policy=$(curl -s -H "$ADMIN_AUTH" "$URL/Users/$ALICE" | jq --arg d "$(date +%A)" --argjson h "$h" \
     '.Policy | .AccessSchedules = [{DayOfWeek:$d, StartHour:0, EndHour:$h}]')
   curl -s -X POST -H "$ADMIN_AUTH" -H 'Content-Type: application/json' "$URL/Users/$ALICE/Policy" -d "$policy"
   ```

5. Sign in to jellyfin-web as alice (before the boundary; login is refused after it). Play the MP4 in one browser and
   the HEVC clip in another.
6. Optionally, in a shell, act as a second alice client (`$TOKEN` from `POST /Users/AuthenticateByName`):

   ```sh
   # Direct play: the anonymous stream route, authenticated only by ApiKey
   curl -s -o /dev/null -w '%{http_code}\n' -H 'Range: bytes=50000000-51048575' \
     "$URL/Videos/$MP4/stream?static=true&mediaSourceId=$MP4&deviceId=repro-curl&ApiKey=$TOKEN"
   # Any [Authorize] route, for comparison
   curl -s -o /dev/null -w '%{http_code}\n' -H "Authorization: MediaBrowser Client=\"repro\", Device=\"d\", DeviceId=\"repro-curl\", Version=\"1\", Token=\"$TOKEN\"" "$URL/Users/Me"
   ```

7. After the boundary, watch for about 10 minutes:
   - the two players;
   - `pgrep -af -- -hls_segment_filename` (the HLS ffmpeg);
   - `GET /Sessions` as admin (alice's `NowPlayingItem`, `PlayState.PositionTicks`, `TranscodingInfo`);
   - the server log.
   Repeat the curl commands from step 6.

Expected on stock 12.2: the MP4 keeps playing. The HEVC player freezes within seconds. ffmpeg and alice's sessions stay
alive for 5 to 10 minutes. The ApiKey stream request still answers 206, and `/Users/Me` answers 403.

### What the automated harness did

Scripts are in the scratchpad (`…/scratchpad/tools/`), not in the repo. `run.sh` sets the window to end 170 s after
it starts. It then runs four alice clients at once, each with its own DeviceId, and keeps them going 480 s (stock) or
300 s (fork) past the boundary:

| Client | What it does |
|---|---|
| `browser-direct` | jellyfin-web, plays the MP4. Every 2 s it samples `video.currentTime`, `paused`, `readyState`, the buffered end and `location.hash`. It logs every HTTP response, WebSocket frame and console error, and takes screenshots at +20 s from start, −15 s, +30 s, +120 s and the end |
| `browser-hls` | the same, playing the HEVC clip |
| `curl-direct` | `PlaybackInfo` and `Sessions/Playing`. Then one long-lived `stream?static=true&ApiKey=` download at 520 kB/s (about the file's bitrate), and every 10 s a 1 MiB range request at the current play offset, a progress report, and every 30 s `PlaybackInfo` and `Users/Me` |
| `curl-hls` | `PlaybackInfo` with an HLS-only device profile, then `master.m3u8` and `main.m3u8`. It fetches segments in real time, 30 s ahead (like hls.js), and retries a refused segment every 10 s. Progress every 10 s. At the end it sends `Sessions/Playing/Stopped` and `DELETE Videos/ActiveEncodings` |
| `monitor.sh` | every 2 s, the HLS ffmpeg PIDs with their elapsed time; every 10 s, `GET /Sessions` as admin |

## 3. Results

### 3.1 Stock 12.2 versus fork, per scenario

Times are relative to the window end (+0 s).

| Scenario | Stock 12.2 [O] | Fork `kids-native` [O] |
|---|---|---|
| **A1. Browser, direct play (MP4)** | Keeps playing with no stall: `currentTime` 165.0 at +0.3 s, then 643.1 at +478 s. 238 samples, none paused, `readyState` 4 throughout. No new stream request was needed; the open response kept filling the buffer. No logout, no error dialog; the page stayed on `#/video` | Playstate `Stop` arrives over the WebSocket at +0.6 s. The player stops at `currentTime` 165.2, the stream request is aborted (`FAILED`), and the page goes back to `#/details`. No logout, no error dialog |
| **A2. Browser, HLS (HEVC→H.264)** | The next segment (`hls1/main/57.mp4`) gets **403** at +2.7 s (hls.js: `fragLoadError`). Playback stops at about 164.5. By +4.2 s the video element is reset (`currentTime 0`, `readyState 0`). The last frame stays on screen, the page stays on `#/video`, and there is no error message and no logout. hls.js made no further segment requests | `Stop` at +1.0 s. The player stops at 162.5 and returns to `#/details`. No 403 segment was even requested |
| **A. Requests the web client makes after the boundary** | `Sessions/Playing/Progress` 403 every 10 s (96 times), `SyncPlay/List` 403. The WebSocket stays open | `Sessions/Playing/Stopped` 403, `UserViews` 403, `SyncPlay/List` 403. The WebSocket stays open |
| **B1. curl, direct play (ApiKey stream)** | **All 47 range requests after the boundary: 206.** The long-lived download kept its full rate: 92.1 MB at +3.9 s, 346.1 MB at +480.6 s | Same: **all 30 range requests 206**, and the long-lived download went from 92.1 MB at +4.1 s to 252.9 MB at +306 s. The fork's `Stop` cannot reach this client, which has no WebSocket |
| **B2. curl, HLS** | First segment after the boundary (`segment-67`) **403** at +1.7 s; all 48 segment requests after the boundary 403 | Same: all 30 segment requests 403 |
| **B. Other API calls as alice** | `Sessions/Playing/Progress`, `PlaybackInfo`, `Users/Me`, `Sessions/Playing/Stopped` and `DELETE Videos/ActiveEncodings`: all **403** | Same, all 403 |
| **HLS ffmpeg processes** | Both stay alive after the boundary with no segment being fetched. They were still running at +507 s (`ps` state `SNl`, elapsed about 690 s). They were killed at **+599 s** (browser job, after the browser closed at +480 s) and **+643 s** (curl job, 59 s after the idle sweep at +584 s) | Both killed by `ParentalScheduleHost` at +0.8 and +1.0 s. ffmpeg exited by +1.3 s. Count went 2 → 0 between the −7 s and +2 s samples, and no ffmpeg was started again |
| **Server-side session (admin's `/Sessions`)** | All four alice sessions show `NowPlayingItem`, with `PositionTicks` still advancing every second (synthetic tick) and `TranscodingInfo` set. `LastPlaybackCheckIn` stays frozen at the boundary. The curl sessions were ended by the idle sweep at 15:22:04 (+584 s); the log says `User "alice" stopped playback … at "167000"ms`. The browser sessions went only when the harness closed the browsers | The sessions are **not** ended server-side. All four still show `NowPlayingItem` with an advancing position (curl ones still at +319 s). `ParentalScheduleHost` re-sends `Stop` about every 10.5 s per session: 28 Stop frames per browser over 300 s, and 118 "Stopping playback" log lines in total |
| **Is the user logged out?** | No. The token stays valid; every `[Authorize]` request gets 403, not 401 | No; same |

### 3.2 Summary

| Question | Stock | Fork |
|---|---|---|
| Does running playback continue after the window? | Direct play: yes, for the whole observation (8 min) and at full rate; it would run to the end of the item [I]. HLS: no, it stalls at the next segment (2–4 s) | Web clients: no, stopped in under 1.1 s. A socket-less direct-play client: yes, unchanged |
| Server answer | 403 on every `[Authorize]` route, segments included. 206 on `Videos/{id}/stream` with `ApiKey` | Same |
| Is the transcode cleaned up? | No. The client's `Stopped` and `DELETE ActiveEncodings` get 403. ffmpeg runs until the idle sweep plus the 60 s HLS kill timer: 10–11 min here | Yes, under 1.3 s |

## 4. Log excerpts

Paths shortened to `$SCRATCH`. Full logs: `…/scratchpad/runs/{stock,fork}/server-log/`.

### Stock 12.2 (window end 15:12:20)

```text
[15:09:35.195] [INF] SessionManager: User "alice" started playback of '"Direct Play Clip (H264)"' ("Jellyfin Web" "12.2.0")
[15:09:37.510] [INF] SessionManager: User "alice" started playback of '"Transcode Clip (HEVC)"' ("Jellyfin Web" "12.2.0")
[15:12:21.645] [INF] Jellyfin.Api.Auth.CustomAuthenticationHandler: AuthenticationScheme: "CustomAuthentication" was forbidden.
   … 355 such lines until 15:20:27, nothing else about alice's sessions …
[15:20:20.240] [INF] WebSocketManager: WS "127.0.0.1" closed            <- harness closes the browsers (+480 s)
[15:22:04.244] [INF] SessionManager: User "alice" stopped playback of '"Transcode Clip (HEVC)"' at "167000"ms ("repro-curl" "0.0.1")
[15:22:04.271] [INF] SessionManager: User "alice" stopped playback of '"Direct Play Clip (H264)"' at "162000"ms ("repro-curl" "0.0.1")
[15:22:19.729] [INF] TranscodeManager: Transcoding kill timer stopped for JobId "ec9fa545…" PlaySessionId "86e664cc…". Killing transcoding
[15:22:19.730] [INF] TranscodingJob: Stopping ffmpeg process with q command for "$SCRATCH/state-stock/cache/transcodes/b2f4b623….m3u8"
[15:22:20.458] [INF] TranscodeManager: FFmpeg exited with code 0
[15:23:03.515] [INF] TranscodeManager: Transcoding kill timer stopped for JobId "43aa6080…" PlaySessionId "d7dc46f7…". Killing transcoding
[15:23:03.515] [INF] TranscodingJob: Stopping ffmpeg process with q command for "$SCRATCH/state-stock/cache/transcodes/ad2236eb….m3u8"
[15:23:03.735] [INF] TranscodeManager: FFmpeg exited with code 0
```

`d7dc46f7…` is the curl-hls PlaySessionId (from its `PlaybackInfo`), so `86e664cc…` is the browser's. The ffmpeg
transcode log at +260 s still shows `speed=5.45x elapsed=0:04:25`, writing segment 481.

### Fork (window end 15:28:34)

```text
[15:28:34.611] [INF] ParentalScheduleHost: Stopping playback of session "86516064…": user "alice" is outside their access schedule
[15:28:34.671] [INF] CustomAuthenticationHandler: AuthenticationScheme: "CustomAuthentication" was forbidden.
[15:28:34.757] [INF] ParentalScheduleHost: Stopping playback of session "d1553ec7…": user "alice" is outside their access schedule
[15:28:34.761] [INF] TranscodingJob: Stopping ffmpeg process with q command for "$SCRATCH/state-fork/cache/transcodes/3b7c8a33….m3u8"
[15:28:34.967] [INF] ParentalScheduleHost: Stopping playback of session "bdc56ea6…": user "alice" is outside their access schedule
[15:28:34.968] [INF] TranscodingJob: Stopping ffmpeg process with q command for "$SCRATCH/state-fork/cache/transcodes/ffdc0baf….m3u8"
[15:28:34.983] [INF] ParentalScheduleHost: Stopping playback of session "7dd8b947…": user "alice" is outside their access schedule
[15:28:35.142] [INF] TranscodeManager: FFmpeg exited with code 0
[15:28:35.315] [INF] TranscodeManager: FFmpeg exited with code 0
[15:28:45.604] [INF] ParentalScheduleHost: Stopping playback of session "86516064…": user "alice" is outside their access schedule
   … repeated for all four sessions about every 10.5 s until the run ended (118 lines) …
```

WebSocket frame received by both web clients (fork only; stock sent nothing):

```json
{"MessageId":"5fb9a2e2…","Data":{"Command":"Stop"},"MessageType":"Playstate"}
```

## 5. Observed versus inferred

Observed [O]:
- Stock 12.2 refuses every `[Authorize]` request from alice once the window ends, HLS segments included (403, not 401).
  It does nothing to her running sessions: no Stop, no kill, no session end.
- Direct play through the anonymous `Videos/{id}/stream` route keeps working after the window. The open response
  keeps streaming, and new range requests with alice's `ApiKey` are answered 206.
- The web client's own `Sessions/Playing/Stopped`, and curl's `DELETE Videos/ActiveEncodings`, get 403. The HLS
  ffmpeg jobs keep running with no segment consumer until the idle sweep removes the session. The sweep came at +584 s
  here, and the kill followed after the 60 s HLS ping timeout.
- In `/Sessions`, alice's position keeps advancing after the boundary while `LastPlaybackCheckIn` stays frozen.
- jellyfin-web 12.2 shows nothing when the schedule cuts in: no message, no logout. HLS freezes on the last frame.
- The fork stops both web clients (Stop over the WebSocket) and kills both HLS jobs within 1.3 s of the boundary.
- The fork doesn't stop the curl direct-play client, and doesn't refuse its stream requests. It doesn't end any
  session server-side. It re-sends Stop and logs an INF line every ~10.5 s per session for as long as the session
  stays "playing".

Inferred [I]:
- What keeps the stock ffmpeg alive is the server's 1 s synthetic progress tick
  (`SessionInfo.OnProgressTimerCallback` → `SessionManager.OnPlaybackProgress(…, isAutomated: true)` →
  `TranscodeManager.OnPlaybackProgress` → `PingTranscodingJob`, `TranscodeManager.cs:95-96, 710-716`). It runs while
  the session has a `NowPlayingItem`. This run didn't log the pings (they are Debug level). But the jobs survived far
  past the 60 s HLS ping timeout (`TranscodeManager.cs:157`) with no segment requests and no accepted client reports.
  And they died exactly one ping timeout after their session ended. Both facts fit that explanation.
- The idle sweep (`SessionManager.CheckForIdlePlayback`) fires every 5 minutes and acts on sessions whose last
  check-in is more than 5 minutes old. So the stock lingering time is 5 to 10 minutes plus 60 s, depending on the
  timer's phase. Here the timer had been armed by the earlier smoke test.
- A direct-play web client left open would play to the end of the item (720 s here; a feature film in practice).
  Observed: it went from 165 s to 643 s in the 478 s observed, with no stall.
- The fork's repeated Stop is harmless for jellyfin-web (it is already stopped), but it is log noise. The lingering
  server-side session keeps the synthetic tick, and so the repeated Stop, running until the idle sweep.

## 6. Caveats

- All clients connect from 127.0.0.1, which counts as the local network. Remote clients take the same schedule check
  (`DefaultAuthorizationHandler.cs:80-85`), but this was not tested.
- The window was set through the API, with a fractional `EndHour`, a few minutes ahead. The check is
  `hour <= EndHour` in server local time; the first refusal came 1.6 s (stock) and 0.6 s (fork) after the computed
  end.
- The four clients ran at once on one server. Each had its own DeviceId, and the results are attributed by DeviceId
  and PlaySessionId. In the fork, `KillTranscodingJobs` matches by DeviceId, so the clients didn't interfere.
- Playwright logs a second `FAILED` entry for each keepalive POST from jellyfin-web, for example
  `Sessions/Playing/Progress`. The server log and the status codes show these requests were answered. This is a
  harness artefact.
- Unrelated errors in the fork log: `AutoDiscoveryHost: Unable to bind to 0.0.0.0:7359`, because the stock server
  held that UDP port at the time. And a `DisplayPreferences` UNIQUE-constraint error when two web clients opened the
  same page at once.
- Only jellyfin-web and curl were tested. Other clients (Android TV, Wholphin, Swiftfin, Kodi) were not.
- Servers, state directories and the generated media were removed after the run. The harness, per-client JSONL and TSV
  data, screenshots and copies of the server logs are in
  `/tmp/claude-1000/-home-aldo-Dev-aldoborrero-Wholphin/d7deabb5-53f4-4f7b-97cf-2ec8030c63d9/scratchpad/runs/`.
