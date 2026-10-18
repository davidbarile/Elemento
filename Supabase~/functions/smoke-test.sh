#!/usr/bin/env bash
set -euo pipefail

: "${SUPABASE_URL:?SUPABASE_URL is required}"
: "${ANON_KEY:?ANON_KEY is required}"
command -v python3 >/dev/null 2>&1 || { echo "smoke-test requires python3" >&2; exit 1; }

SUPABASE_URL=${SUPABASE_URL%/}
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
INITIAL_STATE_FIXTURE="$SCRIPT_DIR/fixtures/initial-state-v2.json"

json_get() {
  python3 -c 'import json,sys
value=json.load(sys.stdin)
for part in sys.argv[1].split("."):
    value=value[int(part)] if isinstance(value,list) else value[part]
if isinstance(value,bool): print(str(value).lower())
elif value is None: print("null")
elif isinstance(value,(dict,list)): print(json.dumps(value,separators=(",",":"),sort_keys=True))
else: print(value)' "$1"
}

assert_json() {
  local body=$1 path=$2 expected=$3 actual
  actual=$(printf '%s' "$body" | json_get "$path")
  [[ $actual == "$expected" ]] || {
    echo "assertion failed: $path expected $expected, got $actual" >&2
    exit 1
  }
}

json_length() {
  python3 -c 'import json,sys; print(len(json.load(sys.stdin)))'
}

make_json() {
  local kind=$1
  shift
  python3 - "$kind" "$INITIAL_STATE_FIXTURE" "$@" <<'PY'
import json
import sys

kind, fixture_path, *args = sys.argv[1:]
if kind == "profile":
    value = [{"id": args[0], "username": args[1]}]
elif kind == "create":
    with open(fixture_path, encoding="utf-8") as source:
        state = json.load(source)
    value = {
        "ProtocolVersion": 2,
        "Rules": state["Rules"],
        "InitialState": state,
        "InitialStateHash": "smoke-initial-v2",
    }
elif kind == "join":
    value = {"ProtocolVersion": 2, "InviteCode": args[0]}
elif kind == "view":
    value = {"ProtocolVersion": 2, "MatchId": args[0], "SinceRevision": int(args[1])}
elif kind == "list":
    value = {"ProtocolVersion": 2}
elif kind == "old_view":
    value = {"ProtocolVersion": 1, "MatchId": args[0], "SinceRevision": 0}
elif kind == "turn":
    match_id, seat, revision, nonce, next_seat = args
    seat, revision, next_seat = int(seat), int(revision), int(next_seat)
    with open(fixture_path, encoding="utf-8") as source:
        final_state = json.load(source)
    final_state.update({
        "MatchId": match_id,
        "Revision": revision + 1,
        "TurnNumber": revision + 1,
        "CurrentPlayerId": next_seat,
    })
    value = {
        "ProtocolVersion": 2,
        "MatchId": match_id,
        "Seat": seat,
        "ExpectedRevision": revision,
        "ClientNonce": nonce,
        "Turn": {
            "MatchId": match_id,
            "Seat": seat,
            "TurnNumber": revision + 1,
            "StartRevision": revision,
            "Actions": [{
                "Index": 0,
                "Move": None,
                "DrawnCards": [],
                "PowerCard": None,
                "Kind": "EndTurn",
            }],
            "FinalState": final_state,
            "FinalStateHash": f"smoke-state-{revision + 1}",
            "Status": "active",
            "WinnerSeat": -1,
            "WinCondition": 0,
            "NextSeat": next_seat,
        },
    }
else:
    raise SystemExit(f"unknown JSON kind: {kind}")
print(json.dumps(value, separators=(",", ":")))
PY
}

signup() {
  curl --fail-with-body --silent --show-error -X POST "$SUPABASE_URL/auth/v1/signup" -H "apikey: $ANON_KEY" -H "Content-Type: application/json" -d '{}'
}

upsert_profile() {
  local token=$1 body=$2 status
  status=$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' -X POST "$SUPABASE_URL/rest/v1/profiles" -H "apikey: $ANON_KEY" -H "Authorization: Bearer $token" -H "Content-Type: application/json" -H "Prefer: return=minimal" -d "$body")
  [[ $status == 2?? || $status == 409 ]] || {
    echo "profile insert failed with HTTP $status" >&2
    return 1
  }
}

call_function() {
  local name=$1 token=$2 body=$3
  curl --fail-with-body --silent --show-error -X POST "$SUPABASE_URL/functions/v1/$name" -H "apikey: $ANON_KEY" -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "$body"
}

