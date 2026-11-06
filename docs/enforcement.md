# Enforcement

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
