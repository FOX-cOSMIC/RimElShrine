# Runtime validation

Runs the Shield Expanded 1.7 port inside RimWorld and records pass or fail results. Decision: `docs/adr/0009-validation-runner-in-this-repo.md`.

## Game

- Install: `C:\SteamLibrary\steamapps\common\RimWorld`.
- Build: the installed build is 1.7.5000 rev1275 (Steam unstable). Every run before `20261105-233814` used 1.7.4998 rev1299, which ADR 0002 targets. The later runs use the installed build until the target is decided.

## Rules

- Each gameplay change runs red before its fix and green after it. The run fails on the bug and passes on the fix.
- Every run uses its own save folder under `.artifacts/runs/<timestamp>/`, passed with `-savedatafolder`. Never write to the live save folder or the live `ModsConfig.xml`.
- Refuse to start while `RimWorld.exe` is running. The runner also stops a run when another RimWorld process starts during it.
- One RimWorld session at a time. Every session writes its log to the same `AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`, and `-logFile` does not move that log in this build. Two sessions mix their lines, so the log copy proves nothing about either run. The ZLevels runner in `rimworld-modding` writes to the same log.
- The port's mod folder reaches the game through a junction under `<RimWorld>\Mods\`. Create the junction for the run, and remove it after the run.
- A run passes only when the results file and the game log agree, every scenario row reads PASS, and the log has no errors from the port.
- Never commit `.artifacts/`.
- Each scenario group writes one `<group> group complete` row when it ends. To add a group, list its completion row in `$groups` and its scenario rows in `$required`, both in `Invoke-Scenarios.ps1`.
- Game time: the game ignores a speed change made while the player has no control. A new colony's first tick is still fading, so the groups' speed setter does nothing, and the speed stays Normal. The mech serum group advances the game with the game's own per-tick function, because a finger takes 28,833 ticks to regrow. The other groups run in real time. See `docs/game-api-findings.md`.

## Status

- Smoke run (`Invoke-Smoke.ps1`): passed on 2026-11-05 (run `20261105-145122`, 1.7.4998 rev1299). The 1.7 build loads in a new colony, the mod is listed and loaded, and the port's Harmony patches apply. The one error line in that log is from another mod (duplicate packageId `Aoba.DeadManSwitch.Core`).
- Scenario run (`Invoke-Scenarios.ps1`): all three groups passed on 2026-11-07 on 1.7.5000 rev1275 (run `20261107-135819`, all 15 rows). The control run (`20261107-140232`) fails exactly the four mech rows that need the serum, as designed. The first two groups also passed on 2026-11-05 on 1.7.4998 rev1299 (run `20261105-152607`, all seven rows).
  - Shield Buildings & Shield Network: passed in run `20261105-152607`. The control with an empty battery (`-ExtraGameArgs '-shieldvalidation-nopower'`, run `20261105-152510`) fails only the allocation check. That failure is the expected result.
  - Shield Equipment: passed in run `20261105-152607`. The checks confirm that the colonist wears the ES0 belt, that a ranged hit is absorbed with the expected energy loss, that a hit gets through an empty belt, and that the belt recharges at its rate while worn. The control with the belt not worn (`-ExtraGameArgs '-shieldvalidation-nowear'`, run `20261105-152302`) fails the worn, absorb, and recharge checks. The no-energy check still passes in that control, because it needs no worn belt.
  - Mech Serums: passed in run `20261107-135819` (1.7.5000 rev1275). All five required rows read PASS. They cover the recipes, injectors and research; a regenerator injector adding the regen effect; a missing finger regrowing at the expected time, at tick 28,833 inside the window 23,040 to 31,680; the effect ending when its count runs out; and a cleaner stopping a running effect. The control run `20261107-140232` (`-ExtraGameArgs '-shieldvalidation-noserum'`) fails the four rows that need the serum and passes the recipe row, as designed. Run `20261105-233814` also reads PASS on all five, but it does not count, because its log was shared with another RimWorld session.
  - Mech Serums, Fast variants: not covered. The Fast flag is never applied, and a healthy pawn cannot use fast regen. In the clean run, the diagnostic line for a Fast injector reads severity 0.1, quick False, no blood loss, and the finger back at tick 28,833, the same as a normal injector. The current behavior is recorded in `docs/game-api-findings.md`, and the fix needs a design decision first.
  - Mech Serum Healer and Resurrector: only the recipes are checked. Their effects come from vanilla Biotech.
  - Combat interception and the Combat Extended path: not covered yet.
