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
- Each scenario group writes one `<group> group complete` row when it ends. To add a group, list its completion row in `$groups` and its scenario rows in `$required`, both in `Invoke-Scenarios.ps1`.

## Status

- Smoke run (`Invoke-Smoke.ps1`): passed on 2026-11-05 (run `20261105-145122`). The 1.7 build loads in a new colony in RimWorld 1.7.4998 rev1299, the mod is listed and loaded, and the port's Harmony patches apply. The one error line in that log is from another mod (duplicate packageId `Aoba.DeadManSwitch.Core`).
- Scenario run (`Invoke-Scenarios.ps1`): passed on 2026-11-05 (run `20261105-152607`, both groups below). The run also logs one error line from another mod, the duplicate packageId `Aoba.DeadManSwitch.Core`. The verdict ignores it, because it checks only error lines that mention the port or the validation mod.
  - Shield Buildings & Shield Network: passed in run `20261105-152607`. The control with an empty battery (`-ExtraGameArgs '-shieldvalidation-nopower'`, run `20261105-152510`) fails only the allocation check. That failure is the expected result.
  - Shield Equipment: passed in run `20261105-152607`. The checks confirm that the colonist wears the ES0 belt, that a ranged hit is absorbed at the expected energy loss, that a hit gets through an empty belt, and that the belt recharges at its rate while worn. The control with the belt not worn (`-ExtraGameArgs '-shieldvalidation-nowear'`, run `20261105-152302`) fails the worn, absorb, and recharge checks. The no-energy check still passes in that control, because it needs no worn belt.
  - Mech Serums: not written yet.
  - Combat interception and the Combat Extended path: not covered yet.
