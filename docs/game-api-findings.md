# RimWorld 1.7 API findings

Checked on 2026-11-04 against the installed `Assembly-CSharp.dll` for RimWorld 1.7.4998 rev1299 (unstable). The check loaded the assembly with `System.Reflection.MetadataLoadContext` and read each member by name. It did not run the game, so none of these findings is a runtime proof.

## Harmony patch targets in the core library

| Patch class | Target in 1.7 | Result | Prefix signature |
|---|---|---|---|
| `Instance_ModSettings_Write` | `Verse.ModSettings.Write()` | One overload. Instance. Returns void. | `Prefix(ModSettings __instance)` |
| `Static_LoadedModManager_ParseAndProcessXML` | `Verse.LoadedModManager.ParseAndProcessXML(XmlDocument xmlDoc, Dictionary<XmlNode, LoadableXmlAsset> assetlookup, bool hotReload)` | One overload. Static. | `Prefix(xmlDoc, assetlookup, hotReload)` |

Both patch classes live in `Source/RimElShrine/Patches/`.

## Reflected members in the core library

| Lookup | Result | Caller |
|---|---|---|
| `Verse.Hediff.part` | Non-public field of type `BodyPartRecord` | `Source/RimElShrine/CommonUtility.cs` |
| `Verse.Mod.modSettings` | Non-public field of type `ModSettings` | `Source/RimElShrine/RimElShrineMod.cs` |
| `Verse.ModSettings.Mod` | Public instance property of type `Mod` | `Source/RimElShrine/RimElShrineMod.cs` |

## Scenario calls for shield equipment (2026-11-05)

Checked with `ilspycmd` against the installed `Assembly-CSharp.dll`, the game's Core defs, and the port's defs. The runs listed in `tools/validation/README.md` show the behavior at runtime.

| Name | Finding |
|---|---|
| `Verse.PawnGenerator.GeneratePawn` | Static. Takes `PawnKindDef kindDef`, `Faction faction = null`, and `PlanetTile? tile = null`. Lives in the `Verse` namespace. |
| `RimWorld.Faction.OfPlayer` | Static property. |
| `Pawn_ApparelTracker.Wear` | Takes `Apparel newApparel`, `bool dropReplacedApparel = true`, and `bool locked = false`. |
| `Pawn_ApparelTracker.DestroyAll` | Takes `DestroyMode mode = DestroyMode.Vanish`. |
| `Pawn_ApparelTracker.WornApparel` | Property of type `List<Apparel>`. |
| `Apparel.Wearer` | Property of type `Pawn`. |
| `CompShield.ShieldState` | Computed, not stored. `Disabled` for a pawn that is charging or shut down, and for a parent with a `CompCanBeDormant` that is not awake. `Resetting` while `ticksToReset` is above 0. Otherwise `Active`. A new belt starts with `ticksToReset` at -1, so it starts `Active`. |
| `CompShield.Energy` | Read-only in the base class. `Comp_ApparelShield` hides it with a `new` property that has a setter. |
| `Comp_ApparelShield.PostPreApplyDamage` | Absorbs only when `ShieldState` is `Active` and the belt has a wearer. For ranged and explosive damage, the energy loss is `amount × energyLossPerDamage × DmgGlobalFactor`. |
| `DamageDef` `Bullet` | In the Core file `Data\Core\Defs\DamageDefs\Damages_RangedWeapon.xml`, `isRanged` is true and `externalViolence` is true. |
| `ELS_Thing_ES0Shield` | In `Shields.xml`, `EnergyShieldRechargeRate` is 0.17 and `EnergyShieldEnergyMax` is 1. `CompTick` adds the rate divided by 60 on each tick. Run `20261105-152607` measured 0.34 after 120 ticks, which matches `120 × 0.17 / 60`. |

## Scenario calls for mech serums (2026-11-05)

Checked with `ilspycmd` against the installed `Assembly-CSharp.dll` at `C:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll`. That DLL is RimWorld 1.7.5000 rev1275, the build installed on 2026-11-05 (see `tools/validation/README.md`).

