import { CardDefinitionData } from "./deck_definition_data.ts";
import { GameSimulator } from "./game_simulator.ts";
import { GameStateData, MoveData, RulesConfigData, cloneMove } from "./game_state_data.ts";
import { MatchFactory } from "./match_factory.ts";
import { MoveCommandData, MoveLogEntryData, MoveResultData, ProtocolVersions } from "./protocol_data.ts";

export class MatchAuthority {
  static readonly AcceptedReason = "Accepted";
  static readonly ProtocolVersionReason = "Unsupported protocol version.";
  static readonly MatchReason = "Command match id does not match the state.";
  static readonly SeatReason = "Command seat is invalid.";
  static readonly TurnReason = "It is not this seat's turn.";
  static readonly RevisionReason = "Command revision is stale.";
  static readonly MoveReason = "Move is not legal in the current state.";

  private simulator = new GameSimulator();
  private matchFactory = new MatchFactory();
  private moveLog: MoveLogEntryData[] = [];

  get MoveLog(): readonly MoveLogEntryData[] { return this.moveLog; }

  ValidateAndApply(inState: GameStateData, inCommand: MoveCommandData | null): MoveResultData {
    if (inState == null) throw new TypeError("inState");
    if (inCommand == null) return this.Reject(inState.Revision, MatchAuthority.MoveReason);
    if (inCommand.ProtocolVersion !== ProtocolVersions.Current) return this.Reject(inState.Revision, MatchAuthority.ProtocolVersionReason);
    if (inCommand.MatchId !== inState.MatchId) return this.Reject(inState.Revision, MatchAuthority.MatchReason);
    if (!inState.Players.some((player) => player.Id === inCommand.Seat) || inCommand.Move == null || inCommand.Move.PlayerId !== inCommand.Seat) return this.Reject(inState.Revision, MatchAuthority.SeatReason);
    if (inState.CurrentPlayerId !== inCommand.Seat) return this.Reject(inState.Revision, MatchAuthority.TurnReason);
    if (inCommand.ExpectedRevision !== inState.Revision) return this.Reject(inState.Revision, MatchAuthority.RevisionReason);
    const legalMove = this.simulator.GetLegalMoves(inState, inCommand.Seat).find((candidate) => this.MovesMatch(candidate, inCommand.Move!));
    if (legalMove == null) return this.Reject(inState.Revision, MatchAuthority.MoveReason);
    const revisionBefore = inState.Revision;
    this.simulator.ApplyMove(inState, legalMove);
    const entry = Object.assign(new MoveLogEntryData(), {
      MatchId: inState.MatchId,
      Seat: inCommand.Seat,
      ClientMoveSequence: inCommand.ClientMoveSequence,
      ClientNonce: inCommand.ClientNonce,
      RevisionBefore: revisionBefore,
      RevisionAfter: inState.Revision,
      Move: cloneMove(legalMove),
    });
    this.moveLog.push(entry);
    return Object.assign(new MoveResultData(), { Accepted: true, Reason: MatchAuthority.AcceptedReason, NewRevision: inState.Revision, LogEntry: entry });
  }

  validateAndApply(inState: GameStateData, inCommand: MoveCommandData | null): MoveResultData {
    return this.ValidateAndApply(inState, inCommand);
  }

  Replay(inSeed: number, inRules: RulesConfigData | null, decks: CardDefinitionData[][], log: MoveLogEntryData[]): GameStateData {
    if (log == null) throw new TypeError("inMoveLog");
    const state = this.matchFactory.Create(inSeed, inRules, decks);
    const replayAuthority = new MatchAuthority();
    for (const entry of log) {
      if (entry == null || entry.ProtocolVersion !== ProtocolVersions.Current) throw new Error("The move log contains an unsupported entry.");
      if (entry.RevisionBefore !== state.Revision) throw new Error("The move log revision sequence is invalid.");
      const result = replayAuthority.ValidateAndApply(state, Object.assign(new MoveCommandData(), {
        MatchId: entry.MatchId, Seat: entry.Seat, ClientMoveSequence: entry.ClientMoveSequence, ClientNonce: entry.ClientNonce,
        ExpectedRevision: entry.RevisionBefore, Move: entry.Move == null ? null : cloneMove(entry.Move),
      }));
      if (!result.Accepted || result.NewRevision !== entry.RevisionAfter) throw new Error("The move log cannot be replayed from the supplied setup.");
    }
    return state;
  }

  replay(inSeed: number, inRules: RulesConfigData | null, decks: CardDefinitionData[][], log: MoveLogEntryData[]): GameStateData {
    return this.Replay(inSeed, inRules, decks, log);
  }

  private Reject(revision: number, reason: string): MoveResultData {
    return Object.assign(new MoveResultData(), { Accepted: false, Reason: reason, NewRevision: revision });
  }
  private MovesMatch(first: MoveData, second: MoveData): boolean {
    return first.Type === second.Type && first.CardId === second.CardId && first.SourceZoneId === second.SourceZoneId && first.TargetPlayerId === second.TargetPlayerId && first.TargetZoneId === second.TargetZoneId && first.PowerCardType === second.PowerCardType;
  }
}
