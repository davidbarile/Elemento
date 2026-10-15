import {
  authenticate,
  bodyJson,
  broadcastBestEffort,
  buildSeatViews,
  createState,
  databaseFailure,
  deckForSeat,
  HttpError,
  isUniqueViolation,
  json,
  publicMatchFields,
  randomInt32,
  requireProfile,
  rulesFrom,
  serve,
} from "../_shared/server.ts";

interface JoinMatchBody {
  inviteCode: string;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const joiningUsername = await requireProfile(service, userId);
  const body = await bodyJson<JoinMatchBody>(request);
  const inviteCode = typeof body.inviteCode === "string" ? body.inviteCode.trim().toUpperCase() : "";
  if (!/^[A-Z2-9]{6}$/.test(inviteCode)) return json({ error: "invalid invite code" }, 400);

  const { data: match, error: matchError } = await service.from("matches")
    .select(publicMatchFields)
    .eq("invite_code", inviteCode)
    .eq("status", "waiting")
    .maybeSingle();
  if (matchError != null) databaseFailure("find match", matchError);
  if (match == null) throw new HttpError(404, "match not found");

  const { data: creator, error: creatorError } = await service.from("match_players")
    .select("user_id,deck")
    .eq("match_id", match.id)
    .eq("seat", 0)
    .single();
  if (creatorError != null) databaseFailure("load match creator", creatorError);
  if (creator.user_id === userId) throw new HttpError(409, "cannot join your own match");

  const { data: creatorProfile, error: profileError } = await service.from("profiles")
    .select("username")
    .eq("id", creator.user_id)
    .single();
  if (profileError != null) databaseFailure("load creator profile", profileError);

  const creatorDeck = deckForSeat(creator.deck, 0);
  const joiningDeck = deckForSeat(undefined, 1);
  const seed = randomInt32();
  const startingSeat = randomInt32() & 1;
  const state = createState(
    seed,
    rulesFrom(match.rules),
    [creatorDeck, joiningDeck],
    match.id,
    startingSeat,
    [String(creatorProfile.username), joiningUsername],
  );
  const views = buildSeatViews(state);

  const { error: playerError } = await service.from("match_players").insert({
    match_id: match.id,
    seat: 1,
    user_id: userId,
    deck: joiningDeck,
  });
  if (playerError != null) {
    if (isUniqueViolation(playerError)) throw new HttpError(409, "match is no longer available");
    databaseFailure("join match", playerError);
  }

  try {
    const { error: secretError } = await service.from("match_secrets").insert({ match_id: match.id, seed, state });
    if (secretError != null) databaseFailure("store match state", secretError);

    const { error: viewsError } = await service.from("match_seat_views").insert(views.map((view, seat) => ({
      match_id: match.id,
      seat,
      revision: state.Revision,
      view,
    })));
    if (viewsError != null) databaseFailure("store seat views", viewsError);

    const { data: activeMatch, error: activeError } = await service.from("matches").update({
      status: "active",
      current_seat: state.CurrentPlayerId,
      revision: state.Revision,
      turn_number: state.TurnNumber,
      updated_at: new Date().toISOString(),
    }).eq("id", match.id).eq("status", "waiting").select(publicMatchFields).maybeSingle();
    if (activeError != null) databaseFailure("activate match", activeError);
    if (activeMatch == null) throw new HttpError(409, "match is no longer available");

    await broadcastBestEffort(service, match.id, state.Revision);
    return json({ match: activeMatch, seat: 1, view: views[1] });
  } catch (error) {
    await service.from("match_seat_views").delete().eq("match_id", match.id);
    await service.from("match_secrets").delete().eq("match_id", match.id);
    await service.from("match_players").delete().eq("match_id", match.id).eq("seat", 1).eq("user_id", userId);
    throw error;
  }
});
