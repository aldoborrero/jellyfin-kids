# Installing Jellyfin Kids

Jellyfin Kids is published as a Jellyfin plugin repository. Once your server knows the repository, it
lists the plugin in its catalog and keeps it updated like any other plugin.

Requires Jellyfin 12.0 or later.

## Add the repository

1. In Jellyfin, open **Dashboard → Plugins** and select **Manage Repositories**.
2. Select **New Repository** (the **+** button) and enter:
   - **Repository Name**: `Jellyfin Kids`
   - **Repository URL**: `https://raw.githubusercontent.com/aldoborrero/jellyfin-kids/gh-pages/manifest.json`
3. Save.

## Install the plugin

1. Back in **Dashboard → Plugins**, filter on **Available** and search for **Kids**.
2. Open it and select **Install**, then confirm.
3. Restart Jellyfin (**Dashboard → Restart**, or restart the service or container). Jellyfin only
   loads plugins at startup.
4. After the restart, **Kids** shows under **Installed** with status Active, and its settings page
   appears in the dashboard.

## Update

Jellyfin's **Update Plugins** scheduled task installs new versions from the repository on its own,
for plugins with automatic updates enabled (the default). To update by hand, open the plugin under
**Dashboard → Plugins**, pick the newer version under **Revision History** and select **Install**.
Either way, restart Jellyfin to load the new version.

## Your data

The plugin keeps kid profiles, the parent code (as a verifier only), screen time, bonuses, live TV channels and
picks in one SQLite database, in a `kids` folder inside Jellyfin's data folder, next to Jellyfin's own `jellyfin.db`:

| Install | Folder |
|---|---|
| Debian, Ubuntu (`.deb`) | `/var/lib/jellyfin/data/kids/` |
| Official Docker image | `/config/data/kids/` inside the container (in the volume you mount on `/config`) |
| Windows | `%ProgramData%\Jellyfin\Server\data\kids\` |
| Any other | `<data dir>/data/kids/`, where `<data dir>` is Jellyfin's `--datadir` |

**Dashboard → Kids** shows the exact folder at the bottom of the page, under **Data**.

- **Don't delete this folder.** It is not a leftover: deleting it loses every kid's settings. A `README.txt` in it says
  so. Updating and uninstalling the plugin keep it on purpose, so a reinstall finds your kids again.
- **Back it up.** Jellyfin's own backup (**Dashboard → Backups**) does not include any plugin's data. Copy the folder
  with Jellyfin stopped, or include it in a backup of Jellyfin's whole data directory, as Jellyfin's
  [backup guide](https://jellyfin.org/docs/general/administration/backup-and-restore/) recommends anyway. With
  `sqlite3` installed you can also copy the database while Jellyfin runs:
  `sqlite3 /var/lib/jellyfin/data/kids/kids.db ".backup kids-backup.db"`.
- **Restore** by stopping Jellyfin and putting `kids.db` back in the folder (without old `kids.db-wal` or `kids.db-shm`
  files).
- **Remove the data for good** by uninstalling the plugin, stopping Jellyfin and deleting the folder.

### Moving from 0.0.9.0 and earlier

Up to 0.0.9.0 the database lived in `plugins/Jellyfin.Plugin.Kids/` (for example
`/var/lib/jellyfin/plugins/Jellyfin.Plugin.Kids/kids.db`), beside the plugin's install folder `plugins/Kids_<version>/`,
where it looked like a stray old install. On its first start, 0.0.10.0 moves it:

1. It copies the database, including any changes not yet merged from `kids.db-wal`, to `data/kids/kids.db.migrating`.
2. It checks the copy (SQLite's integrity check, the same schema version and the same rows in every table) and only
   then renames it to `data/kids/kids.db`.
3. It renames the old file to `kids.db.migrated-<date>` and leaves a `README.txt` in the old folder. That copy is kept
   for one release; you may delete the old folder once Dashboard → Kids shows your kids.

Each step is logged. If the server stops halfway, the next start picks up where it left off: the old database is only
renamed after the new one is complete and checked. If the old database can't be read (locked by another program, or
damaged), nothing is moved or created, kids data is unavailable and the log says why; fix it and restart Jellyfin.

If both folders hold a database, the plugin uses the one written later and keeps the other under
`kids.db.older-<date-time>`. When it can't tell (written at the same time, or the later one is empty while the other
has kids), it stops with an error naming both files: keep the one you want, move the other out of its folder, and
restart.

**Downgrading** to 0.0.9.0 after the move starts with no kids, because 0.0.9.0 looks in the old folder. To go back,
stop Jellyfin and copy `data/kids/kids.db` to `plugins/Jellyfin.Plugin.Kids/kids.db` first.

## Manual install

Every version is also attached to its [GitHub release](https://github.com/aldoborrero/jellyfin-kids/releases)
as `kids_<version>.zip`. To install without the repository, stop Jellyfin, unzip it into a new folder
`plugins/Kids_<version>/` in the Jellyfin data directory, and start Jellyfin again.

## Releasing

For the maintainer. Pushing a version tag runs [`release.yml`](../.github/workflows/release.yml),
which tests and packages the plugin, creates the GitHub release with the zip, and prepends the version to
`manifest.json` on the `gh-pages` branch.

Jellyfin reads the manifest straight from the branch through `raw.githubusercontent.com`, so GitHub
Pages is not needed.

For each release:

1. Bump the version, in four parts (`X.Y.Z.W`), to the same value in `build.yaml` (`version`) and in
   `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`). Write the release notes in
   `changelog` in `build.yaml`; they become the release notes and the catalog changelog. Raise
   `targetAbi` only when the plugin needs a newer Jellyfin.
2. Commit, then tag and push the tag:

   ```sh
   git tag -a v0.0.3.0 -m "Kids 0.0.3.0"
   git push origin v0.0.3.0
   ```

   The workflow refuses a tag that doesn't match both files.

To build and inspect a release locally first, without GitHub:

```sh
nix develop -c nix shell nixpkgs#yq-go -c scripts/release/package.sh --tag v0.0.3.0
```

It writes `artifacts/kids_<version>.zip`, its `.md5sum`, and `artifacts/manifest.json`. Pass
`--manifest <file>` to add the version to an existing manifest, for example a copy of the published one.
