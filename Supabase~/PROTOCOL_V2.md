# Online protocol v3 (compact client-authoritative turns) — shared contract

ProtocolVersions.Current = 3. Every request body carries `ProtocolVersion`. Any function receiving a version != 3
returns HTTP 426 `{ "Error": "update_required", "Reason": "Please update Elemento to play online.", "ProtocolVersion": 3 }`.

The server performs NO rule validation and NO random generation. State/packages are opaque JSON.
Supabase wire states store each distinct card definition once and ordered card references for decks,
hands, and zones; the client expands them back to exact `GameStateData` before hashing or replay.

## Domain types (C# in Assets/Scripts/Domain/Net/ProtocolData.cs)

Request and response envelopes use these PascalCase field names. `GameStateData` values stored in
`InitialState`, `FinalState`, and `State` are encoded by `GameStateWireCodec` with compact field names;
the client decodes them before hashing, replay, or gameplay.
- TurnActionData { int Index; MoveData Move; List<CardInstanceData> DrawnCards; CardInstanceData PowerCard; string Kind }
  Kind: "Move" | "Undo" | "EndTurn". DrawnCards/PowerCard record every random outcome produced by that action
  (power card type + id, cards drawn incl. deck rebuilds). Undo rows restore the previous checkpoint.
- TurnPackageData { string MatchId; int Seat; int TurnNumber; int StartRevision; List<TurnActionData> Actions;
  GameStateData FinalState; string FinalStateHash; string Status ("active"|"finished"); int WinnerSeat (-1 none);
  EWinCondition WinCondition; int NextSeat }
- SubmitTurnRequestData { int ProtocolVersion; string MatchId; int Seat; int ExpectedRevision; string ClientNonce; TurnPackageData Turn }
  The client replaces the simulator's local `GameStateData.MatchId` with the server-assigned `MatchId` before recording/submitting.
  `Turn.MatchId` must equal the outer `MatchId`; submit-turn fills an omitted/empty redundant `Turn.MatchId` for compatibility,
  but rejects a non-empty mismatch. Malformed envelopes return HTTP 400 with the failed check in `Reason`.
- SubmitTurnResultData { bool Accepted; string Reason; int NewRevision; int CurrentSeat; string Status }
  Rejections: 200 + Accepted=false + Reason in {"stale_revision","not_your_turn","match_not_active"}. Same nonce retry => Accepted=true idempotently.
- CreateMatchRequestData { int ProtocolVersion; RulesConfigData Rules; GameStateData InitialState; string InitialStateHash }
  Creator device generates seed, shuffled deck, initial deal and starting seat (coin flip) in InitialState. Revision 0.
- MatchStateUpdateData (get-view / join-match / submit result fetch) { string MatchId; string Status; int MatchRevision;
  int CurrentSeat; int YourSeat; int WinnerSeat; RulesConfigData Rules; GameStateData State (latest committed full state);
  List<TurnPackageData> Turns (revision > SinceRevision, ascending); List<string> SeatNames;
  int OpponentLastSeenSecondsAgo (-1 unknown) }
- get-view request { ProtocolVersion, MatchId, SinceRevision }; poll uses SinceRevision = last applied revision.
- heartbeat request { ProtocolVersion, MatchId } -> { OpponentLastSeenSecondsAgo: int }.
  Authenticated membership is required (404 for a nonmember); old protocol versions receive 426.
  Both heartbeat and get-view stamp the caller's last_seen; submit-turn stamps after envelope and
  seat-ownership validation, including idempotent retries. Opponent age is nonnegative whole seconds
  measured with the database clock, or -1 if the opponent is absent/has never been observed.
  create/join responses default OpponentLastSeenSecondsAgo to -1 until get-view supplies an observation.
- list-my-matches request { ProtocolVersion } -> { Matches: [ {MatchId, Status, MatchRevision, CurrentSeat, YourSeat, OpponentName, UpdatedAt} ] } for status in (waiting, active).

## DB (migration 0005)
- table match_turns(match_id, revision int, seat int, client_nonce text unique per match, package jsonb, created_at) PK(match_id, revision).
- matches keeps single `revision`; `match_state(match_id pk, state jsonb, state_hash text)` holds latest full state (reuse match_secrets.state if simpler).
- RPC commit_client_turn(p_match_id, p_seat, p_expected_revision, p_nonce, p_package, p_state, p_next_seat, p_status, p_winner_seat)
  compare-and-set revision; revision += 1; current_seat = p_next_seat; status/winner from client.
- drop submit-move path: drop function commit_move, commit_turn (old). Revoke from anon/authenticated.
- abandon_stale_matches(): marks active/waiting matches with updated_at older than 3 days abandoned; scheduled with pg_cron hourly if available.
- Realtime: after commit, broadcast `{revision}` on the match channel as today.

## Presence and interruption recovery (migration 0007)

- match_players.last_seen is a nullable timestamptz, initially unknown for existing and new players.
- Service-only RPC touch_match_presence(p_match_id uuid, p_user_id uuid) verifies protocol-v3
  membership, stamps the caller with database time, and returns the opponent age (-1 unknown).
  A missing match/membership returns null, mapped by the Edge Function to HTTP 404.
- The foreground client heartbeats about every 15 seconds while an online match is open and on
  focus regain. Presence does not advance revision or modify matches.updated_at/status/winner.
  The existing three-day abandon_stale_matches sweeper is unchanged.
- At about 20 seconds without an opponent observation, show the localized opponent-away banner.
  At about 120 seconds away in a live game, offer Keep waiting (re-arm) or Leave and continue later
  (Home, no leave-match). Explicit Home/forfeit confirmation remains the only forfeit action.
- Pause, focus loss, app/Editor quit, and network failures never forfeit. Persist the active match
  id and last applied committed revision locally. On launch validate the saved match through
  list-my-matches; on foreground return or resume fetch get-view with that SinceRevision and replay
  missed packages in ascending revision order through TurnReplayer, with Skip available.
- Persist a submit's complete request and ClientNonce before sending. After an interrupted submit,
  retry that same request/nonce before resyncing; the unique nonce guarantees one commit even if
  the first response was lost. Uncommitted local moves may restore from the last committed state.

## C# smoke fixtures

Generate protocol JSON from the same C# types and `NetJson` serializer used by Unity:

```sh
dotnet run --project Simulation~/Harness -- export-first-turn --out Supabase~/functions/fixtures/generated-first-turn
```

This writes `create-match.json` and `submit-turn.json`. Both use `MATCH_ID` where a server-assigned id belongs.
`functions/smoke-test.sh` automatically uses these files from that directory when present, or from
`HARNESS_FIXTURE_DIR` when set, replacing `MATCH_ID` in the first submit request with the created match id.