| Name | Finding |
|---|---|
| `Verse.Hediff.ShouldRemove` | `Severity <= 0f`. `HediffWithComps` overrides it: true when a comp's `CompShouldRemove` is true, otherwise the base result. |
| `Verse.Hediff.Severity` (setter) | Clamps to `[def.minSeverity, def.maxSeverity]`. On a stage change it calls `OnStageIndexChanged` and `Notify_HediffChanged`. |
| `Verse.Pawn_HealthTracker.HealthTick` | Called from `Pawn.Tick` every game tick. It calls `Hediff.Tick()` on each hediff, then removes each hediff whose `ShouldRemove` is true. A regen hediff at severity 0 is removed on the next tick. |
| `Verse.Pawn_HealthTracker.AddHediff(HediffDef, BodyPartRecord part = null, ...)` | Returns the new `Hediff`. |
| `Verse.HediffSet.TryGetHediff(HediffDef, out Hediff)` | Public instance method. The checks use it, not the port's `CommonUtility.TryGetHediff` extension, so a check does not depend on the code it tests. |
| `Verse.HediffSet.PartIsMissing(BodyPartRecord)` | Public instance method, returns bool. |
| `Verse.HediffSet.GetMissingPartsCommonAncestors()` | Returns `List<Hediff_MissingPart>`. |
| `RimWorld.HediffDefOf.MissingBodyPart` | Static field. Its def in `Data/Core/Defs/HediffDefs/Hediffs_Local_Injuries.xml` names `Hediff_MissingPart` as the class. |
| `Verse.BodyPartDef.GetMaxHealth(Pawn)` | `Mathf.CeilToInt(hitPoints * pawn.HealthScale)`. Finger has `hitPoints` 8 in `Data/Core/Defs/Bodies/BodyParts_Humanoid.xml`. |
| `Verse.TicksToSeconds(this int)` | `ticks / 60f`. |
| `Verse.CompUseEffect.DoEffect(Pawn)` and `CanBeUsedBy(Pawn)` | Both public virtual. `CanBeUsedBy` returns `AcceptanceReport`, and its `Accepted` property is public. |
| `Verse.TimeSpeed` | Enum: `Paused`, `Normal`, `Fast`, `Superfast`, `Ultrafast`. |
| `Verse.TickManager.TickRateMultiplier` | Fast 3. Superfast 18 with no map, 12 when nothing is happening, otherwise 6. Ultrafast 15 with a map, 150 with no map. |
| `Verse.TickManager.TickManagerUpdate` | Runs ticks while real time allows, up to `2 * TickRateMultiplier` per frame and at most 45 ms per frame. |
| `Verse.TickManager.CurTimeSpeed` (setter) | Ignored unless `PlayerCanControl`, which is false while `Game.PlayerHasControl` is false. That is the case at the first tick of a new colony, while the screen fades. In the clean run `20261107-135819` the first clock line reads `player has control False`, and every later line reads `True`. The speed reads `Normal` for the whole run, because the network group sets it once at its setup, before control returns, and nothing sets it again. `Find.GravshipController.LandingAreaConfirmationInProgress` was `False` on every clock line of that run, so the gravship landing is not the cause. Run `20261105-233814` read `Paused` from about tick 20,000. The clean run did not pause, and that cause is not confirmed. |
| `Verse.TickManager.DoSingleTick()` | The game's own per-tick function. The frame loop calls it once per tick. The mech group calls it directly while it waits, because the speed setter is ignored. |

Regen behavior, from `HediffWithComps_LimbsRegen.Tick` and confirmed by run `20261107-135819`:

- The regen clock starts on the first tick and gains 0.5 s every 30 ticks. A part heals when the clock is above `maxHealth * LimbsRegenSecondsPerHealth`.
- Finger at the default 60 s per health: 8 * 60 = 480 s, which is 28,833 ticks. The clean run measured the finger back at tick 28,833, inside the window 23,040 to 31,680.
- Each heal lowers `lastRegenCount` by one. At 0 the code sets `Severity` to 0, and the next tick removes the hediff. The clean run showed the effect removed with the count at 0.
- The cleaner sets `Severity` to 0. The clean run showed the effect removed and the finger still missing.

Fast regeneration (ADR 0011, revised by ADR 0014 on 2026-11-08):

