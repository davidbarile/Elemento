import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";

export const protocolVersion = 2;
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

class UpdateRequiredError extends Error {
  constructor() {
    super("update_required");
  }
}

export interface RequestContext {
  userId: string;
  service: SupabaseClient;
}

export interface MatchStateUpdateData {
  MatchId: string;
  Status: string;
  MatchRevision: number;
  CurrentSeat: number;
  YourSeat: number;
  WinnerSeat: number;
  Rules: unknown;
  State: unknown;
  Turns: unknown[];
  SeatNames: string[];
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

export function requireProtocolVersion(value: unknown): void {
  if (value !== protocolVersion) throw new UpdateRequiredError();
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

export function randomInviteCode(): string {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  return Array.from(bytes, (value) => alphabet[value % alphabet.length]).join("");
}

export function databaseFailure(action: string, error: unknown): never {
  console.error(`database failure: ${action}`, error);
  throw new Error(`database failure: ${action}`);
}

export function isUniqueViolation(error: { code?: string } | null): boolean {
  return error?.code === "23505";
}

export async function loadMatchStateUpdate(
  service: SupabaseClient,
  matchId: string,
  userId: string,
  sinceRevision: number,
): Promise<MatchStateUpdateData> {
  const { data, error } = await service.rpc("load_match_state_update", {
    p_match_id: matchId,
    p_user_id: userId,
    p_since_revision: sinceRevision,
  });
  if (error != null) databaseFailure("load match state update", error);
  if (data == null) throw new HttpError(404, "match not found");
  return data as MatchStateUpdateData;
}

export async function broadcastRevision(service: SupabaseClient, matchId: string, revision: number): Promise<void> {
  const topic = `match:${matchId}`;
  const channel = service.channel(topic, { config: { private: true } });
  const httpChannel = channel as unknown as {
    httpSend?: (event: string, payload: unknown) => Promise<
      "ok" | "timed out" | "error" |
      { success: true } |
      { success: false; status: number; error: string }
    >;
  };
  try {
    if (typeof httpChannel.httpSend === "function") {
      const result = await httpChannel.httpSend("revision", { revision });
      if (typeof result === "string" && result !== "ok") throw new Error(`broadcast returned ${result}`);
      if (typeof result === "object" && !result.success) throw new Error(`broadcast returned HTTP ${result.status}: ${result.error}`);
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

export function serve(
  handler: (request: Request) => Promise<Response>,
  onMethodRejected?: (reason: string) => void,
): void {
  Deno.serve(async (request) => {
    if (request.method === "OPTIONS") return new Response("ok", { headers: corsHeaders });
    if (request.method !== "POST") {
      onMethodRejected?.("method_not_allowed");
      return json({ error: "method not allowed" }, 405);
    }
    try {
      return await handler(request);
    } catch (error) {
      if (error instanceof UpdateRequiredError) {
        return json({
          Error: "update_required",
          Reason: "Please update Elemento to play online.",
          ProtocolVersion: protocolVersion,
        }, 426);
      }
      if (error instanceof HttpError) return json({ error: error.message }, error.status);
      console.error("edge function failed", error);
      return json({ error: "internal server error" }, 500);
    }
  });
}
