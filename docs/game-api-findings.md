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

## Not covered

- The ShuttleFuel and MoveMonolith patches. They are out of scope.
- Runtime behavior. A patch that fails to apply shows up only when the game starts with the mod enabled.
