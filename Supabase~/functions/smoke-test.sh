#!/usr/bin/env bash
set -euo pipefail

: "${SUPABASE_URL:?SUPABASE_URL is required}"
: "${ANON_KEY:?ANON_KEY is required}"

if command -v jq >/dev/null 2>&1; then
  JSON_TOOL=jq
elif command -v python3 >/dev/null 2>&1; then
  JSON_TOOL=python3
else
  echo "smoke-test requires jq or python3" >&2
  exit 1
fi

SUPABASE_URL=${SUPABASE_URL%/}

json_raw() {
  local path=$1
  if [[ $JSON_TOOL == jq ]]; then
    jq -r --arg path "$path" 'getpath($path | split(".") | map(if test("^[0-9]+$") then tonumber else . end))'
  else
    python3 -c 'import json,sys
value=json.load(sys.stdin)
for part in sys.argv[1].split("."):
    value=value[int(part)] if isinstance(value,list) else value[part]
print(value if isinstance(value,str) else json.dumps(value,separators=(",",":")))' "$path"
  fi
}

json_compact() {
  local path=$1
  if [[ $JSON_TOOL == jq ]]; then
    jq -c --arg path "$path" 'getpath($path | split(".") | map(if test("^[0-9]+$") then tonumber else . end))'
  else
    json_raw "$path"
  fi
}

make_json() {
  local kind=$1
  shift
  if [[ $JSON_TOOL == jq ]]; then
    case "$kind" in
      profile) jq -cn --arg id "$1" --arg username "$2" '[{id:$id,username:$username}]' ;;
      create) jq -cn '{mode:"live",rules:{NumCardsToDeal:4,MaxCardsInHand:9,TotalPointsRequiredToWin:75,AllowUndo:true}}' ;;
      join) jq -cn --arg inviteCode "$1" '{inviteCode:$inviteCode}' ;;
      view) jq -cn --arg matchId "$1" '{matchId:$matchId}' ;;
      view_log) jq -cn --arg matchId "$1" --argjson sinceRevision "$2" '{matchId:$matchId,sinceRevision:$sinceRevision}' ;;
      turn) jq -cn --arg match "$1" --argjson seat "$2" --argjson revision "$3" --arg nonce "$4" --argjson moves "$5" '{MatchId:$match,Seat:$seat,ExpectedRevision:$revision,ClientNonce:$nonce,Moves:$moves}' ;;
    esac
  else
    python3 -c 'import json,sys
kind=sys.argv[1]; a=sys.argv[2:]
if kind=="profile": value=[{"id":a[0],"username":a[1]}]
elif kind=="create": value={"mode":"live","rules":{"NumCardsToDeal":4,"MaxCardsInHand":9,"TotalPointsRequiredToWin":75,"AllowUndo":True}}
elif kind=="join": value={"inviteCode":a[0]}
elif kind=="view": value={"matchId":a[0]}
elif kind=="view_log": value={"matchId":a[0],"sinceRevision":int(a[1])}
else: value={"MatchId":a[0],"Seat":int(a[1]),"ExpectedRevision":int(a[2]),"ClientNonce":a[3],"Moves":json.loads(a[4])}
print(json.dumps(value,separators=(",",":")))' "$kind" "$@"
  fi
}

json_array_length() {
  if [[ $JSON_TOOL == jq ]]; then
    jq -r 'length'
  else
    python3 -c 'import json,sys; print(len(json.load(sys.stdin)))'
  fi
}

turn_moves() {
  local mode=$1
  if [[ $JSON_TOOL == jq ]]; then
    jq -ce --arg mode "$mode" '
      .view.View.LegalMoves as $legal
      | ($legal | map(select(.Type == 5)) | first) as $end
      | ($legal | map(select(.Type != 5)) | first) as $action
      | if $end == null then error("EndTurn is not legal")
        elif $mode == "end" then [$end]
        elif $action == null then error("no non-EndTurn move is legal")
        else [$action, $end]
        end'
  else
    python3 -c 'import json,sys
value=json.load(sys.stdin); mode=sys.argv[1]; legal=value["view"]["View"]["LegalMoves"]
end=next((move for move in legal if move["Type"]==5),None)
action=next((move for move in legal if move["Type"]!=5),None)
assert end is not None, "EndTurn is not legal"
assert mode=="end" or action is not None, "no non-EndTurn move is legal"
print(json.dumps([end] if mode=="end" else [action,end],separators=(",",":")))' "$mode"
  fi
}

