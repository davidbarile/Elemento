# Online protocol v2 (client-authoritative turns) — shared contract

ProtocolVersions.Current = 2. Every request body carries `ProtocolVersion`. Any function receiving a version != 2
returns HTTP 426 `{ "Error": "update_required", "Reason": "Please update Elemento to play online.", "ProtocolVersion": 2 }`.

The server performs NO rule validation and NO random generation. State/packages are opaque JSON (C# GameStateData via NetJson).

## Types (C# in Assets/Scripts/Domain/Net/ProtocolData.cs, same field names in JSON)
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
  List<TurnPackageData> Turns (revision > SinceRevision, ascending); List<string> SeatNames }
- get-view request { ProtocolVersion, MatchId, SinceRevision }; poll uses SinceRevision = last applied revision.
- list-my-matches request { ProtocolVersion } -> { Matches: [ {MatchId, Status, MatchRevision, CurrentSeat, YourSeat, OpponentName, UpdatedAt} ] } for status in (waiting, active).

## DB (migration 0005)
- table match_turns(match_id, revision int, seat int, client_nonce text unique per match, package jsonb, created_at) PK(match_id, revision).
- matches keeps single `revision`; `match_state(match_id pk, state jsonb, state_hash text)` holds latest full state (reuse match_secrets.state if simpler).
- RPC commit_client_turn(p_match_id, p_seat, p_expected_revision, p_nonce, p_package, p_state, p_next_seat, p_status, p_winner_seat)
  compare-and-set revision; revision += 1; current_seat = p_next_seat; status/winner from client.
- drop submit-move path: drop function commit_move, commit_turn (old). Revoke from anon/authenticated.
- abandon_stale_matches(): marks active/waiting matches with updated_at older than 3 days abandoned; scheduled with pg_cron hourly if available.
- Realtime: after commit, broadcast `{revision}` on the match channel as today.

## C# smoke fixtures

Generate protocol JSON from the same C# types and `NetJson` serializer used by Unity:

```sh
dotnet run --project Simulation~/Harness -- export-first-turn --out Supabase~/functions/fixtures/generated-first-turn
```

This writes `create-match.json` and `submit-turn.json`. Both use `MATCH_ID` where a server-assigned id belongs.
`functions/smoke-test.sh` automatically uses these files from that directory when present, or from
`HARNESS_FIXTURE_DIR` when set, replacing `MATCH_ID` in the first submit request with the created match id.
