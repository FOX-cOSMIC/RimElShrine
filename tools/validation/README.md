# Runtime validation

Runs the Shield Expanded 1.7 port inside RimWorld and records pass or fail results. Decision: `docs/adr/0009-validation-runner-in-this-repo.md`.

## Game

- Install: `C:\SteamLibrary\steamapps\common\RimWorld`.
- Build: RimWorld 1.7.4998 rev1299, Steam unstable branch.

## Rules

- Each gameplay change runs red before its fix and green after it. The run fails on the bug and passes on the fix.
- Every run uses its own save folder under `.artifacts/runs/<timestamp>/`, passed with `-savedatafolder`. Never write to the live save folder or the live `ModsConfig.xml`.
- Refuse to start while `RimWorld.exe` is running.
- The port's mod folder reaches the game through a junction under `<RimWorld>\Mods\`. Create the junction for the run, and remove it after the run.
- A run passes only when the results file and the game log agree, every scenario row reads PASS, and the log has no errors from the port.
- Never commit `.artifacts/`.

## Status

- Smoke run (`Invoke-Smoke.ps1`): passed on 2026-11-05 (run `20261105-145122`). The 1.7 build loads in a new colony in RimWorld 1.7.4998 rev1299, the mod is listed and loaded, and the port's Harmony patches apply. The one error line in that log is from another mod (duplicate packageId `Aoba.DeadManSwitch.Core`).
- Scenarios: none yet. One group per content group: Shield Buildings & Shield Network, Shield Equipment, Mech Serums.