bogus_moves() {
  if [[ $JSON_TOOL == jq ]]; then
    jq -ce '
      .view.View.LegalMoves as $legal
      | ($legal | map(select(.Type == 5)) | first) as $end
      | ($legal | map(select(.Type != 5)) | first) as $action
      | if $end == null or $action == null then error("bogus-card test needs action and EndTurn")
        else [$action, ($action + {CardId:"bogus-card-id"}), $end]
        end'
  else
    python3 -c 'import json,sys
value=json.load(sys.stdin); legal=value["view"]["View"]["LegalMoves"]
end=next((move for move in legal if move["Type"]==5),None)
action=next((move for move in legal if move["Type"]!=5),None)
assert end is not None and action is not None, "bogus-card test needs action and EndTurn"
bogus=dict(action); bogus["CardId"]="bogus-card-id"
print(json.dumps([action,bogus,end],separators=(",",":")))'
  fi
}

assert_view_safe() {
  if [[ $JSON_TOOL == jq ]]; then
    jq -e '
      ([.. | objects | keys[] | ascii_downcase | select(. == "seed" or . == "state")] | length == 0)
      and (.view.View.Player.Hand | type == "array")
      and (.view.View.Opponents | all(has("Hand") | not))
      and (.view.View.Opponents | all(.HandCount | type == "number"))
    ' >/dev/null
  else
    python3 -c 'import json,sys
value=json.load(sys.stdin)
def keys(v):
    if isinstance(v,dict):
        for k,x in v.items(): yield k.lower(); yield from keys(x)
    elif isinstance(v,list):
        for x in v: yield from keys(x)
assert not ({"seed","state"} & set(keys(value)))
view=value["view"]["View"]
assert isinstance(view["Player"]["Hand"],list)
assert all("Hand" not in opponent and isinstance(opponent["HandCount"],int) for opponent in view["Opponents"])'
  fi
}

assert_true() {
  local path=$1 expected=$2
  local actual
  actual=$(json_raw "$path")
  [[ $actual == "$expected" ]] || { echo "assertion failed: $path expected $expected, got $actual" >&2; exit 1; }
}

result_signature() {
  if [[ $JSON_TOOL == jq ]]; then
    jq -cS '{Accepted,NewRevision,MoveCount}'
  else
    python3 -c 'import json,sys
value=json.load(sys.stdin)
print(json.dumps({key:value[key] for key in ("Accepted","NewRevision","MoveCount")},separators=(",",":"),sort_keys=True))'
  fi
}

signup() {
  curl --fail-with-body --silent --show-error \
    -X POST "$SUPABASE_URL/auth/v1/signup" \
    -H "apikey: $ANON_KEY" \
    -H "Content-Type: application/json" \
    -d '{}'
}

upsert_profile() {
  local token=$1 body=$2
  local status
  status=$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    -X POST "$SUPABASE_URL/rest/v1/profiles" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H "Content-Type: application/json" \
    -H "Prefer: return=minimal" \
    -d "$body")
  [[ $status == 2?? || $status == 409 ]] || { echo "profile insert failed with HTTP $status" >&2; return 1; }
}

call_function() {
  local name=$1 token=$2 body=$3
  curl --fail-with-body --silent --show-error \
    -X POST "$SUPABASE_URL/functions/v1/$name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H "Content-Type: application/json" \
    -d "$body"
}

call_function_with_status() {
  local name=$1 token=$2 body=$3
  curl --silent --show-error \
    -X POST "$SUPABASE_URL/functions/v1/$name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H "Content-Type: application/json" \
    -d "$body" \
    --write-out $'\n%{http_code}'
}

echo "Creating two anonymous users..."
auth0=$(signup)
auth1=$(signup)
token0=$(printf '%s' "$auth0" | json_raw access_token)
token1=$(printf '%s' "$auth1" | json_raw access_token)
user0=$(printf '%s' "$auth0" | json_raw user.id)
user1=$(printf '%s' "$auth1" | json_raw user.id)
[[ -n $token0 && -n $token1 && -n $user0 && -n $user1 ]]

upsert_profile "$token0" "$(make_json profile "$user0" "e1_${user0:0:8}")"
upsert_profile "$token1" "$(make_json profile "$user1" "e1_${user1:0:8}")"

