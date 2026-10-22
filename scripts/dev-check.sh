#!/usr/bin/env bash
# Check the Kids plugin against the running dev server (quickstart.md §2): prints PASS or FAIL per check and exits
# non-zero if any check failed. Needs the dev loop up and provisioned (admin token in $JELLYFIN_KIDS_DEV/admin.token,
# kid users alice and bob without passwords).
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
dev=${JELLYFIN_KIDS_DEV:-$root/.dev}
url=${JELLYFIN_URL:-http://127.0.0.1:${JELLYFIN_KIDS_PORT:-8096}}

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

# req_dev <token|-> <deviceId> <method> <path> [json body] [grant]: like req, but the Authorization header claims the
# given DeviceId, and an optional parent grant goes in X-Kids-Grant
req_dev() {
  local token=$1 device=$2 method=$3 path=$4 data=${5-} grant=${6-}
  local auth="MediaBrowser Client=\"jellyfin-kids-check\", Device=\"dev\", DeviceId=\"$device\", Version=\"0.0.1\"" out
  [[ $token != - ]] && auth+=", Token=\"$token\""
  local headers=(-H "Authorization: $auth" -H 'Content-Type: application/json')
  [[ -n $grant ]] && headers+=(-H "X-Kids-Grant: $grant")
  out=$(mktemp)
  status=$(curl --silent --output "$out" --write-out '%{http_code}' -X "$method" "$url$path" "${headers[@]}" \
    ${data:+--data "$data"}) || status=000
  body=$(cat "$out")
  rm -f "$out"
}

# login_as <user> <deviceId> [password]: signs in as that device and prints the access token. Jellyfin registers
# the device with the token, which is what the plugin trusts, whatever later headers claim
login_as() {
  req_dev - "$2" POST /Users/AuthenticateByName "$(jq -n --arg n "$1" --arg p "${3-}" '{Username: $n, Pw: $p}')"
  [[ $status == 200 ]] || { echo "Cannot sign in as $1 on $2 ($status)" >&2; exit 1; }
  jq -r .AccessToken <<<"$body"
}

admin=$(cat "$dev/admin.token")

# Enforcement (feature 5) starts off for every run; its own checks turn it on and off again
plugin_id=d62ee188270c49d8b7b95e524fec2335
# configure <jq filter>: changes the plugin configuration as the dashboard does (read, change, write it all back)
configure() {
  req "$admin" GET "/Plugins/$plugin_id/Configuration"
  req "$admin" POST "/Plugins/$plugin_id/Configuration" "$(jq -c "$1" <<<"$body")"
  [[ $status == 204 ]] || echo "  (configuration $1 answered $status ${body:0:200})"
}
configure '.EnforcementEnabled = false | .SittingBreakMinutes = 15'

# Playback through Jellyfin's own playstate API (feature 3, research R11), on the clip provisioning generates
req "$admin" GET '/Items?Recursive=true&IncludeItemTypes=Movie&SearchTerm=Test%20Pattern&Fields=RunTimeTicks'
clip_id=$(jq -r '[.Items[] | select(.RunTimeTicks > 0)][0].Id // empty' <<<"$body")
[[ -n $clip_id ]] || { echo "The test clip is missing from the Kids library; run the provision step" >&2; exit 1; }
declare -A play_started=()

# play <token> <deviceId> <start|progress|stop> <playSessionId> [paused]: one playstate report; the position follows
# the wall clock since the start
play() {
  local token=$1 device=$2 event=$3 session=$4 paused=${5:-false} path
  case $event in
    start) path=/Sessions/Playing; play_started[$session]=$(date +%s) ;;
    progress) path=/Sessions/Playing/Progress ;;
    stop) path=/Sessions/Playing/Stopped ;;
  esac
  local position=$((($(date +%s) - ${play_started[$session]:-$(date +%s)}) * 10000000))
  req_dev "$token" "$device" POST "$path" "$(jq -nc --arg i "$clip_id" --arg s "$session" --argjson p "$paused" \
    --argjson t "$position" '{ItemId: $i, PlaySessionId: $s, IsPaused: $p, PositionTicks: $t, CanSeek: true,
    PlayMethod: "DirectPlay"}')"
  [[ $status == 204 ]] || echo "  (playback $event for $session answered $status ${body:0:200})"
}

# play_for <token> <deviceId> <playSessionId> <seconds> [paused]: a progress report every 10 s for that long
play_for() {
  local i
  for ((i = 0; i < $4; i += 10)); do
    sleep 10
    play "$1" "$2" progress "$3" "${5:-false}"
  done
}

# used <token> <userId>: prints UsedSeconds from the kid's status
used() {
  req "$1" GET "/Kids/Users/$2/Status"
  jq -r .UsedSeconds <<<"$body"
}

# near <actual> <expected> <tolerance> <name>: PASS if |actual - expected| <= tolerance
near() {
  local actual=$1 expected=$2 tolerance=$3 name=$4 diff
  diff=$((actual - expected))
  if ((diff < 0 ? -diff <= tolerance : diff <= tolerance)); then
    echo "PASS $name ($actual, expected $expected ± $tolerance)"
  else
    echo "FAIL $name: got $actual, expected $expected ± $tolerance"
    failures=$((failures + 1))
  fi
}

# Checks follow quickstart.md §2.

# a. Anonymous detection, with nothing household-specific (no lock type either: constitution IV)
req - GET /Kids/Info
check "a Info is anonymous" 200 \
  '.ApiVersion == 1 and (.Features | index("profiles")) != null and (.Features | index("parentLock")) != null
   and (.Features | index("allowance")) != null and (has("LockType") | not)'
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
req "$admin" GET "/Kids/Users/$tmp_id/Status"
check "k nothing of tmpkid's is left (status is not found)" 404

# l. The picks chooser is for administrators only
req "$alice" GET /Kids/Admin/PickCandidates
check "l alice cannot list pick candidates" 403
req "$admin" GET '/Kids/Admin/PickCandidates?limit=5'
check "l admin lists pick candidates" 200 'type == "array"'

# Parent lock (feature 2, quickstart.md §2 n–ad). The known lock is a REMOTE code; alice signs in as two devices and
# bob as a third, so bob's throttling never touches alice's grants.
known=UDLRrgyb
# The same verifier as VerifierTests.WholphinRemoteUdlrRg12: Wholphin's PasscodeHasher format for the code UDLRrg12
wholphin_code=UDLRrg12
wholphin_verifier='pbkdf2-sha256$120000$f0e1d2c3b4a5968778695a4b3c2d1e0f$fa6a76745eeb53a863f4e826714952f77beb8536eaf52e9a7ee08dd5484df806'
set_known_lock() { req "$admin" PUT /Kids/Parent/Lock "{\"LockType\":\"REMOTE\",\"Code\":\"$known\"}"; }
verify() { req_dev "$1" "$2" POST /Kids/Parent/Verify "{\"Code\":\"$3\"}"; }
alice_rev() { req "$admin" GET "/Kids/Users/$alice_id/Profile"; jq -r .Revision <<<"$body"; }

set_known_lock
check "setup admin sets the known lock" 200 '.LockType == "REMOTE"'
alice_a=$(login_as alice kids-check-A)
alice_b=$(login_as alice kids-check-B)
bob_t=$(login_as bob kids-check-bob)

# n. Any signed-in user reads the lock status, without the verifier
req_dev "$alice_a" kids-check-A GET /Kids/Parent/Lock
check "n alice reads the lock status" 200 '.LockType == "REMOTE" and has("ChangedAt") and (has("Verifier") | not)'

# aa. Codes are validated per type
for bad in 'NUMERIC 123' 'NUMERIC 12a4' 'REMOTE UDLX'; do
  req "$admin" PUT /Kids/Parent/Lock "{\"LockType\":\"${bad% *}\",\"Code\":\"${bad#* }\"}"
  check "aa admin cannot set ${bad% *} code ${bad#* }" 400 "$errors | has(\"Code\")"
done

# o. A wrong code is a 200 "not valid", never 401
verify "$alice_a" kids-check-A 0000
check "o a wrong code is not valid" 200 '.Valid == false and (has("Grant") | not)'

