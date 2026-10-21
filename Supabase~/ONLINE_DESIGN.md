# Elemento Online Multiplayer v2

Protocol v2 uses client-authoritative turns. The active client applies the game rules, generates
every random outcome, and submits one complete TurnPackageData at the end of the turn. Supabase
authenticates the caller and provides durable storage, optimistic concurrency, resume, and
Realtime notifications. It does not replay moves, validate game rules, shuffle decks, deal cards,
or choose a starting player.

PROTOCOL_V2.md is the binding wire contract. JSON field names use the same PascalCase names as the
C# data types and must not be renamed.

## Match creation and joining

create-match accepts CreateMatchRequestData:

    { ProtocolVersion, Rules, InitialState, InitialStateHash }

The creator generates the complete initial GameStateData, including decks, the initial deal,
random outcomes, and the starting-seat coin flip. The server stores Rules as the match rules,
stores InitialState at revision 0, and copies InitialState.CurrentPlayerId to
matches.current_seat. It also creates seat 0 and a six-character invite code. Username/profile
requirements and anonymous Supabase authentication remain unchanged.

join-match accepts { ProtocolVersion, InviteCode }, claims seat 1, activates the match, and
returns the stored state unchanged as MatchStateUpdateData. Joining never deals cards or mutates
the game state.

## Turn commit

submit-turn accepts SubmitTurnRequestData. It checks:

1. the bearer token is valid;
2. ProtocolVersion is 2;
3. the authenticated user owns Seat;
4. the nonce was not already committed for this match;
5. the match is active;
6. ExpectedRevision equals matches.revision;
7. Seat equals matches.current_seat.

The server performs only basic envelope-shape checks. It trusts the submitted actions,
FinalState, state hash, status, winner, and next seat. Rejections caused by match state return
HTTP 200 with Reason equal to stale_revision, not_your_turn, or match_not_active. A retry with the
same nonce returns the original accepted revision without another write.

commit_client_turn performs the compare-and-set transaction. One accepted package creates one
match_turns row and increments the match revision by one. The same transaction updates the latest
full state, state hash, current seat, status, winner, and turn number. The RPC is SECURITY DEFINER,
has an empty search path, and is executable only by service_role.

After a successful or idempotent commit, the function broadcasts {revision} on the private
match:<id> Realtime topic. Clients treat the broadcast as an invalidation signal and fetch the new
data.

## Fetch and resume

get-view accepts { ProtocolVersion, MatchId, SinceRevision } and returns MatchStateUpdateData.
State is always the latest stored full state. Turns contains complete turn packages with revision
greater than SinceRevision, ordered ascending. There is no DrawCard redaction and no ReplayCard;
all random outcomes needed for replay are already inside the client-authored turn actions.

list-my-matches accepts { ProtocolVersion } and returns the caller's waiting and active matches,
including their seat, current seat, revision, opponent name, and update time. This is the resume
entry point after restart or sign-in restoration.

The client persists its active match id and last applied committed revision locally. Returning
from background reconnects/resyncs the open match; relaunch checks list-my-matches before resuming
the saved active match. get-view uses SinceRevision = lastAppliedRevision. Missed turn packages
are replayed in order using TurnReplayer, with Skip available, before accepting new local input.
An unfinished local turn may be discarded in favor of the last committed state. A pending submit
is persisted with its original package and ClientNonce before sending, and retried with that same
nonce after interruption before resyncing, including when the original response was lost.

App pause, focus loss, quit, and network errors do not call leave-match or end a match. A player
can leave the away prompt for Home and later resume through the lobby list. Only the explicit
Home/forfeit confirmation forfeits through leave-match.

## Presence and opponent away

Migration 0007 adds nullable match_players.last_seen. heartbeat accepts
{ ProtocolVersion, MatchId } and returns { OpponentLastSeenSecondsAgo }. It authenticates the
caller and stamps only their own membership row through the service-only touch_match_presence
RPC. get-view and valid, owned submit-turn requests also stamp the caller. The age is calculated
using the database clock, never a device timestamp; -1 means no opponent observation is known.
get-view returns the same age in MatchStateUpdateData; create/join responses default it to -1
until the first presence-aware fetch.

While a match is open and the app is foregrounded, the client sends a heartbeat about every
15 seconds and immediately on focus regain. Heartbeat is the authoritative presence signal;
Realtime match revision broadcasts still trigger fetches and do not imply continued presence.
At about 20 seconds without an opponent observation, the waiting banner becomes “Opponent
away...” / “Rival ausente...”, clearing when presence returns. After about two minutes away in
a live game, the existing confirmation panel offers “Keep waiting” / “Seguir esperando” and
“Leave and continue later” / “Salir y continuar después”. Keeping waiting re-arms the prompt;
leaving returns Home while preserving the active match.

Presence never updates matches.updated_at, revision, status, winner, or turn state, and never
broadcasts a turn revision. It does not modify the existing three-day abandonment policy.

leave-match preserves active-match forfeits: it marks the match abandoned, assigns the other seat
as winner, advances the revision, and broadcasts. A creator can also abandon their own waiting
match. The database sweeper marks waiting or active matches abandoned after three days of
inactivity; when pg_cron is installed it runs hourly.

## Storage and access

- matches stores lifecycle, creator rules, current seat, winner, and the single turn revision.
- match_state stores the latest opaque GameStateData and its hash.
- match_turns stores one opaque TurnPackageData per committed revision and a per-match unique
  client nonce.
- match_players maps authenticated users to seats and records advisory last_seen timestamps.
- profiles keeps the existing anonymous-user username flow.

RLS is enabled on state and turn storage without anon/authenticated policies. Edge Functions use
the service role; clients never write match tables directly.

## VS-AI telemetry

Local VS-AI games keep an atomic recovery journal and queue completed, interrupted, quit,
abandoned, restarted, and corrupt sessions for upload. The `record-ai-game` Edge Function binds
each payload to the authenticated anonymous user; clients have no direct table access. The hidden
Settings debug menu can clear that user's server records, clear local pending records, or open the
Supabase table editor. Clearing every user's records additionally requires the caller's UUID in
the comma-separated `AI_GAME_ADMIN_USER_IDS` Edge Function secret.

Apply `20261021061500_0008_ai_game_telemetry.sql`, deploy `record-ai-game` and `clear-ai-games`, set the admin
secret, then run `functions/smoke-test.sh`. The smoke test verifies upload, owner deletion, and
rejection of a non-admin all-record deletion.

Every request carries ProtocolVersion. Versions other than 2 receive HTTP 426:

    {
      "Error": "update_required",
      "Reason": "Please update Elemento to play online.",
      "ProtocolVersion": 2
    }

## Security boundary

Protocol v2 intentionally favors a simple, deterministic client experience over anti-cheat:
clients can submit any internally shaped final state while it is their turn. Server-side rule
validation, signed turn proofs, anomaly detection, or another anti-cheat layer can be added later
without changing the revision/idempotency storage model.

## Deploy and verify presence

Apply migrations through `20261018070321_0007_presence.sql` before deploying the new heartbeat function and
updated get-view and submit-turn functions. Redeploy create-match and join-match as well to pick
up the shared response field default. Run functions/smoke-test.sh against the deployed test
project; it checks unknown/recent presence, nonmember heartbeat rejection, unchanged revision,
and protocol-v1 HTTP 426 for both get-view and heartbeat.
