import {
  ETurnPhase,
  EWinCondition,
  MoveData,
  PlayerViewData,
  RulesConfigData,
  ZoneStateData,
} from "./game_state_data.ts";

export const ProtocolVersions = { Current: 1 } as const;

export class MoveCommandData {
  ProtocolVersion: number = ProtocolVersions.Current;
  MatchId: string | null = null;
  Seat = 0;
  ClientMoveSequence = 0;
  ExpectedRevision = 0;
  Move: MoveData | null = null;
  ClientNonce: string | null = null;
}
export class MoveResultData {
  ProtocolVersion: number = ProtocolVersions.Current;
  Accepted = false;
  Reason: string | null = null;
  NewRevision = 0;
  LogEntry: MoveLogEntryData | null = null;
}
export class SeatPublicStateData {
  Seat = 0;
  Name: string | null = null;
  Zones: ZoneStateData[] = [];
}
export class MatchSnapshotData {
  ProtocolVersion: number = ProtocolVersions.Current;
  MatchId: string | null = null;
  Rules: RulesConfigData | null = null;
  Revision = 0;
  TurnNumber = 0;
  CurrentSeat = 0;
  TurnPhase: ETurnPhase = ETurnPhase.PlayerDraw;
  MovesRemaining = 0;
  Seats: SeatPublicStateData[] = [];
  HandCounts: number[] = [];
  DeckCount = 0;
  ReserveDeckCount = 0;
  PowerDeckCount = 0;
  DiscardPileCount = 0;
  WinnerSeat = 0;
  WinCondition: EWinCondition = EWinCondition.None;
}
export class MatchSeatViewData {
  ProtocolVersion: number = ProtocolVersions.Current;
  MatchId: string | null = null;
  Seat = 0;
  View: PlayerViewData | null = null;
}
export class MoveLogEntryData {
  ProtocolVersion: number = ProtocolVersions.Current;
  MatchId: string | null = null;
  Seat = 0;
  ClientMoveSequence = 0;
  ClientNonce: string | null = null;
  RevisionBefore = 0;
  RevisionAfter = 0;
  Move: MoveData | null = null;
}
