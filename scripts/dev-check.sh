#!/usr/bin/env bash
# Check the Kids plugin against the running dev server (quickstart.md §2): prints PASS or FAIL per check and exits
# non-zero if any check failed. Needs the dev loop up and provisioned (admin token in $JELLYFIN_KIDS_DEV/admin.token,
# kid users alice and bob without passwords).
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
dev=${JELLYFIN_KIDS_DEV:-$root/.dev}
url=${JELLYFIN_URL:-http://127.0.0.1:8096}

client='MediaBrowser Client="jellyfin-kids-check", Device="dev", DeviceId="jellyfin-kids-check", Version="0.0.1"'
failures=0
status=
body=

# req <token|-> <method> <path> [json body]: sets $status and $body, never fails
req() {
  local token=$1 method=$2 path=$3 data=${4-}
  local auth=$client out
  [[ $token != - ]] && auth+=", Token=\"$token\""
  out=$(mktemp)
  status=$(curl --silent --output "$out" --write-out '%{http_code}' -X "$method" "$url$path" \
    -H "Authorization: $auth" -H 'Content-Type: application/json' ${data:+--data "$data"}) || status=000
  body=$(cat "$out")
  rm -f "$out"
}

# check <name> <expected status> [jq filter that must yield true on the body]
check() {
  local name=$1 expected=$2 filter=${3-}
  if [[ $status != "$expected" ]]; then
    echo "FAIL $name: expected $expected, got $status ${body:0:300}"
    failures=$((failures + 1))
  elif [[ -n $filter ]] && ! jq --exit-status "$filter" <<<"$body" >/dev/null 2>&1; then
    echo "FAIL $name: body does not match $filter: ${body:0:300}"
    failures=$((failures + 1))
  else
    echo "PASS $name"
  fi
}

# login <user> [password]: prints an access token
login() {
  req - POST /Users/AuthenticateByName "$(jq -n --arg n "$1" --arg p "${2-}" '{Username: $n, Pw: $p}')"
  [[ $status == 200 ]] || { echo "Cannot sign in as $1 ($status)" >&2; exit 1; }
  jq -r .AccessToken <<<"$body"
}

admin=$(cat "$dev/admin.token")

# Checks follow quickstart.md §2.

# a. Anonymous detection, with nothing household-specific
req - GET /Kids/Info
check "a Info is anonymous" 200 '.ApiVersion == 1 and (.Features | index("profiles")) != null'
if grep -Eiq '[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-' <<<"$body"; then
  echo "FAIL a Info leaks an id: $body"
  failures=$((failures + 1))
fi

# Profiles API (US3)
req "$admin" GET /Users
user_id() { jq -r --arg n "$1" '.[] | select(.Name == $n) | .Id' <<<"$body"; }
users=$body
alice_id=$(body=$users user_id alice)
bob_id=$(body=$users user_id bob)
[[ -n $alice_id && -n $bob_id ]] || { echo "The dev users alice and bob are missing; run the provision step" >&2; exit 1; }
alice=$(login alice)
# Validation errors are ProblemDetails, whose properties are always camelCase
errors='(.errors // .Errors)'

req "$admin" DELETE "/Kids/Users/$alice_id/Profile"
req "$admin" PUT "/Kids/Users/$bob_id/Profile" '{"Revision":0}'

req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":0}'
check "b admin marks alice" 200 '.Revision == 1 and .AutoplayCap == 3 and (.Sections | length) == 5 and .SearchAllowed == false'

req "$alice" GET "/Kids/Users/$alice_id/Profile"
check "c alice reads her profile" 200 '.Revision == 1'

req "$alice" GET "/Kids/Users/$bob_id/Profile"
check "d alice cannot read bob's profile" 403

req "$alice" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":1,"SearchAllowed":true}'
check "e alice cannot change her own profile" 403
req "$admin" GET "/Kids/Users/$alice_id/Profile"
check "e alice's profile is unchanged" 200 '.Revision == 1 and .SearchAllowed == false'

req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":1,"AutoplayEnabled":true,"AutoplayCap":2}'
check "f admin updates alice" 200 '.Revision == 2 and .AutoplayEnabled == true and .AutoplayCap == 2'
req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":1,"AutoplayCap":5}'
check "f a stale revision is a conflict with the current profile" 409 '.Revision == 2 and .AutoplayCap == 2'

req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":2,"AutoplayCap":11}'
check "g an autoplay limit of 11 is rejected" 400 "$errors | has(\"AutoplayCap\")"
req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":2,"Sections":[{"Section":"Shorts"}]}'
check "g an unknown section is rejected" 400 "$errors | has(\"Sections[0]\")"
req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"AutoplayCap":2}'
check "g a missing revision is rejected" 400 "$errors | has(\"Revision\")"