# p. The sixth attempt from one user and device is throttled, even with the right code
for i in 1 2 3 4 5; do
  verify "$bob_t" kids-check-bob 0000
  check "p bob's wrong code $i is answered" 200 '.Valid == false and (has("RetryAfterSeconds") | not)'
done
verify "$bob_t" kids-check-bob "$known"
check "p bob's sixth attempt is throttled" 200 '.Valid == false and .RetryAfterSeconds > 0'

# q. Claiming another DeviceId in the header does not start a fresh count
verify "$bob_t" spoofed "$known"
check "q a spoofed DeviceId is still throttled" 200 '.Valid == false and .RetryAfterSeconds > 0'
bob_wait=$(jq -r .RetryAfterSeconds <<<"$body")

# r. The right code gives a grant
verify "$alice_a" kids-check-A "$known"
check "r alice gets a grant" 200 '.Valid == true and (.Grant | length) == 43 and has("ExpiresAt")'
grant_a=$(jq -r .Grant <<<"$body")

# s. The grant lets alice's own TV change her profile; her token alone does not
rev=$(alice_rev)
req_dev "$alice_a" kids-check-A PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$rev}" "$grant_a"
check "s a grant allows a profile change" 200 ".Revision == $((rev + 1))"
req_dev "$alice_a" kids-check-A PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$((rev + 1))}"
check "s without the grant it is refused" 403

# t. The grant does not work on another device of the same user
req_dev "$alice_b" kids-check-B PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$((rev + 1))}" "$grant_a"
check "t a grant from device A is refused on device B" 403
check_rev=$(alice_rev)
[[ $check_rev == $((rev + 1)) ]] && echo "PASS t the profile is unchanged" ||
  { echo "FAIL t the profile changed to revision $check_rev"; failures=$((failures + 1)); }

# u. The grant follows the registered device, not the header's claim
req_dev "$alice_a" kids-check-B PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$((rev + 1))}" "$grant_a"
check "u the grant works when the header claims device B" 200 ".Revision == $((rev + 2))"

# A grant does not open administrator-only data
req_dev "$alice_a" kids-check-A GET /Kids/Admin/PickCandidates '' "$grant_a"
check "s a grant does not open the picks chooser" 403

# w. A grant holder can cache the verifier
req_dev "$alice_a" kids-check-A GET /Kids/Parent/Verifier '' "$grant_a"
check "w a grant holder reads the verifier" 200 \
  '.LockType == "REMOTE" and (.Verifier | test("^pbkdf2-sha256\\$120000\\$[0-9a-f]{32}\\$[0-9a-f]{64}$"))'
verifier_hash=$(jq -r '.Verifier | split("$")[3]' <<<"$body")

# x. Changing the code with a grant needs the current code
req_dev "$alice_a" kids-check-A PUT /Kids/Parent/Lock '{"LockType":"NUMERIC","Code":"2468","Current":"UDLR0000"}' "$grant_a"
check "x a wrong current code is refused" 400 "$errors | has(\"Current\")"
req_dev "$alice_a" kids-check-A PUT /Kids/Parent/Lock "{\"LockType\":\"NUMERIC\",\"Code\":\"2468\",\"Current\":\"$known\"}" "$grant_a"
check "x the right current code changes the lock" 200 '.LockType == "NUMERIC"'

# y. Changing the lock revokes the grants
req_dev "$alice_a" kids-check-A PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$((rev + 2))}" "$grant_a"
check "y the old grant is refused after the change" 403
set_known_lock
check "y admin restores the known lock" 200

# v. A kid's token alone never reads the verifier or changes the lock
req_dev "$alice_a" kids-check-A GET /Kids/Parent/Verifier
check "v alice cannot read the verifier" 403
req_dev "$alice_a" kids-check-A PUT /Kids/Parent/Lock "{\"LockType\":\"NUMERIC\",\"Code\":\"1357\",\"Current\":\"$known\"}"
check "v alice cannot change the lock" 403
req_dev "$alice_a" kids-check-A DELETE /Kids/Parent/Lock
check "v alice cannot remove the lock" 403

# z. Importing while a lock is set is a conflict
req "$admin" PUT /Kids/Parent/Lock "{\"LockType\":\"REMOTE\",\"Verifier\":\"$wholphin_verifier\"}"
check "z importing over a lock is a conflict" 409 '.LockType == "REMOTE"'

# ab. Remove the lock, then import Wholphin's verifier: its code works unchanged
req "$admin" DELETE /Kids/Parent/Lock
check "ab admin removes the lock" 204
req "$alice" GET /Kids/Parent/Lock
check "ab no lock is reported" 200 'has("LockType") | not'
verify "$alice_a" kids-check-A "$known"
check "ab with no lock nothing verifies" 200 '.Valid == false'
req "$admin" PUT /Kids/Parent/Lock "{\"LockType\":\"REMOTE\",\"Verifier\":\"$wholphin_verifier\"}"
check "ab admin imports a Wholphin verifier" 200 '.LockType == "REMOTE"'
verify "$alice_a" kids-check-A "$wholphin_code"
check "ab the Wholphin code verifies on the server" 200 '.Valid == true'
set_known_lock
check "ab admin sets the known lock again" 200

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
req "$alice" GET "/Kids/Users/$alice_id/Status"
check "m alice reads her screen time outside allowed hours" 200 'has("UsedSeconds")'
# ac. A parent can unlock at bedtime (FR-006)
verify "$alice_a" kids-check-A "$known"
check "ac alice's TV verifies the code outside allowed hours" 200 '.Valid == true'
last_grant=$(jq -r .Grant <<<"$body")
restore_alice
check "m alice's policy is restored" 204
trap - EXIT

# ad. The dashboard page holds no lock data
req - GET "/web/ConfigurationPage?name=Kids"
check "ad the Kids page is served" 200
if grep -Eq 'Parent lock:|pbkdf2|[0-9]{4}-[0-9]{2}-[0-9]{2}T' <<<"$body"; then
  echo "FAIL ad the Kids page contains lock data"
  failures=$((failures + 1))
else
  echo "PASS ad the Kids page contains no lock data"
fi

# Screen time (feature 3, quickstart.md §2 ae–aq). alice has no allowance here; ap purges her screen time at the end,
# so a second run on the same day starts from zero.
dev_device=jellyfin-kids-check
new_id() { cat /proc/sys/kernel/random/uuid; }

# ae. A kid reads her own status
req "$alice" GET "/Kids/Users/$alice_id/Status"
check "ae alice reads her status" 200 \
  '.UsedSeconds >= 0 and (has("AllowanceSeconds") | not) and (has("RemainingSeconds") | not) and .TimeUp == false
   and .WarnAtSeconds == [300,60] and .NextDayStartsAt > .ServerTime and (.Date | test("^[0-9]{4}-[0-9]{2}-[0-9]{2}$"))'

# af. Another kid is refused; a user who is not a kid is not found
req_dev "$bob_t" kids-check-bob GET "/Kids/Users/$alice_id/Status"
check "af bob cannot read alice's status" 403
req "$admin" DELETE "/Kids/Users/$bob_id/Profile"
req "$admin" GET "/Kids/Users/$bob_id/Status"
check "af a user who is not a kid is not found" 404
req "$admin" PUT "/Kids/Users/$bob_id/Profile" '{"Revision":0}'
check "af admin marks bob again" 200

# ag. 60 s of playing counts 60 s
before=$(used "$alice" "$alice_id")
ps=ag-$(new_id)
play "$alice" "$dev_device" start "$ps"
play_for "$alice" "$dev_device" "$ps" 60
play "$alice" "$dev_device" stop "$ps"
near $(($(used "$alice" "$alice_id") - before)) 60 5 "ag 60 s of playback counts"

# ah. Paused time does not count: 10 s playing, 30 s paused, 10 s playing
before=$(used "$alice" "$alice_id")
ps=ah-$(new_id)
play "$alice" "$dev_device" start "$ps"
play_for "$alice" "$dev_device" "$ps" 10
play "$alice" "$dev_device" progress "$ps" true
play_for "$alice" "$dev_device" "$ps" 30 true
play "$alice" "$dev_device" progress "$ps"
play_for "$alice" "$dev_device" "$ps" 10
play "$alice" "$dev_device" stop "$ps"
near $(($(used "$alice" "$alice_id") - before)) 20 5 "ah a 30 s pause is not counted"

