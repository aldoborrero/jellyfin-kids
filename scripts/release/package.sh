#!/usr/bin/env bash
# Build the plugin and package it for a Jellyfin plugin repository, the way jprm does:
#
#   <out>/kids_<version>.zip         the plugin dll(s) and meta.json at the root of the zip;
#                                    Jellyfin extracts it as-is into plugins/Kids_<version>/
#   <out>/kids_<version>.zip.md5sum  the MD5 Jellyfin checks the download against
#   <out>/manifest.json              the repository manifest with this version prepended
#
# Usage: scripts/release/package.sh [--tag vX.Y.Z.W] [--manifest FILE] [--source-url URL] [--out DIR]
#
#   --tag         fail unless the tag matches the version in build.yaml and Directory.Build.props
#   --manifest    the published manifest.json to add this version to (a missing file starts a new one)
#   --source-url  where the zip will be downloaded from (default: the GitHub release asset)
#   --out         output directory (default: artifacts/)
#
# Needs: dotnet, jq, yq (mikefarah/yq v4), zip, unzip, md5sum.
# Set SOURCE_DATE_EPOCH to pin the zip's file times and the manifest timestamp.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
tag='' manifest_in='' source_url='' out="$root/artifacts"

while (($#)); do
  case $1 in
    --tag) tag=$2; shift 2 ;;
    --manifest) manifest_in=$2; shift 2 ;;
    --source-url) source_url=$2; shift 2 ;;
    --out) out=$2; shift 2 ;;
    -h | --help) sed -n '2,/^set -euo/{/^set -euo/d;s/^# \{0,1\}//;p}' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

die() { echo "error: $*" >&2; exit 1; }
for tool in dotnet jq yq zip unzip md5sum; do
  command -v "$tool" >/dev/null || die "$tool not found on PATH"
done

# build.yaml as JSON, with the trailing newline of folded (>) scalars trimmed
cfg=$(yq -o=json '.' "$root/build.yaml" | jq 'with_entries(.value |= if type == "string" then rtrimstr("\n") else . end)')
get() { jq -r --arg k "$1" '.[$k] // ""' <<<"$cfg"; }

name=$(get name)
version=$(get version)
framework=$(get framework)
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "build.yaml version '$version' is not four-part (X.Y.Z.W)"

# Directory.Build.props carries the assembly version; every one it sets has to agree with build.yaml
for prop in Version AssemblyVersion FileVersion; do
  value=$(sed -n "s|.*<$prop>\(.*\)</$prop>.*|\1|p" "$root/Directory.Build.props")
  [[ -z $value || $value == "$version" ]] || die "Directory.Build.props $prop is $value, build.yaml version is $version"
done
if [[ -n $tag && $tag != "v$version" ]]; then
  die "tag $tag does not match build.yaml version $version (expected v$version)"
fi

