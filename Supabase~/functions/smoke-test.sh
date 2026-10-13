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
      create) jq -cn '{mode:"live"}' ;;
      join) jq -cn --arg inviteCode "$1" '{inviteCode:$inviteCode}' ;;
      view) jq -cn --arg matchId "$1" '{matchId:$matchId}' ;;
      command) jq -cn --arg match "$1" --argjson seat "$2" --argjson revision "$3" --arg nonce "$4" --argjson sequence "$5" --argjson move "$6" '{ProtocolVersion:1,MatchId:$match,Seat:$seat,ClientMoveSequence:$sequence,ExpectedRevision:$revision,Move:$move,ClientNonce:$nonce}' ;;
    esac
  else
    python3 -c 'import json,sys
kind=sys.argv[1]; a=sys.argv[2:]
if kind=="profile": value=[{"id":a[0],"username":a[1]}]
elif kind=="create": value={"mode":"live"}
elif kind=="join": value={"inviteCode":a[0]}
elif kind=="view": value={"matchId":a[0]}
else: value={"ProtocolVersion":1,"MatchId":a[0],"Seat":int(a[1]),"ClientMoveSequence":int(a[4]),"ExpectedRevision":int(a[2]),"Move":json.loads(a[5]),"ClientNonce":a[3]}
print(json.dumps(value,separators=(",",":")))' "$kind" "$@"
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
    jq -c '{ProtocolVersion,Accepted,Reason,NewRevision,LogEntry}'
  else
    python3 -c 'import json,sys
value=json.load(sys.stdin)
print(json.dumps({key:value[key] for key in ("ProtocolVersion","Accepted","Reason","NewRevision","LogEntry")},separators=(",",":"),sort_keys=True))'
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
  curl --fail-with-body --silent --show-error \
    -X POST "$SUPABASE_URL/rest/v1/profiles?on_conflict=id" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H "Content-Type: application/json" \
    -H "Prefer: resolution=merge-duplicates,return=minimal" \
    -d "$body" >/dev/null
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

call_function join-match "$token1" "$(make_json join "$invite_code")" >/dev/null
view_body=$(make_json view "$match_id")
view0=$(call_function get-view "$token0" "$view_body")
view1=$(call_function get-view "$token1" "$view_body")
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
move=$(printf '%s' "$current_view" | json_compact view.View.LegalMoves.0)
nonce="e1-$match_id-1"
command=$(make_json command "$match_id" "$current_seat" "$revision" "$nonce" 1 "$move")

accepted=$(call_function submit-move "$current_token" "$command")
printf '%s' "$accepted" | assert_true Accepted true
accepted_revision=$(printf '%s' "$accepted" | json_raw NewRevision)

idempotent=$(call_function submit-move "$current_token" "$command")
printf '%s' "$idempotent" | assert_true Accepted true
accepted_signature=$(printf '%s' "$accepted" | result_signature)
idempotent_signature=$(printf '%s' "$idempotent" | result_signature)
[[ $idempotent_signature == "$accepted_signature" ]] || { echo "idempotent result changed" >&2; exit 1; }

latest0=$(call_function get-view "$token0" "$view_body")
latest1=$(call_function get-view "$token1" "$view_body")
persisted_revision=$(printf '%s' "$latest0" | json_raw match.revision)
[[ $persisted_revision == "$accepted_revision" ]] || { echo "idempotent retry advanced revision" >&2; exit 1; }
next_seat=$(printf '%s' "$latest0" | json_raw match.current_seat)
if [[ $next_seat == 0 ]]; then
  next_view=$latest0
  next_token=$token0
else
  next_view=$latest1
  next_token=$token1
fi
stale_move=$(printf '%s' "$next_view" | json_compact view.View.LegalMoves.0)
stale_command=$(make_json command "$match_id" "$next_seat" "$revision" "e1-$match_id-stale" 2 "$stale_move")
stale=$(call_function submit-move "$next_token" "$stale_command")
printf '%s' "$stale" | assert_true Accepted false
stale_reason=$(printf '%s' "$stale" | json_raw Reason)
[[ $stale_reason == "Command revision is stale." ]] || { echo "expected stale revision rejection, got: $stale_reason" >&2; exit 1; }

echo "E1 smoke test passed for match $match_id"
