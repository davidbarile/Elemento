import { GameSimulator } from "./game_simulator.ts";
import { CardInstanceData, GameStateData, PlayerViewData, ZoneStateData, cloneCard, cloneRules, cloneZone } from "./game_state_data.ts";
import { MatchSeatViewData, MatchSnapshotData, SeatPublicStateData } from "./protocol_data.ts";

export class FairView {
  static BuildSeatView(inState: GameStateData, inSeat: number): MatchSeatViewData {
    if (inState == null) throw new TypeError("inState");
    return Object.assign(new MatchSeatViewData(), {
      MatchId: inState.MatchId,
      Seat: inSeat,
      View: this.MaskOpponentZones(new GameSimulator().GetPlayerView(inState, inSeat)),
    });
  }

  static MaskCard(inCard: CardInstanceData | null): CardInstanceData | null {
    if (inCard == null || inCard.IsFaceUp) return inCard;
    return Object.assign(new CardInstanceData(), { Id: inCard.Id, IsFaceUp: false });
  }

  static BuildSnapshot(inState: GameStateData): MatchSnapshotData {
    if (inState == null) throw new TypeError("inState");
    const snapshot = Object.assign(new MatchSnapshotData(), {
      MatchId: inState.MatchId,
      Rules: cloneRules(inState.Rules),
      Revision: inState.Revision,
      TurnNumber: inState.TurnNumber,
      CurrentSeat: inState.CurrentPlayerId,
      TurnPhase: inState.TurnPhase,
      MovesRemaining: inState.MovesRemaining,
      DeckCount: inState.Deck.length,
      ReserveDeckCount: inState.ReserveDeck.length,
      PowerDeckCount: inState.PowerDeck.length,
      DiscardPileCount: inState.DiscardPile.length,
      WinnerSeat: inState.WinnerPlayerId,
      WinCondition: inState.WinCondition,
    });
    for (const player of inState.Players) {
      const seat = Object.assign(new SeatPublicStateData(), { Seat: player.Id, Name: player.Name });
      seat.Zones = player.Zones.map((zone) => this.MaskZone(zone));
      snapshot.Seats.push(seat);
      snapshot.HandCounts.push(player.Hand.length);
    }
    return snapshot;
  }

  private static MaskZone(inZone: ZoneStateData): ZoneStateData {
    const zone = cloneZone(inZone);
    zone.Cards = zone.Cards.map((card) => this.MaskCard(card) ?? cloneCard(card));
    return zone;
  }
  private static MaskOpponentZones(inView: PlayerViewData): PlayerViewData {
    for (const opponent of inView.Opponents) opponent.Zones = opponent.Zones.map((zone) => this.MaskZone(zone));
    return inView;
  }
}
