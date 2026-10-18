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

leave-match preserves active-match forfeits: it marks the match abandoned, assigns the other seat
as winner, advances the revision, and broadcasts. A creator can also abandon their own waiting
match. The database sweeper marks waiting or active matches abandoned after three days of
inactivity; when pg_cron is installed it runs hourly.

## Storage and access

- matches stores lifecycle, creator rules, current seat, winner, and the single turn revision.
- match_state stores the latest opaque GameStateData and its hash.
- match_turns stores one opaque TurnPackageData per committed revision and a per-match unique
  client nonce.
- match_players maps authenticated users to seats.
- profiles keeps the existing anonymous-user username flow.

RLS is enabled on state and turn storage without anon/authenticated policies. Edge Functions use
the service role; clients never write match tables directly.

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
