# Jellyfin Kids

A Jellyfin 12.x server plugin for parent-controlled kids profiles. It backs the kids mode of
[Wholphin](https://github.com/damontecres/Wholphin) and other clients: kid profiles, a household parent
lock and screen time shared across devices, enforced on the server.

Status: early development. The plan is in [docs/plan.md](docs/plan.md).

## Development

Everything runs from the Nix devshell (`direnv allow`, or `nix develop`): the .NET 10 SDK, Jellyfin 12.x
from nixpkgs and process-compose.

```sh
dev          # start the dev loop (process-compose up)
dev-reset    # forget the dev server's state
```

The dev loop:

1. `plugin` builds the plugin and installs it in `.dev/data/plugins/` with a `meta.json` made from
   `build.yaml`.
2. `jellyfin` runs the server on <http://127.0.0.1:8096>, with its data, config, cache and logs under
   `.dev/`. It listens on localhost only; the Android emulator reaches it at `http://10.0.2.2:8096`.
3. `provision` finishes the startup wizard and creates the dev users: admin `admin` / `admin` and the
   kids `alice` and `bob` (`DEV_ADMIN_USER`, `DEV_ADMIN_PASSWORD` and `DEV_KIDS` change them). The admin
   token is written to `.dev/admin.token`.

Editing anything under `src/` (or `build.yaml`) rebuilds the plugin and restarts Jellyfin, since
Jellyfin only loads plugins at startup.

Tests:

```sh
dotnet test                # unit tests (validation, revisions and conflicts, access rules, storage, cleanup)
scripts/dev-check.sh       # checks against the running dev server: auth, 403/409/400 answers, cleanup, pages
```

The dashboard page is at Dashboard → Kids once you sign in as `admin`.

## API

Clients detect the plugin with the anonymous `GET /Kids/Info`. The rest of the API is described in
[specs/001-kids-plugin-foundation/contracts/kids-api-v1.md](specs/001-kids-plugin-foundation/contracts/kids-api-v1.md).

Upstream sources used as reference (Jellyfin and plugins) are cloned into `.reference/`, which is not
committed.

## License

MIT, see [LICENSE](LICENSE).
