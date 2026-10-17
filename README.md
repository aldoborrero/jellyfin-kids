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

```sh
curl http://127.0.0.1:8096/Kids/Info
curl -H "Authorization: MediaBrowser Client=\"dev\", Device=\"dev\", DeviceId=\"dev\", Version=\"0\", Token=\"$(cat .dev/admin.token)\"" \
  http://127.0.0.1:8096/Plugins
```

Upstream sources used as reference (Jellyfin and plugins) are cloned into `.reference/`, which is not
committed.

## License

MIT, see [LICENSE](LICENSE).
