# Contract: Kids client API 1.4 (feature 22: statistics)

Agreed with the Peke task on 2026-11-19; neither side changes it without saying so. Additive: `ApiVersion` stays 1.
The JSON rules of the earlier contracts apply (PascalCase by default, camelCase with `profile="CamelCase"`,
ProblemDetails camelCase), except that the two nullable fields below are **always written**, with `null`.

## GET /Kids/Info (changed, anonymous)

`ApiMinorVersion` 4, and `Features` gains `stats` (the endpoint below exists). `advancedStats` is **not** a feature:
whether the household turned it on is a field of the Stats answer.

## GET /Kids/Users/{userId}/Stats?days=N (new)

- `N` 1–90, default 7. Outside: 400 with `errors.Days`.
- `[Authorize(Policy = IgnoreParentalControl)]`; an administrator, an API key or a parent grant holder
  (`X-Kids-Grant`), like `WatchTime/Bonus`. Anyone else, the kid included: 403. Not a kid: 404.

```json
{
  "UserId": "5b2a4e8f0c6d4d4e9f1a1c2d3e4f5a6b",
  "Today": "2026-11-19",
  "AdvancedStats": true,
  "RetentionDays": 90,
  "Days": [
    { "Day": "2026-11-18", "UsedMinutes": 0, "BonusMinutes": 0, "AllowanceMinutes": 60 },
    { "Day": "2026-11-19", "UsedMinutes": 42, "BonusMinutes": 15, "AllowanceMinutes": 60 }
  ],
  "TopSeries": [ { "SeriesId": "0f1e2d3c4b5a69788796a5b4c3d2e1f0", "Name": "Bluey", "Minutes": 42, "Plays": 1, "IsLiveTv": false } ],
  "Breaks": [ { "Day": "2026-11-19", "Count": 1, "Minutes": 15 } ]
}
```

(This is the exact output of `StatsControllerTests.Shape_MatchesTheContract`.)

- `Days`: one per day of the range, oldest first, today last, days with 0 included. `UsedMinutes` is whole minutes,
  as the Status counts them. `AllowanceMinutes`: that day's allowance as the profile had it, or today's if not known;
  `null` = no daily limit.
- `TopSeries`: at most 5, by minutes over the range (rounded). A movie is its own entry (`SeriesId` = the item id);
  live TV is grouped per channel with `IsLiveTv: true` (`SeriesId` = the channel id). `Plays` counts playbacks.
  `SeriesId` is `null` only when the server knew no id.
- `Breaks`: only days that had breaks, oldest first. `Minutes` is how long they lasted together.
- `TopSeries` and `Breaks` are `[]` when `AdvancedStats` is false, even if older data is kept.
- Today's live figures (used, left, sitting, break) stay on `/Status`.

## Dashboard-only routes (not for clients)

`GET /Kids/Users/{userId}/Stats/Detail?days=1–365`, `GET /Kids/Admin/WatchLog` and `DELETE /Kids/Admin/WatchLog` serve
the plugin's statistics page and may change shape without a version change.
