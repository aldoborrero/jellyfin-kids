# Feature Specification: Kids statistics and the watch log

**Feature Branch**: `kids-stats` (from `main` at d5e3017, plugin 0.11.0.0; spec directory `022-kids-stats`, the next
number after `021-unlimited-autoplay`)

**Created**: 2026-11-19

**Status**: Implemented

**Input**: The owner approved the brief "Zona de adultos: normas y estadísticas" on 2026-11-19: parents see each kid's
statistics, today and the last days on the TV (Peke), and in detail (per day, series, episode, hour, device) in the
plugin's dashboard. The detailed watch log is optional ("Advanced statistics", off by default) and its retention is
configurable, like Playback Reporting. The client API was agreed with the Peke task the same day
([contracts/kids-api-v1-stats.md](contracts/kids-api-v1-stats.md)).

Builds on [feature 3](../003-kids-allowance-tracking/spec.md) (usage per day, bonus, pruning), [feature 5](../005-kids-enforcement/spec.md)
(the sitting and its break) and [feature 13](../013-bonus-undo-break/spec.md) (End break).

## Decisions

- **D1. One switch for the house.** `AdvancedStats` is a household setting, off by default (brief §7, proposal
  accepted). It is not a `/Kids/Info` feature; the Stats answer says whether it is on.
- **D2. Retention is one setting for all screen-time data.** `StatsRetentionDays` (30, 90, 180 or 365; other values
  read as 90) replaces the 90-day constant for `usage_day`, `bonus`, `reported_segment`, the noted allowances and the
  watch log. Today is never pruned, whatever the value, since the daily allowance needs it. A change applies at the
  next run of the daily "Prune kids screen time" task. Offline reports keep their own 90-day age limit.
- **D3. Turning it off keeps the data.** Recording stops at once. What was kept stays until it ages out with the
  retention, or until an administrator deletes it with "Delete the watch log" on the statistics page (which keeps
  minutes per day). While off, `TopSeries` and `Breaks` are empty for clients; the dashboard still shows what was kept,
  with a notice saying so. Reason: a parent who switches off to stop recording should not lose last month's history by
  accident, and the delete button is one click away for a parent who wants it gone.
- **D4. Allowance per day.** Each flush of counted time notes, for today's kids, the allowance their profile has at
  that moment (`day_allowance`). A day with nothing noted (no play, or before this version) shows today's allowance.
  Today always shows the profile as it is now. This is recorded whatever `AdvancedStats` says: it is as small as
  `usage_day` and only says what the kid was allowed.
- **D5. API minor version 3, feature `stats`.** On `main` the minor version is 2; this feature takes 3. The branch
  `unlimited-autoplay` also takes 3; whichever merges second takes the next number (see the handoff).

## Segment rules (R2)

A watch segment is one item played in one playback (Jellyfin session + play session) by one kid. Its seconds are
exactly the playing time the allowance counts (`PlaybackClock`: pauses excluded, gaps capped), so a day's segments add
up to that day's screen time from server events. A segment ends when:

1. the playback stops (`PlaybackStopped`);
2. the item changes within the play session (next episode, another channel): a new play;
3. a new household day starts (split at the day start, same play);
4. nothing was counted for 2 minutes (a long pause), same play;
5. the playback goes idle for 6 minutes without events (the clock's sweep).

A seek or a short pause keeps the segment. Two clients playing at once make two segments, each with its own device and
app. Live TV keeps the channel id and name. *Plays* count playbacks, not segments.

A break is noted when the counted time reaches the kid's sitting limit (start = when it was reached, minutes = the
household break), and updated when a grown-up ends it early with End break or Reset (minutes = how long it lasted,
`ended_by_grownup`). Breaks are only noted while `AdvancedStats` is on. Enforcement rules are unchanged.

## Requirements

- **FR-001** Settings `AdvancedStats` (default false) and `StatsRetentionDays` (default 90) in the plugin
  configuration, editable on the statistics page.
- **FR-002** Schema 6 adds `day_allowance`, `watch_segment` and `break_event` (data-model below). Pruning, purges of a
  user and the orphan sweep cover them.
- **FR-003** The watch log is fed from the same playback events as the allowance, with no I/O in the event handlers:
  segments and breaks wait in memory and are written with the usage flush (30 s, and after a stop); a growing segment
  is upserted by its id. Rows of users who are no longer kids are dropped.
- **FR-004** `GET /Kids/Users/{userId}/Stats?days=N` as in the contract: admin or parent grant holder, 403 otherwise
  (the kid's own token included), 400 for N outside 1–90, 404 if not a kid.
- **FR-005** `GET /Kids/Users/{userId}/Stats/Detail?days=N` (1–365, default 14) for the dashboard: days with the time
  per series, series with episodes, a weekday/weekend × 24-hour grid (server time) and devices. Same access.
- **FR-006** `GET /Kids/Admin/WatchLog` (size, oldest day) and `DELETE /Kids/Admin/WatchLog`, administrators only;
  the delete is logged.
- **FR-007** A "Kids statistics" dashboard page with a menu entry: kid and period filters, summary, tabs By day
  (stacked by series, dashed daily allowance), By series (expandable to episodes), By hour (heat map), By device; the
  settings card with the switch, the retention, what happens when it is off, the privacy line and the delete button.
  Empty state when off. English or Spanish from the viewer's language; plain JS and SVG, no new dependency.
- **FR-008** Nothing leaves the server; the page says so.

## Data model (schema 6)

| Table | Columns | Notes |
|---|---|---|
| `day_allowance` | `user_id`, `day`, `minutes` (null = no limit) | PK (user_id, day) |
| `watch_segment` | `id`, `playback_id`, `user_id`, `day`, `started_at`, `ended_at`, `seconds`, `item_id`, `item_type`, `item_name`, `series_id`, `series_name`, `season`, `episode`, `channel_id`, `channel_name`, `device`, `client` | PK id; index (user_id, day) |
| `break_event` | `user_id`, `day`, `started_at`, `minutes`, `ended_by_grownup` | PK (user_id, started_at) |

A plugin older than this one refuses a schema-6 database (kids data unavailable), so a downgrade needs the backup of
`kids.db` taken before upgrading.

## Constitution check

- I: builds on Jellyfin's playback events, scheduled tasks, plugin configuration and dashboard pages; nothing of
  Jellyfin's is duplicated. Playback Reporting (GPL-3.0) was studied for the table shape only, no code copied.
- II: statistics are read-only; no client can change them; the delete is admin only.
- III: every route uses `IgnoreParentalControl`; nothing blocks.
- IV: no telemetry, all data stays in `kids.db`; the page HTML holds nothing per household.
- V: additive (`ApiMinorVersion` 3, feature `stats`).
- VI: tests for segment rules, recording on/off, breaks, pruning, the endpoint shape and access.

## Verification

`nix develop -c dotnet test`; the page rendered against fixture data in headless Chromium (handoff).
