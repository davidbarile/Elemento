# Step B AI experiments

Run date: 2026-10-12. Release build, seed `12345`, default Choice deck/rules, alternating named AIs between first and second seat, and a cap of 400 player-turns. Win-rate intervals are 95% Wilson score intervals over all 1,000 games, including timeouts. Bad-pattern columns are `BombOverShield / WeakPointOverBomb` events per 1,000 decisions.

| Step | Strong win rate (95% CI) | First-player win rate (95% CI) | Timeouts | Bad patterns / 1000: Strong | Bad patterns / 1000: Hard | ms/decision: Strong / Hard | Kept? |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| B1: resolved bomb effects | 476/1000 = 47.60% (44.52–50.70%) | 546/1000 = 54.60% (51.50–57.66%) | 13 | 8.395 / 44.799 | 9.159 / 43.073 | 0.0114 / 0.0033 | Yes — neutral result and simpler direct state-delta scoring |
| B1b: comparable point/bomb scale | 536/1000 = 53.60% (50.50–56.67%) | 546/1000 = 54.60% (51.50–57.66%) | 9 | 8.470 / 32.596 | 8.955 / 42.453 | 0.0106 / 0.0032 | Yes — Strong wins increased and WeakPointOverBomb fell by 12.203/1000 |
| B2: emergency shield | 535/1000 = 53.50% (50.40–56.57%) | 544/1000 = 54.40% (51.30–57.46%) | 8 | 8.343 / 32.515 | 9.070 / 42.656 | 0.0171 / 0.0028 | Yes — win-neutral versus B1b, with fewer timeouts and slightly fewer Strong bad patterns |
| B3: whole-turn planning | 812/1000 = 81.20% (78.66–83.50%) | 544/1000 = 54.40% (51.30–57.46%) | 1 | 27.224 / 59.215 | 11.037 / 49.968 | 4.8355 / 0.0032 | Yes — 277 more Strong wins and 7 fewer timeouts than B2; decision time remained below 5 ms |

Strong's opponent was Legacy Hard, which won 511/1000 = 51.10% (95% CI 48.00–54.19%); the remaining 13 games timed out. B1 was kept because neither named AI's interval separates from 50%, the Strong policy derives bomb value from the authoritative resolved public state instead of estimating removed cards, and its measured decision cost remains well below 0.1 ms.

## B1b diagnosis and decision

B1 had already replaced Strong's legacy point formula with actual gained zone points. The remaining scale mismatch was on bombs: card, health, and frozen-health damage counted by WeakPointOverBomb could still produce a bomb score of zero when no scored points were immediately removed. B1b gives point gains and opponent point losses the same win-target proximity multiplier, uses actual effective damage as the bomb-value floor, and retains the resolved zone-kill and multiplier-strip bonuses. It was kept because Strong gained 60 wins, WeakPointOverBomb fell from 44.799 to 32.596 per 1,000 decisions, and timeouts fell from 13 to 9 versus B1.

## B2 decision record

B2 derives the unseen pool only from the known Choice-deck composition minus Strong's hand and both visible boards. It computes the hypergeometric probability that the opponent's visible hand count contains at least one bomb that can beat the zone's top shield, simulates `GameRules.ResolveBomb` with the strongest plausible unseen bomb, and uses the expected resolved loss avoided as an emergency floor on the existing shield score. No hidden hand or deck order is read.

The first B2 variant replaced the existing shield score outright. It produced 546 Strong wins (51.50–57.66%), 551 first-player wins (52.00–58.16%), 2 timeouts, Strong bad patterns 10.301 / 32.256, Hard bad patterns 9.364 / 42.748, and 0.0183 / 0.0028 ms per decision. It was rejected because BombOverShield regressed from 8.470 to 10.301 per 1,000. The retained variant uses expected loss avoided only when it raises the ordinary shield value; versus B1b it was win-neutral, reduced timeouts from 9 to 8, and reduced Strong's BombOverShield and WeakPointOverBomb rates slightly.

## B3 decision record

B3 rebuilds a planning state only from `PlayerViewData`: Strong's hand, both visible boards, public rules and opponent hand counts. Opponent hands and both decks stay empty, random-draw activations are excluded, and the search never simulates opponent moves or future draws. The retained search uses depth 3, beam 6, and seven B2-pruned expansion candidates. It simulates activation and follow-up power plays, multiplier/point sequences, bombs, and legal own zone-top moves. Zone-top moves count as reorders, are capped at three per plan, and cost 0.35 evaluation points each.

End states score Strong's points with win proximity, minus visible opponent points and B2's expected bomb exposure. Strong executes only the first supplied legal move from the best sequence and rebuilds the plan on every decision. Limited-turn games decrement the planner's move budget exactly for card plays; activation remains free as in the simulator.

B3 was retained because Strong wins rose from 535 to 812 and timeouts fell from 8 to 1. Strong's bad-pattern rates regressed from 8.343 / 32.515 to 27.224 / 59.215, so the gain is strategic rather than an improvement to those local heuristics. Measured cost rose to 4.8355 ms/decision, below but close to the 5 ms target; wider candidate expansion measured 5.3894 ms/decision over 50 games and was rejected.

## Legacy parity rerun

The required Hard-vs-Hard rerun exactly reproduced the baseline in `RESULTS.md`: P1 475 wins, P2 517 wins, 528 first-player wins, 24.60 average / 17.0 median player-turns, 988 points wins, 4 zone-destruction wins, and 8 timeouts. All four decision and bad-pattern count sets also matched exactly. Measured AI time was 0.0047 ms/decision for P1 and 0.0051 ms/decision for P2.

## Commands

```bash
/home/box/.dotnet/dotnet build --no-restore Simulation~/Simulation.sln --configuration Release --maxcpucount:1
/home/box/.dotnet/dotnet test --no-restore Simulation~/Simulation.sln --configuration Release --no-build
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --cap 400 --p1 hard --p2 hard
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --cap 400 --p1 strong --p2 hard
```

The VSTest command could not bind its local MSBuild socket in the sandbox (`SocketException: Permission denied`). The freshly built Release test assembly was therefore executed with an in-process reflection runner: 61 tests passed, 0 failed.
