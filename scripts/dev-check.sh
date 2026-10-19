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
