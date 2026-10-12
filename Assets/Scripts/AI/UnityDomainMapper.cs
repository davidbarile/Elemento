using System;
using Elemento.Domain;
using UnityEngine;
using DomainAiDifficulty = Elemento.Domain.EAiDifficulty;
using DomainDiscardMode = Elemento.Domain.EDiscardMode;
using DomainMoveType = Elemento.Domain.EMoveType;
using DomainPowerCardType = Elemento.Domain.EPowerCardType;
using DomainTurnPhase = Elemento.Domain.ETurnPhase;

public static class UnityDomainMapper
{
    public static RulesConfigData CreateRules(SettingsManager inSettings, UIPlayerBoard[] inPlayerBoards,
        int inCardSpawnBatchSize)
    {
        if (inSettings == null)
            throw new ArgumentNullException(nameof(inSettings));
        if (inPlayerBoards == null)
            throw new ArgumentNullException(nameof(inPlayerBoards));

        int maxCardsPerZone = 0;
        foreach (var board in inPlayerBoards)
        {
            if (board == null || board.ElementZones == null)
                continue;

            foreach (var zone in board.ElementZones)
            {
                if (zone != null && zone.CardCell != null)
                    maxCardsPerZone = Math.Max(maxCardsPerZone, zone.CardCell.MaxCards);
            }
        }

        return new RulesConfigData
        {
            NumCardsToDeal = inSettings.NumCardsToDeal,
            MaxCardsInHand = inSettings.MaxCardsInHand,
            MovesPerTurn = inSettings.TutorialSafeMovesPerTurn,
            ElementZoneStartingHealth = inSettings.TutorialSafeElementZoneHealth,
            FrozenZoneHealth = inSettings.FrozenZoneHealth,
            LightningDamage = inSettings.LightningDamage,
            ElementZonePointsRequiredToActivate = inSettings.ElementZonePointsRequiredToActivate,
            TotalPointsRequiredToWin = inSettings.TotalPointsRequiredToWin,
            FrozenZonesMeltAmountPerTurn = inSettings.FrozenZonesMeltAmountPerTurn,
            MaxCardsPerZone = maxCardsPerZone,
            CardSpawnBatchSize = inCardSpawnBatchSize,
            AllowSelectPowerCardType = inSettings.AllowSelectPowerCardType,
            DiscardMode = (DomainDiscardMode)inSettings.DiscardMode
        };
    }

    public static PlayerViewData CreatePlayerView(UIPlayerBoard inPlayerBoard, UIPlayerBoard[] inPlayerBoards,
        int inDeckCount, int inPowerDeckCount, int inTurnNumber, GameManager.ETurnPhase inTurnPhase,
        int inMovesRemaining, RulesConfigData inRules)
    {
        if (inPlayerBoard == null)
            throw new ArgumentNullException(nameof(inPlayerBoard));
        if (inPlayerBoards == null)
            throw new ArgumentNullException(nameof(inPlayerBoards));
        if (inRules == null)
            throw new ArgumentNullException(nameof(inRules));

        int playerId = (int)inPlayerBoard.PlayerType;
        var playerState = CreatePlayerState(inPlayerBoard, true);
        var state = new GameStateData
        {
            MatchId = "unity-live-game",
            Revision = 0,
            TurnNumber = inTurnNumber,
            CurrentPlayerId = playerId,
            TurnPhase = (DomainTurnPhase)inTurnPhase,
            MovesRemaining = inMovesRemaining,
            WinnerPlayerId = -1,
            Rules = inRules.Clone(),
            Random = new RandomStateData { State = 1 }
        };

        foreach (var board in inPlayerBoards)
        {
            if (board == null)
                throw new InvalidOperationException("The live game contains a missing player board.");

            state.Players.Add(board == inPlayerBoard ? playerState : CreatePlayerState(board, false));
        }

        var view = new PlayerViewData
        {
            Revision = state.Revision,
            TurnNumber = state.TurnNumber,
            CurrentPlayerId = playerId,
            TurnPhase = state.TurnPhase,
            MovesRemaining = state.MovesRemaining,
            WinnerPlayerId = -1,
            WinType = EWinCondition.None,
            DeckCount = inDeckCount,
            PowerDeckCount = inPowerDeckCount,
            Rules = state.Rules.Clone(),
            Player = playerState.Clone()
        };

        foreach (var board in inPlayerBoards)
        {
            if (board == inPlayerBoard)
                continue;

            var opponentState = FindPlayer(state, (int)board.PlayerType);
            var opponent = new OpponentViewData
            {
                Id = opponentState.Id,
                Name = opponentState.Name,
                HandCount = board.Hand == null ? 0 : board.Hand.NumCardsInHand
            };

            foreach (var zone in opponentState.Zones)
                opponent.Zones.Add(zone.Clone());

            view.Opponents.Add(opponent);
        }

        view.LegalMoves = new GameSimulator().GetLegalMoves(state, playerId);
        return view;
    }