- The Fast injectors set `isFastRegen` in the XML. Before 2026-11-07 no code read the flag, and the 1.6 build never read it either.
- Since 2026-11-07, the 1.7 build sets the regen hediff's severity to 1 for a Fast injector, inside an `#if RIMWORLD_17` block. `HediffWithComps_LimbsRegen.IsQuickRegen` is `Severity > 0.5f`, so the effect runs in quick mode.
- Until 2026-11-08, the quick branch took blood loss only from an existing `BloodLoss` hediff, so a healthy pawn regrew at the normal speed. Since 2026-11-08 (ADR 0014), the branch works on a pawn with no blood loss. It reads the bleed with `Verse.HediffSet.TryGetHediff`. When none exists, `QuickRegenBloodLoss` adds a `BloodLoss` hediff at severity 0, and the step raises it by the step's loss.
- Measured in run `20261108-202332` on commit `140d470`: a healthy Fast pawn's finger came back at 9,633 ticks, with blood loss when it came back. The red run `20261108-200958` on commit `614106d` showed that pawn at 28,833 ticks, with no blood loss.
- Measured in run `20261107-175804`: the finger of a pawn with blood loss came back at 9,633 ticks, and the finger of a pawn with none came back at 28,833 ticks. Run `20261107-175322` showed the healthy pawn at 9,633 ticks before the lookup change, because the port's helper gave it a zero-severity `BloodLoss` hediff.
- The keyed text in `LimbsRegenKeys.xml` describes quick regen that costs blood loss: "Fast regeneration may cause additional blood loss symptoms."
- The port's `CommonUtility.TryGetHediff` extension, in `Source/RimElShrine/CommonUtility.cs` at line 90, adds a `BloodLoss` hediff at severity 0 when the pawn has none. When the requested definition is absent, it also sets the pawn's blood loss to 0, even when the pawn is bleeding. The quick branch called it until 2026-11-07. The regen tick no longer calls it.
- Since commit 99dbf40 (2026-11-08), `Comp_LimbsRegenInjector.CanBeUsedBy` reads the regen effect with `Verse.HediffSet.TryGetHediff`, so the check no longer clears blood loss. Lucas first chose to leave the check unchanged on 2026-11-07. Lucas reopened it on 2026-11-08 and chose to fix it in both builds, as decision D4 and ADR 0011 record.

## Raid and turret recordings (2026-11-08)

Checked with `ilspycmd` against the installed `Assembly-CSharp.dll` (1.7.5000 rev1275). The run IDs are in `tools/validation/README.md`.

- `RimWorld.JobGiver_AIFightEnemy.TryGiveJob` returns no job when the pawn has no `mindState.enemyTarget`. `FindAttackTarget` accepts only targets the pawn can see. A raider that loses sight of every colonist does nothing. Run `20261108-110251` showed three idle raiders at the south edge of the map, with the colonists in revolver range.
- The `AssaultColony` duty runs `JobGiver_AIFightEnemies`, then `JobGiver_AIGotoNearestHostile` (`Duties_Misc.xml`). The `Defend` duty runs `JobGiver_AIDefendPoint`, then `ThinkNode_ForbidOutsideFlagRadius` with a 16-cell limit, then `JobGiver_WanderNearDutyLocation` with an 8-cell radius. In run `20261108-110839`, defend-duty raiders still walked about 11 cells from their spot and had no target.
- `Verse.AI.JobDriver_AttackStatic` calls `TryStartAttack` on `TargetA` every tick. It ends when the target is gone, destroyed, or downed after starting healthy, or when `numAttacksMade` reaches `job.maxNumStaticAttacks`. That field defaults to `int.MaxValue` in `Verse.AI.Job`. The job is not casual-interruptible. The scripted raid scene uses it. From run `20261108-111238` on, each raider fired about every two seconds from its spawn spot.
- `new Job(JobDef, LocalTargetInfo)` and `Pawn_JobTracker.StartJob(Job, JobCondition)` start a job. `Pawn_JobTracker.curJob` is public.
- `Pawn_EquipmentTracker.PrimaryEq` returns a `CompEquippable`, not the weapon. The weapon is `PrimaryEq.parent`. Reading `PrimaryEq.def` does not compile.
- `PawnDuty(DutyDef def, LocalTargetInfo focus, float radius = -1f)` takes a focus cell and a radius.
- `LordMaker.MakeNewLord(Faction, LordJob, Map, IEnumerable<Pawn>)` is in `Verse.AI.Group`, not `RimWorld`. `LordJob_AssaultColony` takes `canTimeoutOrFlee`, which defaults to true and gates its exit-the-map toil. The scripted raid scene (`-RaidAI scripted`) does not use a lord. The default raid scene (`-RaidAI game`) starts one with `LordJob_AssaultColony(canKidnap: false, canTimeoutOrFlee: false, canSteal: false)`, so the raiders stay on the map.
- `Comp_ShieldNet_ProjInterceptorELS.InterceptProjectile` stops a projectile only when its launcher is hostile to the shield's faction, and only while `IFFOn` is set. IFF is on by default. The check sits at lines 311 to 317. In run `20261108-111632`, colonists fired 47 shots, and none ended at the dome edge.
- `Comp_ApparelShield.PostPreApplyDamage` absorbs damage that reaches the wearer, ranged or melee. It does not stop projectiles in flight. The ES0 belt's energy max is 1, the value the 1.6 original sets. A revolver hit costs 12 × 0.033 = 0.396 energy, so a full belt takes three revolver hits before it breaks. The third hit drops it below zero, and that hit is still absorbed.
- ES0 capacity. `Shields.xml` sets max energy 1 and recharge 0.17 per second for ES0, the same as the 1.6 original. ES1 to ES3 set 1.2, 1.7, and 3. The ES0 def does not set `energyLossPerDamage`, so it takes the game's default of 0.033 (`RimWorld.CompProperties_Shield`). Vanilla `Apparel_ShieldBelt` sets max 1.1 and recharge 0.13 (`Apparel_Belts.xml`), with the same default cost.
- The port has no Harmony patch on `Verb`, `CanHitTarget`, `ShootLine`, or the AI target finder. The raider AI is the game's own.
- The dome's energy readout is `x/1000`. A dome comes up at energy 100 in these scenes. Each intercepted revolver shot removed about 11 to 12 energy in the logs. The dome went down at energy 0 and came back at 100 about 20 seconds later.