# ai. Two devices at once both count
before=$(used "$alice" "$alice_id")
ps_a=ai-a-$(new_id)
ps_b=ai-b-$(new_id)
play "$alice_a" kids-check-A start "$ps_a"
play "$alice_b" kids-check-B start "$ps_b"
for _ in 1 2 3; do
  sleep 10
  play "$alice_a" kids-check-A progress "$ps_a"
  play "$alice_b" kids-check-B progress "$ps_b"
done
play "$alice_a" kids-check-A stop "$ps_a"
play "$alice_b" kids-check-B stop "$ps_b"
near $(($(used "$alice" "$alice_id") - before)) 60 6 "ai two devices playing 30 s count 60 s"

# aj. A client that goes quiet stops counting about 60 s after its last real report, although the server keeps
# synthesising progress
before=$(used "$alice" "$alice_id")
ps=aj-$(new_id)
play "$alice" "$dev_device" start "$ps"
play "$alice" "$dev_device" progress "$ps"
sleep 120
quiet=$(used "$alice" "$alice_id")
gained=$((quiet - before))
if ((gained <= 75 && gained >= 45)); then
  echo "PASS aj a quiet client counts at most 75 s ($gained)"
else
  echo "FAIL aj a quiet client counted $gained s, expected 45–75"
  failures=$((failures + 1))
fi
sleep 30
near $(($(used "$alice" "$alice_id") - quiet)) 0 1 "aj and then stops growing"
play "$alice" "$dev_device" stop "$ps"

# ak. A kid added to a grown-up's session is not counted
admin_s=$(login_as "${DEV_ADMIN_USER:-admin}" kids-check-admin "${DEV_ADMIN_PASSWORD:-admin}")
req_dev "$admin_s" kids-check-admin GET /Sessions?deviceId=kids-check-admin
admin_session=$(jq -r '.[0].Id // empty' <<<"$body")
req "$admin" POST "/Sessions/$admin_session/User/$alice_id"
check "ak admin adds alice to their session" 204
before=$(used "$alice" "$alice_id")
ps=ak-$(new_id)
play "$admin_s" kids-check-admin start "$ps"
play_for "$admin_s" kids-check-admin "$ps" 30
play "$admin_s" kids-check-admin stop "$ps"
near $(($(used "$alice" "$alice_id") - before)) 0 1 "ak alice is not counted in admin's session"
req "$admin" DELETE "/Sessions/$admin_session/User/$alice_id"

# al. The allowance is validated, and the status uses it from the next read
rev=$(alice_rev)
req "$admin" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$rev,\"DailyAllowanceMinutes\":1441}"
check "al an allowance of 1441 minutes is rejected" 400 "$errors | has(\"DailyAllowanceMinutes\")"
req "$admin" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$rev,\"DailyAllowanceMinutes\":1}"
check "al admin sets a 1-minute allowance" 200 '.DailyAllowanceMinutes == 1'
req "$alice" GET "/Kids/Users/$alice_id/Status"
check "al alice's status has the allowance and time is up" 200 \
  '.AllowanceSeconds == 60 and .UsedSeconds >= 60 and .TimeUp == true and .RemainingSeconds == 0'
req "$alice" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$((rev + 1)),\"DailyAllowanceMinutes\":600}"
check "al alice cannot change her own allowance" 403