    public static DomainAiDifficulty MapDifficulty(SettingsManager.EAiDifficulty inDifficulty)
    {
        switch (inDifficulty)
        {
            case SettingsManager.EAiDifficulty.Easy:
                return DomainAiDifficulty.Easy;
            case SettingsManager.EAiDifficulty.Medium:
                return DomainAiDifficulty.Medium;
            case SettingsManager.EAiDifficulty.Hard:
                return DomainAiDifficulty.Hard;
            default:
                throw new ArgumentOutOfRangeException(nameof(inDifficulty));
        }
    }

    public static bool TryCreateMove(MoveData inDomainMove, PlayerViewData inView,
        UIPlayerBoard inPlayerBoard, UIPlayerBoard[] inPlayerBoards, out Move outMove,
        out bool outShouldEndTurn, out string outError)
    {
        outMove = null;
        outShouldEndTurn = false;
        outError = string.Empty;

        if (inDomainMove == null)
            return Fail("Strong AI returned no move.", out outError);
        if (inView == null || inPlayerBoard == null || inPlayerBoards == null)
            return Fail("Strong AI move mapping received incomplete live state.", out outError);
        if (!IsLegalMove(inDomainMove, inView))
            return Fail("Strong AI returned a move outside the supplied legal move list.", out outError);
        if (inDomainMove.PlayerId != (int)inPlayerBoard.PlayerType)
            return Fail("Strong AI returned a move for the wrong player.", out outError);

        if (inDomainMove.Type == DomainMoveType.EndTurn)
        {
            outShouldEndTurn = true;
            return true;
        }

        if (inDomainMove.Type == DomainMoveType.ActivateZone)
        {
            var activationZone = FindZone(inPlayerBoard, inDomainMove.SourceZoneId);
            if (activationZone == null || !activationZone.IsActivated)
                return Fail("Strong AI activation zone is no longer available.", out outError);

            var powerCardType = MapPowerCardType(inDomainMove.PowerCardType);
            outMove = Move.CreateMove(activationZone.CardCell, activationZone.CardCell,
                false, activationZone, false, powerCardType, inDomainMove.PlayerId);
            outMove.Data.PowerCardType = inDomainMove.PowerCardType;
            return true;
        }

        var sourceCell = FindSourceCell(inPlayerBoard, inDomainMove.CardId, inDomainMove.SourceZoneId);
        if (sourceCell == null || sourceCell.TopCard == null)
            return Fail("Strong AI source card is no longer available.", out outError);

        if (inDomainMove.Type == DomainMoveType.DiscardCard)
        {
            outMove = Move.CreateMove(sourceCell, sourceCell, true, null, false,
                sourceCell.TopCard.CardType, inDomainMove.PlayerId, inDomainMove.CardId);
            return true;
        }

        if (inDomainMove.Type != DomainMoveType.PlayCard)
            return Fail("Strong AI returned an unsupported move type.", out outError);

        UICardCell targetCell;
        if (inDomainMove.TargetZoneId < 0)
        {
            targetCell = inPlayerBoard.Hand == null ? null : inPlayerBoard.Hand.GetFirstOpenCell();
        }
        else
        {
            var targetBoard = FindBoard(inPlayerBoards, inDomainMove.TargetPlayerId);
            var targetZone = FindZone(targetBoard, inDomainMove.TargetZoneId);
            targetCell = targetZone == null ? null : targetZone.CardCell;
        }

        if (targetCell == null)
            return Fail("Strong AI target is no longer available.", out outError);

        outMove = Move.CreateMove(sourceCell, targetCell, false, null, false,
            sourceCell.TopCard.CardType, inDomainMove.PlayerId, inDomainMove.CardId);
        return true;
    }

