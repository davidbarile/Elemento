import {
  authenticate,
  bodyJson,
  broadcastBestEffort,
  databaseFailure,
  HttpError,
  json,
  publicMatchFields,
  requireProtocolVersion,
  serve,
} from "../_shared/server.ts";

interface LeaveMatchRequestData {
  ProtocolVersion: number;
  MatchId: string;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<LeaveMatchRequestData>(request);
  requireProtocolVersion(body?.ProtocolVersion);
  if (typeof body.MatchId !== "string" || body.MatchId.length === 0) {
    return json({ error: "MatchId required" }, 400);
  }

  const { data: player, error: playerError } = await service.from("match_players")
    .select("seat")
    .eq("match_id", body.MatchId)
    .eq("user_id", userId)
    .maybeSingle();
  if (playerError != null) databaseFailure("load match seat", playerError);
  if (player == null) throw new HttpError(404, "match not found");

  for (let attempt = 0; attempt < 3; ++attempt) {
    const { data: match, error: matchError } = await service.from("matches")
      .select(`${publicMatchFields},created_by`)
      .eq("id", body.MatchId)
      .single();
    if (matchError != null) databaseFailure("load match", matchError);

    const abandonsWaitingMatch = match.status === "waiting" && match.created_by === userId;
    if (match.status !== "active" && !abandonsWaitingMatch) return json({ match });

    const revision = Number(match.revision) + 1;
    const update = abandonsWaitingMatch
      ? { status: "abandoned", revision, updated_at: new Date().toISOString() }
      : {
        status: "abandoned",
        winner_seat: 1 - Number(player.seat),
        revision,
        updated_at: new Date().toISOString(),
      };
    const { data: abandoned, error: abandonError } = await service.from("matches")
      .update(update)
      .eq("id", body.MatchId)
      .eq("status", match.status)
      .eq("revision", match.revision)
      .select(publicMatchFields)
      .maybeSingle();
    if (abandonError != null) databaseFailure("leave match", abandonError);
    if (abandoned == null) continue;

    await broadcastBestEffort(service, body.MatchId, revision);
    return json({ match: abandoned });
  }

  databaseFailure("leave match", "concurrent match updates did not settle");
});
