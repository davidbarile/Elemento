using System;
using System.Collections.Generic;

namespace Elemento.Domain
{
    public sealed class GameSimulator
    {
        private const int NumPlayers = 2;
        private const int UnlimitedMoves = 99;

        public GameStateData CreateGame(int inSeed, RulesConfigData inRules = null, int inStartingPlayerId = 0)
        {
            return CreateGame(inSeed, inRules, DeckDefinitionData.CreateChoiceDeck(), inStartingPlayerId);
        }

        public GameStateData CreateGame(int inSeed, RulesConfigData inRules, IList<CardInstanceData> inDeck,
            int inStartingPlayerId = 0)
        {
            if (inDeck == null)
                throw new ArgumentNullException(nameof(inDeck));
            if (inStartingPlayerId < 0 || inStartingPlayerId >= NumPlayers)
                throw new ArgumentOutOfRangeException(nameof(inStartingPlayerId));

            var rules = inRules == null ? new RulesConfigData() : inRules.Clone();
            ValidateRules(rules);

            var state = new GameStateData
            {
                MatchId = "match-" + inSeed,
                Seed = inSeed,
                StartingPlayerId = inStartingPlayerId,
                CurrentPlayerId = inStartingPlayerId,
                WinnerPlayerId = -1,
                Rules = rules,
                Random = new DeterministicRandom(inSeed).GetState()
            };

            state.Players.Add(CreatePlayer(0, "Player One", rules));
            state.Players.Add(CreatePlayer(1, "Player Two", rules));
            foreach (var sourceCard in inDeck)
            {
                if (sourceCard == null)
                    throw new ArgumentException("The deck cannot contain null cards.", nameof(inDeck));

                var card = sourceCard.Clone();
                card.Id = null;
                card.IsFaceUp = false;
                state.ReserveDeck.Add(card);
            }
            Shuffle(state, state.ReserveDeck);
            AddBatchOfCardsToGameDeck(state);
            state.PowerDeck.AddRange(DeckDefinitionData.CreatePowerDeck());
            Shuffle(state, state.PowerDeck);
            BeginTurn(state, inStartingPlayerId);

            return state;
        }

        public List<MoveData> GetLegalMoves(GameStateData inState, int inPlayerId)
        {
            ValidateState(inState);
            var moves = new List<MoveData>();

            if (IsGameOver(inState) || inState.CurrentPlayerId != inPlayerId || !IsPlayingPhase(inState.TurnPhase))
                return moves;

            var player = GetPlayer(inState, inPlayerId);
            bool canPlayCard = inState.Rules.MovesPerTurn <= 0 || inState.MovesRemaining > 0;
            int moveSequence = inState.Moves.Count + 1;

            AddActivationMoves(inState, player, moves, ref moveSequence);

            foreach (var card in player.Hand)
            {
                AddCardMoves(inState, player, card, -1, canPlayCard, moves, ref moveSequence);
            }

            foreach (var sourceZone in player.Zones)
            {
                if (sourceZone.Cards.Count == 0)
                    continue;

                var card = sourceZone.Cards[sourceZone.Cards.Count - 1];
                AddCardMoves(inState, player, card, sourceZone.Id, canPlayCard, moves, ref moveSequence);
            }

            if (inState.Rules.DiscardMode != EDiscardMode.DiscardEndsTurn)
                moves.Add(CreateMove(inState, moveSequence, inPlayerId, EMoveType.EndTurn));

            return moves;
        }

        public List<MoveData> GetLegalMoves(GameStateData inState, PlayerStateData inPlayer)
        {
            if (inPlayer == null)
                throw new ArgumentNullException(nameof(inPlayer));

            return GetLegalMoves(inState, inPlayer.Id);
        }

