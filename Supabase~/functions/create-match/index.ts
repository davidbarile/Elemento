import {
  authenticate,
  bodyJson,
  createState,
  databaseFailure,
  deckForSeat,
  isUniqueViolation,
  json,
  publicMatchFields,
  randomInviteCode,
  requireProfile,
  rulesFrom,
  serve,
} from "../_shared/server.ts";

interface CreateMatchBody {
  deck?: unknown;
  rules?: unknown;
  mode: "live" | "async";
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  await requireProfile(service, userId);
  const body = await bodyJson<CreateMatchBody>(request);
  if (body.mode !== "live" && body.mode !== "async") return json({ error: "mode must be live or async" }, 400);

  const rules = rulesFrom(body.rules);
  const deck = deckForSeat(body.deck, 0);
  createState(0, rules, [deck, deckForSeat(undefined, 1)], "validation");

  for (let attempt = 0; attempt < 8; ++attempt) {
    const { data: match, error: matchError } = await service.from("matches").insert({
      status: "waiting",
      mode: body.mode,
      rules,
      invite_code: randomInviteCode(),
      created_by: userId,
    }).select(publicMatchFields).single();
    if (matchError != null) {
      if (isUniqueViolation(matchError)) continue;
      databaseFailure("create match", matchError);
    }

    const { error: playerError } = await service.from("match_players").insert({
      match_id: match.id,
      seat: 0,
      user_id: userId,
      deck,
    });
    if (playerError == null) return json({ match, seat: 0 }, 201);

    await service.from("matches").delete().eq("id", match.id);
    databaseFailure("create match player", playerError);
  }

  return json({ error: "could not allocate invite code" }, 503);
});