# am. Bonus minutes: a grown-up (admin or grant) adds them, the kid cannot
req "$alice" GET "/Kids/Users/$alice_id/Status"
bonus0=$(jq -r .BonusSeconds <<<"$body")
req "$alice" POST "/Kids/Users/$alice_id/WatchTime/Bonus" '{"Minutes":5}'
check "am alice cannot add bonus time" 403
for bad in 0 241; do
  req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Bonus" "{\"Minutes\":$bad}"
  check "am a bonus of $bad minutes is rejected" 400 "$errors | has(\"Minutes\")"
done
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Bonus" '{"Minutes":5}'
check "am admin adds 5 minutes" 200 ".BonusSeconds == $((bonus0 + 300))"
verify "$alice_a" kids-check-A "$known"
bonus_grant=$(jq -r .Grant <<<"$body")
req_dev "$alice_a" kids-check-A POST "/Kids/Users/$alice_id/WatchTime/Bonus" '{"Minutes":5}' "$bonus_grant"
check "am a grant holder on alice's TV adds 5 minutes" 200 ".BonusSeconds == $((bonus0 + 600))"

# an. Offline reports only add, once per segment id, and one bad segment rejects the request
if [[ $(date -d '-1 hour' +%F) == $(date +%F) ]]; then
  started=$(date -u -d '-1 hour' +%Y-%m-%dT%H:%M:%SZ) seconds=120
else
  started=$(date -u -d '-1 minute' +%Y-%m-%dT%H:%M:%SZ) seconds=30
fi
segment=$(new_id)
report="{\"Segments\":[{\"Id\":\"$segment\",\"StartedAt\":\"$started\",\"Seconds\":$seconds}]}"
before=$(used "$alice" "$alice_id")
req "$alice" POST "/Kids/Users/$alice_id/WatchTime/Report" "$report"
check "an alice reports a new segment" 200
near $(($(used "$alice" "$alice_id") - before)) "$seconds" 2 "an the segment is added"
before=$(used "$alice" "$alice_id")
req "$alice" POST "/Kids/Users/$alice_id/WatchTime/Report" "$report"
check "an the same report again is accepted" 200
near $(($(used "$alice" "$alice_id") - before)) 0 1 "an and adds nothing"
req "$alice" POST "/Kids/Users/$alice_id/WatchTime/Report" \
  "{\"Segments\":[{\"Id\":\"$(new_id)\",\"StartedAt\":\"$started\",\"Seconds\":0},{\"Id\":\"$(new_id)\",\"StartedAt\":\"$started\",\"Seconds\":60}]}"
check "an a zero-second segment rejects the request" 400 "$errors | has(\"Segments[0].Seconds\")"
near $(($(used "$alice" "$alice_id") - before)) 0 1 "an and nothing is added"

# ao. Only the kid reports her own segments
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Report" "{\"Segments\":[{\"Id\":\"$(new_id)\",\"StartedAt\":\"$started\",\"Seconds\":60}]}"
check "ao admin cannot report for alice" 403
req_dev "$bob_t" kids-check-bob POST "/Kids/Users/$alice_id/WatchTime/Report" "{\"Segments\":[{\"Id\":\"$(new_id)\",\"StartedAt\":\"$started\",\"Seconds\":60}]}"
check "ao bob cannot report for alice" 403

# ap. Unmarking keeps screen time; purging removes it. Leaves alice marked with no allowance
used_before=$(used "$alice" "$alice_id")
req "$admin" DELETE "/Kids/Users/$alice_id/Profile"
check "ap admin unmarks alice" 204
req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":0}'
check "ap admin marks alice again" 200
near "$(used "$alice" "$alice_id")" "$used_before" 0 "ap her screen time is kept"
req "$admin" DELETE "/Kids/Users/$alice_id/Profile?purge=true"
check "ap admin unmarks alice with purge" 204
req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":0}'
check "ap admin marks alice again" 200
req "$alice" GET "/Kids/Users/$alice_id/Status"
check "ap her screen time is gone" 200 '.UsedSeconds == 0 and .BonusSeconds == 0 and (has("AllowanceSeconds") | not)'

# aq. The pages Jellyfin serves anonymously hold no usage
for page in Kids KidsUser; do
  req - GET "/web/ConfigurationPage?name=$page"
  check "aq page $page is served" 200
  if grep -Eq 'UsedSeconds|Today:|[0-9]{4}-[0-9]{2}-[0-9]{2}' <<<"$body"; then
    echo "FAIL aq page $page contains usage data"
    failures=$((failures + 1))
  else
    echo "PASS aq page $page contains no usage data"
  fi
done

# ar. Info lists the session limit capability (feature 4)
req - GET /Kids/Info
check "ar Info lists sessionLimit" 200 '(.Features | index("sessionLimit")) != null'

# as. The session limit is validated like the other fields, and a rejected change saves nothing
rev=$(alice_rev)
for bad in 4 121; do
  req "$admin" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$rev,\"SessionLimitMinutes\":$bad}"
  check "as a session limit of $bad minutes is rejected" 400 "$errors | has(\"SessionLimitMinutes\")"
done
req "$admin" GET "/Kids/Users/$alice_id/Profile"
check "as alice's profile is unchanged" 200 ".Revision == $rev and (has(\"SessionLimitMinutes\") | not)"

# at. Round trip: admin sets it with the allowance, alice reads it, and alice cannot change it
req "$admin" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$rev,\"SessionLimitMinutes\":30,\"DailyAllowanceMinutes\":60}"
check "at admin sets a 30-minute session limit" 200 \
  ".Revision == $((rev + 1)) and .SessionLimitMinutes == 30 and .DailyAllowanceMinutes == 60"
req "$alice" GET "/Kids/Users/$alice_id/Profile"
check "at alice reads her session limit" 200 '.SessionLimitMinutes == 30 and .DailyAllowanceMinutes == 60'
req "$alice" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$((rev + 1)),\"SessionLimitMinutes\":120}"
check "at alice cannot change her own session limit" 403

# au. A PUT without the field clears it (full replacement), and a missing limit is left out of the JSON. Leaves alice
# with no session limit and no allowance
req "$admin" PUT "/Kids/Users/$alice_id/Profile" "{\"Revision\":$((rev + 1))}"
check "au a PUT without the field clears it and omits the key" 200 \
  '(has("SessionLimitMinutes") | not) and (has("DailyAllowanceMinutes") | not)'
req "$alice" GET "/Kids/Users/$alice_id/Profile"
check "au alice's profile has no SessionLimitMinutes key" 200 '(has("SessionLimitMinutes") | not)'

# Enforcement (feature 5, quickstart.md §2 ba–bk). Runs with alice left by au: no allowance, no session limit
trap 'restore_alice; configure ".EnforcementEnabled = false | .SittingBreakMinutes = 15"' EXIT

# req_h <token|-> <deviceId> <method> <path> [range]: like req_dev, also sets $headers (lower-case names)
req_h() {
  local token=$1 device=$2 method=$3 path=$4 range=${5-} out hdr
  local auth="MediaBrowser Client=\"jellyfin-kids-check\", Device=\"dev\", DeviceId=\"$device\", Version=\"0.0.1\""
  [[ $token != - ]] && auth+=", Token=\"$token\""
  out=$(mktemp)
  hdr=$(mktemp)
  status=$(curl --silent --output "$out" --dump-header "$hdr" --write-out '%{http_code}' -X "$method" "$url$path" \
    -H "Authorization: $auth" -H 'Content-Type: application/json' ${range:+-H "Range: bytes=$range"} \
    $([[ $method == POST ]] && echo --data '{}')) || status=000
  body=$(head -c 200000 "$out" | tr -d '\000')
  headers=$(tr -d '\r' <"$hdr" | tr '[:upper:]' '[:lower:]')
  rm -f "$out" "$hdr"
}

# refused <name> <reason>: the last req_h was refused with 403 PlaybackNotAllowed and that reason
refused() {
  if [[ $status == 403 ]] && grep -qx 'x-application-error-code: playbacknotallowed' <<<"$headers" \
    && grep -qx "x-kids-reason: ${2,,}" <<<"$headers" && grep -q "^content-type: application/problem+json" <<<"$headers" \
    && jq -e --arg r "$2" '.reason == $r' <<<"$body" >/dev/null 2>&1; then
    echo "PASS $1"
  else
    echo "FAIL $1: $status $(grep -E '^x-(application-error-code|kids-reason)' <<<"$headers" | tr '\n' ' ')${body:0:200}"
    failures=$((failures + 1))
  fi
}

# served <name>: the last req_h was answered with media (200 or 206)
served() {
  if [[ $status == 200 || $status == 206 ]]; then
    echo "PASS $1 ($status)"
  else
    echo "FAIL $1: expected 200 or 206, got $status ${body:0:200}"
    failures=$((failures + 1))
  fi
}

# guarded_routes <token> <deviceId> <name prefix> <expect: refused REASON | served>: every kind of guarded route
guarded_routes() {
  local token=$1 device=$2 name=$3 expect=$4 reason=${5-}
  local q="mediaSourceId=$clip_id&VideoCodec=h264&AudioCodec=aac"
  local -a routes=(
    "GET /Videos/$clip_id/master.m3u8?$q"
    "GET /Videos/$clip_id/main.m3u8?$q"
    "GET /Videos/$clip_id/hls1/main/0.mp4?$q&runtimeTicks=0&actualSegmentLengthTicks=30000000"
    "GET /Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id"
    "GET /Items/$clip_id/File"
    "GET /Items/$clip_id/Download"
    "GET /Audio/$clip_id/universal?UserId=$alice_id"
    "POST /LiveStreams/Open?ItemId=$clip_id"
  )
  local route
  for route in "${routes[@]}"; do
    req_h "$token" "$device" ${route%% *} "${route#* }" 0-1023
    if [[ $expect == refused ]]; then
      refused "$name ${route%%\?*}" "$reason"
    elif [[ ${route%% *} == POST ]]; then
      # Opening a live stream on a file is not something the server does; anything but a refusal will do
      [[ $status != 403 ]] && echo "PASS $name ${route%%\?*} ($status)" || { echo "FAIL $name ${route%%\?*}: 403"; failures=$((failures + 1)); }
    else
      served "$name ${route%%\?*}"
    fi
  done
}

# playback_info <token> <deviceId>: sets $body to a POST PlaybackInfo answer
playback_info() { req_h "$1" "$2" POST "/Items/$clip_id/PlaybackInfo?UserId=$alice_id"; }

set_alice() { local rev; rev=$(alice_rev); req "$admin" PUT "/Kids/Users/$alice_id/Profile" "$(jq -c --argjson r "$rev" '. + {Revision: $r}' <<<"$1")"; }

# ba. Info reports the gate: every guarded route was found on this server
req - GET /Kids/Info
check "ba Info lists enforcement and sitting, and the gate is ok" 200 \
  '(.Features | index("enforcement")) != null and (.Features | index("sitting")) != null
   and .Enforcement.Gate == "ok" and (.Enforcement.Missing | length) == 0'

# bb. Off (the default): a kid with no time left plays as before, and Status says so
set_alice '{"DailyAllowanceMinutes": 0}'
check "bb alice gets an allowance of 0" 200
playback_info "$alice" kids-check-tv
check "bb with enforcement off, PlaybackInfo has media" 200 '(.MediaSources | length) > 0 and (.ErrorCode == null)'
req "$alice" GET "/Kids/Users/$alice_id/Status"
check "bb Status says not enforced, no denial" 200 '.Enforced == false and (has("DeniedReason") | not) and .TimeUp == true'

# bc. On: every guarded route refuses her, with the reason; nothing else changes
configure '.EnforcementEnabled = true'
playback_info "$alice" kids-check-tv
check "bc PlaybackInfo POST is 200 NotAllowed with no media" 200 '.ErrorCode == "NotAllowed" and (.MediaSources | length) == 0'
grep -qx 'x-kids-reason: timeup' <<<"$headers" && echo "PASS bc PlaybackInfo carries X-Kids-Reason" \
  || { echo "FAIL bc PlaybackInfo has no X-Kids-Reason"; failures=$((failures + 1)); }
req_h "$alice" kids-check-tv GET "/Items/$clip_id/PlaybackInfo?UserId=$alice_id"
check "bc PlaybackInfo GET is 200 NotAllowed" 200 '.ErrorCode == "NotAllowed"'
guarded_routes "$alice" kids-check-tv "bc alice refused on" refused timeUp
req_h - none GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&ApiKey=$alice" 0-1023
refused "bc an anonymous stream URL with alice's ApiKey is refused" timeUp
accept_ct=$(curl --silent --output /dev/null --write-out '%{http_code} %{content_type}' -H 'Accept: video/mp4' \
  "$url/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&ApiKey=$alice")
if [[ $accept_ct == "403 application/problem+json"* ]]; then
  echo "PASS bc a refusal is JSON even when the client asks for video ($accept_ct)"
else
  echo "FAIL bc a refusal with Accept: video/mp4 answered $accept_ct"
  failures=$((failures + 1))
fi
req "$alice" GET /Users/Me
check "bc alice can still use the server (Users/Me)" 200
req "$alice" GET "/Items?Recursive=true&Limit=1"
check "bc alice can still browse" 200
req "$alice" GET "/Kids/Users/$alice_id/Status"
check "bc Status says enforced and timeUp until the next day" 200 \
  '.Enforced == true and .DeniedReason == "timeUp" and .DeniedUntil == .NextDayStartsAt'

# bd. Administrators and API keys are never refused
playback_info "$admin" kids-check-admin
check "bd admin gets media" 200 '(.MediaSources | length) > 0'
guarded_routes "$admin" kids-check-admin "bd admin served on" served
req "$admin" POST "/Auth/Keys?app=kids-check-enforcement"
req "$admin" GET /Auth/Keys
api_key=$(jq -r '[.Items[] | select(.AppName == "kids-check-enforcement")][0].AccessToken' <<<"$body")
req_h - none GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&ApiKey=$api_key" 0-1023
served "bd an API key is served"
req "$admin" DELETE "/Auth/Keys/$api_key"

# be. A URL without a token is refused when it names a kid's device, and served otherwise (documented limit)
play "$alice" kids-check-tv start kids-check-be
req_h - none GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&deviceId=kids-check-tv" 0-1023
refused "be a tokenless stream naming alice's device is refused" timeUp
req_h - none GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&deviceId=kids-check-admin" 0-1023
served "be a tokenless stream naming an adult's device is served"
req_h - none GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id" 0-1023
served "be a tokenless stream naming no device is served (documented limit)"

# bf. A bonus lifts the refusal at once: the answer already says so, and the next PlaybackInfo has media
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Bonus" '{"Minutes": 5}'
check "bf the bonus answer has no denial" 200 '.Enforced == true and (has("DeniedReason") | not)'
playback_info "$alice" kids-check-tv
check "bf PlaybackInfo has media again" 200 '(.MediaSources | length) > 0'

# bg. Live cutoff without a WebSocket: alice holds a direct-play download open and reports progress; when her time
# runs out the download is cut, later requests are refused and the server no longer shows her playing. One info line
# per denial and one per stopped session
log_file=$(ls -t "$dev"/log/log_*.log | head -1)
kids_lines() { grep -cE "Kids: .?alice.? may not play now|Kids: stopped .?alice.?'s playback" "$log_file" || true; }
lines_before=$(kids_lines)
req "$alice" GET "/Kids/Users/$alice_id/Status"
used_now=$(jq -r .UsedSeconds <<<"$body")
bonus_now=$(jq -r .BonusSeconds <<<"$body")
# About a minute left: today's bonuses still count, so the allowance makes up the rest
allowance=$(((used_now - bonus_now + 45) / 60 + 1))
set_alice "{\"DailyAllowanceMinutes\": $((allowance < 0 ? 0 : allowance))}"
req "$alice" GET "/Kids/Users/$alice_id/Status"
left=$(jq -r .RemainingSeconds <<<"$body")
echo "  alice has ${left}s left"
download=$(mktemp)
started=$(date +%s)
curl --silent --output /dev/null --limit-rate 40k --write-out '%{http_code} %{exitcode}' \
  "$url/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&deviceId=kids-check-bg&ApiKey=$alice" >"$download" &
download_pid=$!
play "$alice" kids-check-bg start kids-check-bg
while kill -0 "$download_pid" 2>/dev/null && (($(date +%s) - started < left + 60)); do
  sleep 5
  play "$alice" kids-check-bg progress kids-check-bg >/dev/null
done
cut_after=$(($(date +%s) - started))
kill "$download_pid" 2>/dev/null || true
wait "$download_pid" 2>/dev/null || true
read -r download_status download_exit <"$download" || true
rm -f "$download"
if [[ $download_exit != 0 ]] && ((cut_after <= left + 30)); then
  echo "PASS bg the held download was cut ${cut_after}s after the start, with ${left}s left (curl exit $download_exit)"
else
  echo "FAIL bg the held download was not cut in time: ${cut_after}s with ${left}s left (http $download_status, exit $download_exit)"
  failures=$((failures + 1))
fi
req_h - none GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&ApiKey=$alice" 1024-2047
refused "bg the next range request is refused" timeUp
sleep 2
req "$admin" GET /Sessions
check "bg the server no longer shows alice playing on that device" 200 \
  '[.[] | select(.DeviceId == "kids-check-bg") | .NowPlayingItem] | all(. == null)'
lines_after=$(kids_lines)
near $((lines_after - lines_before)) 3 1 "bg info log lines for this cutoff (denial + stopped sessions)"

# bh. Allowed hours: a window that ends in about 80 s stops her running playback at the boundary, and her ApiKey
# stream requests are refused after it, which Jellyfin itself does not do (the 1 s server tick drives it, since the
# core refuses her own reports after hours)
set_alice '{}'
req "$admin" GET "/Users/$alice_id"
end_epoch=$(($(date +%s) + 80))
end_hour=$(date -d "@$end_epoch" +'%-H %-M %-S' | awk '{printf "%.6f", $1 + $2/60 + $3/3600}')
req "$admin" POST "/Users/$alice_id/Policy" "$(jq -c --arg d "$(date +%A)" --argjson h "$end_hour" \
  '.Policy | .AccessSchedules = [{DayOfWeek: $d, StartHour: 0, EndHour: $h}]' <<<"$body")"
check "bh admin sets a window that ends in 80 s" 204
download=$(mktemp)
started=$(date +%s)
curl --silent --output /dev/null --limit-rate 40k --write-out '%{http_code} %{exitcode}' \
  "$url/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&deviceId=kids-check-bh&ApiKey=$alice" >"$download" &
download_pid=$!
play "$alice" kids-check-bh start kids-check-bh
while kill -0 "$download_pid" 2>/dev/null && (($(date +%s) < end_epoch + 60)); do
  sleep 5
  play "$alice" kids-check-bh progress kids-check-bh >/dev/null
done
cut_at=$(($(date +%s) - end_epoch))
kill "$download_pid" 2>/dev/null || true
wait "$download_pid" 2>/dev/null || true
read -r download_status download_exit <"$download" || true
rm -f "$download"
if [[ $download_exit != 0 ]] && ((cut_at >= -2 && cut_at <= 30)); then
  echo "PASS bh the held download was cut ${cut_at}s after the window closed (curl exit $download_exit)"
else
  echo "FAIL bh the held download was not cut at the boundary: ${cut_at}s (http $download_status, exit $download_exit)"
  failures=$((failures + 1))
fi
req_h - none GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id&ApiKey=$alice" 0-1023
refused "bh an ApiKey stream after hours is refused" outsideSchedule
req "$admin" GET /Sessions
check "bh the server no longer shows alice playing after hours" 200 \
  '[.[] | select(.DeviceId == "kids-check-bh") | .NowPlayingItem] | all(. == null)'
restore_alice
check "bh alice's policy is restored" 204

# bi. Sitting limit, across devices, with a 1-minute break (slow: DEV_CHECK_SLOW=1)
if [[ -n ${DEV_CHECK_SLOW-} ]]; then
  configure '.SittingBreakMinutes = 1'
  set_alice '{"SessionLimitMinutes": 5}'
  play "$alice" kids-check-bi-1 start kids-check-bi-1
  play_for "$alice" kids-check-bi-1 kids-check-bi-1 150
  play "$alice" kids-check-bi-1 stop kids-check-bi-1
  play "$alice" kids-check-bi-2 start kids-check-bi-2
  play_for "$alice" kids-check-bi-2 kids-check-bi-2 160
  req "$alice" GET "/Kids/Users/$alice_id/Status"
  check "bi after 5 minutes on two devices alice is on a break" 200 \
    '.DeniedReason == "sessionLimit" and .SittingLimitSeconds == 300 and .SittingRemainingSeconds == 0
     and .BreakRemainingSeconds > 0 and .BreakSeconds == 60'
  playback_info "$alice" kids-check-bi-2
  check "bi PlaybackInfo is NotAllowed during the break" 200 '.ErrorCode == "NotAllowed"'
  sleep 65
  playback_info "$alice" kids-check-bi-2
  check "bi after the break she may play again" 200 '(.MediaSources | length) > 0'
  req "$alice" GET "/Kids/Users/$alice_id/Status"
  check "bi a new sitting starts at zero" 200 '(has("DeniedReason") | not) and .SittingUsedSeconds == 0'
  configure '.SittingBreakMinutes = 15'
else
  echo "SKIP bi sitting limit (set DEV_CHECK_SLOW=1)"
fi

# bj. Off again: every refusal is gone at once
set_alice '{"DailyAllowanceMinutes": 0}'
playback_info "$alice" kids-check-tv
check "bj enforcement on refuses alice" 200 '.ErrorCode == "NotAllowed"'
configure '.EnforcementEnabled = false'
playback_info "$alice" kids-check-tv
check "bj with enforcement off she gets media at once" 200 '(.MediaSources | length) > 0'
req_h "$alice" kids-check-tv GET "/Videos/$clip_id/stream?static=true&mediaSourceId=$clip_id" 0-1023
served "bj with enforcement off her stream is served"
# Leave alice as au did, with today's bonuses and screen time purged, so the next run's allowance checks start clean
req "$admin" DELETE "/Kids/Users/$alice_id/Profile?purge=true"
check "bj admin unmarks alice with purge" 204
req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":0}'
check "bj admin marks alice again, with no allowance and no time used" 200

# Live TV (feature 6, quickstart.md §2 ca–cv). Always on: enforcement stays off here, as bj left it. Provisioning added
# PekeTV and Grown-ups News; alice is a kid with live TV access (Jellyfin's default)
req "$admin" GET /LiveTv/Channels
peke=$(jq -r '[.Items[] | select(.Name == "PekeTV")][0].Id // empty' <<<"$body")
news=$(jq -r '[.Items[] | select(.Name == "Grown-ups News")][0].Id // empty' <<<"$body")
[[ -n $peke && -n $news ]] || { echo "The dev live TV channels are missing; run the provision step" >&2; exit 1; }
# ids compare without dashes, whatever form an answer uses
nid() { tr -d '-' <<<"${1,,}"; }
peke=$(nid "$peke")
news=$(nid "$news")

# req_hb <token|-> <deviceId> <method> <path> <json body>: like req_h, with a body
req_hb() {
  local token=$1 device=$2 method=$3 path=$4 data=$5 out hdr
  local auth="MediaBrowser Client=\"jellyfin-kids-check\", Device=\"dev\", DeviceId=\"$device\", Version=\"0.0.1\""
  [[ $token != - ]] && auth+=", Token=\"$token\""
  out=$(mktemp)
  hdr=$(mktemp)
  status=$(curl --silent --max-time 60 --output "$out" --dump-header "$hdr" --write-out '%{http_code}' -X "$method" "$url$path" \
    -H "Authorization: $auth" -H 'Content-Type: application/json' --data "$data") || status=000
  body=$(head -c 200000 "$out" | tr -d '\000')
  headers=$(tr -d '\r' <"$hdr" | tr '[:upper:]' '[:lower:]')
  rm -f "$out" "$hdr"
}

# not_allowed <name>: the last answer is PlaybackInfo 200 NotAllowed with X-Kids-Reason liveTvChannel
not_allowed() {
  if [[ $status == 200 ]] && jq -e '.ErrorCode == "NotAllowed" and ((.MediaSources // []) | length) == 0' <<<"$body" >/dev/null 2>&1 \
    && grep -qx 'x-kids-reason: livetvchannel' <<<"$headers"; then
    echo "PASS $1"
  else
    echo "FAIL $1: $status $(grep -E '^x-kids-reason' <<<"$headers") ${body:0:200}"
    failures=$((failures + 1))
  fi
}

# live_info <token> <deviceId> <channel>: PlaybackInfo that opens the live stream, as clients do
live_info() {
  req_hb "$1" "$2" POST "/Items/$3/PlaybackInfo?UserId=$(user_of "$1")" \
    '{"AutoOpenLiveStream": true, "EnableDirectPlay": false, "EnableDirectStream": true, "EnableTranscoding": true}'
}
user_of() { if [[ $1 == "$alice" ]]; then echo "$alice_id"; else echo ""; fi; }

# ca. Info reports live TV
req - GET /Kids/Info
check "ca Info lists liveTv and its routes are all found" 200 \
  '(.Features | index("liveTv")) != null and .LiveTv.Gate == "ok" and (.LiveTv.Missing | length) == 0'

# cb. The editor's channel list is for administrators only
req "$admin" GET /Kids/Admin/LiveTvChannels
check "cb admin lists both channels with their numbers" 200 \
  "[.[] | {id: (.Id | gsub(\"-\"; \"\") | ascii_downcase), Number, Type}] | (index({id: \"$peke\", Number: \"1\", Type: \"TV\"}) != null) and (index({id: \"$news\", Number: \"2\", Type: \"TV\"}) != null)"
req "$alice" GET /Kids/Admin/LiveTvChannels
check "cb a kid cannot list the server's channels" 403

# cc. The list is validated, round-trips, and a PUT without it clears it
set_alice "{\"LiveTvChannelIds\": [\"$clip_id\"]}"
check "cc a movie is not a live TV channel" 400 '.errors["LiveTvChannelIds[0]"] == ["Not a live TV channel."]'
set_alice "{\"LiveTvChannelIds\": [\"$peke\", \"$peke\"]}"
check "cc a channel listed twice is refused" 400 '.errors["LiveTvChannelIds[1]"] == ["Listed more than once."]'
set_alice "{\"LiveTvChannelIds\": [\"$peke\"]}"
check "cc alice gets PekeTV" 200 "[.LiveTvChannelIds[] | gsub(\"-\"; \"\") | ascii_downcase] == [\"$peke\"]"
set_alice '{}'
check "cc a PUT without the list clears it" 200 '.LiveTvChannelIds == []'
req "$alice" GET "/Kids/Users/$alice_id/Profile"
check "cc a profile always has the list" 200 'has("LiveTvChannelIds")'

# cd. With no channels, alice may not play any (always on, enforcement off)
live_info "$alice" kids-check-tv "$peke"
not_allowed "cd alice with no channels is refused PekeTV, with enforcement off"
set_alice "{\"LiveTvChannelIds\": [\"$peke\"]}"

# ce. Her channel plays: PlaybackInfo opens its live stream
live_info "$alice" kids-check-tv "$peke"
check "ce alice's PlaybackInfo for PekeTV has a live stream" 200 '(.MediaSources | length) > 0 and (.MediaSources[0].LiveStreamId | length) > 0'
peke_live=$(jq -r '.MediaSources[0].LiveStreamId // empty' <<<"$body")
peke_source=$(jq -r '.MediaSources[0].Id // empty' <<<"$body")
peke_unique=$(jq -r '.MediaSources[0].Path // empty' <<<"$body" | sed -n 's|.*/LiveTv/LiveStreamFiles/\([^/]*\)/.*|\1|p')

# cf. Another channel is refused, PlaybackInfo and every other route
live_info "$alice" kids-check-tv "$news"
not_allowed "cf alice's PlaybackInfo for Grown-ups News is NotAllowed"
req_hb "$admin" kids-check-admin POST "/Items/$news/PlaybackInfo" '{"AutoOpenLiveStream": true, "EnableDirectStream": true}'
check "cf admin opens Grown-ups News" 200 '(.MediaSources[0].LiveStreamId | length) > 0'
news_live=$(jq -r '.MediaSources[0].LiveStreamId // empty' <<<"$body")
news_unique=$(jq -r '.MediaSources[0].Path // empty' <<<"$body" | sed -n 's|.*/LiveTv/LiveStreamFiles/\([^/]*\)/.*|\1|p')
# An opened source carries no open token: read it from an answer that does not open the stream
req_hb "$admin" kids-check-admin POST "/Items/$news/PlaybackInfo" '{"AutoOpenLiveStream": false}'
news_token=$(jq -r '.MediaSources[0].OpenToken // empty' <<<"$body")
[[ -n $news_token ]] || echo "  (News has no open token: ${body:0:200})"
req_hb "$alice" kids-check-tv POST /LiveStreams/Open "{\"OpenToken\": \"$news_token\"}"
refused "cf alice cannot open News with its open token in the body" liveTvChannel
req_hb "$alice" kids-check-tv POST "/LiveStreams/Open?openToken=$(jq -rn --arg t "$news_token" '$t | @uri')" '{}'
refused "cf alice cannot open News with its open token in the query" liveTvChannel
# A token Jellyfin would read as News whatever its type part says (the review's bypass)
crafted="${news_token%%_*}_X_${news}_x"
req_hb "$alice" kids-check-tv POST "/LiveStreams/Open?openToken=$(jq -rn --arg t "$crafted" '$t | @uri')" '{}'
refused "cf alice cannot open News with a crafted open token" liveTvChannel
req_h "$alice" kids-check-tv GET "/Videos/$news/master.m3u8?MediaSourceId=$news&LiveStreamId=$news_live"
refused "cf alice cannot get News' HLS playlist" liveTvChannel
req_h "$alice" kids-check-tv GET "/Videos/$peke/master.m3u8?MediaSourceId=$peke_source&LiveStreamId=$news_live"
refused "cf alice cannot pass News' live stream off as PekeTV" liveTvChannel
if [[ -n $news_unique ]]; then
  req_h "$alice" kids-check-tv GET "/LiveTv/LiveStreamFiles/$news_unique/stream.ts" 0-1023
  refused "cf alice cannot read News' shared live stream file" liveTvChannel
else
  echo "SKIP cf News is not a shared stream on this server (no LiveStreamFiles path)"
fi
req_h "$alice" kids-check-tv GET "/LiveTv/LiveStreamFiles/0123456789abcdef0123456789abcdef/stream.ts" 0-1023
refused "cf an unknown live stream file is refused to a kid" liveTvChannel

# cg. Her own channel's routes are served
req_h "$alice" kids-check-tv GET "/Videos/$peke/master.m3u8?MediaSourceId=$peke_source&LiveStreamId=$peke_live"
check "cg alice gets PekeTV's HLS playlist" 200
if [[ -n $peke_unique ]]; then
  status=$(curl --silent --max-time 5 --output /dev/null --write-out '%{http_code}' \
    "$url/LiveTv/LiveStreamFiles/$peke_unique/stream.ts?ApiKey=$alice") || true
  check "cg alice reads PekeTV's shared live stream file" 200
fi

# ch. Administrators and API keys are never refused
req "$admin" POST "/Auth/Keys?app=kids-check-livetv"
req "$admin" GET /Auth/Keys
livetv_key=$(jq -r '[.Items[] | select(.AppName == "kids-check-livetv")][0].AccessToken' <<<"$body")
req_hb - none POST "/Items/$news/PlaybackInfo?ApiKey=$livetv_key" '{"AutoOpenLiveStream": false}'
check "ch an API key gets News' media sources" 200 '(.MediaSources | length) > 0'
req "$admin" DELETE "/Auth/Keys/$livetv_key"

# ci. Live TV access off in Jellyfin wins over the list, at once
req "$admin" GET "/Users/$alice_id"
req "$admin" POST "/Users/$alice_id/Policy" "$(jq -c '.Policy | .EnableLiveTvAccess = false' <<<"$body")"
check "ci admin turns alice's live TV access off" 204
sleep 1
req_hb "$alice" kids-check-tv POST "/Items/$peke/PlaybackInfo?UserId=$alice_id" '{}'
not_allowed "ci with live TV access off, PekeTV is refused"
req "$alice" GET "/Kids/Users/$alice_id/LiveTv/Channels"
check "ci with live TV access off, her channel list is empty" 200 '.Items == [] and .TotalRecordCount == 0'
restore_alice
check "ci alice's policy is restored" 204
sleep 1

# cj. No recordings or timers for kids
for route in /LiveTv/Recordings /LiveTv/Recordings/Folders /LiveTv/Timers /LiveTv/SeriesTimers; do
  req_h "$alice" kids-check-tv GET "$route"
  refused "cj alice is refused $route" liveTvRecording
done
# Jellyfin's own LiveTvManagement policy refuses this one before any filter runs (a bare 403)
req_hb "$alice" kids-check-tv POST /LiveTv/Timers '{}'
check "cj alice cannot create a timer" 403
req "$admin" GET /LiveTv/Timers
check "cj admin still lists timers" 200

# ck. Jellyfin's own lists only show her channel
req "$alice" GET "/LiveTv/Channels?UserId=$alice_id&Limit=10"
check "ck LiveTv/Channels shows PekeTV only" 200 "[.Items[].Id | gsub(\"-\"; \"\") | ascii_downcase] == [\"$peke\"] and .TotalRecordCount == 1"
req "$admin" GET "/LiveTv/Channels"
check "ck admin still sees both channels" 200 '.TotalRecordCount >= 2'
req "$alice" GET "/LiveTv/Programs?UserId=$alice_id&Limit=50"
check "ck LiveTv/Programs shows PekeTV's programs only" 200 \
  "(.Items | length) > 0 and ([.Items[].ChannelId | gsub(\"-\"; \"\") | ascii_downcase] | unique) == [\"$peke\"]"
req "$alice" GET "/LiveTv/Programs?UserId=$alice_id&ChannelIds=$news"
check "ck asking for News' programs gives none" 200 '.Items == []'
req "$alice" POST /LiveTv/Programs "{\"UserId\": \"$alice_id\", \"ChannelIds\": [\"$news\", \"$peke\"], \"Limit\": 20}"
check "ck POST LiveTv/Programs is narrowed to PekeTV" 200 \
  "(.Items | length) > 0 and ([.Items[].ChannelId | gsub(\"-\"; \"\") | ascii_downcase] | unique) == [\"$peke\"]"
req "$alice" GET "/LiveTv/Programs/Recommended?UserId=$alice_id&IsAiring=true"
check "ck recommended programs are PekeTV's only" 200 \
  "([.Items[].ChannelId | gsub(\"-\"; \"\") | ascii_downcase] | unique) | (. == [] or . == [\"$peke\"])"
req "$alice" GET "/LiveTv/Channels/$news"
check "ck News' channel page is not found for alice" 404
req "$alice" GET "/LiveTv/Channels/$peke"
check "ck PekeTV's channel page works" 200
req "$admin" GET "/LiveTv/Programs?ChannelIds=$news&Limit=1"
news_program=$(jq -r '.Items[0].Id // empty' <<<"$body")
req "$alice" GET "/LiveTv/Programs/$news_program"
check "ck a News program is not found for alice" 404

# cl. The kid endpoints: her channels with what is on now, and her guide
req "$alice" GET "/Kids/Users/$alice_id/LiveTv/Channels"
check "cl her channels: PekeTV with number, type and the program on now" 200 \
  "[.Items[] | (.Id | gsub(\"-\"; \"\") | ascii_downcase)] == [\"$peke\"] and .Items[0].ChannelNumber == \"1\"
   and .Items[0].Type == \"TvChannel\" and (.Items[0].CurrentProgram.Name | startswith(\"Cartoons\"))"
req "$alice" GET "/Kids/Users/$alice_id/LiveTv/Programs"
check "cl her guide: PekeTV's programs in start order, the next 24 h" 200 \
  "(.Items | length) >= 48 and (.Items | length) <= 50
   and ([.Items[].ChannelId | gsub(\"-\"; \"\") | ascii_downcase] | unique) == [\"$peke\"]
   and ([.Items[].StartDate] == ([.Items[].StartDate] | sort))"
start=$(date -u +%Y-%m-%dT%H:%M:%SZ)
end=$(date -u -d '+2 hours' +%Y-%m-%dT%H:%M:%SZ)
req "$alice" GET "/Kids/Users/$alice_id/LiveTv/Programs?startDate=$start&endDate=$end&channelIds=$news,$peke"
check "cl a 2-hour window gives 4 or 5 of her programs, News ignored" 200 \
  "(.Items | length) >= 4 and (.Items | length) <= 5 and ([.Items[].ChannelId | gsub(\"-\"; \"\") | ascii_downcase] | unique) == [\"$peke\"]"
req "$alice" GET "/Kids/Users/$alice_id/LiveTv/Programs?startDate=$end&endDate=$start"
check "cl an end before the start is refused" 400 '.errors.endDate != null'
req "$admin" GET "/Kids/Users/$alice_id/LiveTv/Channels"
check "cl an admin reads her channels" 200 '.TotalRecordCount == 1'
bob_live=$(login bob)
req "$bob_live" GET "/Kids/Users/$alice_id/LiveTv/Channels"
check "cl bob cannot read alice's channels" 403
req "$admin" GET /Kids/Users/00000000000000000000000000000001/LiveTv/Channels
check "cl a user who is not a kid is not found" 404

# cm. Live TV counts as screen time: 60 s of PekeTV counts 60 s
before=$(used "$alice" "$alice_id")
play_tv() {
  local event=$1 path
  case $event in start) path=/Sessions/Playing ;; progress) path=/Sessions/Playing/Progress ;; stop) path=/Sessions/Playing/Stopped ;; esac
  req_dev "$alice" kids-check-livetv POST "$path" "$(jq -nc --arg i "$peke" --arg l "$peke_live" \
    '{ItemId: $i, PlaySessionId: "kids-check-livetv", LiveStreamId: $l, IsPaused: false, CanSeek: false, PlayMethod: "Transcode"}')"
  [[ $status == 204 ]] || echo "  (live playback $event answered $status ${body:0:200})"
}
play_tv start
for ((i = 0; i < 6; i++)); do sleep 10; play_tv progress; done
play_tv stop
sleep 2
req "$admin" GET /Sessions
after=$(used "$alice" "$alice_id")
near $((after - before)) 60 15 "cm 60 s of PekeTV counts as screen time"

# cn. Removing her channel refuses its stream at the next request
set_alice '{}'
check "cn admin removes PekeTV from alice" 200 '.LiveTvChannelIds == []'
sleep 1
req_h "$alice" kids-check-tv GET "/Videos/$peke/master.m3u8?MediaSourceId=$peke_source&LiveStreamId=$peke_live"
refused "cn her PekeTV stream is refused once it is removed" liveTvChannel

# Leave alice with no channels, and close the live streams
for live in "$peke_live" "$news_live"; do
  [[ -n $live ]] && req "$admin" POST "/LiveStreams/Close?liveStreamId=$(jq -rn --arg l "$live" '$l | @uri')"
done

# Reset today (feature 7, quickstart.md §2 da–dg). Feature 6 left alice with no channels, no allowance and enforcement
# off; this section turns enforcement on and off again, and purges her screen time at the end
req - GET /Kids/Info
check "da Info lists watchTimeReset" 200 '(.Features | index("watchTimeReset")) != null'

# db. Only grown-ups reset a day: not the kid, not another kid, not a grant used from another device
set_alice '{"DailyAllowanceMinutes": 1}'
check "db admin sets a 1-minute allowance" 200
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Bonus" '{"Minutes": 5}'
check "db admin adds 5 minutes" 200 '.BonusSeconds >= 300'
bonus_before=$(jq -r .BonusSeconds <<<"$body")
req "$alice" POST "/Kids/Users/$alice_id/WatchTime/Reset"
check "db alice cannot reset her own day" 403
req_dev "$bob_t" kids-check-bob POST "/Kids/Users/$alice_id/WatchTime/Reset"
check "db bob cannot reset alice's day" 403
verify "$alice_a" kids-check-A "$known"
reset_grant=$(jq -r .Grant <<<"$body")
req_dev "$alice_b" kids-check-B POST "/Kids/Users/$alice_id/WatchTime/Reset" '' "$reset_grant"
check "db a grant from alice's TV A is refused on TV B" 403
req "$alice" GET "/Kids/Users/$alice_id/Status"
check "db her bonus is untouched" 200 ".BonusSeconds == $bonus_before"

# dc. A user who is not a kid is not found
req "$admin" POST /Kids/Users/00000000000000000000000000000001/WatchTime/Reset
check "dc reset of a user who is not a kid is not found" 404

# dd. With time used and enforcement on she is refused; a reset clears used time and bonus and lifts it at once
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Reset"
check "dd admin resets alice's day" 200 '.UsedSeconds == 0 and .BonusSeconds == 0'
if [[ $(date -d '-1 hour' +%F) == $(date +%F) ]]; then
  reset_started=$(date -u -d '-1 hour' +%Y-%m-%dT%H:%M:%SZ)
else
  reset_started=$(date -u -d '-1 minute' +%Y-%m-%dT%H:%M:%SZ)
fi
reset_segment="{\"Segments\":[{\"Id\":\"$(new_id)\",\"StartedAt\":\"$reset_started\",\"Seconds\":120}]}"
req "$alice" POST "/Kids/Users/$alice_id/WatchTime/Report" "$reset_segment"
check "dd alice reports 2 minutes" 200 '.UsedSeconds >= 120 and .TimeUp == true'
configure '.EnforcementEnabled = true'
playback_info "$alice" kids-check-tv
check "dd with time up she is NotAllowed" 200 '.ErrorCode == "NotAllowed"'
log_file=$(ls -t "$dev"/log/log_*.log | head -1)
reset_lines() { grep -cE "Screen time of .* reset for .* by .?${DEV_ADMIN_USER:-admin}.?: 2 min used and 0 min bonus cleared" "$log_file" || true; }
lines_before=$(reset_lines)
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Reset"
check "dd the reset answer has nothing used, no bonus and no denial" 200 \
  '.UsedSeconds == 0 and .BonusSeconds == 0 and .TimeUp == false and .Enforced == true and (has("DeniedReason") | not)'
playback_info "$alice" kids-check-tv
check "dd PlaybackInfo has media again at once" 200 '(.MediaSources | length) > 0'
sleep 1
near $(($(reset_lines) - lines_before)) 1 0 "dd the reset is logged with who and what"

# df. A segment reported before the reset adds nothing when sent again
req "$alice" POST "/Kids/Users/$alice_id/WatchTime/Report" "$reset_segment"
check "df the segment sent again is accepted and adds nothing" 200 '.UsedSeconds == 0'

# de. A grant holder on alice's TV resets her day
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Bonus" '{"Minutes": 5}'
verify "$alice_a" kids-check-A "$known"
reset_grant=$(jq -r .Grant <<<"$body")
req_dev "$alice_a" kids-check-A POST "/Kids/Users/$alice_id/WatchTime/Reset" '' "$reset_grant"
check "de a grant holder on alice's TV resets her day" 200 '.BonusSeconds == 0'
sleep 1
if grep -qE "Screen time of .* reset for .* by .?alice with a parent grant.?:" "$log_file"; then
  echo "PASS de the log says the reset came through a parent grant"
else
  echo "FAIL de the log does not name the parent grant"
  failures=$((failures + 1))
fi

# dg. An allowance of 0 means no screen time today: a reset clears the day but she stays refused
set_alice '{"DailyAllowanceMinutes": 0}'
check "dg admin sets an allowance of 0" 200
req "$admin" POST "/Kids/Users/$alice_id/WatchTime/Reset"
check "dg after a reset an allowance of 0 still refuses" 200 '.UsedSeconds == 0 and .DeniedReason == "timeUp"'

# Leave enforcement off and alice with no allowance and no screen time, as bj did
configure '.EnforcementEnabled = false'
req "$admin" DELETE "/Kids/Users/$alice_id/Profile?purge=true"
check "dg admin unmarks alice with purge" 204
req "$admin" PUT "/Kids/Users/$alice_id/Profile" '{"Revision":0}'
check "dg admin marks alice again" 200

# Leave bob unthrottled, so the next run starts clean (keep this block last)
echo "Waiting ${bob_wait}s for bob's throttle to end"
sleep $((bob_wait + 1))
verify "$bob_t" kids-check-bob "$known"
check "p bob verifies after the wait" 200 '.Valid == true'

# Values to search the server log for (quickstart.md §6): none of them may appear there
echo "Log check values: codes $known 2468 $wholphin_code; grants $grant_a $last_grant; verifier hash $verifier_hash"

if ((failures > 0)); then
  echo "$failures check(s) failed"
  exit 1
fi
echo "All checks passed"
