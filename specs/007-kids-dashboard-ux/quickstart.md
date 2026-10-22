# Quickstart: validating the dashboard UX and Reset today

## 1. Build and unit tests

```sh
dotnet build Jellyfin.Plugin.Kids.slnx -c Release     # 0 warnings (TreatWarningsAsErrors)
dotnet test Jellyfin.Plugin.Kids.slnx
```

## 2. Dev loop and dev-check

From the devshell, on a free port:

```sh
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> dev            # builds, installs, starts, provisions
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> scripts/dev-check.sh
```

New checks, section `d` ([contract](contracts/kids-api-v1-reset.md)):

| Check | Expected |
|---|---|
| `da` Info | `watchTimeReset` in `Features` |
| `db` alice resets her own day; bob resets alice's | 403, 403; her status unchanged |
| `db` a grant from alice's device used on another device | 403 |
| `dc` reset a user who is not a kid | 404 |
| `dd` alice with an allowance of 1 min, time used and a bonus, enforcement on: admin resets | 200, `UsedSeconds == 0`, `BonusSeconds == 0`, no `DeniedReason`; PlaybackInfo has media |
| `dd` the server log | a line "Screen time of … reset for … by …" |
| `de` a grant holder on alice's TV resets | 200 |
| `df` a segment reported before the reset, re-sent | 200, `UsedSeconds` unchanged |
| `dg` allowance 0, reset | 200 with `DeniedReason == "timeUp"` still (no screen time today) |

## 3. Browser (jellyfin-web 12.2, headless Chromium)

A throwaway Playwright script (outside the repo), signed in as the dev admin, for each theme (`dark`, `light`) and
viewport (1920×1080, 400×900):

1. Kids page with enforcement off: the banner is in the first screen; every kid row has "App only"; a kid with the
   default policy shows the health lines with Fix links to `/access`, `/parentalcontrol` and `/profile`; the live TV
   line reads "Watches N of M channels" or "No live TV"; the server time line is shown by "Day starts at".
2. "Turn on" → confirm → toast; banner and pills gone. (Turned off again at the end.)
3. Screen-time form: change the break → "Unsaved changes"; Save → toast, note gone.
4. Kid editor: health lines (admin line for an admin kid); "No daily limit" ticked disables the field; untick, 0 →
   "No screen time today" help; Save → toast and status redrawn; "Unsaved changes" appears and clears; the break line
   next to the sitting limit; allowed hours list; the live TV state line.
5. Reset today → the confirmation names the kid → confirm → toast, status shows 0 used.
6. No page errors in the console; a user name with `<b>&${x}` renders as text.

Screenshots: `.dev/screenshots/007/{kids,editor}-{dark,light}-{1920,400}.png`.

## 4. Results (2026-10-22, dev server on port 8296, jellyfin-web 12.2)

| Step | Result |
|---|---|
| §1 Build and tests | `dotnet build -c Release`: 0 warnings, 0 errors. `dotnet test`: 485 passed (12 new for Reset: store, service including a store failure, sitting, state; the Info test updated) |
| §2 dev-check | 236/236 PASS ("All checks passed"), including `da`–`dg` for Reset. Earlier runs found a check bug (Serilog quotes string values) and led to naming a parent grant in the log line (`by "alice with a parent grant"`) |
| §3 Browser | 62/62 checks pass in headless Chromium (playwright-core 1.63, `playwright-chromium` from nixpkgs), no page errors. Covered: banner in the first screen at 1920 and 400 px in both themes, "App only" on every kid, bob's five health lines with Fix links to `/access`, `/parentalcontrol`, `/profile` and the editor, the admin line first for an admin kid (list and editor), "No screen time today" vs "No daily limit", "Watches 1 of 2 channels" / "No live TV", the server time with its offset, Turn on (confirm, toast, banner and pills gone, only `EnforcementEnabled` changed), unsaved notes and save toasts on both forms, an empty allowance blocked inline with nothing sent, the break line, allowed hours with their link, Reset today (cancel sends nothing; confirm names the kid, toast, status at 0), no horizontal overflow |
| Escaping | Jellyfin 12.2 refuses user names with `<` or `&` (400), so the markup-name check was skipped; names are still inserted as text, and escaped in dialog text (Jellyfin's dialogs use `innerHTML`, found during this run) |

Screenshots (`.dev/screenshots/007/`): `kids-{dark,light}-{1920,400}.png`, `editor-{dark,light}-{1920,400}.png`,
`kids-banner-dark-1920.png`, `editor-unsaved-dark-1920.png`, `editor-reset-confirm-dark-1920.png` (the dialog is caught
mid fade-in).

Review pass (T030): C# reviewed against the dotnet skills' guidance (no narrower installed specialist owns a review of
this change). No correctness, async or analyzer issue in the reset path: writes are serialised by
`KidsDatabase.WriteAsync` and the flush lock, and the store is written before the pending time is dropped. Added the
missing store-failure test. Page review: every name is inserted with `textContent`; dialog texts (which Jellyfin
renders as HTML) escape names; every configuration write re-reads the configuration; the anonymous page checks `j`
and `aq` pass.
