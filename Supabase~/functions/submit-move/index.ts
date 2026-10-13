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
  EWinCondition,
  MatchAuthority,
  MoveCommandData,
  MoveLogEntryData,
  MoveResultData,
} from "../_shared/rules/mod.ts";

interface StoredMove {
  revision: number;
  command: MoveCommandData;
}

interface CommitMoveResult {
  duplicate?: boolean;
  ok?: boolean;
  revision?: number;
  stale?: boolean;
}

function storedResult(row: StoredMove): MoveResultData {
  const command = Object.assign(new MoveCommandData(), row.command);
  const logEntry = Object.assign(new MoveLogEntryData(), {
    MatchId: command.MatchId,
    Seat: command.Seat,
    ClientMoveSequence: command.ClientMoveSequence,
    ClientNonce: command.ClientNonce,
    RevisionBefore: command.ExpectedRevision,
    RevisionAfter: row.revision,
    Move: command.Move,
  });
  return Object.assign(new MoveResultData(), {
    Accepted: true,
    Reason: MatchAuthority.AcceptedReason,
    NewRevision: row.revision,
    LogEntry: logEntry,
  });
}

async function idempotentMove(service: Awaited<ReturnType<typeof authenticate>>["service"], command: MoveCommandData): Promise<StoredMove | null> {
  const { data, error } = await service.from("match_moves")
    .select("revision,command")
    .eq("match_id", command.MatchId)
    .eq("seat", command.Seat)
    .eq("client_nonce", command.ClientNonce)
    .maybeSingle();
  if (error != null) databaseFailure("load prior move", error);
  return data as StoredMove | null;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const command = Object.assign(new MoveCommandData(), await bodyJson<MoveCommandData>(request));
  if (typeof command.MatchId !== "string" || command.MatchId.length === 0) return json({ error: "MatchId required" }, 400);
  if (typeof command.ClientNonce !== "string" || command.ClientNonce.length === 0) return json({ error: "ClientNonce required" }, 400);

  const { data: player, error: playerError } = await service.from("match_players")
    .select("seat")
    .eq("match_id", command.MatchId)
    .eq("user_id", userId)
    .maybeSingle();
  if (playerError != null) databaseFailure("load match seat", playerError);
  if (player == null) throw new HttpError(404, "match not found");
  if (player.seat !== command.Seat) throw new HttpError(403, "seat does not belong to user");

  const prior = await idempotentMove(service, command);
  if (prior != null) {
    await broadcastBestEffort(service, command.MatchId, prior.revision);
    return json({ ...storedResult(prior), View: await loadSeatView(service, command.MatchId, command.Seat) });
  }

  const [matchResult, secretResult] = await Promise.all([
    service.from("matches").select("revision,status").eq("id", command.MatchId).single(),
    service.from("match_secrets").select("state").eq("match_id", command.MatchId).single(),
  ]);
  if (matchResult.error != null) databaseFailure("load match", matchResult.error);
  if (secretResult.error != null) databaseFailure("load match state", secretResult.error);

  if (matchResult.data.status !== "active") {
    const result = Object.assign(new MoveResultData(), {
      Reason: "Match is not active.",
      NewRevision: matchResult.data.revision,
    });
    return json({ ...result, View: await loadSeatView(service, command.MatchId, command.Seat) });
  }

  const state = hydrateState(secretResult.data.state);
  const result = new MatchAuthority().ValidateAndApply(state, command);
  if (!result.Accepted) return json({ ...result, View: await loadSeatView(service, command.MatchId, command.Seat) });

  const finished = state.WinCondition !== EWinCondition.None;
  const views = buildSeatViews(state);
  const storedCommand = Object.assign(new MoveCommandData(), command, { Move: result.LogEntry?.Move ?? command.Move });
  const { data: commitData, error: commitError } = await service.rpc("commit_move", {
    p_match: command.MatchId,
    p_expected_revision: command.ExpectedRevision,
    p_new_revision: result.NewRevision,
    p_seat: command.Seat,
    p_client_nonce: command.ClientNonce,
    p_command: storedCommand,
    p_state: state,
    p_views: views.map((view, seat) => ({ seat, view })),
    p_status: finished ? "finished" : "active",
    p_current_seat: state.CurrentPlayerId,
    p_turn_number: state.TurnNumber,
    p_winner: finished ? state.WinnerPlayerId : null,
  });
  if (commitError != null) databaseFailure("commit move", commitError);
  const commit = commitData as CommitMoveResult | null;

  if (commit?.duplicate) {
    const duplicate = await idempotentMove(service, command);
    if (duplicate == null) databaseFailure("load duplicate move", "commit_move reported a missing duplicate");
    await broadcastBestEffort(service, command.MatchId, duplicate.revision);
    return json({ ...storedResult(duplicate), View: await loadSeatView(service, command.MatchId, command.Seat) });
  }

  if (commit?.stale) {
    const raced = await idempotentMove(service, command);
    if (raced != null) {
      await broadcastBestEffort(service, command.MatchId, raced.revision);
      return json({ ...storedResult(raced), View: await loadSeatView(service, command.MatchId, command.Seat) });
    }
    const { data: current } = await service.from("matches").select("revision").eq("id", command.MatchId).single();
    const stale = Object.assign(new MoveResultData(), {
      Reason: MatchAuthority.RevisionReason,
      NewRevision: current?.revision ?? matchResult.data.revision,
    });
    return json({ ...stale, View: await loadSeatView(service, command.MatchId, command.Seat) });
  }
  if (!commit?.ok) databaseFailure("commit move", commit);

  await broadcastBestEffort(service, command.MatchId, result.NewRevision);
  return json({ ...result, View: views[command.Seat] });
});
