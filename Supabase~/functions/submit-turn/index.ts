import {
  authenticate,
  bodyJson,
  broadcastBestEffort,
  databaseFailure,
  json,
  protocolVersion,
  requireProtocolVersion,
  serve,
} from "../_shared/server.ts";

interface TurnPackageData {
  MatchId: string;
  Seat: number;
  TurnNumber: number;
  StartRevision: number;
  Actions: unknown[];
  FinalState: unknown;
  FinalStateHash: string;
  Status: "active" | "finished";
  WinnerSeat: number;
  WinCondition: unknown;
  NextSeat: number;
}

interface SubmitTurnRequestData {
  ProtocolVersion: number;
  MatchId: string;
  Seat: number;
  ExpectedRevision: number;
  ClientNonce: string;
  Turn: TurnPackageData;
}

interface StoredTurn {
  revision: number;
  seat: number;
  package: TurnPackageData;
}

interface CommitClientTurnResult {
  duplicate?: boolean;
  ok?: boolean;
  revision?: number;
  stale?: boolean;
}

function warnRejection(body: Partial<SubmitTurnRequestData> | null, actualRevision: number | null, reason: string): void {
  console.warn("submit-turn rejected", {
    matchId: body?.MatchId ?? null,
    seat: body?.Seat ?? null,
    expectedRevision: body?.ExpectedRevision ?? null,
    actualRevision,
    reason,
  });
}

function requestRejection(
  body: Partial<SubmitTurnRequestData> | null,
  reason: string,
  status: number,
  actualRevision: number | null = null,
): Response {
  warnRejection(body, actualRevision, reason);
  return json({ error: reason }, status);
}

function turnRejection(body: SubmitTurnRequestData, reason: string, actualRevision: number, currentSeat: number, status: string): Response {
  warnRejection(body, actualRevision, reason);
  return json({
    Accepted: false,
    Reason: reason,
    NewRevision: actualRevision,
    CurrentSeat: currentSeat,
    Status: status,
  });
}

function acceptedTurn(row: StoredTurn): Response {
  return json({
    Accepted: true,
    Reason: "",
    NewRevision: row.revision,
    CurrentSeat: row.package.NextSeat,
    Status: row.package.Status,
  });
}

async function loadStoredTurn(
  service: Awaited<ReturnType<typeof authenticate>>["service"],
  matchId: string,
  nonce: string,
): Promise<StoredTurn | null> {
  const { data, error } = await service.from("match_turns")
    .select("revision,seat,package")
    .eq("match_id", matchId)
    .eq("client_nonce", nonce)
    .maybeSingle();
  if (error != null) databaseFailure("load prior turn", error);
  return data as StoredTurn | null;
}

function validTurnEnvelope(body: SubmitTurnRequestData): boolean {
  const turn = body.Turn;
  return turn != null
    && typeof turn === "object"
    && turn.MatchId === body.MatchId
    && turn.Seat === body.Seat
    && turn.StartRevision === body.ExpectedRevision
    && Number.isInteger(turn.TurnNumber)
    && Array.isArray(turn.Actions)
    && turn.FinalState != null
    && typeof turn.FinalState === "object"
    && !Array.isArray(turn.FinalState)
    && typeof turn.FinalStateHash === "string"
    && (turn.Status === "active" || turn.Status === "finished")
    && Number.isInteger(turn.NextSeat)
    && (turn.NextSeat === 0 || turn.NextSeat === 1)
    && Number.isInteger(turn.WinnerSeat)
    && (turn.WinnerSeat === -1 || turn.WinnerSeat === 0 || turn.WinnerSeat === 1);
}

