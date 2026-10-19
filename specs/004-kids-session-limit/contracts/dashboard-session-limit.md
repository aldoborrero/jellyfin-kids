# Contract: session limit on the Kids dashboard pages

This extends [feature 1's pages](../../001-kids-plugin-foundation/contracts/dashboard-pages.md) and
[feature 3's editor field](../../003-kids-allowance-tracking/contracts/dashboard-allowance.md). The HTML stays static
and anonymous (FR-008).

## KidsUser editor

- A "Session limit (minutes)" number field below "Daily allowance": 5–120, empty means "No limit". The help text reads
  "The longest one sitting can last on a TV, counted on each TV. Leave empty for no limit."
- An out-of-range or non-numeric value shows "Between 5 and 120 minutes, or empty for no limit." next to the field, on
  input and on save, and blocks saving.
- Saving sends `SessionLimitMinutes` (null when empty) with the rest of the profile, under the same revision and
  conflict handling as feature 1. A 400 keyed `SessionLimitMinutes` shows its message next to the field.
- The Kids page is unchanged.