req "$admin" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":2,\"PickedItemIds\":[\"$(cat /proc/sys/kernel/random/uuid)\"]}"
check "h an unknown pick is rejected" 400 "$errors | has(\"PickedItemIds[0]\")"

req "$alice" GET /Kids/Profiles
check "i any signed-in user lists kids as ids and revisions" 200 \
  "(map(keys == [\"Revision\", \"UserId\"]) | all) and any(.[]; .UserId == \"$alice_id\")"

# j. Dashboard pages are served anonymously, so they must hold no household data
for page in Kids KidsJs KidsUser KidsUserJs; do
  req - GET "/web/ConfigurationPage?name=$page"
  check "j page $page is served" 200
  if grep -Eiq '[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-|alice|bob|"admin"' <<<"$body"; then
    echo "FAIL j page $page contains household data"
    failures=$((failures + 1))
  fi
done

# k. Deleting a kid's Jellyfin user removes their profile
req "$admin" GET /Users
tmp_id=$(jq -r '.[] | select(.Name == "tmpkid") | .Id' <<<"$body")
[[ -n $tmp_id ]] && req "$admin" DELETE "/Users/$tmp_id"
req "$admin" POST /Users/New '{"Name":"tmpkid"}'
tmp_id=$(jq -r .Id <<<"$body")
req "$admin" PUT "/Kids/Users/$tmp_id/Profile" '{"Revision":0}'
check "k admin marks tmpkid" 200
req "$admin" DELETE "/Users/$tmp_id"
check "k admin deletes tmpkid" 204
req "$admin" GET /Kids/Profiles
check "k tmpkid's profile is gone" 200 "all(.[]; .UserId != \"$tmp_id\")"

# l. The picks chooser is for administrators only
req "$alice" GET /Kids/Admin/PickCandidates
check "l alice cannot list pick candidates" 403
req "$admin" GET '/Kids/Admin/PickCandidates?limit=5'
check "l admin lists pick candidates" 200 'type == "array"'

# m. Reads keep working outside allowed hours (FR-014): give alice a schedule that excludes the current hour, then
# always put her policy back
req "$admin" GET "/Users/$alice_id"
alice_policy=$(jq -c .Policy <<<"$body")
restore_alice() { req "$admin" POST "/Users/$alice_id/Policy" "$alice_policy"; }
trap restore_alice EXIT
hour=$((10#$(date +%H)))
start=$(((hour + 2) % 24))
((start == 23)) && start=1
req "$admin" POST "/Users/$alice_id/Policy" \
  "$(jq -c --argjson s "$start" '.AccessSchedules = [{DayOfWeek: "Everyday", StartHour: $s, EndHour: ($s + 1)}]' <<<"$alice_policy")"
check "m admin sets a schedule that excludes now" 204
req "$alice" GET /Users/Me
if [[ $status == 200 ]]; then
  echo "FAIL m the schedule is not active: /Users/Me answered 200"
  failures=$((failures + 1))
else
  echo "PASS m the schedule is active (/Users/Me answered $status)"
fi
req "$alice" GET "/Kids/Users/$alice_id/Profile"
check "m alice reads her profile outside allowed hours" 200
req "$alice" GET /Kids/Profiles
check "m alice lists kids outside allowed hours" 200
restore_alice
check "m alice's policy is restored" 204
trap - EXIT

if ((failures > 0)); then
  echo "$failures check(s) failed"
  exit 1
fi
echo "All checks passed"
