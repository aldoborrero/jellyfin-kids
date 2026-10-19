# Contract: allowance on the Kids dashboard pages

This extends [feature 1's pages](../../001-kids-plugin-foundation/contracts/dashboard-pages.md) and feature 2's
"Parent lock" section. The HTML stays static and anonymous (FR-027).

## Kids page

- **"Day starts at" setting**, in a "Screen time" section under "Parent lock":
  - a select from 00:00 to 23:00;
  - loaded with `ApiClient.getPluginConfiguration(pluginId)` and saved with `updatePluginConfiguration` (`DayStartsAtHour`);
  - the help text reads "Each kid's screen time starts again from zero at this hour, server time.";
  - the plugin id is `d62ee188-270c-49d8-b7b9-5e524fec2335` (build.yaml).
- **Per-kid usage**: each kid row calls `GET /Kids/Users/{id}/Status` and shows:
  - with an allowance: "Today: 12 of 45 min (33 min left)", or "Today: 47 of 45 min, time is up";
  - without one: "Today: 12 min (no limit)";
  - a failed status read shows "Usage unavailable", never 0.
- **Bonus**: a small "+ minutes" control per kid (number 1–240 and an "Add" button) posts `WatchTime/Bonus`. A 200
  re-renders the row from the returned status, and a 400 shows the message inline.

## KidsUser editor

- A "Daily allowance" number field (minutes, 0–1440, empty means "No limit"). An out-of-range value blocks saving
  with an inline message.
- Saving sends `DailyAllowanceMinutes` (null when empty) with the rest of the profile, under the same revision and
  conflict handling as feature 1.