        public void ApplyMove(GameStateData inState, MoveData inMove)
        {
            ValidateState(inState);
            if (inMove == null)
                throw new ArgumentNullException(nameof(inMove));
            if (inMove.ExpectedRevision != inState.Revision)
                throw new InvalidOperationException("The move was generated for a different game revision.");

            var legalMoves = GetLegalMoves(inState, inMove.PlayerId);
            MoveData legalMove = null;
            foreach (var candidate in legalMoves)
            {
                if (MovesMatch(candidate, inMove))
                {
                    legalMove = candidate;
                    break;
                }
            }

            if (legalMove == null)
                throw new InvalidOperationException("The move is not legal in the current game state.");

            if (legalMove.Type == EMoveType.EndTurn)
            {
                RecordMove(inState, legalMove);
                EndTurnInternal(inState);
                ++inState.Revision;
                return;
            }

            var player = GetPlayer(inState, legalMove.PlayerId);

            if (legalMove.Type == EMoveType.ActivateZone)
            {
                ApplyActivation(inState, player, legalMove);
                RecordMove(inState, legalMove);
                ++inState.Revision;
                return;
            }

            ZoneStateData sourceZone;
            var card = RemoveSourceCard(player, legalMove.CardId, legalMove.SourceZoneId, out sourceZone);

            if (legalMove.Type == EMoveType.DiscardCard)
            {
                RecycleCard(inState, card);
                if (sourceZone != null)
                    RefreshZone(sourceZone, inState.Rules, player);

                RecordMove(inState, legalMove);
                ++inState.Revision;

                if (inState.Rules.DiscardMode == EDiscardMode.DiscardEndsTurn && !IsGameOver(inState))
                    EndTurnInternal(inState);

                return;
            }

            if (legalMove.TargetZoneId < 0)
            {
                card.IsFaceUp = true;
                player.Hand.Add(card);
                RefreshZone(sourceZone, inState.Rules, player);
            }
            else
            {
                var targetPlayer = GetPlayer(inState, legalMove.TargetPlayerId);
                var targetZone = GetZone(targetPlayer, legalMove.TargetZoneId);
                ApplyCardToZone(inState, player, sourceZone, card, targetPlayer, targetZone);
            }

            DecrementMoves(inState);
            RecordMove(inState, legalMove);
            ++inState.Revision;
            CheckPointsWin(inState, player);

            if (!IsGameOver(inState)
                && inState.Rules.MovesPerTurn > 0
                && inState.MovesRemaining <= 0
                && inState.Rules.DiscardMode != EDiscardMode.DiscardEndsTurn)
            {
                EndTurnInternal(inState);
            }
        }

        public void EndTurn(GameStateData inState)
        {
            ValidateState(inState);
            if (IsGameOver(inState))
                throw new InvalidOperationException("The game is already over.");
            if (inState.Rules.DiscardMode == EDiscardMode.DiscardEndsTurn)
                throw new InvalidOperationException("This discard mode requires a discard to end the turn.");

            var move = CreateMove(inState, inState.Moves.Count + 1, inState.CurrentPlayerId, EMoveType.EndTurn);
            RecordMove(inState, move);
            EndTurnInternal(inState);
            ++inState.Revision;
        }

        public bool IsGameOver(GameStateData inState)
        {
            ValidateState(inState);
            return inState.WinCondition != EWinCondition.None;
        }

        public int Winner(GameStateData inState)
        {
            ValidateState(inState);
            return inState.WinnerPlayerId;
        }

        public EWinCondition WinType(GameStateData inState)
        {
            ValidateState(inState);
            return inState.WinCondition;
        }

        public PlayerViewData GetPlayerView(GameStateData inState, int inPlayerId)
        {
            ValidateState(inState);
            var player = GetPlayer(inState, inPlayerId);
            var view = new PlayerViewData
            {
                Revision = inState.Revision,
                TurnNumber = inState.TurnNumber,
                StartingPlayerId = inState.StartingPlayerId,
                CurrentPlayerId = inState.CurrentPlayerId,
                TurnPhase = inState.TurnPhase,
                MovesRemaining = inState.MovesRemaining,
                WinnerPlayerId = inState.WinnerPlayerId,
                WinType = inState.WinCondition,
                DeckCount = inState.Deck.Count + inState.ReserveDeck.Count,
                PowerDeckCount = inState.PowerDeck.Count,
                NextCardInstanceId = inState.GetNextCardInstanceSequence(),
                Rules = inState.Rules.Clone(),
                Player = player.Clone()
            };

            foreach (var opponent in inState.Players)
            {
                if (opponent.Id == inPlayerId)
                    continue;

                var opponentView = new OpponentViewData
                {
                    Id = opponent.Id,
                    Name = opponent.Name,
                    HandCount = opponent.Hand.Count
                };

                foreach (var zone in opponent.Zones)
                    opponentView.Zones.Add(zone.Clone());

                view.Opponents.Add(opponentView);
            }

            foreach (var move in GetLegalMoves(inState, inPlayerId))
                view.LegalMoves.Add(move.Clone());

            return view;
        }

