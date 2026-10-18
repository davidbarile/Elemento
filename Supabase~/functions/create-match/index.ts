import {
  authenticate,
  bodyJson,
  databaseFailure,
  isUniqueViolation,
  json,
  loadMatchStateUpdate,
  protocolVersion,
  randomInviteCode,
  requireProfile,
  requireProtocolVersion,
  serve,
} from "../_shared/server.ts";

interface CreateMatchRequestData {
  ProtocolVersion: number;
  Rules: unknown;
  InitialState: { CurrentPlayerId?: number };
  InitialStateHash: string;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<CreateMatchRequestData>(request);
  requireProtocolVersion(body?.ProtocolVersion);
  await requireProfile(service, userId);
  if (body.Rules == null || typeof body.Rules !== "object" || Array.isArray(body.Rules)) {
    return json({ error: "Rules must be an object" }, 400);
  }
  if (body.InitialState == null || typeof body.InitialState !== "object" || Array.isArray(body.InitialState)) {
    return json({ error: "InitialState must be an object" }, 400);
  }
  const startingSeat = body.InitialState.CurrentPlayerId;
  if (!Number.isInteger(startingSeat) || (startingSeat !== 0 && startingSeat !== 1)) {
    return json({ error: "InitialState.CurrentPlayerId must be 0 or 1" }, 400);
  }
  if (typeof body.InitialStateHash !== "string") return json({ error: "InitialStateHash must be a string" }, 400);

  for (let attempt = 0; attempt < 8; ++attempt) {
    const { data: match, error: matchError } = await service.from("matches").insert({
      status: "waiting",
      protocol_version: protocolVersion,
      rules: body.Rules,
      invite_code: randomInviteCode(),
      revision: 0,
      current_seat: startingSeat,
      winner_seat: null,
      created_by: userId,
    }).select("id,invite_code").single();
    if (matchError != null) {
      if (isUniqueViolation(matchError)) continue;
      databaseFailure("create match", matchError);
    }

    const { error: playerError } = await service.from("match_players").insert({
      match_id: match.id,
      seat: 0,
      user_id: userId,
    });
    if (playerError != null) {
      await service.from("matches").delete().eq("id", match.id);
      databaseFailure("create match player", playerError);
    }

    const { error: stateError } = await service.from("match_state").insert({
      match_id: match.id,
      state: body.InitialState,
      state_hash: body.InitialStateHash,
    });
    if (stateError != null) {
      await service.from("matches").delete().eq("id", match.id);
      databaseFailure("store initial state", stateError);
    }

    return json({
      ...await loadMatchStateUpdate(service, match.id, userId, 0),
      InviteCode: match.invite_code,
    }, 201);
  }

  return json({ error: "could not allocate invite code" }, 503);
});
