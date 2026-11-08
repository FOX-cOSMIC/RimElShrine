# Test the Combat Extended part in RimWorld 1.7 with a Combat Extended 1.7 test build

Lucas decided this on 2026-11-08, answering decision D8. The choice was between keeping the listing's line "not yet tested in game" and testing the Combat Extended part in the game first. Lucas chose to test it first. This record covers that test. It supersedes the reference DLL in ADR 0008 in part.

## Decision

- The Combat Extended test build is the `1.7-Test` branch of CombatExtended-Continued at commit `bcdd75dc`. It is cloned to `%USERPROFILE%\.rimworld-refs\CombatExtended-1.7-Test-src`, and `dotnet build Source/CombatExtended/CombatExtended.csproj -c Release` writes `Assemblies/CombatExtended.dll` there. The port's Combat Extended part and the validation's Combat Extended part compile against that DLL.
- The validation runner takes a `-CombatExtended` switch. With it, `ceteam.combatextended` joins the new-game mod list, the four Combat Extended rows and their group become required, and the runner uses the clone's folder as the mod. Without it, the runner runs the same 18 rows and 3 groups as before.
- The Combat Extended rows live in their own assembly, `ShieldValidation.CombatExtended`. RimWorld loads it only while `CETeam.CombatExtended` is active (`tools/validation/ShieldValidation/LoadFolders.xml`).
- The Combat Extended group starts after the network, equipment, and mech serum groups finish. It advances the game one tick at a time, as the mech serum group does.
- The test shot is `Bullet_22LR_FMJ`, a small Combat Extended bullet with no explosion.

## Choices I made

- The old test DLL in `%USERPROFILE%\.rimworld-refs\CombatExtended-1.7-Test\` is no longer the reference. The clone's build comes from the same commit, with Krafs 1.7.4977-alpha.1, which the source pins. Its SHA-256 is `b3d92ecb...`, and the old file's is `ccf2626c...`. The port's Combat Extended part built against the clone's DLL is `d254e389...`, and the default build reproduces that hash. The clone is outside the repo, as ADR 0008 required.
- The Combat Extended group waits for the other groups. In run `20261108-233855` a friendly Combat Extended shot missed the dome and landed at (22.5, 6.5), where the mech serum group's colonists stood. The mech serum group's cleaner row failed in that run, and the group order removes that path.
- The test shot is not an explosive shell. A HEAT cannon shell broke up into fragments, which the Combat Extended build then failed to draw, and its cost of 341 was above the dome's energy of 107.6 at the start. The dome emptied, and the energy row could not hold. The energy row now refuses to judge a shot whose cost is above the dome's energy before it.
- The energy row measures what the dome took. The charge per tick is measured on a tick with no shot in flight. The expected cost is a bracket from the last two ticks in flight, because a ballistic shot loses damage as it flies (`RemainingKineticEnergyPct`).
- I did not change the mech serum group's rows in this record. One row is timing-sensitive, and it is listed under Known issues.

## Consequences

- Four rows run in a Combat Extended run:
  - `combat extended: CE shots use the CE counterpart`: the dome's rules for a `ProjectileCE` are `Comp_ShieldNet_ProjInterceptorELS_CompatibleCE`.
  - `combat extended: a hostile CE shot is intercepted by the dome`: the shot is removed inside the radius.
  - `combat extended: the dome loses the CE energy cost of a shot`: the energy taken equals the bullet's damage times the building factor.
  - `combat extended: a friendly CE shot is not intercepted`: a friendly launcher takes no energy.
- Controls show that the rows can fail:
  - `-shieldvalidation-cenopower` leaves the dome unpowered. The three dome rows fail with "the dome did not come up".
  - `-shieldvalidation-ceniff` turns the dome's friend-or-foe check off. Only the friendly row fails.
- The Combat Extended rows cover one small bullet against one dome. They do not cover Combat Extended's own weapons, its armour, or its health system.
- To reverse this record, remove `-CombatExtended` from the runner's use, or delete the Combat Extended rows and their assembly.

## Proof

Runs on the working tree that this record's commit contains:

| Run | Setting | Verdict | What it shows |
|---|---|---|---|
| `20261109-000000` | `-CombatExtended` | PASS | All 22 scenario rows and 4 completion rows pass, with 26 markers in the log. 870 ERROR lines, none naming the port or the validation mod. |
| `20261108-234057` | `-CombatExtended`, before the group drove its own ticks | PASS | All 22 scenario rows and 4 completion rows pass. |
| `20261108-234456` | no `-CombatExtended` | PASS | The 18 scenario rows and 3 completion rows pass, as before. One ERROR line, the duplicate package id from another mod. |
| `20261108-235719` | `-CombatExtended -ExtraGameArgs '-shieldvalidation-cenopower'` | FAIL, as designed | The three dome rows fail. The counterpart row passes. |
| `20261108-235830` | `-CombatExtended -ExtraGameArgs '-shieldvalidation-ceniff'` | FAIL, as designed | The friendly row fails with energy taken 7. A mech row also fails (see Known issues). |
| `20261108-235510` | `-CombatExtended` | FAIL on one mech row | All four Combat Extended rows pass. The healthy Fast pawn's blood-loss row fails (see Known issues). |
| `20261108-233855` | `-CombatExtended`, HEAT shell (experiment, reverted) | FAIL | The energy row fails, as the guard predicts. The mech cleaner row fails: the friendly shell landed among the mech colonists. |
| `20261108-232846` | `-CombatExtended`, first run | FAIL | The energy row fails because the HEAT shell's cost was above the dome's energy. A mech regrowth row also fails. Its cause was not confirmed, since the colonists' positions were not logged in that run. |

## Known issues

- The healthy Fast pawn's blood-loss row checks blood loss at the instant the finger returns. The base game says blood loss "naturally recovers over time", so the check can miss a bleed that has cleared. Runs `20261108-235510` and `20261108-235830` failed it, with the finger back at 9,678 and 9,677 ticks and no blood loss at return. Runs `20261109-000000` and `20261108-234057` passed it, with the finger back at 9,633 and 9,639 ticks and blood loss at return. The non-Combat Extended run `20261108-234456` passed at 9,633 ticks. The fast window held in every run. The row should check for blood loss at some tick while the finger regrows. That change is not made here.
- The CE 1.7-Test build logs errors on every run: `AsyncGPUReadback failed`, `Texture ... was not cached`, and `bodyGraphicData` failures. They are not the port's errors, and the runner does not count them.
- In run `20261108-232846` the first colonist's finger was already missing when the group added its own missing-part hediff. A test that adds the same state first passed the regrowth row in three runs (`20261108-233252`, `20261108-233432`, `20261108-233701`), so that state is not the cause. That experiment was reverted, and the group's fixture is unchanged.
