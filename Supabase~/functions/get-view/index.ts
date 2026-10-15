import {
  authenticate,
  bodyJson,
  databaseFailure,
  HttpError,
  json,
  publicMatchFields,
  serve,
} from "../_shared/server.ts";
import { EMoveType, MoveCommandData, MoveData } from "../_shared/rules/mod.ts";

interface GetViewBody {
  matchId: string;
  sinceRevision?: number;
}

interface StoredMove {
  revision: number;
  seat: number;
  command: MoveCommandData;
}

function publicMoves(rows: StoredMove[], viewingSeat: number): StoredMove[] {
  return rows.map((row) => {
    const command = Object.assign(new MoveCommandData(), row.command);
    if (command.Move != null) command.Move = Object.assign(new MoveData(), command.Move);
    if (command.Move?.Type === EMoveType.DrawCard && row.seat !== viewingSeat) command.Move.CardId = null;
    return { ...row, command };
  });
}

async function loadMoves(
  service: Awaited<ReturnType<typeof authenticate>>["service"],
  matchId: string,
  sinceRevision: number,
): Promise<StoredMove[]> {
  const rows: StoredMove[] = [];
  let revision = sinceRevision;
  while (true) {
    const { data, error } = await service.from("match_moves")
      .select("revision,seat,command")
      .eq("match_id", matchId)
      .gt("revision", revision)
      .order("revision", { ascending: true })
      .limit(1000);
    if (error != null) databaseFailure("load move log", error);
    const page = data as StoredMove[] | null;
    if (page == null) databaseFailure("load move log", "query returned no data");
    if (page.length === 0) return rows;
    rows.push(...page);
    revision = page[page.length - 1].revision;
  }
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<GetViewBody>(request);
  if (typeof body.matchId !== "string" || body.matchId.length === 0) return json({ error: "matchId required" }, 400);
  if (body.sinceRevision != null && (!Number.isInteger(body.sinceRevision) || body.sinceRevision < 0)) {
    return json({ error: "sinceRevision must be a non-negative integer" }, 400);
  }

  const { data: player, error: playerError } = await service.from("match_players")
    .select("seat")
    .eq("match_id", body.matchId)
    .eq("user_id", userId)
    .maybeSingle();
  if (playerError != null) databaseFailure("load match seat", playerError);
  if (player == null) throw new HttpError(404, "match not found");

  const movesPromise = body.sinceRevision == null ? Promise.resolve(null) : loadMoves(service, body.matchId, body.sinceRevision);
  const [matchResult, viewResult, moves] = await Promise.all([
    service.from("matches").select(publicMatchFields).eq("id", body.matchId).single(),
    service.from("match_seat_views").select("view").eq("match_id", body.matchId).eq("seat", player.seat).maybeSingle(),
    movesPromise,
  ]);
  if (matchResult.error != null) databaseFailure("load match", matchResult.error);
  if (viewResult.error != null) databaseFailure("load seat view", viewResult.error);

  const response: { match: unknown; view: unknown; moves?: StoredMove[] } = {
    match: matchResult.data,
    view: viewResult.data?.view ?? null,
  };
  if (moves != null) response.moves = publicMoves(moves, player.seat);
  return json(response);
});
