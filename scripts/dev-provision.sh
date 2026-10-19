#!/usr/bin/env bash
# Set up a fresh dev server: finish the startup wizard, sign in as the dev admin and create test kid users.
# Safe to run again; it only does what is missing. Writes the admin access token to $JELLYFIN_KIDS_DEV/admin.token.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
dev=${JELLYFIN_KIDS_DEV:-$root/.dev}
url=${JELLYFIN_URL:-http://127.0.0.1:8096}
admin_user=${DEV_ADMIN_USER:-admin}
admin_password=${DEV_ADMIN_PASSWORD:-admin}
kids=(${DEV_KIDS:-alice bob})

client='MediaBrowser Client="jellyfin-kids-dev", Device="dev", DeviceId="jellyfin-kids-dev", Version="0.0.1"'
api() {
  local method=$1 path=$2
  shift 2
  curl --fail-with-body --silent --show-error -X "$method" "$url$path" \
    -H "Authorization: $client${token:+, Token=\"$token\"}" -H 'Content-Type: application/json' "$@"
}
token=

# While starting, Jellyfin answers every path with a status page instead of the API
until curl --silent --fail "$url/System/Info/Public" | jq --exit-status .Version >/dev/null 2>&1; do sleep 1; done

if [[ $(api GET /System/Info/Public | jq -r .StartupWizardCompleted) != true ]]; then
  echo "Completing the startup wizard"
  api POST /Startup/Configuration -d '{"ServerName":"jellyfin-kids-dev","UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
  api GET /Startup/User >/dev/null
  api POST /Startup/User -d "$(jq -n --arg n "$admin_user" --arg p "$admin_password" '{Name: $n, Password: $p}')"
  api POST /Startup/RemoteAccess -d '{"EnableRemoteAccess":false}'
  api POST /Startup/Complete
fi

token=$(api POST /Users/AuthenticateByName \
  -d "$(jq -n --arg n "$admin_user" --arg p "$admin_password" '{Username: $n, Pw: $p}')" | jq -r .AccessToken)
umask 077
echo "$token" >"$dev/admin.token"

existing=$(api GET /Users | jq -r '.[].Name')
for kid in "${kids[@]}"; do
  if ! grep -qx "$kid" <<<"$existing"; then
    echo "Creating kid user $kid"
    api POST /Users/New -d "$(jq -n --arg n "$kid" '{Name: $n}')" >/dev/null
  fi
done

# A "Kids" movie library with a generated 12-minute clip, for the playback checks (feature 3 quickstart). The name must
# not end in "-clip", "-trailer" and the like, which Jellyfin takes for extras and hides
media="$dev/media/kids"
clip="$media/Test Pattern (2026).mkv"
mkdir -p "$media"
if [[ ! -f $clip ]]; then
  command -v ffmpeg >/dev/null || { echo "ffmpeg is not on PATH; run this from the devshell" >&2; exit 1; }
  echo "Generating $clip"
  ffmpeg -loglevel error -y -f lavfi -i testsrc=size=320x240:rate=10 -f lavfi -i sine=frequency=440 -t 720 \
    -c:v libx264 -preset ultrafast -c:a aac "$clip"
  generated=1
fi

if ! api GET /Library/VirtualFolders | jq --exit-status 'any(.[]; .Name == "Kids")' >/dev/null; then
  echo "Creating the Kids library"
  api POST "/Library/VirtualFolders?name=Kids&collectionType=movies&paths=$(jq -rn --arg p "$media" '$p | @uri')&refreshLibrary=true" \
    -d '{}' >/dev/null
elif [[ -n ${generated-} ]]; then
  api POST /Library/Refresh
fi

clip_query='/Items?Recursive=true&IncludeItemTypes=Movie&SearchTerm=Test%20Pattern&Fields=RunTimeTicks'
for ((waited = 0; ; waited += 2)); do
  api GET "$clip_query" | jq --exit-status '.Items | any(.[]; .RunTimeTicks > 0)' >/dev/null && break
  ((waited < 120)) || { echo "The test clip did not show up in the Kids library within 120 s" >&2; exit 1; }
  sleep 2
done

echo "Dev server ready at $url (admin: $admin_user / $admin_password, kids: ${kids[*]})"
