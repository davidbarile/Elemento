import { CardDefinitionData, DeckDefinitionData } from "./deck_definition_data.ts";
import { FairView } from "./fair_view.ts";
import { GameSimulator } from "./game_simulator.ts";
import { EMoveType, EPowerCardType, RulesConfigData } from "./game_state_data.ts";
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

    const seatOneState = new MatchFactory().Create(fixture.seed, rules, decks, null, 1);
    if (seatOneState.StartingPlayerId !== 1 || seatOneState.CurrentPlayerId !== 1
      || seatOneState.TurnNumber !== 1 || seatOneState.Players[1].Hand.length !== rules.NumCardsToDeal
      || seatOneState.Players[0].Hand.length !== 0) {
      throw new Error(`${name} starting seat 1 setup mismatch`);
    }
    assertNetJsonEqual(new MatchAuthority().Replay(fixture.seed, rules, decks, [], 1), seatOneState,
      `${name} starting seat 1 replay`);

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

export function runPowerCardTests(): void {
  const cases = [
    { type: EPowerCardType.Heal, id: "heal", targetPlayerId: 0 },
    { type: EPowerCardType.Freeze, id: "freeze", targetPlayerId: 1 },
    { type: EPowerCardType.Lightning, id: "lightning", targetPlayerId: 1 },
  ];

  for (const powerCardCase of cases) {
    const simulator = new GameSimulator();
    const rules = Object.assign(new RulesConfigData(), { NumCardsToDeal: 0 });
    const state = simulator.CreateGame(71, rules, []);
    const card = DeckDefinitionData.CreatePowerCard(powerCardCase.type);
    card.Id = powerCardCase.id;
    state.Players[0].Hand.push(card);

    if (powerCardCase.type === EPowerCardType.Heal) {
      state.Players[0].Zones[0].Health = 0;
      state.Players[0].Zones[0].IsDestroyed = true;
    }

    const move = simulator.GetLegalMoves(state, 0).find((candidate) =>
      candidate.Type === EMoveType.PlayCard
      && candidate.CardId === powerCardCase.id
      && candidate.TargetPlayerId === powerCardCase.targetPlayerId
      && candidate.TargetZoneId === 0);
    if (move == null) throw new Error(`${powerCardCase.id}: expected legal power-card move`);

    const powerDeckCount = state.PowerDeck.length;
    const targetZone = state.Players[powerCardCase.targetPlayerId].Zones[0];
    const targetHealth = targetZone.Health;
    simulator.ApplyMove(state, move);

    if (state.Players[0].Hand.length !== 0) throw new Error(`${powerCardCase.id}: source card was not consumed`);
    if (state.PowerDeck.length !== powerDeckCount + 1
      || state.PowerDeck[state.PowerDeck.length - 1].Id !== null
      || !state.PowerDeck[state.PowerDeck.length - 1].IsPowerCard) {
      throw new Error(`${powerCardCase.id}: power card was not recycled`);
    }
    if (state.Revision !== 1 || state.Moves.length !== 1) {
      throw new Error(`${powerCardCase.id}: move was not recorded`);
    }
    if (powerCardCase.type === EPowerCardType.Heal
      && (targetZone.Health !== rules.ElementZoneStartingHealth || targetZone.IsDestroyed)) {
      throw new Error(`${powerCardCase.id}: zone was not healed`);
    }
    if (powerCardCase.type === EPowerCardType.Freeze && targetZone.FrozenHealth !== rules.FrozenZoneHealth) {
      throw new Error(`${powerCardCase.id}: zone was not frozen`);
    }
    if (powerCardCase.type === EPowerCardType.Lightning && targetZone.Health !== targetHealth - rules.LightningDamage) {
      throw new Error(`${powerCardCase.id}: zone was not damaged`);
    }
  }

  console.log("PASS power cards");
}

if (typeof Deno !== "undefined") {
  Deno.test("C# golden fixtures", runGoldenTests);
  Deno.test("power cards", runPowerCardTests);
} else {
  await runGoldenTests();
  runPowerCardTests();
}
