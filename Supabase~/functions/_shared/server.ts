import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";

import {
  CardDefinitionData,
  DeckDefinitionData,
  FairView,
  GameStateData,
  MatchFactory,
  MatchSeatViewData,
  RulesConfigData,
} from "./rules/mod.ts";

export const publicMatchFields = "id,status,mode,protocol_version,rules,invite_code,revision,turn_number,current_seat,winner_seat,turn_deadline,created_at,updated_at";

export const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, apikey, content-type, x-client-info",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

export class HttpError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export interface RequestContext {
  userId: string;
  service: SupabaseClient;
}

function requiredEnv(name: string): string {
  const value = Deno.env.get(name);
  if (value == null || value.length === 0) throw new Error(`${name} is not configured`);
  return value;
}

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "Content-Type": "application/json" },
  });
}

export async function bodyJson<T>(request: Request): Promise<T> {
  try {
    return await request.json() as T;
  } catch {
    throw new HttpError(400, "invalid JSON body");
  }
}

export async function authenticate(request: Request): Promise<RequestContext> {
  const authorization = request.headers.get("Authorization");
  if (authorization == null || !authorization.startsWith("Bearer ")) throw new HttpError(401, "authorization required");

  const url = requiredEnv("SUPABASE_URL");
  const anonKey = requiredEnv("SUPABASE_ANON_KEY");
  const userClient = createClient(url, anonKey, {
    global: { headers: { Authorization: authorization } },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  const { data, error } = await userClient.auth.getUser();
  if (error != null || data.user == null) throw new HttpError(401, "invalid authorization");

  const service = createClient(url, requiredEnv("SUPABASE_SERVICE_ROLE_KEY"), {
    auth: { persistSession: false, autoRefreshToken: false },
  });
  return { userId: data.user.id, service };
}

export async function requireProfile(service: SupabaseClient, userId: string): Promise<string> {
  const { data, error } = await service.from("profiles").select("username").eq("id", userId).maybeSingle();
  if (error != null) databaseFailure("load profile", error);
  if (data == null) throw new HttpError(400, "username required");
  return String(data.username);
}

export function rulesFrom(value: unknown): RulesConfigData {
  if (value == null) return new RulesConfigData();
  if (typeof value !== "object" || Array.isArray(value)) throw new HttpError(400, "invalid rules");
  return Object.assign(new RulesConfigData(), value);
}

export function deckForSeat(value: unknown, seat: number): CardDefinitionData[] {
  const source = value == null ? defaultDeckForSeat(seat) : value;
  if (!Array.isArray(source) || source.length === 0) throw new HttpError(400, "invalid deck");
  try {
    const deck = source.map((definition) => Object.assign(
      Object.create(CardDefinitionData.prototype) as CardDefinitionData,
      definition,
    ));
    for (const definition of deck) {
      if (definition.IsPowerCard) throw new Error("power card");
      definition.CreateCard();
    }
    return deck;
  } catch {
    throw new HttpError(400, "invalid deck");
  }
}

function defaultDeckForSeat(seat: number): CardDefinitionData[] {
  const deck: CardDefinitionData[] = [];
  let cardIndex = 0;
  for (const definition of DeckDefinitionData.NormalCards) {
    for (let copy = 0; copy < definition.ChoiceCount; ++copy) {
      if (cardIndex % 2 === seat) deck.push(definition);
      ++cardIndex;
    }
  }
  return deck;
}

export function createState(
  seed: number,
  rules: RulesConfigData,
  decks: CardDefinitionData[][],
  matchId: string,
  names?: string[],
): GameStateData {
  try {
    const state = new MatchFactory().Create(seed, rules, decks, matchId);
    if (names != null) for (let seat = 0; seat < names.length; ++seat) state.Players[seat].Name = names[seat];
    return state;
  } catch (error) {
    console.error("invalid match setup", error);
    throw new HttpError(400, "invalid deck or rules");
  }
}

export function hydrateState(value: unknown): GameStateData {
  if (value == null || typeof value !== "object" || Array.isArray(value)) throw new Error("invalid stored match state");
  return Object.assign(new GameStateData(), value);
}

export function buildSeatViews(state: GameStateData): MatchSeatViewData[] {
  return [FairView.BuildSeatView(state, 0), FairView.BuildSeatView(state, 1)];
}

export function randomInviteCode(): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ23456789";
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  return Array.from(bytes, (value) => alphabet[value % alphabet.length]).join("");
}

export function randomInt32(): number {
  return crypto.getRandomValues(new Int32Array(1))[0];
}

export function databaseFailure(action: string, error: unknown): never {
  console.error(`database failure: ${action}`, error);
  throw new Error(`database failure: ${action}`);
}

export function isUniqueViolation(error: { code?: string } | null): boolean {
  return error?.code === "23505";
}

export async function loadSeatView(service: SupabaseClient, matchId: string, seat: number): Promise<unknown> {
  const { data, error } = await service.from("match_seat_views").select("view").eq("match_id", matchId).eq("seat", seat).maybeSingle();
  if (error != null) databaseFailure("load seat view", error);
  if (data == null) throw new HttpError(404, "match view not found");
  return data.view;
}

export async function broadcastRevision(service: SupabaseClient, matchId: string, revision: number): Promise<void> {
  const topic = `match:${matchId}`;
  const channel = service.channel(topic) as ReturnType<SupabaseClient["channel"]> & {
    httpSend?: (event: string, payload: unknown) => Promise<{ status?: string }>;
  };
  try {
    if (typeof channel.httpSend === "function") {
      const result = await channel.httpSend("revision", { revision });
      if (result?.status != null && result.status !== "ok") throw new Error(`broadcast returned ${result.status}`);
      return;
    }

    const url = requiredEnv("SUPABASE_URL").replace(/\/$/, "");
    const serviceKey = requiredEnv("SUPABASE_SERVICE_ROLE_KEY");
    const response = await fetch(`${url}/realtime/v1/api/broadcast`, {
      method: "POST",
      headers: {
        apikey: serviceKey,
        Authorization: `Bearer ${serviceKey}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ messages: [{ topic, event: "revision", payload: { revision }, private: true }] }),
    });
    if (!response.ok) throw new Error(`broadcast returned HTTP ${response.status}`);
  } finally {
    await service.removeChannel(channel);
  }
}

export async function broadcastBestEffort(service: SupabaseClient, matchId: string, revision: number): Promise<void> {
  try {
    await broadcastRevision(service, matchId, revision);
  } catch (error) {
    console.error("realtime broadcast failed", error);
  }
}

export function serve(handler: (request: Request) => Promise<Response>): void {
  Deno.serve(async (request) => {
    if (request.method === "OPTIONS") return new Response("ok", { headers: corsHeaders });
    if (request.method !== "POST") return json({ error: "method not allowed" }, 405);
    try {
      return await handler(request);
    } catch (error) {
      if (error instanceof HttpError) return json({ error: error.message }, error.status);
      console.error("edge function failed", error);
      return json({ error: "internal server error" }, 500);
    }
  });
}
