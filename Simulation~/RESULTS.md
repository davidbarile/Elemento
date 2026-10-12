# Step A2 AI simulation results

Run date: 2026-10-12. All baselines used the Release build, seed `12345`, the default Choice deck/rules, alternating named AIs between seat 0 and seat 1 each game, and a cap of 400 player-turns. A “player-turn” is one player's complete opportunity to act; it is half of the UI's two-player displayed round. Win-rate intervals are 95% Wilson score intervals and use all 1,000 games as the denominator, including timeouts.

## Commands

```bash
/home/box/.dotnet/dotnet build Simulation~/Simulation.sln --configuration Release --no-restore --maxcpucount:1
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --p1 hard --p2 hard
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --p1 hard --p2 easy
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --p1 hard --p2 medium
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --p1 random --p2 hard
```

## Win rates and game lengths

| Matchup | P1 wins (95% CI) | P2 wins (95% CI) | First-player wins (95% CI) | Avg / median player-turns | Points / zones / draw / timeout |
| --- | ---: | ---: | ---: | ---: | ---: |
| hard vs hard | 475/1000 = 47.50% (44.42–50.60%) | 517/1000 = 51.70% (48.60–54.78%) | 528/1000 = 52.80% (49.70–55.88%) | 24.60 / 17.0 | 988 / 4 / 0 / 8 |
| hard vs easy | 880/1000 = 88.00% (85.84–89.87%) | 91/1000 = 9.10% (7.47–11.04%) | 513/1000 = 51.30% (48.20–54.39%) | 32.91 / 17.0 | 921 / 50 / 0 / 29 |
| hard vs medium | 796/1000 = 79.60% (76.99–81.98%) | 175/1000 = 17.50% (15.27–19.98%) | 517/1000 = 51.70% (48.60–54.78%) | 35.29 / 20.0 | 937 / 34 / 0 / 29 |
| random vs hard | 4/1000 = 0.40% (0.16–1.02%) | 996/1000 = 99.60% (98.98–99.84%) | 500/1000 = 50.00% (46.91–53.09%) | 17.74 / 16.0 | 886 / 114 / 0 / 0 |

The named P1/P2 participants each started exactly 500 games. “First-player wins” always means seat 0 wins, regardless of which named participant occupied that seat. The Domain rules have no draw terminal state, so the draw column is zero; games still active at 400 player-turns are reported separately as timeouts.

## Bad-pattern definitions

- **BombOverShield:** the selected move resolves a bomb, while an own “precious” zone has points greater than zero and contains a multiplier card whose effective multiplier is greater than one; that zone's top card has no effective shield for the zone element; at least one matching bomb copy remains after subtracting the acting hand and both public boards from the known Choice-deck composition; and a shield play to that zone is currently legal. “Unseen” does not reveal whether a card is in the opponent hand or reserve.
- **WeakPointOverBomb:** the selected move is a normal point play to an own zone with effective point value 1 or 2, while a legal bomb against an opponent would either deal at least 3 effective damage, destroy the zone, or remove a multiplier card. Effective damage is `cards removed + zone health lost + frozen health lost`; multiplier removal compares card IDs before and after `GameRules.ResolveBomb`.

Rates below are event counts per 1,000 AI decisions. End Turn and other non-card choices remain in the decision denominator.

| Matchup / AI | Decisions | BombOverShield count (per 1000) | WeakPointOverBomb count (per 1000) |
| --- | ---: | ---: | ---: |
| hard vs hard / P1 hard | 66,217 | 495 (7.475) | 2,996 (45.245) |
| hard vs hard / P2 hard | 66,559 | 537 (8.068) | 2,867 (43.075) |
| hard vs easy / hard | 70,565 | 664 (9.410) | 3,851 (54.574) |
| hard vs easy / easy | 72,759 | 931 (12.796) | 5,621 (77.255) |
| hard vs medium / hard | 78,023 | 698 (8.946) | 4,142 (53.087) |
| hard vs medium / medium | 80,092 | 1,085 (13.547) | 4,004 (49.993) |
| random vs hard / random | 53,402 | 113 (2.116) | 2,142 (40.111) |
| random vs hard / hard | 51,809 | 244 (4.710) | 3,853 (74.369) |

