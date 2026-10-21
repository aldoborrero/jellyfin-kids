# Data Model: Live TV for kids

## KidProfileSettings (changed, stored)

| Field | Type | Rules |
|---|---|---|
| `LiveTvChannelIds` | list of GUID | Default empty. Null, absent or empty: no live TV. At most 200. Each new id must be an existing live TV channel; no duplicates. Ids already stored are kept even if the channel is gone. Order kept as sent. |

Stored in the existing JSON `settings` column of `kid_profile`; rows written before 0.0.6.0 read as empty (no
migration, research R11). Saved under the profile revision like every other field.

Validation errors (400, `errors` keyed by field):

| Key | Message |
|---|---|
| `LiveTvChannelIds` | `At most 200 channels.` |
| `LiveTvChannelIds[i]` | `Not a live TV channel.` / `Listed more than once.` |

## Effective channels (in memory, per kid)

`EnforcementState` keeps per kid: the profile's `LiveTvChannelIds` and the cached Jellyfin `User`.

```
effective(kid) = {}                                   if the kid is not loaded yet (and a load is requested)
               = {}                                   if User lacks EnableLiveTvAccess
               = set(profile.LiveTvChannelIds)        otherwise
```

Kid reads additionally drop channels Jellyfin hides from the kid or that no longer exist (they are not returned by
Jellyfin's queries with the kid as user).

Reloaded when the profile changes (ProfileChanged), the user changes (OnUserUpdated) and every 60 s (users).

## LiveStreamRegistry (in memory)

| Key | Value |
|---|---|
| live stream id (`MediaSourceInfo.LiveStreamId`) | channel id |
| live stream unique id (`ILiveStream.UniqueId`, the `LiveStreamFiles/{id}` path part) | channel id |

Recorded after a kid's PlaybackInfo or LiveStreams/Open for a channel was let through. Pruned of streams that are no
longer open on each record. Lost on restart (so are Jellyfin's open streams).

## Request judgement (KidsLiveTvFilter)

```
kid = KidRequest.Resolve(request)          # none for admins, API keys, non-kids, unattributable token-less requests
if kid is none: pass

rule Recording           → 403 liveTvRecording
rule Playback            → channels = channelOf(itemId args) ∪ channelOf(openToken) ∪ channelOf(liveStreamId) ∪ channelOf(streamId)
                           unknown live TV stream ids count as a channel that is never allowed
                           any channel ∉ effective(kid) → refuse liveTvChannel (200 NotAllowed or 403 by kind)
                           else run; after a Negotiate/Open for a channel, record the answer's live stream ids
rule ChannelList         → run without paging; keep effective; page; count
rule ProgramList         → channelIds := requested ∩ effective (or effective); empty → empty result
rule RecommendedPrograms → run without paging; keep effective; page; count
rule Channel / Program   → item's channel ∉ effective → 404
```

## LiveTvRule (attached per action)

`Playback` (with 005's `GateKind`: Negotiate, Open, Deliver), `Recording`, `ChannelList`, `ProgramList`,
`ProgramListPosted`, `RecommendedPrograms`, `Channel`, `Program`.

## Reasons

| Code | When |
|---|---|
| `liveTvChannel` | a kid asks for a channel outside their effective set, or an unknown live TV stream |
| `liveTvRecording` | a kid calls a recording, timer or series timer route, or streams a live recording |

The 005 reasons (`timeUp`, `outsideSchedule`, `sessionLimit`) are decided first and win.
