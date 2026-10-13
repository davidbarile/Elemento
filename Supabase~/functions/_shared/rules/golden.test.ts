import { CardDefinitionData } from "./deck_definition_data.ts";
import { FairView } from "./fair_view.ts";
import { RulesConfigData } from "./game_state_data.ts";
import { MatchAuthority } from "./match_authority.ts";
import { MatchFactory } from "./match_factory.ts";

type JsonObject = Record<string, unknown>;

interface GoldenFixture {
  seed: number;
  matchId: string;
  rules: JsonObject;
  decks: JsonObject[][];
  initialState: unknown;
  moveLog: JsonObject[];
  stateAfterEachMove: unknown[];
  finalState: unknown;
  seatViews: unknown[];
}

function normalize(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(normalize);
  if (value !== null && typeof value === "object") {
    const result: JsonObject = {};
    for (const key of Object.keys(value).sort()) result[key] = normalize((value as JsonObject)[key]);
    return result;
  }
  return value;
}

function firstDifference(expected: unknown, actual: unknown, path = "$" ): string | null {
  if (Object.is(expected, actual)) return null;
  if (Array.isArray(expected) && Array.isArray(actual)) {
    if (expected.length !== actual.length) return `${path}.length: expected ${expected.length}, got ${actual.length}`;
    for (let i = 0; i < expected.length; ++i) {
      const difference = firstDifference(expected[i], actual[i], `${path}[${i}]`);
      if (difference != null) return difference;
    }
    return null;
  }
  if (expected !== null && actual !== null && typeof expected === "object" && typeof actual === "object") {
    const expectedKeys = Object.keys(expected as JsonObject).sort();
    const actualKeys = Object.keys(actual as JsonObject).sort();
    if (JSON.stringify(expectedKeys) !== JSON.stringify(actualKeys)) return `${path} keys: expected ${JSON.stringify(expectedKeys)}, got ${JSON.stringify(actualKeys)}`;
    for (const key of expectedKeys) {
      const difference = firstDifference((expected as JsonObject)[key], (actual as JsonObject)[key], `${path}.${key}`);
      if (difference != null) return difference;
    }
    return null;
  }
  return `${path}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`;
}

function assertNetJsonEqual(actual: unknown, expected: unknown, label: string): void {
  const difference = firstDifference(normalize(expected), normalize(actual));
  if (difference != null) throw new Error(`${label}: ${difference}`);
}

function hydrateDefinition(value: JsonObject): CardDefinitionData {
  return Object.assign(Object.create(CardDefinitionData.prototype) as CardDefinitionData, value);
}

async function listFixtures(): Promise<string[]> {
  const url = new URL("./golden/", import.meta.url);
  if (typeof Deno !== "undefined") {
    const paths: string[] = [];
    for await (const entry of Deno.readDir(url)) if (entry.isFile && entry.name.endsWith(".json")) paths.push(new URL(entry.name, url).href);
    return paths.sort();
  }
  const { readdir } = await import("node:fs/promises");
  return (await readdir(url)).filter((name) => name.endsWith(".json")).sort().map((name) => new URL(name, url).href);
}

async function readJson(url: string): Promise<GoldenFixture> {
  if (typeof Deno !== "undefined") return JSON.parse(await Deno.readTextFile(new URL(url))) as GoldenFixture;
  const { readFile } = await import("node:fs/promises");
  return JSON.parse(await readFile(new URL(url), "utf8")) as GoldenFixture;
}

export async function runGoldenTests(): Promise<void> {
  const fixturePaths = await listFixtures();
  if (fixturePaths.length !== 6) throw new Error(`expected 6 golden fixtures, found ${fixturePaths.length}`);
  for (const path of fixturePaths) {
    const fixture = await readJson(path);
    const name = new URL(path).pathname.split("/").pop()!;
    const rules = Object.assign(new RulesConfigData(), fixture.rules);
    const decks = fixture.decks.map((deck) => deck.map(hydrateDefinition));
    const state = new MatchFactory().Create(fixture.seed, rules, decks, fixture.matchId);
    assertNetJsonEqual(state, fixture.initialState, `${name} initialState`);

    const authority = new MatchAuthority();
    for (let i = 0; i < fixture.moveLog.length; ++i) {
      const entry = fixture.moveLog[i];
      const result = authority.validateAndApply(state, {
        ProtocolVersion: entry.ProtocolVersion as number,
        MatchId: entry.MatchId as string,
        Seat: entry.Seat as number,
        ClientMoveSequence: entry.ClientMoveSequence as number,
        ExpectedRevision: entry.RevisionBefore as number,
        Move: entry.Move as never,
        ClientNonce: entry.ClientNonce as string,
      });
      if (!result.Accepted) throw new Error(`${name} move ${i}: rejected: ${result.Reason}`);
      assertNetJsonEqual(state, fixture.stateAfterEachMove[i], `${name} stateAfterEachMove[${i}]`);
    }
    assertNetJsonEqual(state, fixture.finalState, `${name} finalState`);
    assertNetJsonEqual(FairView.BuildSeatView(state, 0), fixture.seatViews[0], `${name} seatViews[0]`);
    assertNetJsonEqual(FairView.BuildSeatView(state, 1), fixture.seatViews[1], `${name} seatViews[1]`);
    console.log(`PASS ${name}`);
  }
}

if (typeof Deno !== "undefined") Deno.test("C# golden fixtures", runGoldenTests);
else await runGoldenTests();
