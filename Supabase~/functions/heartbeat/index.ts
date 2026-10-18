import {
  authenticate,
  bodyJson,
  json,
  requireProtocolVersion,
  serve,
  touchMatchPresence,
} from "../_shared/server.ts";

interface HeartbeatRequestData {
  ProtocolVersion: number;
  MatchId: string;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<HeartbeatRequestData>(request);
  requireProtocolVersion(body?.ProtocolVersion);
  if (typeof body.MatchId !== "string" || body.MatchId.length === 0) {
    return json({ error: "MatchId required" }, 400);
  }
  return json({
    OpponentLastSeenSecondsAgo: await touchMatchPresence(service, body.MatchId, userId),
  });
});
