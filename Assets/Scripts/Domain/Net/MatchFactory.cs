using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    public sealed class MatchFactory
    {
        private const int NumSeats = 2;

        private readonly GameSimulator simulator;

        public MatchFactory()
        {
            this.simulator = new GameSimulator();
        }

        public GameStateData Create(int inSeed, RulesConfigData inRules,
            IList<IList<CardDefinitionData>> inDeckDefinitionsBySeat, string inMatchId = null,
            int inStartingSeat = 0)
        {
            if (inDeckDefinitionsBySeat == null)
                throw new ArgumentNullException(nameof(inDeckDefinitionsBySeat));
            if (inDeckDefinitionsBySeat.Count != NumSeats)
                throw new ArgumentException("Exactly two seat decks are required.", nameof(inDeckDefinitionsBySeat));
            if (inStartingSeat < 0 || inStartingSeat >= NumSeats)
                throw new ArgumentOutOfRangeException(nameof(inStartingSeat));

            List<CardInstanceData> sharedDeck = new();
            foreach (var seatDeck in inDeckDefinitionsBySeat)
            {
                if (seatDeck == null)
                    throw new ArgumentException("A seat deck cannot be null.", nameof(inDeckDefinitionsBySeat));

                foreach (var definition in seatDeck)
                {
                    if (definition == null)
                        throw new ArgumentException("A seat deck cannot contain a null definition.", nameof(inDeckDefinitionsBySeat));
                    if (definition.IsPowerCard)
                        throw new ArgumentException("Power cards cannot be added to a seat deck.", nameof(inDeckDefinitionsBySeat));

                    sharedDeck.Add(definition.CreateCard());
                }
            }

            if (sharedDeck.Count == 0)
                throw new ArgumentException("At least one deck card is required.", nameof(inDeckDefinitionsBySeat));

            var state = this.simulator.CreateGame(inSeed, inRules, sharedDeck, inStartingSeat);
            //never derive the public match id from the secret seed
            state.MatchId = string.IsNullOrEmpty(inMatchId) ? "local-match" : inMatchId;
            return state;
        }
    }
}
