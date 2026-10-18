import {
  authenticate,
  bodyJson,
  databaseFailure,
  json,
  protocolVersion,
  requireProfile,
  requireProtocolVersion,
  serve,
} from "../_shared/server.ts";

interface ListMyMatchesRequestData {
  ProtocolVersion: number;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<ListMyMatchesRequestData>(request);
  requireProtocolVersion(body?.ProtocolVersion);
  await requireProfile(service, userId);

  const { data: seats, error: seatsError } = await service.from("match_players")
    .select("match_id,seat")
    .eq("user_id", userId);
  if (seatsError != null) databaseFailure("load caller matches", seatsError);
  if (seats == null || seats.length === 0) return json({ Matches: [] });

  const seatByMatch = new Map(seats.map((row) => [row.match_id, Number(row.seat)]));
  const matchIds = seats.map((row) => row.match_id);
  const { data: matches, error: matchesError } = await service.from("matches")
    .select("id,invite_code,status,revision,current_seat,updated_at")
    .in("id", matchIds)
    .eq("protocol_version", protocolVersion)
    .in("status", ["waiting", "active"])
    .order("updated_at", { ascending: false });
  if (matchesError != null) databaseFailure("load resumable matches", matchesError);
  if (matches == null || matches.length === 0) return json({ Matches: [] });

  const activeMatchIds = matches.map((match) => match.id);
  const { data: opponents, error: opponentsError } = await service.from("match_players")
    .select("match_id,user_id")
    .in("match_id", activeMatchIds)
    .neq("user_id", userId);
  if (opponentsError != null) databaseFailure("load match opponents", opponentsError);
  const opponentRows = opponents ?? [];
  const opponentUserIds = opponentRows.map((row) => row.user_id);
  const profilesResult = opponentUserIds.length === 0
    ? { data: [], error: null }
    : await service.from("profiles").select("id,username").in("id", opponentUserIds);
  if (profilesResult.error != null) databaseFailure("load opponent names", profilesResult.error);
  const namesByUser = new Map((profilesResult.data ?? []).map((profile) => [profile.id, String(profile.username)]));
  const opponentNameByMatch = new Map(opponentRows.map((row) => [row.match_id, namesByUser.get(row.user_id) ?? ""]));

  return json({
    Matches: matches.map((match) => ({
      MatchId: match.id,
      InviteCode: match.invite_code,
      Status: match.status,
      MatchRevision: Number(match.revision),
      CurrentSeat: match.current_seat == null ? -1 : Number(match.current_seat),
      YourSeat: seatByMatch.get(match.id),
      OpponentName: opponentNameByMatch.get(match.id) ?? "",
      UpdatedAt: match.updated_at,
    })),
  });
});