serve(async (request) => {
  let body: SubmitTurnRequestData | null = null;
  let actualRevision: number | null = null;
  try {
    const { userId, service } = await authenticate(request);
    body = await bodyJson<SubmitTurnRequestData>(request);
    requireProtocolVersion(body?.ProtocolVersion);

    if (typeof body.MatchId !== "string" || body.MatchId.length === 0) {
      return requestRejection(body, "MatchId required", 400);
    }
    if (!Number.isInteger(body.Seat) || (body.Seat !== 0 && body.Seat !== 1)) {
      return requestRejection(body, "Seat must be 0 or 1", 400);
    }
    if (!Number.isInteger(body.ExpectedRevision) || body.ExpectedRevision < 0) {
      return requestRejection(body, "ExpectedRevision must be a non-negative integer", 400);
    }
    if (typeof body.ClientNonce !== "string" || body.ClientNonce.length === 0) {
      return requestRejection(body, "ClientNonce required", 400);
    }
    if (!validTurnEnvelope(body)) return requestRejection(body, "invalid turn package", 400);

    const [playerResult, matchResult] = await Promise.all([
      service.from("match_players")
        .select("seat")
        .eq("match_id", body.MatchId)
        .eq("user_id", userId)
        .maybeSingle(),
      service.from("matches")
        .select("revision,status,current_seat")
        .eq("id", body.MatchId)
        .eq("protocol_version", protocolVersion)
        .maybeSingle(),
    ]);
    if (playerResult.error != null) databaseFailure("load match seat", playerResult.error);
    if (matchResult.error != null) databaseFailure("load match", matchResult.error);
    actualRevision = matchResult.data == null ? null : Number(matchResult.data.revision);
    if (playerResult.data == null || matchResult.data == null) {
      return requestRejection(body, "match not found", 404, actualRevision);
    }
    if (Number(playerResult.data.seat) !== body.Seat) {
      return requestRejection(body, "seat does not belong to user", 403, actualRevision);
    }

    const prior = await loadStoredTurn(service, body.MatchId, body.ClientNonce);
    if (prior != null) {
      if (prior.seat !== body.Seat) return requestRejection(body, "nonce_conflict", 409, actualRevision);
      await broadcastBestEffort(service, body.MatchId, prior.revision);
      return acceptedTurn(prior);
    }

    if (matchResult.data.status !== "active") {
      return turnRejection(body, "match_not_active", actualRevision!, Number(matchResult.data.current_seat ?? -1), matchResult.data.status);
    }
    if (body.ExpectedRevision !== actualRevision) {
      return turnRejection(body, "stale_revision", actualRevision!, Number(matchResult.data.current_seat), matchResult.data.status);
    }
    if (Number(matchResult.data.current_seat) !== body.Seat) {
      return turnRejection(body, "not_your_turn", actualRevision!, Number(matchResult.data.current_seat), matchResult.data.status);
    }

    const { data: commitData, error: commitError } = await service.rpc("commit_client_turn", {
      p_match_id: body.MatchId,
      p_seat: body.Seat,
      p_expected_revision: body.ExpectedRevision,
      p_nonce: body.ClientNonce,
      p_package: body.Turn,
      p_state: body.Turn.FinalState,
      p_next_seat: body.Turn.NextSeat,
      p_status: body.Turn.Status,
      p_winner_seat: body.Turn.WinnerSeat,
    });
    if (commitError != null) databaseFailure("commit client turn", commitError);
    const commit = commitData as CommitClientTurnResult | null;

    if (commit?.ok || commit?.duplicate) {
      const stored = await loadStoredTurn(service, body.MatchId, body.ClientNonce);
      if (stored == null) databaseFailure("load committed turn", "commit_client_turn returned success without a row");
      await broadcastBestEffort(service, body.MatchId, stored.revision);
      return acceptedTurn(stored);
    }
    if (!commit?.stale) databaseFailure("commit client turn", commit);

    const raced = await loadStoredTurn(service, body.MatchId, body.ClientNonce);
    if (raced != null && raced.seat === body.Seat) {
      await broadcastBestEffort(service, body.MatchId, raced.revision);
      return acceptedTurn(raced);
    }
    const { data: current, error: currentError } = await service.from("matches")
      .select("revision,status,current_seat")
      .eq("id", body.MatchId)
      .single();
    if (currentError != null) databaseFailure("load current match", currentError);
    const currentRevision = Number(current.revision);
    actualRevision = currentRevision;
    if (current.status !== "active") {
      return turnRejection(body, "match_not_active", currentRevision, Number(current.current_seat ?? -1), current.status);
    }
    if (body.ExpectedRevision !== currentRevision) {
      return turnRejection(body, "stale_revision", currentRevision, Number(current.current_seat), current.status);
    }
    return turnRejection(body, "not_your_turn", currentRevision, Number(current.current_seat), current.status);
  } catch (error) {
    const reason = error instanceof Error && error.message.length > 0 ? error.message : "internal_error";
    warnRejection(body, actualRevision, reason);
    throw error;
  }
}, (reason) => warnRejection(null, null, reason));