- `LordJob_AssaultColony` starts in `LordToil_AssaultColony` unless sappers or breachers are on. Exit to the map edge depends on `canTimeoutOrFlee`, or on the faction turning non-hostile. `canKidnap` and `canSteal` start other toils. With `canTimeoutOrFlee`, `canKidnap`, and `canSteal` all off, the raiders stay on the map.
- In run `20261108-114853`, raiders under the lord held about 24 cells east in the `Wait_Combat` job and fired 140 shots. The bare assault duty and the defend duty gave them no target in runs `20261108-110251` and `20261108-110839`. I did not isolate why the lord finds targets and the duties do not.
- The diagnostic line's target column reads `none` under `Wait_Combat`, because that job keeps no `TargetA`. The raiders still fired.

## Blood loss and the usability check (2026-11-08)

Checked with `ilspycmd` against the installed `Assembly-CSharp.dll`, RimWorld 1.7.5000 rev1275. The decompile is in `.artifacts/decompile/game/`. The reset and the label ran in game: red run `20261108-142312` and green run `20261108-142616`. The other facts below come from the decompile only.

- `HediffGiver_Bleeding.OnIntervalPassed` adds `BleedRateTotal * 0.001` to the blood loss hediff while `BleedRateTotal` is at least 0.1. Below 0.1 it subtracts 0.000333. `Pawn_HealthTracker` calls it every 60 ticks. Over 60,000 ticks, an open wound adds its bleed rate in total.
- `Hediff.ShouldRemove` is true at severity 0 or below. `Pawn_HealthTracker` removes a hediff that reports it, so a blood loss hediff at zero is removed.
- Before commit 99dbf40, `Comp_LimbsRegenInjector.CanBeUsedBy` called the port's `TryGetHediff` helper, `Source/RimElShrine/CommonUtility.cs` line 90. When the regen hediff is absent, the helper gets or adds the blood loss hediff and sets its severity to 0. That applies to any pawn, including one that is bleeding.
- The game calls `CompUsable.CanBeUsedBy`, which calls the injector's check, from four places:
  - `CompFloatMenuOptions` at `CompUsable.cs` line 78, once for each selected pawn when the item's menu is built.
  - `TryStartUseJob` at `CompUsable.cs` line 105.
  - `UsedBy` at `CompUsable.cs` line 163, before the effect applies.
  - `JobDriver_UseItem.MakeNewToils` at line 42, as a `FailOn` condition. `JobDriver.DriverTick` checks it on every tick while the job runs.
