import {
  authenticate,
  bodyJson,
  broadcastBestEffort,
  databaseFailure,
  HttpError,
  json,
  loadMatchStateUpdate,
  protocolVersion,
  requireProfile,
  requireProtocolVersion,
  serve,
} from "../_shared/server.ts";

interface JoinMatchRequestData {
  ProtocolVersion: number;
  InviteCode: string;
}

interface JoinClientMatchResult {
  ok?: boolean;
  revision?: number;
  unavailable?: boolean;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<JoinMatchRequestData>(request);
  requireProtocolVersion(body?.ProtocolVersion);
  await requireProfile(service, userId);
  const inviteCode = typeof body.InviteCode === "string" ? body.InviteCode.trim().toUpperCase() : "";
  if (!/^[A-Z2-9]{6}$/.test(inviteCode)) return json({ error: "invalid invite code" }, 400);

  const { data: match, error: matchError } = await service.from("matches")
    .select("id,revision,created_by")
    .eq("invite_code", inviteCode)
    .eq("protocol_version", protocolVersion)
    .eq("status", "waiting")
    .maybeSingle();
  if (matchError != null) databaseFailure("find match", matchError);
  if (match == null) throw new HttpError(404, "match not found");
  if (match.created_by === userId) throw new HttpError(409, "cannot join your own match");

  const { data: joinData, error: joinError } = await service.rpc("join_client_match", {
    p_match_id: match.id,
    p_user_id: userId,
  });
  if (joinError != null) databaseFailure("join match", joinError);
  const join = joinData as JoinClientMatchResult | null;
  if (!join?.ok) throw new HttpError(409, "match is no longer available");

  await broadcastBestEffort(service, match.id, join.revision!);
  return json(await loadMatchStateUpdate(service, match.id, userId, join.revision!));
});
