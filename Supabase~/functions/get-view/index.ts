import {
  authenticate,
  bodyJson,
  databaseFailure,
  HttpError,
  json,
  publicMatchFields,
  serve,
} from "../_shared/server.ts";

interface GetViewBody {
  matchId: string;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<GetViewBody>(request);
  if (typeof body.matchId !== "string" || body.matchId.length === 0) return json({ error: "matchId required" }, 400);

  const { data: player, error: playerError } = await service.from("match_players")
    .select("seat")
    .eq("match_id", body.matchId)
    .eq("user_id", userId)
    .maybeSingle();
  if (playerError != null) databaseFailure("load match seat", playerError);
  if (player == null) throw new HttpError(404, "match not found");

  const [matchResult, viewResult] = await Promise.all([
    service.from("matches").select(publicMatchFields).eq("id", body.matchId).single(),
    service.from("match_seat_views").select("view").eq("match_id", body.matchId).eq("seat", player.seat).single(),
  ]);
  if (matchResult.error != null) databaseFailure("load match", matchResult.error);
  if (viewResult.error != null) databaseFailure("load seat view", viewResult.error);

  return json({ match: matchResult.data, view: viewResult.data.view });
});
