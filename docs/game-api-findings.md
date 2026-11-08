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

Fast regeneration (decided 2026-11-07, ADR 0011):

- The Fast injectors set `isFastRegen` in the XML. Before 2026-11-07 no code read the flag, and the 1.6 build never read it either.
- Since 2026-11-07, the 1.7 build sets the regen hediff's severity to 1 for a Fast injector, inside an `#if RIMWORLD_17` block. `HediffWithComps_LimbsRegen.IsQuickRegen` is `Severity > 0.5f`, so the effect runs in quick mode.
- The quick branch takes blood loss only from an existing `BloodLoss` hediff, read with `Verse.HediffSet.TryGetHediff`, which never creates one. With none, it sets `reachedMaxBloodLoss`, and quick regen waits until a bleed appears. A healthy pawn therefore regrows at the normal speed.
- Measured in run `20261107-175804`: the finger of a pawn with blood loss came back at 9,633 ticks, and the finger of a pawn with none came back at 28,833 ticks. Run `20261107-175322` showed the healthy pawn at 9,633 ticks before the lookup change, because the port's helper gave it a zero-severity `BloodLoss` hediff.
- The keyed text in `LimbsRegenKeys.xml` describes quick regen that costs blood loss: "Fast regeneration may cause additional blood loss symptoms."
- The port's `CommonUtility.TryGetHediff` extension creates a zero-severity `BloodLoss` hediff when the pawn has none of the requested def, and it sets an existing bleed's severity to 0. The quick branch called it until 2026-11-07, which would have given a healthy pawn with quick regen a phantom bleed. The branch now calls `Verse.HediffSet.TryGetHediff`.
- `Comp_LimbsRegenInjector.CanBeUsedBy` still calls the extension for the regen effect. A bleeding pawn that has no regen effect loses its bleed when the check runs. The 1.6 build has the same call. Lucas chose on 2026-11-07 to leave both builds unchanged, as decision D4 and ADR 0011 record.

## Raid and turret recordings (2026-11-08)

Checked with `ilspycmd` against the installed `Assembly-CSharp.dll` (1.7.5000 rev1275). The run IDs are in `tools/validation/README.md`.

- `RimWorld.JobGiver_AIFightEnemy.TryGiveJob` returns no job when the pawn has no `mindState.enemyTarget`. `FindAttackTarget` accepts only targets the pawn can see. A raider that loses sight of every colonist does nothing. Run `20261108-110251` showed three idle raiders at the south edge of the map, with the colonists in revolver range.
- The `AssaultColony` duty runs `JobGiver_AIFightEnemies`, then `JobGiver_AIGotoNearestHostile` (`Duties_Misc.xml`). The `Defend` duty runs `JobGiver_AIDefendPoint`, then `ThinkNode_ForbidOutsideFlagRadius` with a 16-cell limit, then `JobGiver_WanderNearDutyLocation` with an 8-cell radius. In run `20261108-110839`, defend-duty raiders still walked about 11 cells from their spot and had no target.
- `Verse.AI.JobDriver_AttackStatic` calls `TryStartAttack` on `TargetA` every tick. It ends when the target is gone, destroyed, or downed after starting healthy, or when `numAttacksMade` reaches `job.maxNumStaticAttacks`. That field defaults to `int.MaxValue` in `Verse.AI.Job`. The job is not casual-interruptible. The raid scene uses it. From run `20261108-111238` on, each raider fired about every two seconds from its spawn spot.
- `new Job(JobDef, LocalTargetInfo)` and `Pawn_JobTracker.StartJob(Job, JobCondition)` start a job. `Pawn_JobTracker.curJob` is public.
- `Pawn_EquipmentTracker.PrimaryEq` returns a `CompEquippable`, not the weapon. The weapon is `PrimaryEq.parent`. Reading `PrimaryEq.def` does not compile.
- `PawnDuty(DutyDef def, LocalTargetInfo focus, float radius = -1f)` takes a focus cell and a radius.
- `LordMaker.MakeNewLord(Faction, LordJob, Map, IEnumerable<Pawn>)` is in `Verse.AI.Group`, not `RimWorld`. `LordJob_AssaultColony` takes `canTimeoutOrFlee`, which defaults to true and gates its exit-the-map toil. The raid scene does not use a lord.
- `Comp_ShieldNet_ProjInterceptorELS.InterceptProjectile` stops a projectile only when its launcher is hostile to the shield's faction, and only while `IFFOn` is set. IFF is on by default. The check sits at lines 311 to 317. In run `20261108-111632`, colonists fired 47 shots, and none ended at the dome edge.
- `Comp_ApparelShield.PostPreApplyDamage` absorbs damage that reaches the wearer, ranged or melee. It does not stop projectiles in flight. The ES0 belt's energy max reads 1.0 in these scenes, so a belt breaks after about four hits.
- The port has no Harmony patch on `Verb`, `CanHitTarget`, `ShootLine`, or the AI target finder. The raider AI is the game's own.
- The dome's energy readout is `x/1000`. A dome comes up at energy 100 in these scenes. Each intercepted revolver shot removed about 11 to 12 energy in the logs. The dome went down at energy 0 and came back at 100 about 20 seconds later.

## Not covered

- The ShuttleFuel and MoveMonolith patches. They are out of scope.
- Runtime behavior. A patch that fails to apply shows up only when the game starts with the mod enabled.
