import {
  authenticate,
  bodyJson,
  databaseFailure,
  HttpError,
  json,
  serve,
} from "../_shared/server.ts";

interface AiGameData {
  SchemaVersion: number;
  ClientGameId: string;
  Status: string;
  StartedAtUtc?: string;
  EndedAtUtc?: string;
  AppVersion?: string;
  BuildGuid?: string;
  AiImplementation?: string;
  Snapshots?: unknown[];
  Diagnostics?: unknown[];
}

interface RecordAiGameRequestData {
  Game: AiGameData;
}

const statuses = new Set([
  "completed",
  "interrupted",
  "quit",
  "abandoned",
  "restarted",
  "corrupt",
]);
const maxPayloadBytes = 8 * 1024 * 1024;

function requireDate(value: string | undefined, name: string): string | null {
  if (value == null || value.length === 0) return null;
  if (!Number.isFinite(Date.parse(value))) throw new HttpError(400, `${name} must be an ISO date`);
  return value;
}

serve(async (request) => {
  const { userId, service } = await authenticate(request);
  const body = await bodyJson<RecordAiGameRequestData>(request);
  const game = body?.Game;
  if (game == null || typeof game !== "object" || Array.isArray(game)) {
    throw new HttpError(400, "Game must be an object");
  }
  if (game.SchemaVersion !== 1 && game.SchemaVersion !== 2) {
    throw new HttpError(400, "unsupported AI game schema");
  }
  if (typeof game.ClientGameId !== "string" || !/^[a-f0-9]{32}$/i.test(game.ClientGameId)) {
    throw new HttpError(400, "ClientGameId must be a 32-character hexadecimal ID");
  }
  if (!statuses.has(game.Status)) throw new HttpError(400, "invalid AI game status");
  if (!Array.isArray(game.Snapshots) || !Array.isArray(game.Diagnostics)) {
    throw new HttpError(400, "Snapshots and Diagnostics must be arrays");
  }
  if (game.Snapshots.length > 2000 || game.Diagnostics.length > 50) {
    throw new HttpError(400, "AI game exceeds collection limits");
  }
  if (new TextEncoder().encode(JSON.stringify(game)).byteLength > maxPayloadBytes) {
    throw new HttpError(413, "AI game payload is too large");
  }

  const { error } = await service.from("ai_games").upsert({
    owner_id: userId,
    client_game_id: game.ClientGameId,
    schema_version: game.SchemaVersion,
    status: game.Status,
    app_version: game.AppVersion ?? null,
    ai_version: [game.AiImplementation, game.BuildGuid].filter(Boolean).join(":"),
    started_at: requireDate(game.StartedAtUtc, "StartedAtUtc"),
    ended_at: requireDate(game.EndedAtUtc, "EndedAtUtc"),
    payload: game,
    updated_at: new Date().toISOString(),
  }, { onConflict: "owner_id,client_game_id" });
  if (error != null) databaseFailure("store AI game", error);

  return json({ stored: true });
});
