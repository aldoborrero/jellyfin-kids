<divignores the stop keeps playing what it has already downloaded.

We want kids mode in more clients. The API is open to any Jellyfin client and documented under [API](#api); if you
work on a client and want to add it, open an issue and we will help.

| | Pekealign="center">

<img src="docs/assets/banner.svg" alt="Jellyfin Kids: kid profiles, a parent lock and shared screen time, enforced by your Jellyfin server" width="100%" />

### A kids mode for the whole house, set up once on your Jellyfin server.

**Mark who is a kid, pick one parent code and a daily allowance. Every TV and every app follows the same rules.**

[![CI](https://img.shields.io/github/actions/workflow/status/aldoborrero/jellyfin-kids/ci.yml?branch=main&style=flat-square&label=CI)](https://github.com/aldoborrero/jellyfin-kids/actions/workflows/ci.yml)
[![Jellyfin 12.x](https://img.shields.io/badge/Jellyfin-12.x-aa5cc3?style=flat-square)](https://jellyfin.org/)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![MIT license](https://img.shields.io/badge/license-MIT-f4c430?style=flat-square)](LICENSE)

[Install](#install) · [What it does](#what-it-does) · [Screenshots](#screenshots) · [How it works](#how-it-works) · [Clients](#clients) · [API](#api) · [Development](#development) · [Roadmap](#roadmap)

<code>https://raw.githubusercontent.com/aldoborrero/jellyfin-kids/gh-pages/manifest.json</code>

</div>

---

Jellyfin can already keep a kid's account safe: it limits libraries, maximum ratings, tags and allowed
hours for every app. It doesn't know which accounts belong to kids, though, and it has no parent code you
can type with a remote and no daily screen-time allowance that holds across the living room TV, the bedroom
TV and the tablet.

Jellyfin Kids adds those to the server. A parent sets it up once from the Jellyfin dashboard, and every
client that speaks its API picks it up. Screen time is counted from the server's own playback events, so
it counts no matter which app the kid uses. A kid's token can never change the rules, including their
own.

The plugin sends nothing off your server. It has no telemetry, needs no account and uses no outside service.

> [!NOTE]
> Early development. The plugin targets Jellyfin 12.x and is tested on 12.2.
> Peke for Jellyfin, a kids app for Android TV, is the reference client. The API is open to any Jellyfin client.

## Install

1. In Jellyfin, open **Dashboard → Plugins → Repositories** and add
   `https://raw.githubusercontent.com/aldoborrero/jellyfin-kids/gh-pages/manifest.json`.
2. Open **Catalog**, install **Kids**, and restart Jellyfin.
3. Open **Dashboard → Kids** to mark kids, set the parent code and choose daily allowances.

Updates appear in the catalog like any other plugin. See [docs/install.md](docs/install.md) for details
and for installing a release by hand.

> [!IMPORTANT]
> **Your kids' settings live in `data/kids/` inside Jellyfin's data folder** (for example
> `/var/lib/jellyfin/data/kids/kids.db`, or `/config/data/kids/` in Docker), shown under **Data** on the Kids page.
> Don't delete that folder, and back it up: Jellyfin's own backup doesn't include plugin data. Updating to 0.0.10.0
> moves the database there from `plugins/Jellyfin.Plugin.Kids/` on its own. See
> [Your data](docs/install.md#your-data).

## What it does

| | |
|---|---|
| **Kid profiles** | Mark any Jellyfin user as a kid, with their own avatar, home sections, picks, search and autoplay settings. Warns when a kid account is an administrator, since admins bypass every check. |
| **Household parent code** | One code for the whole house, digits or remote buttons. Checked by the server, rate limited per device and per kid, and stored only as a PBKDF2 verifier. A right code gives a short-lived grant for changes. |
| **Daily allowance** | Minutes per day per kid, shared across devices. Counted on the server from real playback (paused time doesn't count, and a kid watching in a parent's session isn't charged). Parents can add bonus time for today and undo a wrong one within 10 minutes, end a break after the sitting limit early, or reset today back to zero (all logged). |
| **Enforcement on every client** (opt-in) | When a kid runs out of time, reaches their sitting limit or their allowed hours end, the server warns them, shows a message, stops the player, ends transcodes and cuts streams, and refuses new playback until a bonus, a break or the next day. This also applies in jellyfin-web and every other client. Off until you turn it on; see [Enforcement](#enforcement). |
| **Live TV channels per kid** | Pick which live TV channels each kid may watch (for example the kids channels of an ErsatzTV tuner). On every client, a kid can only play, list and see the guide of their own channels, and never recordings or timers. Live TV counts as screen time. Always on, so kids have no live TV until you pick their channels; see [Live TV](#live-tv). |
| **Day start** | Choose the hour the day resets, with daylight saving handled. |
| **Offline playback** | Clients can report downloads watched offline. Reports can only add time, never remove it. |
| **Dashboard pages** | You manage all of this from Jellyfin's own dashboard. The list of kids shows what still leaves each one unprotected in Jellyfin: an administrator account, every library, no maximum rating, unrated movies and shows, live TV access, picked channels that Jellyfin's parental control hides, or downloads. You fix each of these on the kid's own page, which edits the libraries, maximum rating, unrated items, allowed hours, live TV access and downloads next to the kids settings. A banner says when limits only apply in the kids app. |
| **Cleanup** | A deleted user's kid profile and usage go with them, and usage older than 90 days is pruned daily. |

## Screenshots

The Kids dashboard pages in Jellyfin 12.2, on a development server with three sample kids. Select a picture to see it
full size.

<table>
  <tr>
    <td width="50%" valign="top"><a href="docs/assets/screenshots/kids-page-dark.png"><img src="docs/assets/screenshots/kids-page-dark.png" alt="Kids page: alice, a protected kid with 60 minutes a day and one live TV channel; bob and carol with the gaps Jellyfin still leaves open and their Fix links; the enforcement banner, the parent lock, the screen time settings and the data folder"></a></td>
    <td width="50%" valign="top"><a href="docs/assets/screenshots/kid-editor-dark.png"><img src="docs/assets/screenshots/kid-editor-dark.png" alt="Kid editor for bob: the gaps with Fix links above the Kids, Profile, Access and Parental control tabs; the Kids tab with screen time and Reset today, daily allowance, sitting limit and bonus, avatar, browsing, home sections, picks and live TV channels"></a></td>
  </tr>
  <tr>
    <td align="center"><b>Kids page</b>: who is a kid, today's screen time, what still leaves each kid unprotected, the parent lock and household settings.</td>
    <td align="center"><b>Kid editor</b>: screen time, avatar, home sections, picks and live TV channels, with tabs for the kid's Jellyfin profile, libraries and parental control.</td>
  </tr>
</table>

## Enforcement

Enforcement is off after installing or updating. Turn it on per household with **Enforce on every app** under
Screen time on the Kids dashboard page, which also sets the break between sittings and shows whether every playback
route could be guarded on your Jellyfin version. Or through the plugin configuration API:

```sh
curl -s -H "Authorization: MediaBrowser Token=\"$ADMIN_TOKEN\"" "$JELLYFIN/Plugins/d62ee188270c49d8b7b95e524fec2335/Configuration" \
  | jq '.EnforcementEnabled = true' \
  | curl -s -X POST -H "Authorization: MediaBrowser Token=\"$ADMIN_TOKEN\"" -H 'Content-Type: application/json' \
      -d @- "$JELLYFIN/Plugins/d62ee188270c49d8b7b95e524fec2335/Configuration"
```

`SittingBreakMinutes` (default 15) is how long a kid must have nothing playing for a new sitting to start.

With enforcement on, for kids only:

- 5 and 1 minutes before the allowance, the sitting or the allowed hours end, each playing app shows a notice.
- When one ends, the app shows a message, and about 5 seconds later the server stops it, ends its transcodes and
  live streams, cuts the streams it is sending and ends the playback on the server. If the app keeps playing, the
  server stops it again, at most every 10 seconds.
- New playback is refused: jellyfin-web shows "Playback of this media is not allowed", other routes answer 403 with
  `X-Application-Error-Code: PlaybackNotAllowed`. Browsing, search and the rest of Jellyfin keep working.
- A bonus, Reset today, the end of the break or the next day lifts it at once.

Limits: an app that ignores the stop keeps playing what it has already downloaded (about 35 seconds for a browser in
our tests). A stream address with no token and no device id can't be tied to a kid, and DLNA renderers and offline
downloads are out of reach. `GET /Kids/Info` reports whether every playback route could be guarded on your Jellyfin
version.

## Live TV

Jellyfin's own live TV permission is all or nothing, so the plugin adds a per-kid channel list: open the kid in
Dashboard → Kids and tick their channels under **Live TV channels**.

After updating to 0.0.6.0, kids have no live TV until you pick their channels. The channel rules are always on,
with or without enforcement, because they are a content rule, like ratings. For kids only:

- Playing a channel that is not in their list is refused on every client (jellyfin-web shows "Playback of this media
  is not allowed"; other routes answer 403 with `X-Kids-Reason: liveTvChannel`).
- Jellyfin's own channel list and guide show only their channels; recordings and timers are refused.
- The kid's Jellyfin user still needs **Allow Live TV access**: turn it on in the kid's Profile tab (the editor warns
  when it is off). Jellyfin's ratings and "block unrated" settings still apply to channels and programs: the
  dashboard names each picked channel they hide from a kid, and the editor tab that fixes it.
- Live TV counts toward the daily allowance and the sitting, and enforcement stops it like any other playback.

Kids clients read the kid's channels and guide from `GET /Kids/Users/{id}/LiveTv/Channels` and `/LiveTv/Programs`.
Limits: generic `/Items` lookups of a channel id and images are not filtered.

## How it works

Jellyfin Kids uses Jellyfin's own features where they exist and adds the rest:

| Jellyfin keeps doing | Jellyfin Kids adds |
|---|---|
| Library access, parental ratings, unrated items, tags | Which users are kids, and their kids mode settings |
| Allowed hours (access schedules) | A daily allowance, bonus time and a sitting limit |
| Sign-in and authorization | A household parent code and short-lived parent grants |
| Playback sessions and events | Counting kids' screen time from those events, and stopping playback when it runs out |
| Playback and stream routes | A filter on those routes only, which refuses a kid who may not play now |
| Live TV (all or nothing per user) | Which channels each kid may watch, held on the playback, guide and recording routes |

Plugin data lives in a small SQLite database in `data/kids/` in Jellyfin's data folder, next to Jellyfin's own
database (see [Your data](docs/install.md#your-data)). Admins and API keys are never blocked, and disabling the plugin
removes everything it enforces.

## Clients

Every Jellyfin client gets the server's rules. A kids app that speaks the plugin's API also gets kids mode: a profile
picker, a kids home and the parent code on the TV. Clients find the plugin with the anonymous `GET /Kids/Info`, which
lists the features the server supports, and clients without kids mode keep working as usual.

<a href="https://github.com/aldoborrero/peke"><img src="docs/assets/clients/peke.svg" alt="Peke for Jellyfin: the kids app for Android TV, made with this plugin" width="100%"></a>

[Peke for Jellyfin](https://github.com/aldoborrero/peke) is an Android TV app made only for kids, built together with
this plugin. Each kid picks their avatar and gets their own home with their picks, shows and live channels. Peke shows
the time left, warns before it runs out and stops when it does, even with enforcement off. Grown-ups get in with the
household parent code. It needs Jellyfin Kids 0.0.7.0 or later and is in development.

Other Jellyfin apps have no kids mode yet, but the rules live on the server, so a kid's screen time, live TV channels
and, with enforcement on, the stop at time's up apply in them too. They are not tested one by one, and an app that
ignores the stop keeps playing what it has already downloaded.

We want kids mode in more clients. The API is open to any Jellyfin client and documented under [API](#api); if you
work on a client and want to add it, open an issue and we will help.

| | Peke | Other Jellyfin apps |
|---|:---:|:---:|
| Kid picker, kids home and picks | ✓ | |
| Parent code on the TV | ✓ | |
| Jellyfin's libraries, ratings and allowed hours | ✓ | ✓ |
| Screen time counted on the server | ✓ | ✓ |
| Only the kid's live TV channels | ✓ | ✓ |
| Warning and stop when time is up | ✓ | with enforcement |

## API

The client API lives under `/Kids` and is versioned (`ApiVersion` and `ApiMinorVersion` in `GET /Kids/Info`: the
major version changes only on breaking changes, the minor one on additions). The contracts:

- [Profiles and detection](specs/001-kids-plugin-foundation/contracts/kids-api-v1.md)
- [Parent lock and grants](specs/002-kids-parent-lock/contracts/kids-api-v1-parent-lock.md)
- [Daily allowance](specs/003-kids-allowance-tracking/contracts/kids-api-v1-allowance.md)
- [Session limit](specs/004-kids-session-limit/contracts/kids-api-v1-session-limit.md)
- [Enforcement and the sitting](specs/005-kids-enforcement/contracts/kids-api-v1-enforcement.md)
- [Live TV](specs/006-kids-live-tv/contracts/kids-api-v1-live-tv.md)
- [Reset today](specs/007-kids-dashboard-ux/contracts/kids-api-v1-reset.md)
- [Avatars, API 1.1 and channel ratings](specs/012-kids-avatar/contracts/kids-api-v1-avatar.md)
- [Bonus undo and End break, API 1.2](specs/013-bonus-undo-break/contracts/kids-api-v1-bonus-undo-break.md)

The design and the reasoning behind it are in [docs/plan.md](docs/plan.md).

## Development

Everything runs from the Nix devshell (`direnv allow`, or `nix develop`): the .NET 10 SDK, Jellyfin 12.x
from nixpkgs, jellyfin-ffmpeg and process-compose.

```sh
dev          # start the dev loop (process-compose up)
dev-reset    # forget the dev server's state
```

The dev loop:

1. `plugin` builds the plugin and installs it in `.dev/data/plugins/` with a `meta.json` made from
   `build.yaml`.
2. `jellyfin` runs the server on <http://127.0.0.1:8096> (`JELLYFIN_KIDS_PORT` picks another port, for example to
   run two dev loops: `JELLYFIN_KIDS_PORT=8196 dev -p 8197`), with its data, config, cache and logs under
   `.dev/`. It listens on localhost only; the Android emulator reaches it at `http://10.0.2.2:8096`.
3. `provision` finishes the startup wizard and creates the dev users: admin `admin` / `admin` and the
   kids `alice` and `bob` (`DEV_ADMIN_USER`, `DEV_ADMIN_PASSWORD` and `DEV_KIDS` change them). It also
   adds a Kids library with a generated test clip, and a live TV tuner with two channels, PekeTV and Grown-ups News,
   and their guide. The admin token is written to `.dev/admin.token`.
4. `livetv` serves those two channels as live MPEG-TS streams from jellyfin-ffmpeg, on ports `JELLYFIN_KIDS_PORT` + 10
   and + 11.

Editing anything under `src/` (or `build.yaml`) rebuilds the plugin and restarts Jellyfin, since
Jellyfin only loads plugins at startup.

Tests:

```sh
dotnet test                # unit tests
scripts/dev-check.sh       # checks against the running dev server: auth, answers, playback counting, pages, enforcement, live TV
```

Features are planned with [spec-kit](https://github.com/github/spec-kit) under [specs/](specs/), following
the project's [constitution](.specify/memory/constitution.md). Upstream sources used as reference
(Jellyfin and other plugins) are cloned into `.reference/`, which is not committed.

## Roadmap

- [x] Kid profiles and dashboard pages
- [x] Household parent code with server-side checks and grants
- [x] Daily allowance counted on the server
- [x] Per-kid sitting limit
- [x] Stop playback when time is up, on every client (opt-in)
- [x] Live TV channels per kid, with their guide
- [x] A dashboard that shows each kid's protection gaps, and Reset today
- [x] Kid avatars, and a warning when Jellyfin hides a picked channel from a kid
- [x] The kid's Jellyfin profile, libraries, ratings and allowed hours, edited from the kid editor
- [ ] Recommended restrictions in one step, and picks backed by Jellyfin collections
- [ ] Per-kid codes, viewing history, and export and import of plugin data

## License

The code is MIT. See [LICENSE](LICENSE).

The banner in [docs/assets/banner.svg](docs/assets/banner.svg) includes the Jellyfin logo from
[jellyfin-ux](https://github.com/jellyfin/jellyfin-ux), © the Jellyfin contributors, under
[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), and is shared under the same license. The Peke
card in [docs/assets/clients/](docs/assets/clients/) uses Peke's app icon from
[Peke for Jellyfin](https://github.com/aldoborrero/peke). Jellyfin Kids is a community plugin, not an official
Jellyfin project.
