# Step B AI experiments

Run date: 2026-10-12. Release build, seed `12345`, default Choice deck/rules, alternating named AIs between first and second seat, and a cap of 400 player-turns. Win-rate intervals are 95% Wilson score intervals over all 1,000 games, including timeouts. Bad-pattern columns are `BombOverShield / WeakPointOverBomb` events per 1,000 decisions.

| Step | Strong win rate (95% CI) | First-player win rate (95% CI) | Timeouts | Bad patterns / 1000: Strong | Bad patterns / 1000: Hard | ms/decision: Strong / Hard | Kept? |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| B1: resolved bomb effects | 476/1000 = 47.60% (44.52–50.70%) | 546/1000 = 54.60% (51.50–57.66%) | 13 | 8.395 / 44.799 | 9.159 / 43.073 | 0.0114 / 0.0033 | Yes — neutral result and simpler direct state-delta scoring |

Strong's opponent was Legacy Hard, which won 511/1000 = 51.10% (95% CI 48.00–54.19%); the remaining 13 games timed out. B1 was kept because neither named AI's interval separates from 50%, the Strong policy derives bomb value from the authoritative resolved public state instead of estimating removed cards, and its measured decision cost remains well below 0.1 ms.

## Legacy parity rerun

The required Hard-vs-Hard rerun exactly reproduced the baseline in `RESULTS.md`: P1 475 wins, P2 517 wins, 528 first-player wins, 24.60 average / 17.0 median player-turns, 988 points wins, 4 zone-destruction wins, and 8 timeouts. All four decision and bad-pattern count sets also matched exactly. Measured AI time was 0.0034 ms/decision for P1 and 0.0033 ms/decision for P2.

## Commands

```bash
/home/box/.dotnet/dotnet build --no-restore Simulation~/Simulation.sln --configuration Release --maxcpucount:1
/home/box/.dotnet/dotnet test --no-restore Simulation~/Simulation.sln --configuration Release --no-build
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --cap 400 --p1 hard --p2 hard
/home/box/.dotnet/dotnet run --project Simulation~/Harness/Harness.csproj --configuration Release --no-build --no-restore -- --games 1000 --seed 12345 --cap 400 --p1 strong --p2 hard
```

The VSTest command could not bind its local MSBuild socket in the sandbox (`SocketException: Permission denied`). The already-built Release test assembly was therefore executed with the existing in-process reflection runner: 51 tests passed, 0 failed.