call_function_with_status() {
  local name=$1 token=$2 body=$3
  curl --silent --show-error -X POST "$SUPABASE_URL/functions/v1/$name" -H "apikey: $ANON_KEY" -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "$body" --write-out $'\n%{http_code}'
}

echo "Creating two anonymous users..."
auth0=$(signup)
auth1=$(signup)
token0=$(printf '%s' "$auth0" | json_get access_token)
token1=$(printf '%s' "$auth1" | json_get access_token)
user0=$(printf '%s' "$auth0" | json_get user.id)
user1=$(printf '%s' "$auth1" | json_get user.id)
upsert_profile "$token0" "$(make_json profile "$user0" "e2_${user0:0:8}")"
upsert_profile "$token1" "$(make_json profile "$user1" "e2_${user1:0:8}")"

created=$(call_function create-match "$token0" "$(make_json create)")
match_id=$(printf '%s' "$created" | json_get MatchId)
invite_code=$(printf '%s' "$created" | json_get InviteCode)
assert_json "$created" MatchRevision 0
assert_json "$created" CurrentSeat 0
assert_json "$created" State.CurrentPlayerId 0
assert_json "$created" Rules.TotalPointsRequiredToWin 75

joined=$(call_function join-match "$token1" "$(make_json join "$invite_code")")
assert_json "$joined" MatchId "$match_id"
assert_json "$joined" Status active
assert_json "$joined" MatchRevision 0
assert_json "$joined" YourSeat 1
assert_json "$joined" State.CurrentPlayerId 0

view0=$(call_function get-view "$token0" "$(make_json view "$match_id" 0)")
view1=$(call_function get-view "$token1" "$(make_json view "$match_id" 0)")
assert_json "$view0" YourSeat 0
assert_json "$view1" YourSeat 1
assert_json "$view0" SeatNames.1 "e2_${user1:0:8}"

nonce="v2-$match_id-turn-1"
turn=$(make_json turn "$match_id" 0 0 "$nonce" 1)
accepted=$(call_function submit-turn "$token0" "$turn")
assert_json "$accepted" Accepted true
assert_json "$accepted" NewRevision 1
assert_json "$accepted" CurrentSeat 1
assert_json "$accepted" Status active

stale=$(call_function submit-turn "$token1" "$(make_json turn "$match_id" 1 0 "v2-$match_id-stale" 0)")
assert_json "$stale" Accepted false
assert_json "$stale" Reason stale_revision
assert_json "$stale" NewRevision 1

idempotent=$(call_function submit-turn "$token0" "$turn")
assert_json "$idempotent" Accepted true
assert_json "$idempotent" NewRevision 1
assert_json "$idempotent" CurrentSeat 1

not_your_turn=$(call_function submit-turn "$token0" "$(make_json turn "$match_id" 0 1 "v2-$match_id-wrong-seat" 1)")
assert_json "$not_your_turn" Accepted false
assert_json "$not_your_turn" Reason not_your_turn
assert_json "$not_your_turn" NewRevision 1

updated=$(call_function get-view "$token1" "$(make_json view "$match_id" 0)")
assert_json "$updated" MatchRevision 1
assert_json "$updated" State.CurrentPlayerId 1
turn_count=$(printf '%s' "$updated" | json_get Turns | json_length)
[[ $turn_count == 1 ]] || { echo "expected one stored turn, got $turn_count" >&2; exit 1; }
assert_json "$updated" Turns.0.FinalStateHash smoke-state-1

mine0=$(call_function list-my-matches "$token0" "$(make_json list)")
mine1=$(call_function list-my-matches "$token1" "$(make_json list)")
assert_json "$mine0" Matches.0.MatchId "$match_id"
assert_json "$mine0" Matches.0.YourSeat 0
assert_json "$mine0" Matches.0.OpponentName "e2_${user1:0:8}"
assert_json "$mine1" Matches.0.MatchId "$match_id"
assert_json "$mine1" Matches.0.YourSeat 1

old_response=$(call_function_with_status get-view "$token0" "$(make_json old_view "$match_id")")
old_status=${old_response##*$'\n'}
old_body=${old_response%$'\n'*}
[[ $old_status == 426 ]] || { echo "expected HTTP 426, got $old_status" >&2; exit 1; }
assert_json "$old_body" Error update_required
assert_json "$old_body" ProtocolVersion 2

echo "Protocol v2 smoke test passed for match $match_id"
