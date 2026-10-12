# Elemento deterministic simulator rules

This document records the rules implemented by `Elemento.Domain.GameSimulator`. Line references point to the Unity implementation or serialized source of truth that was used for the port.

## Configuration and game setup

- The default rules are the serialized `SettingsManager` values in `Assets/Scenes/Main.unity:10587-10599`: choose a power-card type, deal 5, maximum hand 7, unlimited moves (`0`), zone health 5, frozen health 5, lightning 5, activation at 10, win at 50, melt 1, and allow discards. `RulesConfigData` uses those values rather than the different field initializers in `Assets/Scripts/Managers/SettingsManager.cs:53-63`.
- A zone holds at most 20 cards, from `Assets/Prefabs/Element Zone.prefab:3224-3225`. The live draw pile is topped up to 10 cards, from `Assets/Scenes/Main.unity:9912-9914` and `Assets/Scripts/Managers/CardsManager.cs:215-240`.
- A game contains two players with Air, Earth, Fire, and Water zones. Unity selects the next player cyclically (`Assets/Scripts/Managers/GameManager.cs:624-632` and `Assets/Scripts/Domain/TurnRules.cs:25-34`).
- Player One begins. A displayed turn increments only on Player One's draw phase (`Assets/Scripts/Managers/GameManager.cs:639-669`); Player Two's half of the round keeps the same turn number (`Assets/Scripts/Managers/GameManager.cs:686-717`).
- Draw and play are exposed as phases in the state. The simulator resolves drawing synchronously and returns in the corresponding playing phase. Unity advances draw to play after dealing (`Assets/Scripts/Managers/GameManager.cs:397-449` and `Assets/Scripts/Managers/GameManager.cs:614-622`).
- At the start of each player's turn, deal up to `NumCardsToDeal` without exceeding `MaxCardsInHand` (`Assets/Scripts/Managers/GameManager.cs:315-333` and `Assets/Scripts/Managers/GameManager.cs:397-449`).

## Card catalog and decks

- `DeckDefinitionData.NormalCards` contains the 455 non-triple definitions referenced by `CardsManager` in its exact extraction order. Triple point and multiplier arrays are intentionally excluded because `shouldIncludeTripleConfigs` is false in `Assets/Scenes/Main.unity:9923`; the inclusion/extraction order is `Assets/Scripts/Managers/CardsManager.cs:103-135`.
- Every definition copies the serialized `CardConfig` count, flags, four point/multiplier/bomb/shield arrays, seven wild/power values, and power flag. The conversion contract is `Assets/Scripts/CardConfig.cs:204-232`. Serialized array blobs are authoritative where old named inspector fields disagree.
- Choice and Chance counts are decoded from `Assets/DeckConfigs/Deck_Choice.asset:15-18` and `Assets/DeckConfigs/Deck_Chance.asset:15-18`. The default simulator deck is Choice because the persisted active deck is `ChoiceDeckID` in `Assets/PlayerData/PlayerData.json:22-28`, and Unity rebuilds the active deck from those counts (`Assets/Scripts/Managers/CardsManager.cs:357-372`).
- The default Choice deck has 555 cards. All 455 definitions remain in the catalog, including zero-count Choice entries. The Chance/config-count deck totals 890.
- The power catalog contains Heal x10, Freeze x12, and Lightning x15 from the three power `CardConfig` assets referenced by `Assets/Scenes/Main.unity` and expanded by `Assets/Scripts/Managers/CardsManager.cs:184-200`.
- The normal reserve is shuffled once when constructed and again whenever a batch is added, matching `GenerateShuffledGameDeck` and `AddBatchOfCardsToGameDeck` (`Assets/Scripts/Managers/CardsManager.cs:215-257`). The top of the live deck is the last card in its list, matching `UICardCell.TopCard` (`Assets/Scripts/UICardCell.cs:49-61`).
- Before a deal, Unity tops up if the live deck has two or fewer cards; after every dealt card it tops up again through the deck-to-hand move (`Assets/Scripts/Managers/GameManager.cs:397-449`, `Assets/Scripts/UICardCell.cs:705-738`). The simulator preserves both points at which batching occurs.
- Used/discarded normal cards and cards destroyed by bombs, lightning, or activation are returned to the normal reserve. Used power cards return to the power deck. Unity performs this recycling in `Card.DestroySelf` (`Assets/Scripts/Card.cs:498-518`). A recycled card receives a new instance ID when it is spawned into the live deck again, matching `CardsManager.SpawnCard` (`Assets/Scripts/Managers/CardsManager.cs:314-354`).
- When fewer than two random power cards remain after a random power draw, Unity discards the remainder and rebuilds/shuffles all configured power cards (`Assets/Scripts/Managers/CardsManager.cs:272-287`). Selected Heal/Freeze/Lightning cards do not consume the random power deck when spawned (`Assets/Scripts/Managers/CardsManager.cs:292-302`), but their later destruction still adds them to that deck (`Assets/Scripts/Card.cs:505-508`). The simulator preserves this slightly unusual growth behavior.

