import {
  authenticate,
  bodyJson,
  broadcastBestEffort,
  buildSeatViews,
  databaseFailure,
  hydrateState,
  HttpError,
  json,
  loadSeatView,
  serve,
} from "../_shared/server.ts";
import {
  CardInstanceData,
  EMoveType,
  EWinCondition,
  MatchAuthority,
  MoveCommandData,
  MoveData,
} from "../_shared/rules/mod.ts";
import { cloneCard, cloneMove } from "../_shared/rules/game_state_data.ts";

interface SubmitTurnBody {
  MatchId: string;
  Seat: number;
  ExpectedRevision: number;
  ClientNonce: string;
  Moves: MoveData[];
}

interface StoredTurn {
  revision: number;
  command: MoveCommandData;
}

interface CommitTurnResult {
  duplicate?: boolean;
  ok?: boolean;
  revision?: number;
  stale?: boolean;
}

function findReplayCard(state: ReturnType<typeof hydrateState>, move: MoveData): CardInstanceData | null {
  if ((move.Type !== EMoveType.PlayCard && move.Type !== EMoveType.DiscardCard) || move.CardId == null) return null;
  const player = state.Players.find((candidate) => candidate.Id === move.PlayerId);
  if (player == null) return null;
  const cards = move.SourceZoneId < 0
    ? player.Hand
    : player.Zones.find((zone) => zone.Id === move.SourceZoneId)?.Cards;
  const card = cards?.find((candidate) => candidate.Id === move.CardId);
  return card == null ? null : cloneCard(card);
}

function acceptTerminalEndMarker(
  state: ReturnType<typeof hydrateState>,
  move: MoveData,
  submittingSeat: number,
  hasPriorAutoEndingMove: boolean,
): MoveData | null {
  if (!hasPriorAutoEndingMove || move.Type !== EMoveType.EndTurn || move.PlayerId !== submittingSeat
    || (state.CurrentPlayerId === submittingSeat && state.WinCondition === EWinCondition.None)) return null;
  const logged = cloneMove(move);
  logged.Id = MoveData.CreateId(state.Moves.length + 1);
  logged.ExpectedRevision = state.Revision;
  state.Moves.push(logged);
  state.Revision = (state.Revision + 1) | 0;
  return logged;
}

async function idempotentTurn(
  service: Awaited<ReturnType<typeof authenticate>>["service"],
  body: SubmitTurnBody,
): Promise<StoredTurn | null> {
  const { data, error } = await service.from("match_moves")
    .select("revision,command")
    .eq("match_id", body.MatchId)
    .eq("seat", body.Seat)
    .eq("client_nonce", body.ClientNonce)
    .maybeSingle();
  if (error != null) databaseFailure("load prior turn", error);
  return data as StoredTurn | null;
}

async function storedResponse(
  service: Awaited<ReturnType<typeof authenticate>>["service"],
  body: SubmitTurnBody,
  row: StoredTurn,
): Promise<Response> {
  return json({
    Accepted: true,
    NewRevision: row.revision,
    MoveCount: row.command.ClientMoveSequence,
    View: await loadSeatView(service, body.MatchId, body.Seat),
  });
}

async function rejectedResponse(
  service: Awaited<ReturnType<typeof authenticate>>["service"],
  body: SubmitTurnBody,
  revision: number,
  reason: string,
  failedIndex: number | null,
): Promise<Response> {
  return json({
    Accepted: false,
    Reason: reason,
    FailedIndex: failedIndex,
    NewRevision: revision,
    MoveCount: 0,
    View: await loadSeatView(service, body.MatchId, body.Seat),
  });
}