        private static PlayerStateData CreatePlayer(int inId, string inName, RulesConfigData inRules)
        {
            var player = new PlayerStateData { Id = inId, Name = inName };
            for (int i = 0; i < 4; ++i)
            {
                player.Zones.Add(new ZoneStateData
                {
                    Id = i,
                    ElementType = (EElementType)(i + 1),
                    Health = inRules.ElementZoneStartingHealth,
                    MaxHealth = inRules.ElementZoneStartingHealth
                });
            }

            return player;
        }

        private static void ValidateRules(RulesConfigData inRules)
        {
            if (inRules.NumCardsToDeal < 0)
                throw new ArgumentOutOfRangeException(nameof(inRules.NumCardsToDeal));
            if (inRules.MaxCardsInHand < 1)
                throw new ArgumentOutOfRangeException(nameof(inRules.MaxCardsInHand));
            if (inRules.MovesPerTurn < 0)
                throw new ArgumentOutOfRangeException(nameof(inRules.MovesPerTurn));
            if (inRules.ElementZoneStartingHealth < 0)
                throw new ArgumentOutOfRangeException(nameof(inRules.ElementZoneStartingHealth));
            if (inRules.FrozenZoneHealth < 0)
                throw new ArgumentOutOfRangeException(nameof(inRules.FrozenZoneHealth));
            if (inRules.LightningDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(inRules.LightningDamage));
            if (inRules.MaxCardsPerZone < 1)
                throw new ArgumentOutOfRangeException(nameof(inRules.MaxCardsPerZone));
            if (inRules.CardSpawnBatchSize < 1)
                throw new ArgumentOutOfRangeException(nameof(inRules.CardSpawnBatchSize));
        }

