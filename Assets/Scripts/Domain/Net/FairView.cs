using System;

namespace Elemento.Domain.Net
{
    public static class FairView
    {
        public static MatchSeatViewData BuildSeatView(GameStateData inState, int inSeat)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));

            return new MatchSeatViewData
            {
                MatchId = inState.MatchId,
                Seat = inSeat,
                View = MaskOpponentZones(new GameSimulator().GetPlayerView(inState, inSeat))
            };
        }

        //face-down cards are hidden information: only their instance id survives
        public static CardInstanceData MaskCard(CardInstanceData inCard)
        {
            if (inCard == null || inCard.IsFaceUp)
                return inCard;

            return new CardInstanceData { Id = inCard.Id, IsFaceUp = false };
        }

        private static ZoneStateData MaskZone(ZoneStateData inZone)
        {
            var zone = inZone.Clone();
            for (int i = 0; i < zone.Cards.Count; i++)
                zone.Cards[i] = MaskCard(zone.Cards[i]);

            return zone;
        }

        private static PlayerViewData MaskOpponentZones(PlayerViewData inView)
        {
            foreach (var opponent in inView.Opponents)
            {
                for (int i = 0; i < opponent.Zones.Count; i++)
                    opponent.Zones[i] = MaskZone(opponent.Zones[i]);
            }

            return inView;
        }

        public static MatchSnapshotData BuildSnapshot(GameStateData inState)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));

            var snapshot = new MatchSnapshotData
            {
                MatchId = inState.MatchId,
                Rules = inState.Rules.Clone(),
                Revision = inState.Revision,
                TurnNumber = inState.TurnNumber,
                CurrentSeat = inState.CurrentPlayerId,
                TurnPhase = inState.TurnPhase,
                MovesRemaining = inState.MovesRemaining,
                DeckCount = inState.Deck.Count,
                ReserveDeckCount = inState.ReserveDeck.Count,
                PowerDeckCount = inState.PowerDeck.Count,
                DiscardPileCount = inState.DiscardPile.Count,
                WinnerSeat = inState.WinnerPlayerId,
                WinCondition = inState.WinCondition
            };

            foreach (var player in inState.Players)
            {
                var seat = new SeatPublicStateData
                {
                    Seat = player.Id,
                    Name = player.Name
                };
                foreach (var zone in player.Zones)
                    seat.Zones.Add(MaskZone(zone));

                snapshot.Seats.Add(seat);
                snapshot.HandCounts.Add(player.Hand.Count);
            }

            return snapshot;
        }
    }
}