slug=$(tr '[:upper:]' '[:lower:]' <<<"$name" | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g')
zip_name="${slug}_${version}.zip"
repo=${GITHUB_REPOSITORY:-aldoborrero/jellyfin-kids}
source_url=${source_url:-"https://github.com/$repo/releases/download/v$version/$zip_name"}
epoch=${SOURCE_DATE_EPOCH:-$(date +%s)}
timestamp=$(date -u -d "@$epoch" +%Y-%m-%dT%H:%M:%SZ)

mkdir -p "$out"
out=$(cd "$out" && pwd)
publish="$out/publish"
stage="$out/stage"
rm -rf "$publish" "$stage" "${out:?}/$zip_name" "$out/$zip_name.md5sum"

echo "==> Building $name $version ($framework, Release)"
dotnet publish "$root/src/Jellyfin.Plugin.Kids/Jellyfin.Plugin.Kids.csproj" \
  --configuration Release --framework "$framework" --output "$publish" --nologo --verbosity quiet

echo "==> Packaging $zip_name"
mkdir -p "$stage"
mapfile -t artifacts < <(jq -r '.artifacts[]' <<<"$cfg")
((${#artifacts[@]})) || die "build.yaml lists no artifacts"
for artifact in "${artifacts[@]}"; do
  [[ -f "$publish/$artifact" ]] || die "artifact $artifact not found in the build output"
  cp "$publish/$artifact" "$stage/"
done

# meta.json, the same fields jprm writes; Jellyfin reconciles it with the repository entry on install
jq -S --arg version "$version" --arg timestamp "$timestamp" \
  '{guid, name, description, overview, owner, category, changelog, targetAbi,
    version: $version, timestamp: $timestamp}
   + (if .imageUrl then {imageUrl} else {} end)' <<<"$cfg" >"$stage/meta.json"

find "$stage" -exec touch -d "@$epoch" {} +
(cd "$stage" && zip -X -q -D "$out/$zip_name" ./*)
checksum=$(md5sum "$out/$zip_name" | cut -d' ' -f1)
printf '%s *%s\n' "$checksum" "$zip_name" >"$out/$zip_name.md5sum"

# Jellyfin extracts the zip straight into the plugin folder, so everything must sit at its root
listing=$(unzip -Z1 "$out/$zip_name")
for f in meta.json "${artifacts[@]}"; do
  grep -qxF "$f" <<<"$listing" || die "$f missing from the root of $zip_name"
done

echo "==> Writing manifest.json"
entry=$(jq -n --arg version "$version" --arg changelog "$(get changelog)" --arg targetAbi "$(get targetAbi)" \
  --arg sourceUrl "$source_url" --arg checksum "$checksum" --arg timestamp "$timestamp" \
  '{version: $version, changelog: $changelog, targetAbi: $targetAbi,
    sourceUrl: $sourceUrl, checksum: $checksum, timestamp: $timestamp}')
plugin=$(jq '{guid, name, description, overview, owner, category} + (if .imageUrl then {imageUrl} else {} end)' <<<"$cfg")

existing='[]'
if [[ -n $manifest_in && -s $manifest_in ]]; then
  existing=$(cat "$manifest_in")
fi

# Refresh the plugin's metadata from build.yaml and prepend this version, replacing a previous
# upload of the same version; other plugins and older versions are kept as they are.
jq --argjson plugin "$plugin" --argjson entry "$entry" '
  (map(select(.guid == $plugin.guid)) | first // {}) as $current
  | [$plugin + {versions: ([$entry] + (($current.versions // []) | map(select(.version != $entry.version))))}]
    + map(select(.guid != $plugin.guid))
' <<<"$existing" >"$out/manifest.json"

# The checks Jellyfin's repository parser and installer depend on
jq -e '
  def fourpart: test("^[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$");
  type == "array" and length > 0 and all(.[];
    (.guid | test("^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$"))
    and (.name | length > 0)
    and (.versions | length > 0)
    and ([.versions[].version] | length == (unique | length))
    and all(.versions[];
      (.version | fourpart) and (.targetAbi | fourpart)
      and (.sourceUrl | startswith("https://"))
      and (.checksum | test("^[0-9a-fA-F]{32}$"))
      and (.timestamp | test("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:]{8}Z$"))))
' "$out/manifest.json" >/dev/null || die "manifest.json failed validation"

rm -rf "$publish" "$stage"

echo
echo "zip:      $out/$zip_name"
unzip -l "$out/$zip_name" | sed 's/^/          /'
echo "md5:      $checksum"
echo "manifest: $out/manifest.json ($(jq '.[0].versions | length' "$out/manifest.json") version(s))"

if [[ -n ${GITHUB_OUTPUT:-} ]]; then
  {
    echo "version=$version"
    echo "zip=$out/$zip_name"
    echo "md5sum=$out/$zip_name.md5sum"
    echo "checksum=$checksum"
    echo "manifest=$out/manifest.json"
  } >>"$GITHUB_OUTPUT"
fi
