import {
  authenticate,
  bodyJson,
  broadcastBestEffort,
  databaseFailure,
  HttpError,
  json,
  publicMatchFields,
  serve,
} from "../_shared/server.ts";

interface LeaveMatchBody {
  matchId: string;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<LeaveMatchBody>(request);
  if (typeof body.matchId !== "string" || body.matchId.length === 0) {
    return json({ error: "matchId required" }, 400);
  }

  const { data: player, error: playerError } = await service.from("match_players")
    .select("seat")
    .eq("match_id", body.matchId)
    .eq("user_id", userId)
    .maybeSingle();
  if (playerError != null) databaseFailure("load match seat", playerError);
  if (player == null) throw new HttpError(404, "match not found");

  for (let attempt = 0; attempt < 3; ++attempt) {
    const { data: match, error: matchError } = await service.from("matches")
      .select(publicMatchFields)
      .eq("id", body.matchId)
      .single();
    if (matchError != null) databaseFailure("load match", matchError);

    if (match.status !== "active") {
      if (match.status === "abandoned") {
        await broadcastBestEffort(service, body.matchId, match.revision);
      }
      return json({ match });
    }

    const revision = match.revision + 1;
    const { data: abandoned, error: abandonError } = await service.from("matches")
      .update({
        status: "abandoned",
        winner_seat: 1 - player.seat,
        revision,
        updated_at: new Date().toISOString(),
      })
      .eq("id", body.matchId)
      .eq("status", "active")
      .eq("revision", match.revision)
      .select(publicMatchFields)
      .maybeSingle();
    if (abandonError != null) databaseFailure("leave match", abandonError);
    if (abandoned == null) continue;

    await broadcastBestEffort(service, body.matchId, revision);
    return json({ match: abandoned });
  }

  databaseFailure("leave match", "concurrent match updates did not settle");
});