    public static bool IsLegalMove(MoveData inMove, PlayerViewData inView)
    {
        if (inMove == null || inView == null || inView.LegalMoves == null)
            return false;

        foreach (var legalMove in inView.LegalMoves)
        {
            if (MovesMatch(legalMove, inMove))
                return true;
        }

        return false;
    }

    private static PlayerStateData CreatePlayerState(UIPlayerBoard inBoard, bool inIncludeHand)
    {
        if (inBoard.Hand == null)
            throw new InvalidOperationException("A live player board is missing its hand.");

        var state = inBoard.CreateStateData();
        foreach (var zone in state.Zones)
        {
            foreach (var card in zone.Cards)
            {
                if (card == null || string.IsNullOrEmpty(card.Id))
                    throw new InvalidOperationException("A visible board card is missing its instance ID.");
            }
        }

        if (!inIncludeHand)
            return state;

        foreach (var cell in inBoard.Hand.CardCells)
        {
            if (cell == null || cell.TopCard == null)
                continue;

            state.Hand.Add(CreateCard(cell.TopCard));
        }

        return state;
    }

    private static CardInstanceData CreateCard(Card inCard)
    {
        if (inCard == null || inCard.Data == null || inCard.CardFlip == null || string.IsNullOrEmpty(inCard.Id))
            throw new InvalidOperationException("A live card is missing data, face state, or its instance ID.");

        return inCard.CreateInstanceData();
    }

    private static PlayerStateData FindPlayer(GameStateData inState, int inPlayerId)
    {
        foreach (var player in inState.Players)
        {
            if (player.Id == inPlayerId)
                return player;
        }

        throw new InvalidOperationException("The live player could not be mapped to the Domain state.");
    }

    private static UIPlayerBoard FindBoard(UIPlayerBoard[] inBoards, int inPlayerId)
    {
        foreach (var board in inBoards)
        {
            if (board != null && (int)board.PlayerType == inPlayerId)
                return board;
        }

        return null;
    }

    private static UIElementZone FindZone(UIPlayerBoard inBoard, int inZoneId)
    {
        if (inBoard == null || inBoard.ElementZones == null)
            return null;

        foreach (var zone in inBoard.ElementZones)
        {
            if (zone != null && zone.CardCell != null && (int)zone.CardCell.ElementType == inZoneId)
                return zone;
        }

        return null;
    }

    private static UICardCell FindSourceCell(UIPlayerBoard inBoard, string inCardId, int inSourceZoneId)
    {
        if (string.IsNullOrEmpty(inCardId))
            return null;

        if (inSourceZoneId >= 0)
        {
            var zone = FindZone(inBoard, inSourceZoneId);
            return zone != null && zone.CardCell.TopCard != null && zone.CardCell.TopCard.Id == inCardId
                ? zone.CardCell
                : null;
        }

        foreach (var cell in inBoard.Hand.CardCells)
        {
            if (cell != null && cell.TopCard != null && cell.TopCard.Id == inCardId)
                return cell;
        }

        return null;
    }

    private static Card.ECardType MapPowerCardType(DomainPowerCardType inPowerCardType)
    {
        switch (inPowerCardType)
        {
            case DomainPowerCardType.None:
                return Card.ECardType.None;
            case DomainPowerCardType.Heal:
                return Card.ECardType.Heal;
            case DomainPowerCardType.Freeze:
                return Card.ECardType.Freeze;
            case DomainPowerCardType.Lightning:
                return Card.ECardType.Lightning;
            default:
                throw new ArgumentOutOfRangeException(nameof(inPowerCardType));
        }
    }

    private static bool MovesMatch(MoveData inFirst, MoveData inSecond)
    {
        return inFirst.PlayerId == inSecond.PlayerId
            && inFirst.Type == inSecond.Type
            && inFirst.CardId == inSecond.CardId
            && inFirst.SourceZoneId == inSecond.SourceZoneId
            && inFirst.TargetPlayerId == inSecond.TargetPlayerId
            && inFirst.TargetZoneId == inSecond.TargetZoneId
            && inFirst.PowerCardType == inSecond.PowerCardType;
    }

    private static bool Fail(string inError, out string outError)
    {
        outError = inError;
        return false;
    }
}

public sealed class UnityDomainRandom : IRandom
{
    public int NextInt(int inMaxExclusive)
    {
        return UnityEngine.Random.Range(0, inMaxExclusive);
    }

    public double NextDouble()
    {
        return UnityEngine.Random.value;
    }
}
