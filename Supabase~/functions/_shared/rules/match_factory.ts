import { CardDefinitionData } from "./deck_definition_data.ts";
import { GameSimulator } from "./game_simulator.ts";
import { GameStateData, RulesConfigData } from "./game_state_data.ts";

export class MatchFactory {
  private simulator = new GameSimulator();

  Create(inSeed: number, inRules: RulesConfigData | null, inDeckDefinitionsBySeat: CardDefinitionData[][], inMatchId: string | null = null, inStartingSeat = 0): GameStateData {
    if (inDeckDefinitionsBySeat == null) throw new TypeError("inDeckDefinitionsBySeat");
    if (inDeckDefinitionsBySeat.length !== 2) throw new Error("Exactly two seat decks are required.");
    if (!Number.isInteger(inStartingSeat) || inStartingSeat < 0 || inStartingSeat >= 2) throw new RangeError("inStartingSeat");
    const sharedDeck = [];
    for (const seatDeck of inDeckDefinitionsBySeat) {
      if (seatDeck == null) throw new Error("A seat deck cannot be null.");
      for (const definition of seatDeck) {
        if (definition == null) throw new Error("A seat deck cannot contain a null definition.");
        if (definition.IsPowerCard) throw new Error("Power cards cannot be added to a seat deck.");
        sharedDeck.push(definition.CreateCard());
      }
    }
    if (sharedDeck.length === 0) throw new Error("At least one deck card is required.");
    const state = this.simulator.CreateGame(inSeed, inRules, sharedDeck, inStartingSeat);
    state.MatchId = inMatchId == null || inMatchId.length === 0 ? "local-match" : inMatchId;
    return state;
  }
}
