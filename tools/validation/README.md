# Runtime validation

Runs the Shield Expanded 1.7 port inside RimWorld and records pass or fail results. Decision: `docs/adr/0009-validation-runner-in-this-repo.md`.

## Game

- Install: `C:\SteamLibrary\steamapps\common\RimWorld`.
- Build: the target is the installed build, 1.7.5000 rev1275 (Steam unstable). ADR 0002 records this choice, updated on 2026-11-07. Every run before `20261105-233814` used 1.7.4998 rev1299, the build installed then.

## Rules

- Each gameplay change runs red before its fix and green after it. The run fails on the bug and passes on the fix.
- Every run uses its own save folder under `.artifacts/runs/<timestamp>/`, passed with `-savedatafolder`. Never write to the live save folder or the live `ModsConfig.xml`.
- Refuse to start while `RimWorld.exe` is running. The scenario runner also stops a run when another RimWorld process starts during it.
- One RimWorld session at a time. Every session writes its log to the same `AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`, and `-logFile` does not move that log in this build. Two sessions mix their lines, so the log copy proves nothing about either run. The ZLevels runner in `rimworld-modding` writes to the same log.
- The port's mod folder reaches the game through a junction under `<RimWorld>\Mods\`. Create the junction for the run, and remove it after the run.
- A run passes only when the results file and the game log agree, every scenario row reads PASS, and the log has no errors from the port.
- Never commit `.artifacts/`.
- Each scenario group writes one `<group> group complete` row when it ends. To add a group, list its completion row in `$groups` and its scenario rows in `$required`, both in `Invoke-Scenarios.ps1`.
- Game time: the game ignores a speed change made while the player has no control. A new colony's first tick is still fading, so the groups' speed setter does nothing, and the speed stays Normal. The mech serum group advances the game with the game's own per-tick function, because a finger takes 28,833 ticks to regrow. The other groups run in real time. See `docs/game-api-findings.md`.

## Status

- Smoke run (`Invoke-Smoke.ps1`): passed on 2026-11-05 (run `20261105-145122`, 1.7.4998 rev1299). The 1.7 build loads in a new colony, the mod is listed and loaded, and the port's Harmony patches apply. The one error line in that log is from another mod (duplicate packageId `Aoba.DeadManSwitch.Core`).
- Scenario run (`Invoke-Scenarios.ps1`): all three groups passed on 2026-11-07 on 1.7.5000 rev1275 in run `20261107-175804`, all 17 rows. The no-serum control `20261107-180127` (`-ExtraGameArgs '-shieldvalidation-noserum'`) fails the six mech rows that need the serum, as designed, and passes the recipe row and the network and equipment rows. The earlier clean run `20261107-135819` passed its 15 rows before the two Fast rows existed. The first two groups also passed on 2026-11-05 on 1.7.4998 rev1299 (run `20261105-152607`, all seven rows).
  - Shield Buildings & Shield Network: passed in run `20261105-152607`, and again in run `20261107-175804`. The control with an empty battery (`-ExtraGameArgs '-shieldvalidation-nopower'`, run `20261105-152510`) fails only the allocation check. That failure is the expected result.
  - Shield Equipment: passed in run `20261105-152607`, and again in run `20261107-175804`. The checks confirm that the colonist wears the ES0 belt, that a ranged hit is absorbed with the expected energy loss, that a hit gets through an empty belt, and that the belt recharges at its rate while worn. The control with the belt not worn (`-ExtraGameArgs '-shieldvalidation-nowear'`, run `20261105-152302`) fails the worn, absorb, and recharge checks. The no-energy check still passes in that control, because it needs no worn belt.
  - Mech Serums: passed in run `20261107-175804` (1.7.5000 rev1275). All seven mech rows read PASS. They cover the recipes, injectors and research; a regenerator injector adding the regen effect; a missing finger regrowing at the expected time, at tick 28,833 inside the window 23,040 to 31,680; the effect ending when its count runs out; a cleaner stopping a running effect; a Fast injector speeding up a pawn with blood loss; and a Fast injector leaving a pawn with no blood loss at the normal time. Run `20261105-233814` also reads PASS on the first five, but it does not count, because its log was shared with another RimWorld session.
  - Mech Serums, Fast variants: covered since 2026-11-07. Decision: `docs/adr/0011-fast-regen-only-while-bleeding.md`. Two red runs came before the fix. Run `20261107-174938` fails the bleeding row, with quick regen off and the finger back at 28,833 ticks. Run `20261107-175322` fails the healthy row, with the finger back at 9,633 ticks, because the port's helper gave the healthy pawn a zero-severity bleed. The green run `20261107-175804` shows the pawn with blood loss back at 9,633 ticks and the pawn with none back at 28,833 ticks. The rows were renamed after that run, to match what they set and check. Run `20261107-181001` passes all 17 rows under the new names. Run `20261107-174532` was the first red run, before the check moved to the moment the finger comes back.
  - Mech Serum Healer and Resurrector: only the recipes are checked. Their effects come from vanilla Biotech.
  - Combat interception and the Combat Extended path: not covered yet.

## Known issues

- The injector's usability check cures a bleeding pawn. `Comp_LimbsRegenInjector.CanBeUsedBy` calls the port's `TryGetHediff` helper for the regen effect. When the pawn has no regen effect, the helper sets the pawn's existing blood loss to 0. The 1.6 build has the same call. Decision D4 asks Lucas how to handle it, and Lucas has not answered.
