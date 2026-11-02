<!--
Sync Impact Report
- Version change: 1.0.2 → 1.0.3 (PATCH: wording clarification)
- Modified sections: Technical Constraints. "Plugin state in a SQLite database under the plugin's data folder" becomes
  "under the server's data folder (`<data dir>/data/kids`)". Reason: feature 14 moves kids.db out of
  `BasePlugin.DataFolderPath` (`plugins/Jellyfin.Plugin.Kids`), which looks like a stray plugin install and was nearly
  deleted; the server's `DataPath` is where Jellyfin and other plugins keep databases (specs/014-kids-db-location
  research R1–R6). The note that plugin data is not in Jellyfin's backups stays true.
- Modified principles: none
- Added sections: none
- Removed sections: none
- Templates: plan/spec/tasks templates read this file at runtime; no template edits required
- Follow-up: docs/plan.md §2.1, §2.6 and §6.2 updated in the same change
- Deferred TODOs: none
Previous (1.0.1 → 1.0.2): II gained "the only way to lower a day's usage is a parent's explicit Reset" (feature 7).
-->
# Jellyfin Kids Constitution

## Core Principles

### I. Leverage Jellyfin, Don't Duplicate It

- Content restrictions (libraries, maximum parental rating, unrated items, tags) and allowed hours
  (`AccessSchedules`) are Jellyfin's job. The plugin MUST NOT reimplement them; it may only help
  admins set them (for example a recommended policy with a preview of the change).
- Every feature MUST first look for a Jellyfin mechanism to build on (session events and commands,
  user manager, authorization policies, scheduled tasks, the dashboard page system) and cite it in
  its plan. New mechanisms are added only for what Jellyfin does not do: kid profiles, the household
  parent lock, screen-time allowances and their enforcement.

Rationale: native restrictions already apply to every client; a parallel copy would drift and leak.

### II. The Server Decides

- Rules that matter MUST be decided and enforced on the server, so they hold for every client,
  not only Wholphin: counting screen time from server playback events, stopping playback, refusing
  new playback when time is up.
- A kid's own token MUST never be enough to change kids settings, the parent lock or screen time,
  including its own. Changes require an admin (or API key) or a short-lived parent grant.
- Client reports may only add usage, never remove it. The only way to lower a day's usage is a parent's
  explicit Reset (admin, API key or parent grant), which is logged.
- Where the server cannot enforce something (for example a direct-play stream already in
  progress), the limit MUST be documented and mitigated, not hidden.

Rationale: a limit that a kid can bypass by switching apps or editing a request is not a limit.

### III. Parents Are Never Locked Out

- Admins and API keys MUST never be blocked by the plugin.
- Parent unlock, status and profile reads MUST work outside the kid's allowed hours (use
  `Policies.IgnoreParentalControl` rather than plain `[Authorize]` for them).
- Changes to admin-owned state (user policies, schedules) MUST be small, re-read before writing,
  opt-in where intrusive, journaled, and restorable on startup and on uninstall.
- Disabling or removing the plugin MUST remove all of its blocking.

Rationale: a parental tool that can brick a household's server, or lock a parent out at bedtime,
will be uninstalled.

### IV. Secrets Stay Secret

- The parent lock is stored only as a PBKDF2 verifier, compatible with the app's
  `pbkdf2-sha256$iter$saltHex$hashHex` format, and compared in constant time.
- Wrong attempts MUST be rate limited per user and device. Verification answers MUST NOT use HTTP
  401, which clients treat as an expired session.
- Parent grants are random, short-lived, bound to the device and kept only in memory.
- Nothing secret or per-household may appear in anonymous responses or in the dashboard page HTML,
  which Jellyfin serves without authentication.
- No telemetry and no third-party services: all data stays on the server.

Rationale: the TV in the living room is shared with the people the lock is meant to stop.

### V. A Stable, Client-Agnostic Contract

- The client API lives under `/Kids`, is detected through the anonymous `GET /Kids/Info`, and is
  versioned (`apiVersion`). Breaking changes raise the API version; old versions are kept or
  refused explicitly, never changed silently.
- The contract MUST be usable by any Jellyfin client, with Wholphin as the reference client.
  Clients MUST keep working without kids features when the plugin is absent.
- The plugin MUST load on every Jellyfin 12.x release from 12.0 (`targetAbi 12.0.0.0`, compiled
  against the 12.0.0 packages). Anything that depends on Jellyfin internals, such as controller or
  action names, MUST be checked at startup and logged if missing.

Rationale: households mix clients and update servers on their own schedule.

### VI. Tested Where It Matters

- Pure logic (allowance arithmetic and day boundaries, verifier compatibility, grants and
  throttling, revision conflicts) MUST have automated tests.
- Behaviour that depends on a running server (authorization, playback events, stop commands,
  blocking filters) MUST be verified against the dev server from the process-compose loop, and
  risky mechanisms MUST be proven by a spike before features depend on them.
- The build MUST stay free of analyzer warnings (`TreatWarningsAsErrors`).

Rationale: enforcement bugs either fail open (useless) or fail closed (locks families out).

## Technical Constraints

- .NET 10 (`net10.0`); `Jellyfin.Controller` and `Jellyfin.Model` 12.0.0 with runtime assets
  excluded; analyzers configured by `.editorconfig`.
- Plugin state in a SQLite database under the server's data folder (`<data dir>/data/kids`; `Microsoft.Data.Sqlite`, shipped
  with the server). The plugin XML configuration holds only small admin settings. Plugin data is not
  part of Jellyfin's system backups, so export and import MUST be provided before a stable release.
- Licence: MIT. Code may be copied only from compatibly licensed sources (for example MIT plugins
  such as Trakt or Reports), keeping their notices. GPL-licensed and unlicensed plugins may be
  studied for patterns but not copied.
- Tooling: the Nix flake devshell (blueprint) and the process-compose dev loop are the supported way
  to build, run and test.

## Development Workflow

- Each feature goes through spec-kit (`/speckit-specify`, `/speckit-plan`, `/speckit-tasks`,
  `/speckit-implement`), using `docs/plan.md` as the research input and keeping it up to date when
  findings change.
- Every plan MUST include a constitution check against these principles, and justify any exception.
- Commits are signed and describe only the change, with no AI attribution.

## Governance

This constitution supersedes other practices for this repository. Amendments are made through
`/speckit-constitution`, recorded in the Sync Impact Report, and versioned semantically: MAJOR for
removing or redefining a principle, MINOR for a new principle or materially expanded guidance,
PATCH for clarifications. Reviews of plans and changes verify compliance with the principles above.

**Version**: 1.0.3 | **Ratified**: 2026-10-17 | **Last Amended**: 2026-10-29