## Turns, moves, hands, and discards

- `MovesPerTurn == 0` means unlimited and Unity represents it as 99 (`Assets/Scripts/Managers/GameManager.cs:935-942`). Otherwise every played/moved card decrements the counter (`Assets/Scripts/UICardCell.cs:745-750`, `Assets/Scripts/UIHand.cs:244-249`, and power/bomb branches in `Assets/Scripts/UICardCell.cs:233-590`).
- With limited moves, reaching zero automatically ends the turn unless discard-to-end-turn mode is active (`Assets/Scripts/Managers/GameManager.cs:1250-1267`).
- Only the top card of a zone can move because all zone actions use `UICardCell.TopCard` (`Assets/Scripts/UICardCell.cs:49-61`, `Assets/Scripts/UICardCell.cs:461-590`, and `Assets/Scripts/UIHand.cs:194-201`). Any card in the player's hand can be selected.
- A top zone card can move back to its owner's non-full hand, or to another legal own zone. Moving it spends a move and recalculates its source zone (`Assets/Scripts/UIHand.cs:168-249` and `Assets/Scripts/UICardCell.cs:691-750`).
- Hand-to-hand swaps are presentation-only and do not change simulation semantics (`Assets/Scripts/UICardCell.cs:214-227` and `Assets/Scripts/UICardCell.cs:444-459`), so they are not emitted as simulator moves.
- `EDiscardMode.None` emits no discard moves. `AllowDiscard` permits discarding without spending a move or ending the turn. `DiscardEndsTurn` permits no ordinary End Turn move; discarding immediately advances the turn (`Assets/Scripts/UICardCell.cs:129-153` and `Assets/Scripts/Managers/GameManager.cs:665-681`).
- Discarding is allowed from a hand or from the top of an owned zone. A discarded zone card causes that zone's score/activation state to be recalculated (`Assets/Scripts/UICardCell.cs:129-153`).
- When discard-to-end-turn is active and the current hand is empty, Unity refuses to refresh activation and refuses a points win (`Assets/Scripts/Managers/GameManager.cs:799-807` and `Assets/Scripts/Managers/GameManager.cs:850-865`). The simulator preserves the existing activation flag in this case and blocks activation moves/wins.

## Normal placement and scoring

