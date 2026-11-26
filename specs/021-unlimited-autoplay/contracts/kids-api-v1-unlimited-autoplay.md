# Contract: Kids client API 1.3 (feature 21: autoplay without a limit)

Extends the [feature 1 contract](../../001-kids-plugin-foundation/contracts/kids-api-v1.md) and the
[feature 13 contract](../../013-bonus-undo-break/contracts/kids-api-v1-bonus-undo-break.md). Everything here is
**additive**: `ApiVersion` stays 1. Examples are PascalCase.

## GET /Kids/Info (changed, anonymous)

```json
{"PluginVersion": "0.12.0.0", "ApiVersion": 1, "ApiMinorVersion": 3,
 "Features": ["profiles", "parentLock", "allowance", "sessionLimit", "enforcement", "sitting", "liveTv",
              "watchTimeReset", "avatar", "bonusUndo", "breakEnd", "unlimitedAutoplay"], …}
```

- Offer "No limit" only with `unlimitedAutoplay`. Against an older plugin, a 0 is refused with a 400.

## GET and PUT /Kids/Users/{userId}/Profile (changed)

Same routes, auth and other fields. `AutoplayCap`:

| Value | Meaning |
|---|---|
| `0` | No limit: the next episode always plays on its own (while `AutoplayEnabled` is true) |
| `1`–`10` | That many episodes in a row, as before |
| missing (PUT) | 3, as before |
| anything else | **400**: `{"errors":{"AutoplayCap":["Must be 0 (no limit) or between 1 and 10."]}}` |

- Stored profiles keep their value; nothing is migrated.
- A client that predates this feature and reads 0 may coerce it to 1.