- Before the fix, and from the decompile only, not run in game: while a use job runs, its pawn's blood loss stays at 0. After the injector applies, an open wound refills blood loss at its bleed rate.
- On the unfixed build, `HediffWithComps_LimbsRegen.Tick` found no blood loss hediff on a pawn with none, so `reachedMaxBloodLoss` became true. The label then read "Disable Fast", and the tip read "disabled due to current severe blood loss symptoms", while a body part regrew on a pawn that was not bleeding. The timing was correct. Red run `20261108-142312` showed the label on a healthy Fast pawn reading `Disable Fast`. On the fixed build, green run `20261108-142616` shows `Fast Regen`, because only blood loss at the cap sets the flag now.
- `Comp_ApparelShield.CompTick` calls the same helper with `BloodLoss` as the definition. That finds the existing hediff and does not set it to 0.
- Each fast step adds the extra seconds divided by `LimbsRegenSecondsPerHealth`, times `QuickRegenBloodLossPercentPerHealth`. By that formula, one finger adds about 0.027 blood loss, under the `QuickRegenBloodLossMax` cap of 0.35.
- The checks on patient A in `MechSerumValidation.cs` use a pawn with no blood loss, so they never see the reset. The row `mech serum: injector usability check leaves blood loss alone` checks a bleeding pawn. Red run `20261108-142312` fails it, and green run `20261108-142616` passes it.
- The cap flag, `reachedMaxBloodLoss`, was set only inside the regrowth block. Once the last finger came back, nothing cleared it, so it could outlive the blood loss. Red run `20261108-143801` shows the label stuck on `Disable Fast` after the blood loss ended. Green run `20261108-144040` shows it cleared, because the flag now follows blood loss on every tick.

## Combat Extended counterpart (1.7)

Verified in game on the Combat Extended 1.7-Test build (ADR 0016), in run `20261109-000000` unless a run is named.

- RimWorld 1.7 puts a thing in `ThingRequestGroup.Projectile` when its def has a `projectile` block. The check is in `Verse.ThingListGroupHelper`, decompiled from `Assembly-CSharp.dll` of 1.7.5000 rev1275. A Combat Extended bullet has that block, so the dome's `ThingsInGroup(Projectile)` sees it.
- The Combat Extended 1.7-Test `ProjectileCE` derives from `ThingWithComps`, not from `Projectile`. The port's counterpart is keyed on the Combat Extended assembly, and `CompatibilityManager` finds it in game. The dome picks `Comp_ShieldNet_ProjInterceptorELS_CompatibleCE` for a `ProjectileCE`.
- For 1.7, `ModContentPack.InitLoadFolders` takes the newest `LoadFolders.xml` block not above the game version. Combat Extended's 1.7-Test branch has only a `v1.6` block, so it loads on 1.7 through that block.
- `ProjectileCE.DamageAmount` is the bullet's damage times `RemainingKineticEnergyPct`, which is the square of the speed ratio for a ballistic bullet. The energy a shot costs the dome therefore falls while the shot flies.
- `ProjectileCE.Launch` takes a `distance` argument and ignores it. The landing point comes from the shot angle, the shot height, and the bullet's speed.
- A shot the dome does not catch flies on past the dome. In run `20261108-233855` a HEAT shell from the friendly side landed at (22.5, 6.5), in the row where the mech serum group's colonists stood.
- The Combat Extended 1.7-Test build logs errors on every run: `AsyncGPUReadback failed`, `Texture ... was not cached`, and `bodyGraphicData` failures. An explosive HEAT shell also makes `BoundMap` exceptions while its fragments tick. The rows use `Bullet_22LR_FMJ`, which does not explode.

## Not covered

- The ShuttleFuel and MoveMonolith patches. They are out of scope.
- Real wounds. The validation rows set blood loss directly, so they do not model a wound.
- Combat Extended beyond the rows. The rows fire one small Combat Extended bullet at one dome. Combat Extended's own weapons, armour, and health system are not covered.
- The 1.6 build at runtime. No 1.6 install is available on this machine, and no runner exists for it.
- The game's use job, start to end. The rows call `DoEffect` and `CanBeUsedBy` directly.
- Patch failures. A patch that fails to apply shows up only when the game starts with the mod enabled. The smoke run checks that the port's patches apply.