The pattern rate depends on opportunities created by the opponent and is not a context-free quality score. For example, hard has a higher WeakPointOverBomb rate against random because random leaves many unusually strong bomb opportunities available.

## Concrete examples

- Hard, hard-vs-hard game 1 seed 12345, player-turn 1: chose `Card - Fire 1 Water 2` into its Water zone while `Card - Bomb Wild 4` was legal against opponent Air for 4 effective damage.
- Hard, hard-vs-hard game 1 seed 12345, player-turn 6: chose `Combo - Bomb Air 2 = Points Fire 3` against opponent Air while its own Earth zone had 6 points, a multiplier, no top shield, 100 matching unseen bomb copies, and a legal shield play.
- Easy, hard-vs-easy game 2 seed 20264, player-turn 13: chose `Combo - Bomb Wild 3 = Shield Wild 2` against opponent Earth while its own Air zone had 6 points, a multiplier, no top shield, and a legal shield play.
- Medium, hard-vs-medium game 1 seed 12345, player-turn 14: chose `Card - Bomb Wild 4` against opponent Air while its own Fire zone had 24 points, a multiplier, no top shield, and a legal shield play.
- Hard, random-vs-hard game 1 seed 12345, player-turn 4: chose a 2-point Earth play from `Combo - Bomb Fire 1 = Points Earth 2` while a wild-3 bomb was legal for 3 effective damage.

## Diagnosis

1. **Difficulty separation is very large and intentional in the legacy mechanism.** Medium never samples the top 10% and Easy always samples the lower half, so their “mistakes” are mandatory rather than probabilistic. Hard won 88.0% against Easy and 79.6% against Medium. Random move selection was noncompetitive: Hard won 99.6%.
2. **Weak point plays are the dominant measured tactical miss.** Hard still chose a 1–2 point play over a strong/destructive/multiplier-stripping bomb roughly 43–75 times per 1,000 decisions, depending on the opponent-created opportunity distribution. The scoring formula boosts point placement through zone points, multipliers, hand composition, and remaining-move modifiers without first comparing the actual state delta of a legal bomb.
3. **Defensive shields lack an emergency override.** BombOverShield occurred around 7.5–9.4 per 1,000 decisions for Hard and 12.8–13.5 for Easy/Medium in their direct matchups. Shield scoring values cards already above a shield and late-move timing, but it does not explicitly gate offensive bombs when a multiplied scoring zone is exposed.
4. **First-player advantage was small in these samples.** Seat 0 won 50.0–52.8%; every 95% interval includes 50%. The 47.5%/51.7% named split in hard-vs-hard is sampling asymmetry because named participants alternate seats and use the same policy.
5. **Activation/late-game behavior can stall.** Hard-vs-Easy and Hard-vs-Medium each timed out 29 games. The inherited activation valuation, hand-only candidate set, and forced lower-rank difficulty choices can repeatedly trade progress for low-impact actions.

## Proposed fixes (not implemented)

1. Add a tactical pre-pass: if a precious zone is exposed and a legal shield exists, compare its expected protected value against the best offensive move before ordinary scoring.
2. Score bombs from the resolved public-state delta (`ResolveBomb`) with explicit bonuses for destroying a zone and removing multiplier cards; use that value when comparing against point plays.
3. Replace the rank-band difficulty rule with a mistake probability or temperature over scores, allowing Medium/Easy to choose the best move sometimes and avoiding guaranteed bottom-half play.
4. Evaluate short turn plans rather than isolated moves, especially multiplier-then-point, shield timing, activation, and bomb follow-ups; also decide deliberately whether zone-top moves should re-enter the candidate set.
5. Refine threat probability using opponent hand count and a hypergeometric estimate. The current requested/simple unseen-pool predicate is deliberately conservative and is almost always true for a large Choice deck.
6. Add a late-game progress term or repetition guard to reduce activation loops and timeout-cap results.

## Validation

- Release solution build: succeeded with 0 warnings and 0 errors.
- All 49 `[Test]` methods in the compiled Domain test assembly passed via an in-process reflection runner, including the three new AI tests.
- Outside the Codex sandbox, `~/.dotnet/dotnet test --no-restore Simulation~/Simulation.sln` passes: 56/56 tests (2026-10-12).
