# Elemento Online Multiplayer: Protocol and Authority Design (draft)

Status: drafts only. Project 'Elemento' exists (ref dgmpxhxsfgfxaidxwmzq, us-west-1, free plan) but no migration has been applied and nothing is configured.

Decided: anonymous sign-in + unique username (profiles.username, citext, 3-20 [A-Za-z0-9_]); optional later identity linking (Apple on iOS, Google on Android, both on desktop/web). Authority: Edge Function with a TS rules port + golden fixtures.
(`Supabase~` ends in `~` so Unity ignores the folder.)

## Protocol (Elemento.Domain.Net)
- `MoveCommandData`: matchId, seat, clientSeq, expectedRevision, move, clientNonce, protocolVersion.
- `MoveResultData`: accepted/rejected + reason, newRevision.
- `MatchSnapshotData`: public match state at a revision (counts only for hidden zones).
- `MatchSeatViewData`: fair per-seat view built from authoritative state: own hand, public boards,
  opponent hand COUNT, deck COUNTS. Never opponent hand contents, never deck order, never the seed.
- `MoveLogEntryData`: revision, seat, command. Replay(seed, rules, decks, log) == live state.
- Deterministic setup: `MatchFactory(seed, rules, decks)` uses only `DeterministicRandom`, so any
  holder of the seed reproduces the deck order. Therefore the seed is a SECRET held by the server.

## Authority options
| Option | Pros | Cons |
|---|---|---|
| A. TS port of the rules in an Edge Function | Real server authority, hidden info stays server-side, cheap (free tier), fits async | Two rule implementations; drift risk |
| B. Postgres RPC (PL/pgSQL port) | Transactional, no function cold start | Painful to write/maintain the rules in SQL |
| C. Run C# Elemento.Domain on the server (.NET WASM in Edge Function, or a small .NET container) | One rules codebase | WASM in Deno is fiddly; a container costs money/ops |
| D. Trusted host (one client is authority, server just stores) | No port | Host sees deck order/opponent hand; cheatable; host must be online (breaks async) |

**Recommendation: A**, an Edge Function `match-move` with a TypeScript port of `GameRules`/`GameSimulator`,
kept honest by **golden fixtures**: the dotnet test project exports move logs + expected
snapshots (JSON) from seeded AI self-play, and a Deno test replays them in the TS port and must
match byte-for-byte. Revisit C (.NET WASM) if the port proves costly. D is rejected because the
seed/deck order would be visible to the host and async play needs an always-on authority.

## Batched online turns
New clients send the whole ordered turn to `submit-turn` as
`{MatchId, Seat, ExpectedRevision, ClientNonce, Moves}`. `ClientNonce` identifies the whole turn,
and `Moves` must be non-empty and end with `EndTurn`; the server does not append it. The function
authenticates seat ownership, loads `match_secrets.state`, and replays every move through the shared
TypeScript `MatchAuthority` with consecutive expected revisions. Any illegal move rejects the whole
batch without writes and returns its zero-based `FailedIndex` plus the caller's current seat view.

An accepted turn is committed by one `commit_turn` RPC transaction: the match row is conditionally
advanced from `ExpectedRevision`, one `match_moves` row is inserted per move at consecutive
revisions, the turn nonce is stored only on the final row, and the secret state plus both fair seat
views are updated together. A repeated `(match, seat, nonce)` returns the stored revision and move
count with the latest available seat view. Stale revisions write nothing. After commit, the function
broadcasts only `{revision}` on the private `match:<id>` topic.

Strict trailing `EndTurn` currently supports only turns whose preceding actions leave `EndTurn`
legal. Winning actions and rules that end the turn implicitly (`DiscardEndsTurn` or exhausted finite
moves) need a future protocol decision before they can use batched submission; the server does not
silently treat `EndTurn` as an unvalidated delimiter.

`get-view` accepts optional `sinceRevision` and then includes ordered `moves` rows with revisions
greater than that value. Move actions are public, but `CardId` is removed from an opponent's
`DrawCard` action. Direct authenticated reads of `match_moves` are disabled so this redaction cannot
be bypassed.

The single-command `submit-move` endpoint remains deployed only for old builds and is deprecated.

Live vs async are the same pipeline: live = both clients subscribed to Realtime; async = push
notification / poll on `turn_deadline`, resume by fetching the seat view at current revision.

## Client (Unity)
`OnlineSession : IGameSession`, viewing seat = local seat via `SeatViewBinder` (local player always
at bottom). Local input is matched to an authoritative legal `MoveData` and submitted through
`OnlineMatchClient`; each returned or pushed `MatchSeatViewData` fully rebuilds the visible board.
Rejected optimistic UI changes are discarded by re-rendering the last server view. Supabase uses
`IAsyncMatchTransport`; the in-memory transport remains available for simulation tests.

## Decisions David must make
1. Supabase org + project (new org vs existing; project name; region, e.g. us-west-1).
2. Plan: Free tier to start (projects pause after ~1 week inactivity; limits on Edge Function
   invocations/Realtime) vs Pro (~$25/mo) at launch.
3. Auth: anonymous sign-in first (zero-friction), with linking to Sign in with Apple (required on
   iOS if any other social login is offered) and/or Google; email magic link optional.
4. Live, async, or both at launch; async turn timeout and forfeit policy.
5. Matchmaking: invite code/friends only vs random queue.
6. Authority choice (recommend A, TS port + golden fixtures) and acceptable anti-cheat level.
7. Push notifications for async turns (APNs/FCM) now or later.
8. Data retention for finished matches/move logs; privacy policy update.

## Hidden-information rules (decided)
- Pile point totals (`ZoneStateData.Points`, including face-down cards) are PUBLIC to both players (David, 2026-10-12).
  Face-down card identities are masked to `{Id, IsFaceUp=false}` in opponent views and snapshots.
- Never sent to clients: seed, deck/reserve order, opponent hand contents (count only).

## Known issues fixed
- O1: authoritative online view rebuilds now cancel the active drag before destroying cards, block new drags for the full rebuild, and clear drag/render locks in `finally`. Previously a dragged card could be destroyed before `StopDraggingItem` ran, leaving the static input lock and drag overlay active indefinitely; Home now clears stale locks as an escape path.

## TODOs after U2
- Validate the online board projection, drag/drop, panel prefabs, and reconnect behavior in the Unity Editor; U2 was completed with static review only because Unity was unavailable.
- Add explicit online game-over presentation from `WinnerPlayerId`/`WinType` instead of relying on local board-side win checks.
- Drive online-only rule affordances and counters entirely from `PlayerViewData.Rules` (including power-deck/discard presentation) if server match rules can differ from the local settings asset.
- Add cancellation/lifetime guards for requests that complete after leaving a match, plus a user-facing reconnect/retry state for prolonged network failures.
- Decide and implement async-turn deadlines, forfeits, and APNs/FCM notifications before shipping async matches.
