# Jellyfin kids and parental controls: landscape research

Research date: 2026-10-19. Server and web sources checked: `.reference/jellyfin` (`SharedVersion.cs:3-4`, 12.2.0) and
`.reference/jellyfin-web` (`package.json:3`, 12.2.0). In the citations below, `S:` means `.reference/jellyfin` and `W:` means
`.reference/jellyfin-web`.

**Legend**
- **[V]** I verified it: the page, issue or PR was read in full, or the source was read locally.
- **[S]** It was seen only in a search snippet or summary.
- **[I]** It is my inference, not a fact.

Shorthand for links:
- `jf#N` is `https://github.com/jellyfin/jellyfin/issues/N`. Pull requests use the same URL scheme, and GitHub redirects.
- `web#N`, `atv#N` and `meta` use the same scheme for jellyfin-web, jellyfin-androidtv and jellyfin-meta.
- `Fider #N` is `https://features.jellyfin.org/posts/N`. Vote counts are Fider votes.

Versioning context [V]:
- 10.12 was renamed **12.0**, released 2026-09-08.
- 12.1 shipped 2026-09-15.
- 12.2 shipped 2026-10-05.

---

## 1. Jellyfin's own plans, discussions and rejections

### 1.1 How Jellyfin wants features proposed [V]

- **Feature requests go to Fider, not to GitHub.**
  - Source: <https://jellyfin.org/docs/general/contributing/issues/>.
  - It says: "Please keep all feature requests to this page and not GitHub issues". Issues "should **only** detail software bug reports".
  - It is enforced. thornbill closed [web#7447](https://github.com/jellyfin/jellyfin-web/issues/7447) as not_planned and invalid: "Feature requests are tracked on features.jellyfin.org, not in GitHub issues."
- **Cross-project or substantial changes start as a jellyfin-meta discussion.**
  - Source: <https://jellyfin.org/docs/general/contributing/development/>.
  - Changes "affecting multiple sub-projects should first be proposed as a" jellyfin-meta discussion.
  - A PR must reference an issue.
  - PRs to `master` need two team reviews.
- **Proposal first, PR after a go-ahead.**
  - nielsvanvelzen in [androidtv discussion #5739](https://github.com/jellyfin/jellyfin-androidtv/discussions/5739): "with bigger change like that we want a proposal in our jellyfin-meta repository first. When there is a go-ahead from the appropriate teams … you can start on a pull request."
  - In the same discussion: "we ONLY support the official Jellyfin API (that is part of the server source code)".
  - The project's pattern is that core adds *interfaces* and plugins implement them: metadata, lyrics and media segments, plus recommendations and search in 12.0.
- **UI/UX approval gates web features.**
  - Source: [web discussion #7059](https://github.com/jellyfin/jellyfin-web/discussions/7059), relayed from Matrix: "all features must go through UI/UX approval or else a contributor is highly likely to not look at it."
- **LLM policy.**
  - Source: <https://jellyfin.org/docs/general/contributing/llm-policies/>.
  - It says: "pure 'vibe coding' will be rejected"; "If you can't explain what the LLM did, we are not interested in the change"; and no `.claude` configs in commits.
  - LLM-written text is banned in issues and comments.
  - It is actively enforced on exactly this feature area:
    - [atv#5352](https://github.com/jellyfin/jellyfin-androidtv/pull/5352) and [atv#5518](https://github.com/jellyfin/jellyfin-androidtv/pull/5518) (PIN PRs);
    - [jf#17737](https://github.com/jellyfin/jellyfin/pull/17737) (parental refusal fix);
    - Fider #3653 and #3767.
- **Development is volunteer-driven.**
  - joshuaboniface on [Fider #493](https://features.jellyfin.org/posts/493) (user groups, 203 votes, open): "development happens when someone does it."
  - nielsvanvelzen in [meta #102](https://github.com/jellyfin/jellyfin-meta/discussions/102): "'planned' means we want it to happen, not necessarily that somebody is working on it."
- **No roadmap or RFC on parental controls exists in jellyfin-meta.** The research agent scanned about 100 discussion titles [V].

### 1.2 Kids and child profiles, and restricted UI modes

| Item | Status | Notes |
|---|---|---|
| [Fider #531](https://features.jellyfin.org/posts/531) "Child Mode" (simplified, YouTube Kids-like UI) | open, 77 votes | No staff response [V] |
| [Fider #912](https://features.jellyfin.org/posts/912) "Create a childrens page" | open, 37 votes | No staff response |
| [Fider #3508](https://features.jellyfin.org/posts/3508) "Parental Control Profile", [#3007](https://features.jellyfin.org/posts/3007) "Parent account linked to child" | open, 8 and 2 votes | No staff response |
| [Fider #493](https://features.jellyfin.org/posts/493) user groups | open, 203 votes | joshuaboniface: "not a big fan of Account -> Profile style (like say Netflix) because that seems like it would get *Very* messy in the backend". He prefers groups of individual accounts [V] |
| [Fider #1198](https://features.jellyfin.org/posts/1198) profile selection | **declined** (EraYaN, 2021) | "The current account system does this already, set all accounts to publicly show." [V] |
| [Org discussion #16558](https://github.com/orgs/jellyfin/discussions/16558) "True Managed Profiles (Netflix style Who's Watching)" | open | No team reply [V] |
| Forum: [first-class household profiles](https://forum.jellyfin.org/t-first-class-household-profiles-for-shared-devices) (2026-05) | n/a | Moderator: "While there aren't 'child profiles', you can create an account with parental rating restrictions." [V] |
| [Fider #999](https://features.jellyfin.org/posts/999) "kids/parental" tag on the tracker | open | Shows the topic has no owner [V] |

**Verdict [V+I]:** Jellyfin has no plan, owner or RFC for kids profiles or a kiosk mode. The official answer is "one Jellyfin
account per kid, with parental restrictions". A kids flag in core would be a new concept, and joshuaboniface's comment
suggests resistance to sub-profiles inside an account. That is fine for us, because our kid profiles are ordinary
Jellyfin users.

### 1.3 Screen time and watch-time limits

| Item | Status | Notes |
|---|---|---|
| [Fider #699](https://features.jellyfin.org/posts/699) "Parental Control to Limit Screentime" | open, 29 votes | No staff response [V] |
| [Fider #2266](https://features.jellyfin.org/posts/2266) "Daily time limit" ("max 1 hour per day, then stop playback") | open, 26 votes | No staff response [V] |
| [jf#14442](https://github.com/jellyfin/jellyfin/issues/14442) "increase the length of time my account is accessed daily" | closed | Feature requests are not accepted as issues [V] |
| [meta #136](https://github.com/jellyfin/jellyfin-meta/discussions/136) "Playback history & watch statistics" (Shadowghost, 2026-06) | open, implemented on his `playback-history` branch | **The natural core foundation for watch-time limits** [V] |
| [meta #121](https://github.com/jellyfin/jellyfin-meta/discussions/121) | open | nielsvanvelzen: they "discussed moving this functionality [playback reporting plugin] into the server as core feature … But no work on that has started." [V] |
| Forum threads asking for time banks: [library access time management](https://forum.jellyfin.org/t-library-access-time-management), [schedule a pause / limit time](https://forum.jellyfin.org/t-schedule-a-pause-for-users-or-limit-time-to-access-watch?pid=46953), [micromanage kids viewing](https://forum.jellyfin.org/t-micromanage-library-access-to-control-kids-viewing-capabilities?pid=72643) | n/a | Users are pointed to AccessSchedules and external tools [S] |

**Verdict [V]:** there is demand, but no maintainer has taken a position. Nobody has rejected a watch-time limit either. It has simply never
been proposed as a meta RFC.

### 1.4 Profile PINs and switching, including the removed Easy PIN

- **Easy PIN (EasyPassword) was removed for security reasons** [V]:
  - [jf#3423](https://github.com/jellyfin/jellyfin/pull/3423) removed it from auth providers (crobibero, 2020).
  - [jf#9820](https://github.com/jellyfin/jellyfin/pull/9820) deprecated it (merged 2023-05). cvium: "Given what happened with Emby, it is a terrible idea to have LAN-specific passwords. Quick Connect is more secure and only slightly more annoying."
  - [web#4721](https://github.com/jellyfin/jellyfin-web/pull/4721) removed the UI (thornbill, 2023-07).
  - The [10.9.0 blog](https://jellyfin.org/posts/jellyfin-release-10.9.0) says it was "a big security risk especially for administrator accounts; QuickConnect login is still supported".
  - 12.0 removed the `POST /Users/{id}/EasyPassword` route.
  - Earlier bugs showed it was fragile. For example, [jf#2671](https://github.com/jellyfin/jellyfin/issues/2671): "Local PIN is authenticated from public network".
  - Forum: [easy pin code local login](https://forum.jellyfin.org/t-easy-pin-code-local-login). Moderators point to Quick Connect, and the question of how kids on phone-less tablets should log in went unanswered [V].
- **Client-only PINs were rejected on Android TV, and server-side PINs were named as the condition.**
  - nielsvanvelzen on [atv#5518](https://github.com/jellyfin/jellyfin-androidtv/pull/5518) (closed 2026-04): "There are currently no plans for this functionality from the client perspective. If we were to implement this the pins would need to be configurable server-side. In addition the code appears to be in violation of our LLM policy." [V]
  - [atv#3509](https://github.com/jellyfin/jellyfin-androidtv/issues/3509) "PIN protection for users" is still open with 56 reactions and no maintainer comment [V].
- **Swiftfin ships a client-local per-user PIN** ([Swiftfin#1383](https://github.com/jellyfin/Swiftfin/pull/1383), merged 2025-01) [V].
  - So the projects are inconsistent: iOS and tvOS have it, Android TV refuses it.
  - Jellyfin's iOS client is Swiftfin. jellyfin-expo is a web wrapper, and searching it found no relevant hits.
- **PIN requests on Fider are all open with no staff response** [V]:
  - [#2544](https://features.jellyfin.org/posts/2544) "Profile PIN so kids can't use parent profile" (62 votes)
  - [#1739](https://features.jellyfin.org/posts/1739) "Password/PIN protect a library" (60 votes)
  - [#355](https://features.jellyfin.org/posts/355) (20 votes)
  - [#779](https://features.jellyfin.org/posts/779) (13 votes)
  - [#2205](https://features.jellyfin.org/posts/2205) (12 votes)
  - [#1697](https://features.jellyfin.org/posts/1697) "Parental Control local code" (2 votes)
- **PIN requests that were closed or declined on Fider** [V]:
  - [#2206](https://features.jellyfin.org/posts/2206) completed. thornbill: "This is what quick connect is for?"
  - [#2894](https://features.jellyfin.org/posts/2894) declined: "Lacks description."
- **Profile switching is wanted but stalled** [V]:
  - [Fider #2348](https://features.jellyfin.org/posts/2348) "Multiple account switch" (**started**, 156 votes). thornbill: Android TV already switches users.
  - [web#6377](https://github.com/jellyfin/jellyfin-web/pull/6377) (a proof of concept). thornbill: "this is definitely a feature we are interested in supporting". The bot closed it in 2025 for merge conflicts.
  - [web#7446](https://github.com/jellyfin/jellyfin-web/pull/7446) is open and stale. It is waiting on the UI/UX team, which wants a full "Who's Watching" redesign ([web discussion #7059](https://github.com/jellyfin/jellyfin-web/discussions/7059), 52 upvotes). That discussion deliberately leaves PIN storage (server or device) out of scope.
  - [meta #90](https://github.com/jellyfin/jellyfin-meta/discussions/90) "Support PIN authorization when changing profile" (10 upvotes, 2025-02) has no team response.
- **Auto sign-out and persistent sessions** [V]:
  - [jellyfin-webos#136](https://github.com/jellyfin/jellyfin-webos/issues/136) "Auto sign out (parental control)" is open. A parent left logged in exposes their account.
  - [Fider #2203](https://features.jellyfin.org/posts/2203) "timeout idle users" is open.
  - [atv#1080](https://github.com/jellyfin/jellyfin-androidtv/issues/1080) led to "always require authentication" ([atv#1084](https://github.com/jellyfin/jellyfin-androidtv/pull/1084)).

### 1.5 Allowed hours (AccessSchedules) and their known problems

- **What exists** [V]:
  - Per-user weekday and hour windows.
  - [Fider #629](https://features.jellyfin.org/posts/629) was marked completed ("this feature already exists").
  - [web#4193](https://github.com/jellyfin/jellyfin-web/pull/4193) added half-hour slots.
  - [jf#8013](https://github.com/jellyfin/jellyfin/pull/8013) lets an admin change the password of a user who is outside their schedule.
- **How it works in 12.2** [V, source]:
  - It is checked at login (`S:Jellyfin.Server.Implementations/Users/UserManager.cs:643-649`).
  - It is checked on every request by the default authorization policy (`S:Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs:81-85`, policy wired at `S:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:65-67`).
  - It uses server local time with inclusive ends (`S:Jellyfin.Data/UserEntityExtensions.cs:227-236`).
  - Nothing stops active sessions. The schedule check in `GetPlayAccess` is commented out (`S:MediaBrowser.Controller/Entities/BaseItem.cs:1135`).
- **Known problems:**
  1. **Server time only, by design.** [jf#15033](https://github.com/jellyfin/jellyfin/issues/15033) was closed. Shadowghost: "the client can then bypass the schedule by modifying the timezone/local time it reports." [V]
  2. **Clients don't explain the refusal.** [web#4447](https://github.com/jellyfin/jellyfin-web/issues/4447) is open and labelled bug and confirmed (filed by JPVenson). Web shows endless spinners when the window closes, which Shadowghost blamed on web "not properly handling error (especially 401) responses gracefully". [V]
  3. **The refusal is a bare 403.**
     - [jf#17740](https://github.com/jellyfin/jellyfin/issues/17740) "Parental control refusal is invisible to clients" is open (2026-08-30).
     - The server never sets `X-Application-Error-Code: ParentalControl`. A source search for `ParentalControl` in `S:Jellyfin.Api` and `S:Jellyfin.Server` finds only the policy names. Web's handler at `W:src/components/router/appRouter.js:197-209` is therefore dead code.
     - [jf#17737](https://github.com/jellyfin/jellyfin/pull/17737) was closed for the LLM policy. [jf#17741](https://github.com/jellyfin/jellyfin/pull/17741) and [jf#17772](https://github.com/jellyfin/jellyfin/pull/17772) compete as fixes and have no maintainer review yet. [V]
  4. **Playback already running is not stopped.**
     - The [Infuse community thread](https://community.firecore.com/t/video-keeps-playing-after-jellyfin-access-should-be-blocked/38501) and the [ElfHosted guide](https://docs.elfhosted.com/guides/families/jellyfin-screen-time/) report it [S].
     - It matches the source: HLS stalls at the next segment because `hls1` is `[Authorize]`, but anonymous `Videos/{id}/stream` keeps going [I from V source].
     - No GitHub issue tracks it [V search].
  5. **There are no per-library schedules.** [Org discussion #12659](https://github.com/orgs/jellyfin/discussions/12659) is unanswered [V].
  6. **The admin UI is being reshuffled.** [web#8636](https://github.com/jellyfin/jellyfin-web/pull/8636) and [web#8645](https://github.com/jellyfin/jellyfin-web/pull/8645) are open (2026-10), which may affect our helper copy [V].

### 1.6 Content filtering by rating and tags, and its known problems

This is where nearly all upstream parental-control work goes. It is active and maintained.

- **Major work** [V]:
  - [jf#8526](https://github.com/jellyfin/jellyfin/pull/8526): rating overhaul, 10.9.
  - [jf#9139](https://github.com/jellyfin/jellyfin/pull/9139): **AllowedTags**, merged 2023-02. It implements [Fider #235](https://features.jellyfin.org/posts/235) (86 votes). Its web counterpart [web#4535](https://github.com/jellyfin/jellyfin-web/pull/4535) was only merged in 2024-03.
  - [jf#12615](https://github.com/jellyfin/jellyfin/pull/12615): "Rework parental ratings", 10.11. It adds JSON rating systems with score and sub-score, and **MaxParentalSubRating**.
  - [web#7145](https://github.com/jellyfin/jellyfin-web/pull/7145) sets the max rating and sub-rating in the UI.
  - [jf#15493](https://github.com/jellyfin/jellyfin/pull/15493) removed InheritedTags. Episodes and seasons now check the series' tags.
- **Leaks fixed in 12.0** [V, release notes]:
  - [jf#17019](https://github.com/jellyfin/jellyfin/pull/17019): special features.
  - [jf#17466](https://github.com/jellyfin/jellyfin/pull/17466): ItemByName.
  - [jf#16553](https://github.com/jellyfin/jellyfin/pull/16553): albums.
  - [jf#16328](https://github.com/jellyfin/jellyfin/pull/16328): fall back to unrated.
  - [jf#16852](https://github.com/jellyfin/jellyfin/pull/16852): splash screen capped at 13.
  - [jf#17377](https://github.com/jellyfin/jellyfin/pull/17377): people exempt from the allowed-tags check. This breaks the workaround of "block people individually".
- **Fixes in 12.1 and 12.2** [V]:
  - [jf#17844](https://github.com/jellyfin/jellyfin/pull/17844): case-insensitive matching.
  - [jf#18138](https://github.com/jellyfin/jellyfin/pull/18138): no allowed tags on by-name items. This fixes the empty Genres page in [jf#17785](https://github.com/jellyfin/jellyfin/issues/17785).
  - [jf#18279](https://github.com/jellyfin/jellyfin/pull/18279): recompute the score on save. This fixes [jf#18240](https://github.com/jellyfin/jellyfin/issues/18240), where ratings set by a plugin were not enforced.
  - [jf#18280](https://github.com/jellyfin/jellyfin/pull/18280): rating propagation.
- **Still open on 12.2** [V]:
  - [jf#18341](https://github.com/jellyfin/jellyfin/issues/18341): allowed tags and custom ratings fail for single episodes of a restricted series.
  - [jf#18331](https://github.com/jellyfin/jellyfin/issues/18331): allowed-tags users can't see extras. A fix is open in [jf#18437](https://github.com/jellyfin/jellyfin/pull/18437).
  - [jf#18227](https://github.com/jellyfin/jellyfin/issues/18227): re-running MigrateRatingLevels clears inherited ratings, so restricted users see mature episodes until the next scan.
  - [jf#12855](https://github.com/jellyfin/jellyfin/issues/12855): library artwork is shared by all users. gnattu called it "a design flaw".
  - [web#8608](https://github.com/jellyfin/jellyfin-web/issues/8608): the displayed rating ignores the custom rating.
  - [Org discussion #16350](https://github.com/orgs/jellyfin/discussions/16350): NC-17 and TV-MA share a score in the embedded `us.json`, which admins can't edit. [Fider #3821](https://features.jellyfin.org/posts/3821) asks for this.
  - [jellyfin.org#1892](https://github.com/jellyfin/jellyfin.org/issues/1892): parental controls are barely documented.
- **Triage** [V]:
  - Older leak reports were closed as not_planned and told to re-test, because "Parental Control logic was reworked for 12.x" (theguymadmax, 2026-09-22). Examples: [jf#16497](https://github.com/jellyfin/jellyfin/issues/16497) and [jf#11153](https://github.com/jellyfin/jellyfin/issues/11153).
- **Granularity requests are open with no staff response** [V]:
  - [Fider #2732](https://features.jellyfin.org/posts/2732): allowed tags combined with max rating (26 votes).
  - [Fider #2580](https://features.jellyfin.org/posts/2580): finer parental control (25 votes).
  - [Fider #3087](https://features.jellyfin.org/posts/3087): override the rating with a whitelist.
  - [Fider #4030](https://features.jellyfin.org/posts/4030): per-item exceptions.
  - [Fider #1739](https://features.jellyfin.org/posts/1739): PIN-protect a library.
- **Backdrops** [V]:
  - Home backdrops are capped at PG-13 ([Fider #553](https://features.jellyfin.org/posts/553), completed).
  - A user setting for it ([web#7032](https://github.com/jellyfin/jellyfin-web/pull/7032)) went stale.
  - [Fider #3653](https://features.jellyfin.org/posts/3653) was declined under the LLM policy.
  - Android TV has a screensaver age-rating preference ([atv#3513](https://github.com/jellyfin/jellyfin-androidtv/pull/3513)).

---

## 2. Community plugins and projects

All the projects marked [V] were read in their local clones under `.reference/`. Paths are relative to each clone.

| Project | Licence | Target | Type | How it enforces | Works on jellyfin-web? |
|---|---|---|---|---|---|
| [benben42/jellyfin-kids-limit-plugin](https://github.com/benben42/jellyfin-kids-limit-plugin) | **none** (study only) | 12.0, net10.0 | C# plugin | Shows a `DisplayMessage`, waits, then sends `SendPlaystateCommand(null, id, Stop)`. If the send throws, it sends Pause. Then `KillTranscodingJobs` and it closes the live stream (`Services/PlaybackTerminator.cs:78-152`). Repeats on PlaybackStart and on a 5 s sweep (`Services/WatchTimeTracker.cs:42,111-119,520-582`). Its policy-based hard block was removed in 3.1, and `LegacyBlockRestorer.cs:14-80` undoes old blocks | Yes, cooperatively: web obeys Stop. No request gate |
| [Endif12/jelly-kids-guard](https://github.com/Endif12/jelly-kids-guard) | none | 10.8 to 12.x via the API, plus Playback Reporting | Python NiceGUI app | A "soft lock" replaces `EnabledFolders` (`app/locker.py:212-228`). A "hard lock" POSTs `/Sessions/{id}/Playing/Stop` (`app/jellyfin_client.py:168-193`). Usage comes from Playback Reporting SQL (`app/stats.py:24-69`) | Partly. [I] The poll timer lives inside the page handler (`app/main.py:269-286`), so enforcement only runs while the dashboard is open |
| [irfadrazick/jellyfin-time-limiter](https://github.com/irfadrazick/jellyfin-time-limiter) | none | core API | cron script | POSTs a **partial** `UserPolicy` with `EnableAllFolders=false` and `EnabledFolders=[]` (`jellyfin_time_limiter.py:286-299`) | Blocks browsing only. [I] The partial POST resets the other policy fields, because `UpdatePolicyAsync` sets every field (`S:…/UserManager.cs:882-908`, `AccessSchedules.Clear()` at `:905`) |
| [Joker-KP/jelly-watch-wise](https://github.com/Joker-KP/jelly-watch-wise) | MIT, 37 stars | any, plus Playback Reporting or Jellystat | Python NiceGUI app | Soft lock: `EnabledFolders` (`jellyfin/interact.py:67-86`). Manual lock: `IsDisabled=true` (`jellyfin/api.py:41-44`), which revokes tokens (`S:Jellyfin.Api/Controllers/UserController.cs:443-451`) | Yes, by logging the kid out. Extra time added with its +/- buttons is lost on restart |
| [macman2034/jf-parental-helper](https://github.com/macman2034/jf-parental-helper) | none | 10.8, net6.0 | C# plugin plus JS | Nothing. An admin-only header button opens jelly-watch-wise in an iframe (`wwwroot/parental-helper.js:76-104`). [I] It does not compile: it uses a non-existent `IHasWebResources` and has mismatched braces (`Plugin.cs:39-47`) | n/a |
| [Trewqa/jellyfin-plugin-sessionflowlimits](https://github.com/Trewqa/jellyfin-plugin-sessionflowlimits) | GPL-3.0 | 10.11 | C# plugin | Caps concurrent streams: Stop on the extra sessions at PlaybackStart (`SessionEntryPoint.cs:89-124`). The Task is not awaited (`:111-118`) | Yes, via Stop |
| [voc0der/jellyfin-plugin-session-provisioning](https://github.com/voc0der/jellyfin-plugin-session-provisioning) | MIT | 12.0 | C# plugin | Not a limiter. Mints tokens with `AuthenticateDirect` behind a secret, a rate limiter and a constant-time compare (`Api/SessionProvisioningController.cs:96-238`) | n/a |
| [jellyfin/jellyfin-plugin-sessioncleaner](https://github.com/jellyfin/jellyfin-plugin-sessioncleaner) | GPL-3.0 | 12.0 | official plugin | Not a limiter. Logs out idle devices (`SessionCleanerTask.cs:61-81`) | n/a |
| [iStarFire/jellyfin-children-mode](https://github.com/iStarFire/jellyfin-children-mode) | none | 10.8 | C# scaffold | [V via gh, I] It never references `ISessionManager`. It is an unfinished scaffold that enforces nothing | No |
| [philips/jellykids](https://github.com/philips/jellykids) | none detected | API | Text-only kids PWA frontend | None. It relies on OS screen time [S] | Separate client |
| [JellyKids for iOS](https://forum.jellyfin.org/t-jellykids-a-simple-ui-for-jellyfin) | closed source | API | Kids client | Client-side filtering [S] | Separate client |
| [mattheys/JellyfinParentalProxy](https://github.com/mattheys/JellyfinParentalProxy) | ? | API | Reverse proxy | Rewrites responses to strip items above a rating, using TMDB ratings [S] | Yes, as a proxy |
| [sethantcliff-lgtm/Jellyfin-Parental-Ratings-Pro](https://github.com/sethantcliff-lgtm/Jellyfin-Parental-Ratings-Pro) | MIT | plugin | Normalises and fills ratings | Ratings only [S] | n/a |
| [ElfHosted JellyStreams](https://docs.elfhosted.com/guides/families/jellyfin-screen-time/) | commercial | hosted | Daily cap per profile, with a warning | Its docs describe a websocket Stop plus refusing new playback, calling it "cooperative" [S]. Not inspectable | Yes, per its docs |
| [n00bcodr/Jellyfin-Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced) | GPL-3.0 | 12.x | plugin plus web JS | No screen-time features. It applies the parental rating and tags to Seerr results [V] | n/a |

**Briefly, other servers:**
- **Emby** has Access Schedules but no daily quota, and doesn't cut off running playback ([docs](https://emby.media/support/articles/Parental-Controls.html), [staff thread](https://emby.media/community/topic/95212-time-limit-for-accounts/)) [S].
- **Emby's WatchingEye plugin** offers daily, weekly and other limits ([thread](https://emby.media/community/topic/141047-plugin-watchingeye-manage-viewers)). Its source link returned 404 [S].
- **Plex** has managed users with restriction profiles and PINs, but no time limits ([docs](https://support.plex.tv/articles/parental-controls/)) [S].

**Takeaways:**
- No project blocks PlaybackInfo, stream or HLS requests [V].
- None injects a kids UI into jellyfin-web, apart from macman's admin-only iframe [V].
- The proven approach is benben42's: count playback events, warn, then Stop repeatedly and kill the transcode. The external tools all change policy, which is intrusive and in irfadrazick's case destructive.
- The only reusable licensed sources are jelly-watch-wise and voc0der (MIT), and the GPL plugins.

---

## 3. Technical paths to support jellyfin-web (checked against the 12.2 source)

### 3.1 Server-side Stop and message commands through `ISessionManager` [V]

- **Which socket web uses.** Web 12.2 subscribes through the **@jellyfin/sdk websocket** (`W:src/lib/jellyfin-apiclient/ServerConnections.js:57`).
  - The SDK answers `ForceKeepAlive` and reconnects with backoff.
- **Messages web handles** (`W:src/scripts/serverNotifications.js:193-205`): only `Play`, `Playstate`, `GeneralCommand` and SyncPlay.
- **Playstate `Stop`** calls `playbackManager.stop()` on the *current* player (`serverNotifications.js:164-184`, `W:src/components/playback/playbackmanager.js:4067-4078`).
  - That covers html video, html audio and the Chromecast sender, which forwards Stop to the receiver (`W:src/plugins/chromecastPlayer/plugin.js:849`).
- **`DisplayMessage`:**
  - With `TimeoutMs` set, web shows a **toast**.
  - Without `TimeoutMs`, it shows a **modal alert** (`serverNotifications.js:20-27`). The server only adds `TimeoutMs` when one is given (`S:Emby.Server.Implementations/Session/SessionManager.cs:1289-1307`).
  - So "Time's up" should go **without** `TimeoutMs`, and the 5-minute warning **with** it.
- **`GoHome`** works in web (`inputManager.js:106-107`).
  - benben42 measured that Android TV 12 ignores it (its `REQUIREMENTS.md:54-71`).
- **The server sends commands blindly.** It doesn't check `SupportedCommands` (`SessionManager.cs:1310-1333, 1542-1559`).
  - Passing `controllingSessionId=null` skips `AssertCanControl`.
- **Policy changes don't reach an open web tab.**
  - Web 12.2 has no handler for `UserUpdated`, `UserDeleted` or `UserConfigurationUpdated`, even though the server pushes `UserUpdated` (`S:Jellyfin.Server.Implementations/Events/Consumers/Users/UserUpdatedNotifier.cs:33-39`).
- **Logout and `RevokeUserTokens`** send nothing to the client (`SessionManager.cs:1816-1879`).
  - Web then simply starts failing with 401s.
- **[I] Limits of this path:**
  - The kid can press play again straight away, so Stop only works as "stop again on every PlaybackStart or Progress event".
  - A Chromecast receiver only gets the Stop while the sender tab is open. Killing the transcode and the request gate are the backstops.

### 3.2 Blocking PlaybackInfo, HLS and stream requests with an MVC filter [V]

- **Plugins share the server's DI and MVC options.**
  - `ApplicationHost.Init` calls `RegisterServices` (`S:Emby.Server.Implementations/ApplicationHost.cs:490-492`) before `Startup.ConfigureServices` (`S:Jellyfin.Server/Program.cs:179-181`).
  - `AddMvc` is at `S:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:111-163`.
  - So `services.Configure<MvcOptions>(o => o.Filters.Add…)` from a plugin composes with the server's own options.
- **Jellyfin-Enhanced ships five global `IAsyncActionFilter`s this way** (`.reference/n00bcodr_Jellyfin-Enhanced/Jellyfin.Plugin.JellyfinEnhanced/PluginServiceRegistrator.cs:184-196`).

Endpoints to gate:

| Endpoint | Auth in 12.2 | Location |
|---|---|---|
| `Items/{id}/PlaybackInfo` (GET and POST) | `[Authorize]` | `MediaInfoController.cs:29,72,116` |
| `Videos`/`Audio` `master.m3u8`, `main.m3u8`, `live.m3u8`, `hls1/…` segments | `[Authorize]` (class) | `DynamicHlsController.cs:39,164,404,577,745,914,1086,1268` |
| `Audio/{id}/universal` (**web's music path, which skips PlaybackInfo**) | `[Authorize]` | `UniversalAudioController.cs:92-94`. Web builds it directly (`playbackmanager.js:310-324, 420-430`) |
| `Videos/{id}/stream[.{container}]` | **anonymous** | `VideosController.cs:314-318,552` |
| `Audio/{id}/stream` | **anonymous** | `AudioController.cs:88,252` |
| Legacy HLS audio segments | **anonymous** ("Can't require authentication just yet") | `HlsSegmentController.cs:53-56` |
| `Items/{id}/Download` | `Policies.Download` | `LibraryController.cs:669-670` |

How web shows a block:
- **PlaybackInfo with HTTP 403** gives a generic alert, "There was an error processing the request" (`playbackmanager.js:2436-2452`).
- **PlaybackInfo with HTTP 200 and `ErrorCode = NotAllowed`** gives "Playback of this media is not allowed." (`playbackmanager.js:637-655`, `en-us.json:1429-1430`, enum `S:MediaBrowser.Model/Dlna/PlaybackErrorCode.cs:6-22`).
- **[I]** So answer PlaybackInfo with 200 and `NotAllowed`, and answer streams and playlists with 403.
- Web direct-play URLs carry `ApiKey=<token>`. A filter can identify the kid on anonymous stream actions from the `ApiKey` or `deviceId` query.

Other levers:
- **`EnableMediaPlayback=false`** is not enforced on the server for streams (`BaseItem.cs:1128-1141`; it only blocks a remote `SendPlayCommand`, `SessionManager.cs:1394`).
  - Web checks it client-side in `W:src/plugins/playAccessValidation/plugin.js:28-40`, using the cached user, so it only takes effect after a reload.
- **Bypasses [I from V]:**
  - DLNA is now a separate plugin.
  - SyncPlay members each fetch PlaybackInfo, so they hit the gate. But a group `Unpause` can resume a session that is already open (`S:MediaBrowser.Controller/SyncPlay/GroupStates/WaitingGroupState.cs:211-241`).
  - Offline downloads are not covered.
- **Risk [I]:**
  - The filter must match controller and action names, which a minor release could rename. This matches the startup self-check in plan §8.
  - Robustness is high and intrusiveness low: removing the plugin removes the block.

### 3.3 Injecting UI into jellyfin-web [V]

- **File Transformation** (GPL-3.0):
  - It is an `IStartupFilter` middleware. It buffers registered `/web/*` responses, applies regex or callback transforms, falls back to the original on error, can serve virtual files, and sets an ETag with `no-cache` (`Infrastructure/FileTransformationStartupFilter.cs:16-25`, `FileTransformationMiddleware.cs:27-153`).
  - It uses no Harmony.
  - Other plugins call it by reflection over `AssemblyLoadContext.All` (`PluginInterface.cs:13-34`).
- **Jellyfin-Enhanced 12.x** injects `<script src="../JellyfinEnhanced/script?v=<key>">` through its own `ScriptInjectionStartupFilter` (`Services/ScriptInjectionStartupFilter.cs:11-50,163-172`).
  - Its fallback rewrites `index.html` on disk (`JellyfinEnhanced.cs:207-213,380-405`).
- **JavaScript Injector 12.x** uses the same pattern, falling back to File Transformation and then to a disk write (`Services/StartupService.cs:20-111`).
- **Robustness in practice** [V, gh issues]:
  - [file-transformation#81](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/issues/81) broke on 12.0 day one.
  - In [#87](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/issues/87) the author pins one ABI per release because "even a minor [update] can have changes to the web … that cause the plugin to break".
  - [#86](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/issues/86): pattern collisions between plugins.
  - [Jellyfin-Enhanced#634](https://github.com/n00bcodr/Jellyfin-Enhanced/issues/634): stale-chunk `ChunkLoadError`.
  - [#815](https://github.com/n00bcodr/Jellyfin-Enhanced/issues/815): its menu vanished on 12.0 when web's navigation moved.
  - [#931](https://github.com/n00bcodr/Jellyfin-Enhanced/issues/931): it doesn't run on webOS 6.
  - Core refused to adopt this: [jf#9095](https://github.com/jellyfin/jellyfin/pull/9095), a core file-transformation service, was closed unmerged.
- **The least fragile hook is web's own plugin loader, not DOM patching** [V]:
  - Each string in the `plugins` array of `config.json` that names a `window` key is loaded as `window[name]`, an async function returning a class (`W:src/components/pluginManager.js:63-95`).
  - The class is constructed with `{events, playbackManager, inputManager, toast, confirm, appRouter, ServerConnections, …}`.
  - A `PreplayIntercept` plugin runs before every play (`playbackmanager.js:2458-2476`). The built-in `W:src/plugins/stillWatching/plugin.ts` is a template.
  - `config.json` is fetched with `no-store` (`W:src/scripts/settings/webSettings.js:6-24`).
- **[I] A kids web layer would need three pieces:**
  1. an `IStartupFilter` that rewrites `/web/config.json` to add `"KidsPlugin"`;
  2. an injected `<script>` in `index.html` defining `window.KidsPlugin`;
  3. a PreplayIntercept that calls `GET /Kids/Status` and shows a Time's-up or parent-unlock dialog.
- **[I] Its limits:**
  - It only runs where the server-hosted web UI loads: browsers, Jellyfin Media Player and Desktop, the Android and iOS web wrappers, and webOS or Tizen when the engine is recent enough. Native clients don't run it.
  - It is advisory, since devtools or another client bypass it.
  - It runs for every user, so it must check kid status itself.
  - It contradicts the current non-goal in `docs/plan.md` §1.

### 3.4 What jellyfin-web offers natively [V]

- **Plugin pages are admin-only.**
  - `GET web/ConfigurationPages` requires elevation (`S:Jellyfin.Api/Controllers/DashboardController.cs:49-50`).
  - `EnableInMainMenu` only adds an entry to the *dashboard* drawer (`W:src/apps/dashboard/components/drawer/sections/PluginDrawerSection.tsx:14-39`).
  - `MenuSection` and `MenuIcon` are unused in web 12.2.
  - **Jellyfin has no page surface for non-admin users.**
- **`config.json` `menuLinks`** (`{name, icon, url}`, `W:src/types/webConfig.ts:8-21`) show for all users.
  - They appear in the main drawer and the top navigation (`MainDrawerContent.tsx:59-80`, `UserViewNav.tsx:64-83`) and open in a new tab.
  - They are global, not per user.
  - A plugin can only add one by rewriting the `/web/config.json` response, which an `IStartupFilter` can do because it runs before `UseStaticFiles` (`S:Jellyfin.Server/Startup.cs:202-229`).
- **Branding custom CSS** is a server-wide `<style>` for every user (`W:src/components/CustomCss.tsx:6-23`).
  - **Any user can turn it off** in display settings (`displaySettings.template.html:186-194`).
  - `EnableUserPreferenceAccess=false` doesn't hide that toggle; it only gates avatar and password (`userprofile.tsx:59`, `UserPasswordForm.tsx:45`).
  - It is useless for enforcement and weak for skinning.
- **Admin user pages:** `Profile.tsx` edits `IsDisabled` and `EnableMediaPlayback`, and `ParentalControl.tsx` edits `AccessSchedules`.
  - Our helper and recommended-policy features can link to these pages instead of duplicating them.

---

## 4. A native or upstream path

### 4.1 Natural contributions, ordered by how likely they are to be accepted [I, grounded in §1]

1. **Make parental refusals visible to clients.** This is a bug fix and needs no RFC.
   - The server sets `X-Application-Error-Code: ParentalControl` on schedule refusals. Web already reacts to that header (`W:…/appRouter.js:197-209`).
   - It is tracked in [jf#17740](https://github.com/jellyfin/jellyfin/issues/17740) and [web#4447](https://github.com/jellyfin/jellyfin-web/issues/4447), which is confirmed.
   - Two fixes are already open ([jf#17741](https://github.com/jellyfin/jellyfin/pull/17741), [jf#17772](https://github.com/jellyfin/jellyfin/pull/17772)). The best contribution is a careful *review and test* on the issue, not a third PR.
   - It also helps our plugin: a standard "not allowed now" signal that every client could learn.
2. **Stop active sessions when an AccessSchedule window closes.**
   - It is an obvious gap with no GitHub issue (§1.5 item 4), and it is small: a timer that reuses the inactive-session stop pattern at `S:…/SessionManager.cs:684-697`.
   - It closes the hole where an existing direct-play stream keeps going.
   - File a bug, then a PR.
3. **Have web refresh the current user on `UserUpdated`.**
   - Without it, policy changes are ignored until a reload (§3.1).
   - It is a small jellyfin-web bug or enhancement.
4. **A playback error code for policy refusals.** Today `NotAllowed` exists, but the server never sets it.
   - A standard, documented "PlaybackInfo returns `NotAllowed` when a policy or plugin refuses" would let plugin gates be understood by every client.
   - Possibly add an extension point, such as an `IPlaybackAuthorizer` interface that plugins implement. This matches the maintainers' preference for "core adds interfaces, plugins implement" (§1.1).
   - It needs a jellyfin-meta proposal.
5. **A daily watch-time limit in `UserPolicy`.** This is the most valuable piece and the hardest to land.
   - Fider demand exists (#699 and #2266, 55 votes combined) and nobody has rejected it.
   - It is blocked on core playback accounting: [meta #136](https://github.com/jellyfin/jellyfin-meta/discussions/136) (Shadowghost's playback history) and [meta #121](https://github.com/jellyfin/jellyfin-meta/discussions/121).
   - It would touch:
     - `MediaBrowser.Model/Users/UserPolicy.cs`;
     - the `User` entity plus an EF migration, or a new `PreferenceKind` with no migration (`Entities/Preference.cs`, the enum ends at `AllowedTags = 12`);
     - `UserManager.GetUserDto` and `UpdatePolicyAsync` (`:848-927`);
     - the gates on PlaybackInfo, HLS and UniversalAudio;
     - a stop loop;
     - the OpenAPI and SDK regeneration;
     - `ParentalControl.tsx` in web;
     - client UX in every app.
   - Pitch it as a consumer of #136, not standalone.
6. **Server-side profile PINs.** Maintainers named this as the condition for Android TV PINs ([atv#5518](https://github.com/jellyfin/jellyfin-androidtv/pull/5518)).
   - Two things count against it:
     - EasyPassword was removed for security ([jf#9820](https://github.com/jellyfin/jellyfin/pull/9820)), so a PIN that *authenticates* will likely be rejected.
     - Web's profile switching is waiting on a UI/UX redesign.
   - A PIN that only *authorises a profile switch on an already-authenticated device* is a different and plausible design. It fits [meta #90](https://github.com/jellyfin/jellyfin-meta/discussions/90) and [web discussion #7059](https://github.com/jellyfin/jellyfin-web/discussions/7059).
7. **A kids flag or kids mode in core.** It has the lowest chance of acceptance.
   - It has the most votes ([Fider #531](https://features.jellyfin.org/posts/531), 77) but no owner, and the UI/UX team would have to design it.
   - joshuaboniface opposes Netflix-style sub-profiles.
   - Keep it in the plugin and its clients.

### 4.2 What maintainers seem open to [V quotes, I synthesis]

- **Open:**
  - fixes to parental-control correctness: rating and tag leaks are merged quickly through 12.2;
  - server-side rather than client-only controls ("pins would need to be configurable server-side");
  - profile switching ("definitely a feature we are interested in supporting");
  - moving playback stats into core (#121, #136);
  - plugin extension interfaces.
- **Not open:**
  - LAN or easy PINs that authenticate;
  - client-only PINs on Android TV;
  - client-reported time zones for schedules;
  - feature requests filed as GitHub issues;
  - LLM-authored PRs or comments;
  - web features without UI/UX approval;
  - a core file-transformation service ([jf#9095](https://github.com/jellyfin/jellyfin/pull/9095)).

### 4.3 How to propose [V]

1. **Bugs** (items 1 to 3 in §4.1): a GitHub issue in the right repo, then a PR that references it.
2. **User-facing features** (watch-time limit, PIN for switching, kids mode): a Fider post, or upvote the existing ones and add a detailed comment: #699, #2266, #2544, #531.
3. **Server or multi-project features** (a policy field, an error code, an authorizer interface): a **jellyfin-meta discussion** first, getting a go-ahead from the server team and the UI/UX team when web is affected. Only then a PR.
4. Comply with the [LLM policy](https://jellyfin.org/docs/general/contributing/llm-policies/):
   - the human author must understand and explain every change;
   - no `.claude` files in commits;
   - no LLM-written issue or PR text.
   - This matters for us: three parental-control PRs were closed on these grounds in 2026.

---

## Recommendations for our plugin (ranked) [I]

1. **Build the server gate filter (feature 4, plan §3.3 A) and treat jellyfin-web as a first-class acceptance target.**
   - Use a global `IAsyncActionFilter` via `Configure<MvcOptions>` that matches these actions:
     - `MediaInfo.GetPlaybackInfo` and `GetPostedPlaybackInfo`;
     - the `DynamicHls` master and main playlists, plus `hls1` segments;
     - `UniversalAudio` (web's music path);
     - the anonymous `Videos` and `Audio` `stream` actions, identifying the kid by the `ApiKey` or `deviceId` query;
     - `Library.GetDownload`.
   - Answer PlaybackInfo with **200 and `PlaybackInfoResponse{ErrorCode = NotAllowed}`**, which jellyfin-web shows as "Playback of this media is not allowed." Answer the other actions with 403 and `{"kids":"timeUp"}`.
   - Keep the startup self-check against `IActionDescriptorCollectionProvider`.
   - This is the only path that holds for *every* client. No community project does it today.
2. **The enforcement loop, tuned for web.**
   - The 5- and 1-minute warnings go as `DisplayMessage` **with** `TimeoutMs` (a toast in web).
   - "Time's up" goes **without** `TimeoutMs` (a modal alert in web). Then send Stop with `controllingSessionId=null`.
   - Repeat on every PlaybackStart or Progress event while the kid is out of time, and `KillTranscodingJobs` after the grace period.
   - Also apply it when an AccessSchedule window closes, which Jellyfin itself doesn't do.
3. **Don't change policy, and don't revoke tokens.**
   - Web never refreshes policy live, Logout gives it no UX, and partial policy writes are destructive.
   - The other tools' experience (benben42's `LegacyBlockRestorer`) confirms this.
   - Keep the strict lock as an opt-in only.
4. **Add an optional, separately toggled jellyfin-web layer, only after items 1 and 2.**
   - Build it as a web *plugin* through `config.json` `plugins` and `window.KidsPlugin`, not by patching the DOM. Wire it with our own `IStartupFilter`; there is no need for a GPL File Transformation dependency.
   - Use a **PreplayIntercept** that checks `/Kids/Status` and shows a Time's up or "ask a parent" dialog with a parent-code entry that calls `/Kids/Verify`.
   - Optionally add a time-left toast on play.
   - It is UX polish over server enforcement, never the enforcement itself.
   - It needs a plan and constitution amendment, because §1 lists web injection as a non-goal.
   - Pin it to the tested web versions, and fail silently on others.
5. **Expose state through standards before custom UI.**
   - Have `GET /Kids/Status` documented in the contract for third-party clients.
   - Consider a `menuLinks` entry to a kid-safe status page served by the plugin. It would be per kid, under `IgnoreParentalControl`, and would need care with Principle IV.
6. **Guard against filtering leaks in the recommended-policy helper.** Warn about the open 12.2 bugs: [jf#18341](https://github.com/jellyfin/jellyfin/issues/18341), [jf#18227](https://github.com/jellyfin/jellyfin/issues/18227), [jf#12855](https://github.com/jellyfin/jellyfin/issues/12855) (artwork) and [jf#18331](https://github.com/jellyfin/jellyfin/issues/18331). Point admins to native settings instead of duplicating them (Principle I).
7. **Don't use branding CSS for a kids skin.** It is server-wide and each user can turn it off.

## What to propose upstream, and how [I]

1. **Now, as bug-level work and with no RFC:**
   - Test and review the ParentalControl error-code fixes ([jf#17741](https://github.com/jellyfin/jellyfin/pull/17741) / [jf#17772](https://github.com/jellyfin/jellyfin/pull/17772)) on the issue thread.
   - File a server issue: "active playback continues after the AccessSchedule window closes".
   - File a web issue: "web ignores `UserUpdated`, so policy changes need a reload".
   - All of these are human-written, small and well tested.
2. **Next, as a jellyfin-meta discussion:**
   - Propose a **"playback authorization" extension point**: a plugin interface consulted before PlaybackInfo, HLS and UniversalAudio, which returns a standard refusal code that clients display.
   - Frame it like the existing media-segment and lyrics provider interfaces.
   - It turns our MVC filter into a supported API and serves other plugins too (session-limit plugins, for example).
3. **Later, a jellyfin-meta discussion plus Fider:**
   - Propose a **daily watch-time allowance in `UserPolicy`**, built on Shadowghost's playback history ([meta #136](https://github.com/jellyfin/jellyfin-meta/discussions/136)).
   - Cite Fider [#699](https://features.jellyfin.org/posts/699) and [#2266](https://features.jellyfin.org/posts/2266), and offer our plugin as field evidence.
4. **Only if the UI/UX team engages:**
   - Propose a **server-side switch PIN**: a profile-switch authorisation that is *not* an authentication factor.
   - Offer it to [web discussion #7059](https://github.com/jellyfin/jellyfin-web/discussions/7059) and [meta #90](https://github.com/jellyfin/jellyfin-meta/discussions/90). It meets nielsvanvelzen's "server-side" condition.
5. **Don't propose:**
   - a core kids mode or flag;
   - LAN or easy PINs;
   - client-time schedules;
   - a core file-transformation service.