- A normal card can be placed only on the current player's matching, non-destroyed, non-frozen, non-full elemental zone (`Assets/Scripts/UICardCell.cs:614-679`).
- Element matching ignores bomb values. A card matches when it has points, multiplier, or shield for the target element, or a wild point/multiplier/shield (`Assets/Scripts/CardData.cs:125-149`). A bomb-only card therefore cannot be placed normally.
- Bomb-combo cards have two modes. On the owner's unfrozen board they are normal point/multiplier/shield cards if their non-bomb half matches. Against an opponent, or against the owner's frozen zone, they resolve as bombs (`Assets/Scripts/CardData.cs:75-98` and `Assets/Scripts/UICardCell.cs:206-230`).
- Zone points first sum all applicable point values, then apply every applicable multiplier in card order. A wild value replaces (does not add to) the element-specific value for that component. Each multiplier greater than one multiplies the running total (`Assets/Scripts/Domain/GameRules.cs:7-41`, with Unity regression coverage in `Assets/Tests/Editor/EditMode/CurrentRulesRegressionTests.cs:64-79`).
- Face-up points run the same calculation while ignoring face-down cards. Player total points are the sum of all four zone point totals (`Assets/Scripts/UIElementZone.cs:182-193`, `Assets/Scripts/UIPlayerBoard.cs:318-335`, and `Assets/Scripts/BoardState.cs:47-58`).

## Activation and power cards

- A non-frozen, non-destroyed zone activates at `points >= ElementZonePointsRequiredToActivate` (`Assets/Scripts/Managers/GameManager.cs:799-831`, `Assets/Scripts/UIElementZone.cs:119-137`). The comparison is inclusive.
- Activation does not spend a move. It removes/recycles every card in the zone, zeroes its points, and deals one power card (`Assets/Scripts/UIElementZone.cs:255-307`).
- When selection is enabled, activation offers Heal, Freeze, and Lightning (`Assets/Scripts/UIElementZone.cs:222-246` and `Assets/Scripts/UIPowerCardPanel.cs:135-157`). Otherwise it draws the first card from the shuffled power deck (`Assets/Scripts/Managers/CardsManager.cs:272-302`).
- Power-card displayed/effect values are updated to the active health/freeze/lightning settings when a card is configured (`Assets/Scripts/Card.cs:80-101`). The simulator applies the rule values when spawning power cards.
- Heal is legal only on the acting player's own empty, non-frozen zone whose health is below starting health. It restores both current and maximum health and can revive a destroyed zone (`Assets/Scripts/UICardCell.cs:233-292`).
- Freeze can target either player's zone. It sets frozen health to the configured maximum, deactivates the zone, and can refresh a partially frozen zone. Due to Unity's branch ordering, a destroyed zone can also be frozen, even when already at maximum frozen health (`Assets/Scripts/UICardCell.cs:165-205`, `Assets/Scripts/UICardCell.cs:310-321`).
- At the start of a player's playing phase, each of that player's frozen zones loses `FrozenZonesMeltAmountPerTurn` frozen health (`Assets/Scripts/Managers/GameManager.cs:779-796`). Reaching zero unfreezes the zone and reactivates it if its points meet the inclusive threshold (`Assets/Scripts/UIElementZone.cs:119-137`).
- Lightning is legal on an opponent's non-frozen, non-destroyed zone, or on the acting player's own frozen, non-destroyed zone. The zone must contain cards unless zone-health damage is enabled (`Assets/Scripts/UICardCell.cs:323-400`).
- Lightning first damages frozen health if frozen; otherwise it removes up to its strength in cards from the top and ignores shields; if the zone is empty it damages zone health (`Assets/Scripts/UICardCell.cs:376-425`).

## Bombs, shields, zone health, and wins

- A bomb needs a bomb value for the target element or a wild bomb. It can attack an opponent's non-frozen zone or the acting player's own frozen zone; it cannot attack an opponent's frozen zone, an own unfrozen zone, or a destroyed zone (`Assets/Scripts/UICardCell.cs:461-514`).
- A bomb first damages frozen health. Otherwise, if cards exist, it removes cards from the top up to bomb strength (`Assets/Scripts/UICardCell.cs:525-560`). Zone health is damaged only when no cards remain at the start of resolution (`Assets/Scripts/UICardCell.cs:561-563`).
- A shield encountered by a bomb stops all deeper removal. If shield strength is greater than bomb strength it persists; if it is equal or lower, the shield is removed and the bomb still stops without touching lower cards (`Assets/Scripts/UICardCell.cs:531-552`). Shield strength is the maximum of the target-element shield and wild shield.
- Zone damage is clamped to zero. At zero health the zone is destroyed and deactivated (`Assets/Scripts/UIElementZone.cs:151-172`). Destroyed zones reject ordinary cards, bombs, and lightning; Heal can revive an empty own zone, and Freeze follows the Unity quirk described above (`Assets/Scripts/UICardCell.cs:179-213`).
- Reaching `TotalPointsRequiredToWin` is an immediate points win; the comparison is inclusive (`Assets/Scripts/Managers/GameManager.cs:833-865`).
- Destroying all four zones is an immediate win for the other player (`Assets/Scripts/Managers/GameManager.cs:833-853`). The simulator reports `EWinCondition.PointsTarget` or `EWinCondition.AllZonesDestroyed`, the winner ID, and game-over state.

