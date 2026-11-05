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

## Not covered

- The ShuttleFuel and MoveMonolith patches. They are out of scope.
- Runtime behavior. A patch that fails to apply shows up only when the game starts with the mod enabled.
