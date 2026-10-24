# Quickstart: validating avatars and hidden channel warnings

## 1. Build and unit tests

```sh
dotnet build Jellyfin.Plugin.Kids.slnx -c Release     # 0 warnings (TreatWarningsAsErrors)
dotnet test Jellyfin.Plugin.Kids.slnx
```

## 2. Dev loop and dev-check

From the devshell, on a free port (never against the household server):

```sh
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> dev
JELLYFIN_KIDS_DEV=$PWD/.dev JELLYFIN_KIDS_PORT=<port> scripts/dev-check.sh
```

New checks, section `e` ([contract](contracts/kids-api-v1-avatar.md)):

| Check | Expected |
|---|---|
| `ea` Info | `ApiVersion` 1, `ApiMinorVersion` 1, `avatar` in `Features` |
| `eb` admin sets alice's `Avatar: "Lucy"` | 200, `Avatar == "lucy"`; alice reads her profile: `"lucy"`; `/Kids/Profiles` has it for alice |
| `ec` alice sets her own avatar | 403, unchanged |
| `ed` `Avatar: "bluey"` | 400 `errors.Avatar` |
| `ee` `PUT` without `Avatar` | 200, `Avatar` absent in the profile and in `/Kids/Profiles` |
| `ef` admin lists channels for alice with defaults | `HiddenFromUser` false for PekeTV |
| `eg` block unrated `LiveTvChannel` in alice's policy | PekeTV `HiddenFromUser` true, no `Rating`; policy restored after |
| `eh` `?userId=` of an unknown user; alice calls it | 404; 403 |

## 3. Browser (jellyfin-web 12.2)

1. Kid editor: Avatar select shows "Jellyfin profile image"; choose Sara → "S" preview and "Unsaved changes"; Save →
   toast; reload → Sara selected.
2. Kids page: alice's row shows the "S" initial.
3. Tick Live TV under "Block items with no or unrecognized rating information" for alice in Jellyfin: the Kids page
   and the editor show "PekeTV is hidden from alice: unrated live TV channels are blocked…" with Fix → Parental
   control. Untick: the line goes.
4. Banner with enforcement off reads "Limits only apply in the kids app."
