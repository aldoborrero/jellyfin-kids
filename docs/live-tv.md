# Live TV

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