created=$(call_function create-match "$token0" "$(make_json create)")
match_id=$(printf '%s' "$created" | json_raw match.id)
invite_code=$(printf '%s' "$created" | json_raw match.invite_code)
[[ -n $match_id && -n $invite_code ]]
printf '%s' "$created" | assert_true match.rules.TotalPointsRequiredToWin 75
printf '%s' "$created" | assert_true match.rules.AllowUndo true

view_body=$(make_json view "$match_id")
waiting_response=$(call_function_with_status get-view "$token0" "$view_body")
waiting_status=${waiting_response##*$'\n'}
waiting_view=${waiting_response%$'\n'*}
[[ $waiting_status == 200 ]] || { echo "waiting get-view failed with HTTP $waiting_status" >&2; exit 1; }
printf '%s' "$waiting_view" | assert_true match.status waiting
printf '%s' "$waiting_view" | assert_true match.rules.TotalPointsRequiredToWin 75
printf '%s' "$waiting_view" | assert_true match.rules.AllowUndo true
printf '%s' "$waiting_view" | assert_true view null

joined=$(call_function join-match "$token1" "$(make_json join "$invite_code")")
printf '%s' "$joined" | assert_true match.rules.TotalPointsRequiredToWin 75
printf '%s' "$joined" | assert_true match.rules.AllowUndo true
printf '%s' "$joined" | assert_true view.View.Rules.TotalPointsRequiredToWin 75
view0=$(call_function get-view "$token0" "$view_body")
view1=$(call_function get-view "$token1" "$view_body")
printf '%s' "$view0" | assert_true match.rules.TotalPointsRequiredToWin 75
printf '%s' "$view1" | assert_true match.rules.TotalPointsRequiredToWin 75
printf '%s' "$view0" | assert_true view.View.Rules.AllowUndo true
printf '%s' "$view1" | assert_true view.View.Rules.AllowUndo true
printf '%s' "$view0" | assert_view_safe
printf '%s' "$view1" | assert_view_safe

current_seat=$(printf '%s' "$view0" | json_raw match.current_seat)
revision=$(printf '%s' "$view0" | json_raw match.revision)
if [[ $current_seat == 0 ]]; then
  current_view=$view0
  current_token=$token0
else
  current_view=$view1
  current_token=$token1
fi
end_moves=$(printf '%s' "$current_view" | turn_moves end)
first_turn=$(make_json turn "$match_id" "$current_seat" "$revision" "t2a-$match_id-1" "$end_moves")
first_accepted=$(call_function submit-turn "$current_token" "$first_turn")
printf '%s' "$first_accepted" | assert_true Accepted true
printf '%s' "$first_accepted" | assert_true MoveCount 1
first_revision=$(printf '%s' "$first_accepted" | json_raw NewRevision)

latest0=$(call_function get-view "$token0" "$view_body")
latest1=$(call_function get-view "$token1" "$view_body")
next_seat=$(printf '%s' "$latest0" | json_raw match.current_seat)
if [[ $next_seat == 0 ]]; then
  next_view=$latest0
  next_token=$token0
else
  next_view=$latest1
  next_token=$token1
fi
action_moves=$(printf '%s' "$next_view" | turn_moves action)
action_count=$(printf '%s' "$action_moves" | json_array_length)
[[ $action_count == 2 ]] || { echo "expected action + EndTurn" >&2; exit 1; }
second_nonce="t2a-$match_id-2"
second_turn=$(make_json turn "$match_id" "$next_seat" "$first_revision" "$second_nonce" "$action_moves")
second_accepted=$(call_function submit-turn "$next_token" "$second_turn")
printf '%s' "$second_accepted" | assert_true Accepted true
printf '%s' "$second_accepted" | assert_true MoveCount 2
second_revision=$(printf '%s' "$second_accepted" | json_raw NewRevision)

idempotent=$(call_function submit-turn "$next_token" "$second_turn")
printf '%s' "$idempotent" | assert_true Accepted true
accepted_signature=$(printf '%s' "$second_accepted" | result_signature)
idempotent_signature=$(printf '%s' "$idempotent" | result_signature)
[[ $idempotent_signature == "$accepted_signature" ]] || { echo "idempotent turn result changed" >&2; echo "A=$accepted_signature" >&2; echo "B=$idempotent_signature" >&2; exit 1; }

latest0=$(call_function get-view "$token0" "$view_body")
latest1=$(call_function get-view "$token1" "$view_body")
persisted_revision=$(printf '%s' "$latest0" | json_raw match.revision)
[[ $persisted_revision == "$second_revision" ]] || { echo "idempotent retry advanced revision" >&2; exit 1; }
current_seat=$(printf '%s' "$latest0" | json_raw match.current_seat)
if [[ $current_seat == 0 ]]; then
  current_view=$latest0
  current_token=$token0
else
  current_view=$latest1
  current_token=$token1
fi

stale_moves=$(printf '%s' "$current_view" | turn_moves end)
stale_turn=$(make_json turn "$match_id" "$current_seat" "$first_revision" "t2a-$match_id-stale" "$stale_moves")
stale=$(call_function submit-turn "$current_token" "$stale_turn")
printf '%s' "$stale" | assert_true Accepted false
stale_reason=$(printf '%s' "$stale" | json_raw Reason)
[[ $stale_reason == "Command revision is stale." ]] || { echo "expected stale revision rejection, got: $stale_reason" >&2; exit 1; }

illegal_moves=$(printf '%s' "$current_view" | bogus_moves)
illegal_turn=$(make_json turn "$match_id" "$current_seat" "$second_revision" "t2a-$match_id-illegal" "$illegal_moves")
before_illegal_view=$(printf '%s' "$current_view" | json_compact view)
illegal=$(call_function submit-turn "$current_token" "$illegal_turn")
printf '%s' "$illegal" | assert_true Accepted false
printf '%s' "$illegal" | assert_true FailedIndex 1
after_illegal=$(call_function get-view "$current_token" "$view_body")
after_illegal_revision=$(printf '%s' "$after_illegal" | json_raw match.revision)
[[ $after_illegal_revision == "$second_revision" ]] || { echo "illegal turn changed revision" >&2; exit 1; }
after_illegal_view=$(printf '%s' "$after_illegal" | json_compact view)
[[ $after_illegal_view == "$before_illegal_view" ]] || { echo "illegal turn changed the seat view" >&2; exit 1; }

log_body=$(make_json view_log "$match_id" "$revision")
turn_log=$(call_function get-view "$token0" "$log_body")
move_count=$(printf '%s' "$turn_log" | json_compact moves | json_array_length)
[[ $move_count == 3 ]] || { echo "expected 3 moves after sinceRevision, got $move_count" >&2; exit 1; }
last_log_revision=$(printf '%s' "$turn_log" | json_raw moves.2.revision)
[[ $last_log_revision == "$second_revision" ]] || { echo "move log did not reach accepted revision" >&2; exit 1; }

left=$(call_function leave-match "$token0" "$view_body")
printf '%s' "$left" | assert_true match.status abandoned
printf '%s' "$left" | assert_true match.winner_seat 1
forfeit_revision=$(printf '%s' "$left" | json_raw match.revision)
expected_forfeit_revision=$((second_revision + 1))
[[ $forfeit_revision == "$expected_forfeit_revision" ]] || { echo "forfeit did not advance revision once" >&2; exit 1; }

opponent_after_leave=$(call_function get-view "$token1" "$view_body")
printf '%s' "$opponent_after_leave" | assert_true match.status abandoned
printf '%s' "$opponent_after_leave" | assert_true match.winner_seat 1
printf '%s' "$opponent_after_leave" | assert_true match.revision "$forfeit_revision"

left_again=$(call_function leave-match "$token0" "$view_body")
printf '%s' "$left_again" | assert_true match.status abandoned
printf '%s' "$left_again" | assert_true match.revision "$forfeit_revision"

forfeit_moves=$(printf '%s' "$current_view" | turn_moves end)
post_forfeit_turn=$(make_json turn "$match_id" "$current_seat" "$forfeit_revision" "t2a-$match_id-after-forfeit" "$forfeit_moves")
post_forfeit=$(call_function submit-turn "$current_token" "$post_forfeit_turn")
printf '%s' "$post_forfeit" | assert_true Accepted false
post_forfeit_reason=$(printf '%s' "$post_forfeit" | json_raw Reason)
[[ $post_forfeit_reason == "Match is not active." ]] || { echo "expected inactive match rejection, got: $post_forfeit_reason" >&2; exit 1; }

echo "T2a + forfeit smoke test passed for match $match_id"
