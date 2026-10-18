using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    public sealed class TurnReplayResult
    {
        public GameStateData State;
        public bool IsMatch;
        public string ActualHash;
        public string Reason;
    }

    public sealed class TurnReplayer
    {
        public TurnReplayResult Replay(GameStateData inState, TurnPackageData inTurn)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));
            if (inTurn == null || inTurn.FinalState == null)
                throw new ArgumentException("A committed turn requires its final state.", nameof(inTurn));

            var state = inState.Clone();
            var simulator = new GameSimulator();
            Stack<GameStateData> checkpoints = new();
            string reason = null;
            try
            {
                foreach (var action in inTurn.Actions)
                {
                    if (action.Kind == "Undo")
                    {
                        state = checkpoints.Pop();
                        continue;
                    }
                    checkpoints.Push(state.Clone());
                    var move = action.Move;
                    if (action.Kind == "EndTurn" && move == null)
                        move = new MoveData { Type = EMoveType.EndTurn, PlayerId = state.CurrentPlayerId };
                    simulator.ApplyRecordedMove(state, move, action.DrawnCards, action.PowerCard);
                }

                var finalState = inTurn.FinalState;
                if (!HaveSameKnownDeckCards(state.Deck, finalState.Deck))
                    reason = "known_deck_card_mismatch";
                else if (!HaveSameReservoir(state.Deck, state.ReserveDeck, finalState.Deck, finalState.ReserveDeck))
                    reason = "normal_reservoir_mismatch";
                else if (!HaveSameReservoir(state.PowerDeck, null, finalState.PowerDeck, null))
                    reason = "power_reservoir_mismatch";
                else if (TryValidateFinalIdentities(finalState, out reason))
                {
                    // V2 records outcomes, not shuffle permutations or RNG cursors.
                    state.Deck = CloneCards(finalState.Deck);
                    state.ReserveDeck = CloneCards(finalState.ReserveDeck);
                    state.PowerDeck = CloneCards(finalState.PowerDeck);
                    state.Random = finalState.Random.Clone();
                    state.NextCardInstanceId = finalState.NextCardInstanceId;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                || exception is ArgumentException || exception is NullReferenceException)
            {
                reason = exception.Message;
            }

            string actualHash = GameStateHasher.Compute(state);
            bool matches = reason == null && string.Equals(actualHash, inTurn.FinalStateHash, StringComparison.Ordinal)
                && string.Equals(actualHash, GameStateHasher.Compute(inTurn.FinalState), StringComparison.Ordinal);
            return new TurnReplayResult
            {
                State = matches ? state : inTurn.FinalState.Clone(),
                IsMatch = matches,
                ActualHash = actualHash,
                Reason = matches ? null : reason ?? "state_hash_mismatch"
            };
        }

        private static List<CardInstanceData> CloneCards(List<CardInstanceData> inCards)
        {
            List<CardInstanceData> result = new();
            foreach (var card in inCards)
                result.Add(card.Clone());
            return result;
        }

        private static bool HaveSameReservoir(List<CardInstanceData> inFirst,
            List<CardInstanceData> inFirstRemainder, List<CardInstanceData> inSecond,
            List<CardInstanceData> inSecondRemainder)
        {
            List<string> first = ReservoirSignatures(inFirst, inFirstRemainder);
            List<string> second = ReservoirSignatures(inSecond, inSecondRemainder);
            if (first.Count != second.Count)
                return false;
            for (int i = 0; i < first.Count; ++i)
                if (!string.Equals(first[i], second[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static bool HaveSameKnownDeckCards(List<CardInstanceData> inReplayDeck,
            List<CardInstanceData> inFinalDeck)
        {
            foreach (var replayCard in inReplayDeck)
            {
                CardInstanceData finalCard = inFinalDeck.Find(inCard => inCard.Id == replayCard.Id);
                if (finalCard == null || NetJson.Serialize(finalCard) != NetJson.Serialize(replayCard))
                    return false;
            }
            return true;
        }

        private static List<string> ReservoirSignatures(List<CardInstanceData> inCards,
            List<CardInstanceData> inRemainder)
        {
            List<string> result = new();
            AddReservoirSignatures(inCards, result);
            AddReservoirSignatures(inRemainder, result);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static void AddReservoirSignatures(List<CardInstanceData> inCards, List<string> inResult)
        {
            if (inCards == null)
                return;
            foreach (var source in inCards)
            {
                var card = source.Clone();
                card.Id = null;
                card.IsFaceUp = false;
                inResult.Add(NetJson.Serialize(card));
            }
        }

        private static bool TryValidateFinalIdentities(GameStateData inState, out string outReason)
        {
            List<string> ids = new();
            int anonymous = 0;
            if (!AddLiveIdentities(inState.Deck, ids) || !AddLiveIdentities(inState.DiscardPile, ids))
            {
                outReason = "live_card_id_missing";
                return false;
            }
            if (!AddAnonymousIdentities(inState.ReserveDeck, ref anonymous)
                || !AddAnonymousIdentities(inState.PowerDeck, ref anonymous))
            {
                outReason = "reservoir_card_has_id";
                return false;
            }
            foreach (var player in inState.Players)
            {
                if (!AddLiveIdentities(player.Hand, ids))
                {
                    outReason = "live_card_id_missing";
                    return false;
                }
                foreach (var zone in player.Zones)
                {
                    if (!AddLiveIdentities(zone.Cards, ids))
                    {
                        outReason = "live_card_id_missing";
                        return false;
                    }
                }
            }
            if (!CardIdentityValidator.TryValidate(ids, anonymous, ids, anonymous, out outReason))
                return false;

            int requiredNextId = 0;
            foreach (string id in ids)
                requiredNextId = CardInstanceData.ReserveSequence(requiredNextId, id);
            if (inState.NextCardInstanceId < requiredNextId)
            {
                outReason = "next_card_id_regressed";
                return false;
            }
            outReason = null;
            return true;
        }

        private static bool AddLiveIdentities(List<CardInstanceData> inCards, List<string> inIds)
        {
            foreach (var card in inCards)
            {
                if (string.IsNullOrEmpty(card.Id))
                    return false;
                inIds.Add(card.Id);
            }
            return true;
        }

        private static bool AddAnonymousIdentities(List<CardInstanceData> inCards, ref int inAnonymous)
        {
            foreach (var card in inCards)
            {
                if (!string.IsNullOrEmpty(card.Id))
                    return false;
                ++inAnonymous;
            }
            return true;
        }
    }
}
