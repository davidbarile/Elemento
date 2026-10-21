import {
  authenticate,
  bodyJson,
  databaseFailure,
  HttpError,
  json,
  serve,
} from "../_shared/server.ts";

interface ClearAiGamesRequestData {
  Scope: "owner" | "all";
}

function adminUserIds(): Set<string> {
  return new Set((Deno.env.get("AI_GAME_ADMIN_USER_IDS") ?? "")
    .split(",")
    .map((value) => value.trim().toLowerCase())
    .filter((value) => value.length > 0));
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<ClearAiGamesRequestData>(request);
  if (body?.Scope !== "owner" && body?.Scope !== "all") {
    throw new HttpError(400, "Scope must be owner or all");
  }

  if (body.Scope === "all" && !adminUserIds().has(userId.toLowerCase())) {
    throw new HttpError(403, "this user is not authorized to clear all AI games");
  }

  let deletion = service.from("ai_games").delete().select("id");
  deletion = body.Scope === "owner"
    ? deletion.eq("owner_id", userId)
    : deletion.not("id", "is", null);
  const { data, error } = await deletion;
  if (error != null) databaseFailure("clear AI games", error);

  return json({ deleted: data?.length ?? 0 });
});