## Determinism and player observation

- All simulator randomness uses serialized `DeterministicRandom` state and cursor. Every normal-reserve shuffle, initial power shuffle, and power-deck rebuild advances that same state, so cloning a `GameStateData` is sufficient to resume exactly.
- `GetLegalMoves` is deterministic: it emits activation moves by zone, then hand-card moves, then top-zone-card moves, and finally End Turn when available.
- `PlayerViewData` exposes the viewer's full hand/board, public opponent boards and opponent hand counts, rules, turn/win state, and deck counts. It deliberately contains no opponent hand cards, normal or power deck order, random state, reserve contents, or move history.

## Explicit guesses, simplifications, and animation-driven differences

1. **Seeded shuffle algorithm:** Unity uses `UnityEngine.Random` and a remove/append shuffle (`Assets/Scripts/ExtensionMethods.cs:8-26`). The domain requirement calls for `DeterministicRandom`, so the simulator uses deterministic Fisher-Yates. Shuffle timing and contents match Unity, but a numeric seed is not expected to reproduce Unity's card order.
2. **Atomic animation sequences:** deals, activation discards, bomb/lightning destruction, and power-card delivery are coroutines/tweens in Unity. The simulator commits each accepted move atomically in the same logical order and has no intermediate animation frames, input locks, sound, particles, confirmation panels, or delayed `Destroy` frame.
3. **Draw phases are synchronous:** `PlayerDraw`/`OpponentDraw` exist in serialized state, but public simulator calls finish the automatic deal and return in a playing phase. The UI preference to press Begin Turn instead of auto-deal changes presentation timing, not the resulting rules (`Assets/Scripts/Managers/GameManager.cs:724-734`).
4. **Two-player scope:** the simulator creates the two seats used by the Main scene. Pass-and-play handoff screens, network authority, Unity AI timing, tutorial-only restrictions, and extra unused player enum values are outside this rules simulator.
5. **Default deck selection:** `CreateGame(seed, rules)` always uses the persisted default Choice deck. Both Choice and Chance counts are retained in the catalog, but the requested API has no deck-selection argument.
6. **Power selection UI:** the selected power type is encoded directly in `MoveData.PowerCardType` instead of reproducing `UIPowerCardPanel` and the global `CardsManager.PowerCardOverride`.
7. **Fresh-game manager state:** every `CreateGame` starts with a freshly built seeded power deck. Unity's singleton can preserve/recycle its power list across repeated scene games, but there is no prior singleton lifetime in a standalone simulator creation.
8. **Hand layout:** hand-cell swapping and exact visual insertion positions are omitted because they do not change legality, score, deck contents, or turn state. Hand list order remains deterministic but is not a pixel/layout model.
9. **Discard pile field:** `GameStateData.DiscardPile` is retained for schema compatibility but stays empty. Unity destroys a discarded card and immediately returns its data to the appropriate shuffled reserve (`Assets/Scripts/Card.cs:498-518`); it has no persistent rules-level graveyard.
10. **Activation refresh quirk:** Unity sometimes skips activation recomputation when discard-to-end-turn is active and the hand is empty (`Assets/Scripts/Managers/GameManager.cs:799-807`). The simulator intentionally preserves the prior activation bit rather than “fixing” that behavior.
11. **No tie result:** the current Unity rule path ends immediately on the first qualifying points or destroyed-zones check and exposes win/loss, not a simultaneous score tie (`Assets/Scripts/Managers/GameManager.cs:833-932`). The domain win enum therefore has no tie condition.

