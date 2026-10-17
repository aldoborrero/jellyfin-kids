#!/usr/bin/env bash
# Build the plugin and install it in the dev server's plugins folder.
# Jellyfin only loads plugins at startup, so the dev loop restarts it afterwards.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
dev=${JELLYFIN_KIDS_DEV:-$root/.dev}

# Single-line scalars from build.yaml, the same metadata jprm packages
meta() { sed -n "s/^$1: *\"\{0,1\}\([^\"]*\)\"\{0,1\} *$/\1/p" "$root/build.yaml"; }
name=$(meta name)
version=$(meta version)

out="$dev/build"
dest="$dev/data/plugins/${name}_${version}"

dotnet build "$root/src/Jellyfin.Plugin.Kids" --configuration Debug --output "$out" --nologo --verbosity quiet

mkdir -p "$dest"
cp "$out/Jellyfin.Plugin.Kids.dll" "$out/Jellyfin.Plugin.Kids.pdb" "$dest/"
# Rewritten on every deploy, so a build that failed to load (status Malfunctioned) is retried
jq -n \
  --arg guid "$(meta guid)" --arg name "$name" --arg version "$version" \
  --arg targetAbi "$(meta targetAbi)" --arg overview "$(meta overview)" \
  --arg owner "$(meta owner)" --arg category "$(meta category)" \
  --arg timestamp "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{guid: $guid, name: $name, version: $version, targetAbi: $targetAbi, overview: $overview,
    owner: $owner, category: $category, description: $overview, changelog: "",
    timestamp: $timestamp, status: "Active", autoUpdate: false, assemblies: []}' \
  >"$dest/meta.json"
echo "Installed $name $version in $dest"
