# Installing Jellyfin Kids

Jellyfin Kids is published as a Jellyfin plugin repository. Once your server knows the repository, it
lists the plugin in its catalog and keeps it updated like any other plugin.

Requires Jellyfin 12.0 or later.

## Add the repository

1. In Jellyfin, open **Dashboard → Plugins** and select **Manage Repositories**.
2. Select **New Repository** (the **+** button) and enter:
   - **Repository Name**: `Jellyfin Kids`
   - **Repository URL**: `https://aldoborrero.github.io/jellyfin-kids/manifest.json`
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

## Manual install

Every version is also attached to its [GitHub release](https://github.com/aldoborrero/jellyfin-kids/releases)
as `kids_<version>.zip`. To install without the repository, stop Jellyfin, unzip it into a new folder
`plugins/Kids_<version>/` in the Jellyfin data directory, and start Jellyfin again.

## Releasing

For the maintainer. Pushing a version tag runs [`release.yml`](../.github/workflows/release.yml),
which tests and packages the plugin, creates the GitHub release with the zip, and prepends the version to
`manifest.json` on the `gh-pages` branch.

One-time setup: once the first release has created the `gh-pages` branch, enable Pages in
**Settings → Pages** with **Source: Deploy from a branch**, branch `gh-pages`, folder `/ (root)`.

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
