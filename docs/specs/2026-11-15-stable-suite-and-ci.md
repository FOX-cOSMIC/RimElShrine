# Stable suite, use-job row, and reproducible build

## Goal

The validation suite passes every run, it covers the path a player takes to use a regen injector, and a rebuild of the 1.7 DLLs gives the same bytes. Decisions: ADR 0017.

## Acceptance checks

1. The 1.7 build passes: `dotnet build Source/RimElShrine.Shields/RimElShrine.Shields.csproj -c Release -p:RimWorldVersion=1.7` ends with 0 errors.
2. Red for D12: with the port changed so fast regen adds no blood loss (a throwaway build), `Invoke-Scenarios.ps1` fails the row `mech serum: fast regen speeds up regrowth on a pawn with no blood loss`.
3. Red for D13: the control `-shieldvalidation-noserum` fails the new row `mech serum: a colonist uses a Fast injector through the use job`.
4. Green: `Invoke-Scenarios.ps1` on the final build passes, with the new row present.
5. Green with Combat Extended: `Invoke-Scenarios.ps1 -CombatExtended` on the final build passes.
6. CI: dropped. Lucas chose on 2026-11-15 to skip CI (ADR 0017, D15).
7. The committed 1.7 DLLs equal the build output of check 1 (no source change to the port, so `git status` shows no DLL change after the build).

## Out of scope

1.6 runtime tests, Combat Extended weapons, armour and health, the Workshop upload.