        private static void ValidateState(GameStateData inState)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));
        }

        private static bool IsPlayingPhase(ETurnPhase inPhase)
        {
            return inPhase == ETurnPhase.PlayerPlaying || inPhase == ETurnPhase.OpponentPlaying;
        }

        private static PlayerStateData GetPlayer(GameStateData inState, int inPlayerId)
        {
            foreach (var player in inState.Players)
            {
                if (player.Id == inPlayerId)
                    return player;
            }

            throw new ArgumentOutOfRangeException(nameof(inPlayerId));
        }

        private static ZoneStateData GetZone(PlayerStateData inPlayer, int inZoneId)
        {
            foreach (var zone in inPlayer.Zones)
            {
                if (zone.Id == inZoneId)
                    return zone;
            }

            throw new ArgumentOutOfRangeException(nameof(inZoneId));
        }

        private static void BeginTurn(GameStateData inState, int inPlayerId)
        {
            inState.CurrentPlayerId = inPlayerId;
            if (inPlayerId == inState.StartingPlayerId)
                ++inState.TurnNumber;
            inState.TurnPhase = inPlayerId == 0 ? ETurnPhase.PlayerDraw : ETurnPhase.OpponentDraw;
            inState.MovesRemaining = inState.Rules.MovesPerTurn > 0 ? inState.Rules.MovesPerTurn : UnlimitedMoves;

            var player = GetPlayer(inState, inPlayerId);
            DrawCards(inState, player, inState.Rules.NumCardsToDeal);

            inState.TurnPhase = inPlayerId == 0 ? ETurnPhase.PlayerPlaying : ETurnPhase.OpponentPlaying;
            MeltFrozenZones(player, inState.Rules);
            inState.MovesRemaining = inState.Rules.MovesPerTurn > 0 ? inState.Rules.MovesPerTurn : UnlimitedMoves;
        }

        private static void EndTurnInternal(GameStateData inState)
        {
            int currentIndex = inState.Players.FindIndex(inPlayer => inPlayer.Id == inState.CurrentPlayerId);
            int nextIndex = TurnRules.GetNextPlayerIndex(currentIndex, inState.Players.Count);
            BeginTurn(inState, inState.Players[nextIndex].Id);
        }

        private static void DrawCards(GameStateData inState, PlayerStateData inPlayer, int inCount)
        {
            int cardsToDraw = Math.Min(inCount, inState.Rules.MaxCardsInHand - inPlayer.Hand.Count);
            for (int i = 0; i < cardsToDraw; ++i)
            {
                if (inState.Deck.Count <= 2)
                    AddBatchOfCardsToGameDeck(inState);

                if (inState.Deck.Count == 0)
                    break;

                int topIndex = inState.Deck.Count - 1;
                var card = inState.Deck[topIndex];
                inState.Deck.RemoveAt(topIndex);
                card.IsFaceUp = true;
                inPlayer.Hand.Add(card);
                AddBatchOfCardsToGameDeck(inState);
            }
        }

        private static void AddBatchOfCardsToGameDeck(GameStateData inState)
        {
            Shuffle(inState, inState.ReserveDeck);
            int count = Math.Min(inState.Rules.CardSpawnBatchSize - inState.Deck.Count, inState.ReserveDeck.Count);
            for (int i = 0; i < count; ++i)
            {
                var template = inState.ReserveDeck[0];
                inState.ReserveDeck.RemoveAt(0);
                var card = template.Clone();
                card.Id = inState.TakeNextCardInstanceId();
                card.IsFaceUp = false;
                inState.Deck.Add(card);
            }
        }

        private static void Shuffle(GameStateData inState, IList<CardInstanceData> inCards)
        {
            var random = new DeterministicRandom(inState.Random);
            DeterministicShuffle.Shuffle(inCards, random);
            inState.Random = random.GetState();
        }

        private static void MeltFrozenZones(PlayerStateData inPlayer, RulesConfigData inRules)
        {
            if (inRules.FrozenZonesMeltAmountPerTurn <= 0)
                return;

            foreach (var zone in inPlayer.Zones)
            {
                if (!zone.IsFrozen)
                    continue;

                CopyZone(GameRules.ResolveMelt(zone, inRules), zone);
            }
        }

        private static void AddActivationMoves(GameStateData inState, PlayerStateData inPlayer, List<MoveData> inMoves, ref int inMoveSequence)
        {
            bool activationBlocked = inState.Rules.DiscardMode == EDiscardMode.DiscardEndsTurn && inPlayer.Hand.Count == 0;
            if (activationBlocked)
                return;

            foreach (var zone in inPlayer.Zones)
            {
                if (!zone.IsActivated)
                    continue;

                if (inState.Rules.AllowSelectPowerCardType)
                {
                    AddActivationMove(inState, inPlayer.Id, zone.Id, EPowerCardType.Heal, inMoves, ref inMoveSequence);
                    AddActivationMove(inState, inPlayer.Id, zone.Id, EPowerCardType.Freeze, inMoves, ref inMoveSequence);
                    AddActivationMove(inState, inPlayer.Id, zone.Id, EPowerCardType.Lightning, inMoves, ref inMoveSequence);
                }
                else
                {
                    AddActivationMove(inState, inPlayer.Id, zone.Id, EPowerCardType.None, inMoves, ref inMoveSequence);
                }
            }
        }

        private static void AddActivationMove(GameStateData inState, int inPlayerId, int inZoneId, EPowerCardType inPowerCardType, List<MoveData> inMoves, ref int inMoveSequence)
        {
            var move = CreateMove(inState, inMoveSequence++, inPlayerId, EMoveType.ActivateZone);
            move.SourceZoneId = inZoneId;
            move.TargetPlayerId = inPlayerId;
            move.TargetZoneId = inZoneId;
            move.PowerCardType = inPowerCardType;
            inMoves.Add(move);
        }

        private static void AddCardMoves(GameStateData inState, PlayerStateData inPlayer, CardInstanceData inCard, int inSourceZoneId, bool inCanPlayCard, List<MoveData> inMoves, ref int inMoveSequence)
        {
            if (inState.Rules.DiscardMode != EDiscardMode.None)
            {
                var discard = CreateMove(inState, inMoveSequence++, inPlayer.Id, EMoveType.DiscardCard);
                discard.CardId = inCard.Id;
                discard.SourceZoneId = inSourceZoneId;
                inMoves.Add(discard);
            }

            if (!inCanPlayCard)
                return;

            if (inSourceZoneId >= 0 && inPlayer.Hand.Count < inState.Rules.MaxCardsInHand)
            {
                var returnToHand = CreateMove(inState, inMoveSequence++, inPlayer.Id, EMoveType.PlayCard);
                returnToHand.CardId = inCard.Id;
                returnToHand.SourceZoneId = inSourceZoneId;
                returnToHand.TargetPlayerId = inPlayer.Id;
                returnToHand.TargetZoneId = -1;
                inMoves.Add(returnToHand);
            }

            foreach (var targetPlayer in inState.Players)
            {
                foreach (var targetZone in targetPlayer.Zones)
                {
                    if (targetPlayer.Id == inPlayer.Id && targetZone.Id == inSourceZoneId)
                        continue;
                    if (!CanPlayCardToZone(inState, inPlayer, inCard, targetPlayer, targetZone))
                        continue;

                    var play = CreateMove(inState, inMoveSequence++, inPlayer.Id, EMoveType.PlayCard);
                    play.CardId = inCard.Id;
                    play.SourceZoneId = inSourceZoneId;
                    play.TargetPlayerId = targetPlayer.Id;
                    play.TargetZoneId = targetZone.Id;
                    inMoves.Add(play);
                }
            }
        }

        private static bool CanPlayCardToZone(GameStateData inState, PlayerStateData inPlayer, CardInstanceData inCard, PlayerStateData inTargetPlayer, ZoneStateData inTargetZone)
        {
            bool isOwnZone = inPlayer.Id == inTargetPlayer.Id;

            if (inCard.IsPowerCard)
            {
                if (inCard.WildZoneHeal > 0)
                    return GameRules.CanHealZone(inTargetZone, inCard, inState.Rules, isOwnZone);
                if (inCard.WildFreeze > 0)
                    return GameRules.CanFreezeZone(inTargetZone, inCard, inState.Rules);
                if (inCard.WildLightning > 0)
                    return GameRules.CanLightningZone(inTargetZone, inCard, inState.Rules, isOwnZone);

                return false;
            }

            if (HasBomb(inCard) && (!isOwnZone || inTargetZone.IsFrozen))
                return GameRules.CanBombZone(inTargetZone, inCard, inState.Rules, isOwnZone);

            if (IsPureBomb(inCard))
                return false;

            return isOwnZone
                && !inTargetZone.IsDestroyed
                && !inTargetZone.IsFrozen
                && inTargetZone.Cards.Count < inState.Rules.MaxCardsPerZone
                && GameRules.CanPlayCardInZone(inCard, inTargetZone.ElementType);
        }

        private static bool HasBomb(CardInstanceData inCard)
        {
            if (inCard.WildBomb > 0)
                return true;

            foreach (int bomb in inCard.Bombs)
            {
                if (bomb > 0)
                    return true;
            }

            return false;
        }

        private static bool IsPureBomb(CardInstanceData inCard)
        {
            return HasBomb(inCard)
                && !HasAnyValue(inCard.Points, inCard.WildPoints)
                && !HasAnyValue(inCard.Multipliers, inCard.WildMultiplier)
                && !HasAnyValue(inCard.Shields, inCard.WildShield);
        }

        private static bool HasAnyValue(int[] inValues, int inWildValue)
        {
            if (inWildValue > 0)
                return true;

            foreach (int value in inValues)
            {
                if (value > 0)
                    return true;
            }

            return false;
        }

        private static MoveData CreateMove(GameStateData inState, int inSequence, int inPlayerId, EMoveType inType)
        {
            return new MoveData
            {
                Id = MoveData.CreateId(inSequence),
                PlayerId = inPlayerId,
                Type = inType,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1,
                ExpectedRevision = inState.Revision
            };
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

        private static void ApplyActivation(GameStateData inState, PlayerStateData inPlayer, MoveData inMove)
        {
            var zone = GetZone(inPlayer, inMove.SourceZoneId);
            while (zone.Cards.Count > 0)
            {
                int topIndex = zone.Cards.Count - 1;
                var card = zone.Cards[topIndex];
                zone.Cards.RemoveAt(topIndex);
                RecycleCard(inState, card);
            }

            RefreshZone(zone, inState.Rules, inPlayer);
            CardInstanceData powerCard;

            if (inMove.PowerCardType == EPowerCardType.None)
                powerCard = DrawRandomPowerCard(inState);
            else
                powerCard = SpawnPowerCard(inState, DeckDefinitionData.CreatePowerCard(inMove.PowerCardType));

            if (powerCard != null)
                inPlayer.Hand.Add(powerCard);
        }

        private static CardInstanceData DrawRandomPowerCard(GameStateData inState)
        {
            if (inState.PowerDeck.Count == 0)
                RebuildPowerDeck(inState);
            if (inState.PowerDeck.Count == 0)
                return null;

            var template = inState.PowerDeck[0];
            inState.PowerDeck.RemoveAt(0);
            var card = SpawnPowerCard(inState, template);

            if (inState.PowerDeck.Count < 2)
                RebuildPowerDeck(inState);

            return card;
        }

        private static void RebuildPowerDeck(GameStateData inState)
        {
            inState.PowerDeck.Clear();
            inState.PowerDeck.AddRange(DeckDefinitionData.CreatePowerDeck());
            Shuffle(inState, inState.PowerDeck);
        }

        private static CardInstanceData SpawnPowerCard(GameStateData inState, CardInstanceData inTemplate)
        {
            if (inTemplate == null)
                return null;

            var card = inTemplate.Clone();
            card.Id = inState.TakeNextCardInstanceId();
            card.IsFaceUp = true;
            if (card.WildZoneHeal > 0)
                card.WildZoneHeal = inState.Rules.ElementZoneStartingHealth;
            if (card.WildFreeze > 0)
                card.WildFreeze = inState.Rules.FrozenZoneHealth;
            if (card.WildLightning > 0)
                card.WildLightning = inState.Rules.LightningDamage;

            return card;
        }

        private static CardInstanceData RemoveSourceCard(PlayerStateData inPlayer, string inCardId, int inSourceZoneId, out ZoneStateData outSourceZone)
        {
            outSourceZone = null;
            if (inSourceZoneId < 0)
                return RemoveCard(inPlayer.Hand, inCardId);

            outSourceZone = GetZone(inPlayer, inSourceZoneId);
            if (outSourceZone.Cards.Count == 0 || outSourceZone.Cards[outSourceZone.Cards.Count - 1].Id != inCardId)
                throw new InvalidOperationException("Only the top zone card can be moved.");

            int topIndex = outSourceZone.Cards.Count - 1;
            var card = outSourceZone.Cards[topIndex];
            outSourceZone.Cards.RemoveAt(topIndex);
            return card;
        }

        private static CardInstanceData RemoveCard(List<CardInstanceData> inCards, string inCardId)
        {
            for (int i = 0; i < inCards.Count; ++i)
            {
                if (inCards[i].Id != inCardId)
                    continue;

                var card = inCards[i];
                inCards.RemoveAt(i);
                return card;
            }

            throw new InvalidOperationException("The source card no longer exists.");
        }

        private static void ApplyCardToZone(GameStateData inState, PlayerStateData inPlayer, ZoneStateData inSourceZone, CardInstanceData inCard, PlayerStateData inTargetPlayer, ZoneStateData inTargetZone)
        {
            var originalTarget = inTargetZone.Clone();
            bool isOwnZone = inPlayer.Id == inTargetPlayer.Id;

            if (inCard.IsPowerCard)
            {
                ZoneStateData result;
                if (inCard.WildZoneHeal > 0)
                    result = GameRules.ResolveHeal(inTargetZone, inState.Rules);
                else if (inCard.WildFreeze > 0)
                    result = GameRules.ResolveFreeze(inTargetZone, inState.Rules);
                else
                    result = GameRules.ResolveLightning(inTargetZone, inCard, inState.Rules);

                RecycleMissingCards(inState, originalTarget, result);
                PreserveBlockedActivation(inState, inPlayer, originalTarget, result);
                CopyZone(result, inTargetZone);
                RecycleCard(inState, inCard);
            }
            else if (HasBomb(inCard) && (!isOwnZone || inTargetZone.IsFrozen))
            {
                var result = GameRules.ResolveBomb(inTargetZone, inCard, inState.Rules);
                RecycleMissingCards(inState, originalTarget, result);
                PreserveBlockedActivation(inState, inPlayer, originalTarget, result);
                CopyZone(result, inTargetZone);
                RecycleCard(inState, inCard);
            }
            else
            {
                inCard.IsFaceUp = true;
                inTargetZone.Cards.Add(inCard);
                RefreshZone(inTargetZone, inState.Rules, inTargetPlayer);
            }

            if (inSourceZone != null)
                RefreshZone(inSourceZone, inState.Rules, inPlayer);

            if (inTargetZone.IsDestroyed)
                CheckDestroyedZonesWin(inState, inTargetPlayer);
        }

        private static void PreserveBlockedActivation(GameStateData inState, PlayerStateData inPlayer, ZoneStateData inBefore, ZoneStateData inAfter)
        {
            if (inState.Rules.DiscardMode != EDiscardMode.DiscardEndsTurn || inPlayer.Hand.Count > 0)
                return;
            if (inAfter.IsDestroyed)
                return;

            if (inBefore.IsFrozen)
            {
                inAfter.IsActivated = !inAfter.IsFrozen
                    && GameRules.HasReachedActivationThreshold(inAfter.Points, inState.Rules.ElementZonePointsRequiredToActivate);
            }
            else
            {
                inAfter.IsActivated = inBefore.IsActivated;
            }
        }

        private static void RecycleMissingCards(GameStateData inState, ZoneStateData inBefore, ZoneStateData inAfter)
        {
            foreach (var beforeCard in inBefore.Cards)
            {
                bool remains = false;
                foreach (var afterCard in inAfter.Cards)
                {
                    if (beforeCard.Id == afterCard.Id)
                    {
                        remains = true;
                        break;
                    }
                }

                if (!remains)
                    RecycleCard(inState, beforeCard);
            }
        }

        private static void RecycleCard(GameStateData inState, CardInstanceData inCard)
        {
            var template = inCard.Clone();
            template.Id = null;
            template.IsFaceUp = false;

            if (template.IsPowerCard)
                inState.PowerDeck.Add(template);
            else
                inState.ReserveDeck.Add(template);
        }

        private static void RefreshZone(ZoneStateData inZone, RulesConfigData inRules, PlayerStateData inOwner)
        {
            if (inZone == null)
                return;

            inZone.Points = GameRules.CalculateZonePoints(inZone);
            inZone.FaceUpPoints = GameRules.CalculateZonePoints(inZone, true);
            if (inRules.DiscardMode == EDiscardMode.DiscardEndsTurn && inOwner.Hand.Count == 0)
                return;

            inZone.IsActivated = !inZone.IsFrozen
                && !inZone.IsDestroyed
                && GameRules.HasReachedActivationThreshold(inZone.Points, inRules.ElementZonePointsRequiredToActivate);
        }

        private static void CopyZone(ZoneStateData inSource, ZoneStateData inTarget)
        {
            inTarget.Id = inSource.Id;
            inTarget.ElementType = inSource.ElementType;
            inTarget.Points = inSource.Points;
            inTarget.FaceUpPoints = inSource.FaceUpPoints;
            inTarget.Health = inSource.Health;
            inTarget.MaxHealth = inSource.MaxHealth;
            inTarget.FrozenHealth = inSource.FrozenHealth;
            inTarget.IsActivated = inSource.IsActivated;
            inTarget.IsDestroyed = inSource.IsDestroyed;
            inTarget.Cards.Clear();
            foreach (var card in inSource.Cards)
                inTarget.Cards.Add(card.Clone());
        }

        private static void DecrementMoves(GameStateData inState)
        {
            if (inState.Rules.MovesPerTurn > 0)
                inState.MovesRemaining = Math.Max(0, inState.MovesRemaining - 1);
        }

        private static void CheckDestroyedZonesWin(GameStateData inState, PlayerStateData inDestroyedPlayer)
        {
            if (!GameRules.AreAllZonesDestroyed(inDestroyedPlayer))
                return;

            foreach (var player in inState.Players)
            {
                if (player.Id == inDestroyedPlayer.Id)
                    continue;

                inState.WinnerPlayerId = player.Id;
                inState.WinCondition = EWinCondition.AllZonesDestroyed;
                return;
            }
        }

        private static void CheckPointsWin(GameStateData inState, PlayerStateData inPlayer)
        {
            if (inState.WinCondition != EWinCondition.None)
                return;
            if (inState.Rules.DiscardMode == EDiscardMode.DiscardEndsTurn && inPlayer.Hand.Count == 0)
                return;
            if (!GameRules.HasPointsTargetWin(inPlayer, inState.Rules))
                return;

            inState.WinnerPlayerId = inPlayer.Id;
            inState.WinCondition = EWinCondition.PointsTarget;
        }

        private static void RecordMove(GameStateData inState, MoveData inMove)
        {
            var record = inMove.Clone();
            record.Id = MoveData.CreateId(inState.Moves.Count + 1);
            record.ExpectedRevision = inState.Revision;
            inState.Moves.Add(record);
        }
    }
}
