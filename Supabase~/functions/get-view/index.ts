import {
  authenticate,
  bodyJson,
  json,
  loadMatchStateUpdate,
  requireProtocolVersion,
  serve,
} from "../_shared/server.ts";

interface GetViewRequestData {
  ProtocolVersion: number;
  MatchId: string;
  SinceRevision: number;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<GetViewRequestData>(request);
  requireProtocolVersion(body?.ProtocolVersion);
  if (typeof body.MatchId !== "string" || body.MatchId.length === 0) {
    return json({ error: "MatchId required" }, 400);
  }
  if (!Number.isInteger(body.SinceRevision) || body.SinceRevision < 0) {
    return json({ error: "SinceRevision must be a non-negative integer" }, 400);
  }
  return json(await loadMatchStateUpdate(service, body.MatchId, userId, body.SinceRevision));
});