async function rejectedOrStored(
  service: Awaited<ReturnType<typeof authenticate>>["service"],
  body: SubmitTurnBody,
  revision: number,
  reason: string,
  failedIndex: number | null,
): Promise<Response> {
  const prior = await idempotentTurn(service, body);
  if (prior == null) return await rejectedResponse(service, body, revision, reason, failedIndex);
  await broadcastBestEffort(service, body.MatchId, prior.revision);
  return await storedResponse(service, body, prior);
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<SubmitTurnBody>(request);
  if (typeof body.MatchId !== "string" || body.MatchId.length === 0) return json({ error: "MatchId required" }, 400);
  if (!Number.isInteger(body.Seat)) return json({ error: "Seat must be an integer" }, 400);
  if (!Number.isInteger(body.ExpectedRevision) || body.ExpectedRevision < 0) return json({ error: "ExpectedRevision must be a non-negative integer" }, 400);
  if (typeof body.ClientNonce !== "string" || body.ClientNonce.length === 0) return json({ error: "ClientNonce required" }, 400);
  if (!Array.isArray(body.Moves) || body.Moves.length === 0) return json({ error: "Moves must not be empty" }, 400);

  const { data: player, error: playerError } = await service.from("match_players")
    .select("seat")
    .eq("match_id", body.MatchId)
    .eq("user_id", userId)
    .maybeSingle();
  if (playerError != null) databaseFailure("load match seat", playerError);
  if (player == null) throw new HttpError(404, "match not found");
  if (player.seat !== body.Seat) throw new HttpError(403, "seat does not belong to user");

  const prior = await idempotentTurn(service, body);
  if (prior != null) {
    await broadcastBestEffort(service, body.MatchId, prior.revision);
    return await storedResponse(service, body, prior);
  }

  const [matchResult, secretResult] = await Promise.all([
    service.from("matches").select("revision,status,current_seat").eq("id", body.MatchId).single(),
    service.from("match_secrets").select("state").eq("match_id", body.MatchId).single(),
  ]);
  if (matchResult.error != null) databaseFailure("load match", matchResult.error);
  if (secretResult.error != null) databaseFailure("load match state", secretResult.error);
  const currentRevision = matchResult.data.revision;

  if (matchResult.data.status !== "active") {
    return await rejectedOrStored(service, body, currentRevision, "Match is not active.", null);
  }
  if (body.ExpectedRevision !== currentRevision) {
    return await rejectedOrStored(service, body, currentRevision, MatchAuthority.RevisionReason, null);
  }
  if (matchResult.data.current_seat !== body.Seat) {
    return await rejectedOrStored(service, body, currentRevision, MatchAuthority.TurnReason, null);
  }
  if (body.Moves[body.Moves.length - 1]?.Type !== EMoveType.EndTurn) {
    return await rejectedOrStored(service, body, currentRevision, "Turn must end with EndTurn.", body.Moves.length - 1);
  }

  const state = hydrateState(secretResult.data.state);
  const authority = new MatchAuthority();
  const commands: MoveCommandData[] = [];
  for (let index = 0; index < body.Moves.length; ++index) {
    const requestedMove = Object.assign(new MoveData(), body.Moves[index]);
    const command = Object.assign(new MoveCommandData(), {
      MatchId: body.MatchId,
      Seat: body.Seat,
      ClientMoveSequence: index + 1,
      ExpectedRevision: body.ExpectedRevision + index,
      Move: requestedMove,
      ReplayCard: findReplayCard(state, requestedMove),
      ClientNonce: body.ClientNonce,
    });
    const previousMove = commands.length > 0 ? commands[commands.length - 1].Move : null;
    const terminalMarker = index === body.Moves.length - 1
      ? acceptTerminalEndMarker(state, requestedMove, body.Seat,
        previousMove != null && previousMove.Type !== EMoveType.EndTurn)
      : null;
    if (terminalMarker != null) {
      command.Move = terminalMarker;
      commands.push(command);
      continue;
    }
    const result = authority.ValidateAndApply(state, command);
    if (!result.Accepted) {
      return await rejectedOrStored(service, body, currentRevision, result.Reason ?? MatchAuthority.MoveReason, index);
    }
    commands.push(Object.assign(new MoveCommandData(), command, { Move: result.LogEntry?.Move ?? command.Move }));
  }

  const finished = state.WinCondition !== EWinCondition.None;
  const views = buildSeatViews(state);
  const { data: commitData, error: commitError } = await service.rpc("commit_turn", {
    p_match: body.MatchId,
    p_expected_revision: body.ExpectedRevision,
    p_new_revision: state.Revision,
    p_seat: body.Seat,
    p_client_nonce: body.ClientNonce,
    p_commands: commands,
    p_state: state,
    p_views: views.map((view, seat) => ({ seat, view })),
    p_status: finished ? "finished" : "active",
    p_current_seat: state.CurrentPlayerId,
    p_turn_number: state.TurnNumber,
    p_winner: finished ? state.WinnerPlayerId : null,
  });
  if (commitError != null) databaseFailure("commit turn", commitError);
  const commit = commitData as CommitTurnResult | null;

  if (commit?.duplicate) {
    const duplicate = await idempotentTurn(service, body);
    if (duplicate == null) databaseFailure("load duplicate turn", "commit_turn reported a missing duplicate");
    await broadcastBestEffort(service, body.MatchId, duplicate.revision);
    return await storedResponse(service, body, duplicate);
  }

  if (commit?.stale) {
    const raced = await idempotentTurn(service, body);
    if (raced != null) {
      await broadcastBestEffort(service, body.MatchId, raced.revision);
      return await storedResponse(service, body, raced);
    }
    const { data: current, error: currentError } = await service.from("matches").select("revision").eq("id", body.MatchId).single();
    if (currentError != null) databaseFailure("load current revision", currentError);
    return await rejectedOrStored(service, body, current.revision, MatchAuthority.RevisionReason, null);
  }
  if (!commit?.ok) databaseFailure("commit turn", commit);

  await broadcastBestEffort(service, body.MatchId, state.Revision);
  return json({ Accepted: true, NewRevision: state.Revision, MoveCount: commands.length, View: views[body.Seat] });
});