## AI port (Step A2)

`LegacyAiPlayer` is the engine-free port of `AIMoveCalculator.CalculateBestMove`; it keeps the original point, multiplier, shield, bomb, power-card, activation, emergency, and late-game weights. In particular, it preserves the bomb multiplier reduction (`* .3`, clamped to 1), the original activation-threshold denominator used by the score heuristics, and the difficulty mechanism:

- Hard always chooses sorted rank 0.
- Medium randomly chooses from `[ceil(count * .1), floor(count * .5))`.
- Easy randomly chooses from `[round-to-even(count * .5), count)`.

The port also preserves the original hand-only candidate behavior. `UIPlayerBoard.GenerateUsefulData` leaves the code that would add zone-top cells commented out (`Assets/Scripts/UIPlayerBoard.cs:246-254`), even though `DoMovesCo` counts eligible zone tops when fixing its turn budget (`Assets/Scripts/UIPlayerBoard.cs:392-397`). The Domain AI therefore does not choose zone-top moves. `RandomLegalAi` is intentionally different and samples every legal move uniformly.

`PlayerViewData.LegalMoves` is a projection of moves already legal for that viewer at the current revision. It exposes no opponent hand cards, deck order, reserve contents, or random state; it only saves each AI from reconstructing legality from public state.

### AI-port deviations from the Unity implementation

1. **Fair replacement for a hidden-hand peek:** the original power-card selection reads `opponentBoard.BiggestBombsInHand` and `opponentBoard.BombsInHand` (`Assets/Scripts/AIMoveCalculator.cs:1089-1095`). The port cannot read the opponent hand. It instead counts matching bomb copies in the known Choice-deck composition after subtracting the acting hand and both visible boards, and uses the maximum/count of that unseen pool. This pool includes the opponent hand and shuffled reserves without identifying either.
2. **Deterministic AI randomness:** difficulty mistakes and random power selection use the supplied `IRandom`; the harness supplies `DeterministicRandom`. Numeric seeds are not expected to reproduce `UnityEngine.Random`, but repeated Domain runs reproduce exactly.
3. **Fresh visible analysis:** Unity calls `GenerateUsefulData` only on the acting `UIPlayerBoard`, so cached aggregate arrays on an opponent can be stale between its turns. The port recomputes all own and public-opponent aggregates from the current fair view. It does not preserve stale UI cache state.
4. **Legal candidates are projected first:** the Unity calculator constructs many discard placeholders while rejecting target combinations. The port scores only current `LegalMoves`, while retaining the legacy extra rejection for a bomb weaker than the top shield. Illegal placeholders therefore do not occupy a difficulty bucket.
5. **Stable ties:** Unity's `List.Sort` does not promise a stable order for equal scores. The port resolves equal scores by deterministic legal-move generation order so seeded results are portable.
6. **No-positive-move handoff:** the UI coroutine stops when the calculator returns `null` or a non-positive move. The port returns the legal End Turn move when available; if discard-to-end-turn is active it returns `null` unless the legacy forced-discard condition selects a hand discard. The harness then uses the legal End Turn move, or the first legal discard in discard-to-end-turn mode, to guarantee progress.
7. **Turn execution without animation timing:** the harness snapshots the same initial move budget as `DoMovesCo`, asks the AI once per decision, and then advances the turn. It does not model coroutine waits, confirmation panels, or the `isHandDepleted` loop-condition typo. The 400-player-turn cap is harness-only and produces a timeout result rather than changing Domain win rules.
8. **No persistent discarded-card observation:** A1 faithfully recycles discarded/destroyed cards immediately and leaves `GameStateData.DiscardPile` empty. Consequently the fair AI view has visible played cards but no graveyard history to expose. Known deck composition minus currently visible cards is the only unseen-pool inference used.
