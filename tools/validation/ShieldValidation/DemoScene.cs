using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using RimElShrine.Buildings.ProjInterceptorNet;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShieldValidation
{
    // Shared by the recorded scenes, CombatDemo (turret) and RaidDemo (raiders). Both write only [ShieldCombat] lines.
    public static class DemoScene
    {
        public const string Prefix = "[ShieldCombat] ";
        public const string GeneratorDef = "ELS_Thing_Building_ShieldNet_Shield";
        // At root size 14 on a 16:9 window, the camera reaches about 25 cells to each side and 14 up and down.
        public const int ViewHalfWidth = 25;
        public const int ViewHalfHeight = 14;
        // The game saves a frame every this many game ticks.
        public const int FrameEveryTicks = 6;
        // The port keeps one entry per shot it stops, in a protected list. Read it by reflection.
        public static readonly FieldInfo InterceptionsField = typeof(Comp_ShieldNet_ProjInterceptorELS)
            .GetField("interceptions", BindingFlags.Instance | BindingFlags.NonPublic);

        public class DomeRow
        {
            public CompPowerBattery Battery;
            public ThingWithComps Generator;
            public Comp_ShieldNet_ProjInterceptorELS Interceptor;
            public CompPowerBattery GeneratorBattery;
        }

        // Battery, two conduits, and the generator sit in one row from origin. The grid battery starts full. The generator's
        // own battery starts with 300 Wd, because a grid with no producer never charges it, and the shield counts as powered
        // only while that battery holds energy.
        public static DomeRow SpawnDomeRow(Map map, IntVec3 origin)
        {
            var battery = Owned(ValidationSupport.Spawn("Battery", origin, map, Rot4.East));
            var batteryComp = battery.GetComp<CompPowerBattery>() ?? throw new InvalidOperationException("the battery has no CompPowerBattery");
            batteryComp.AddEnergy(batteryComp.Props.storedEnergyMax);
            Owned(ValidationSupport.Spawn("PowerConduit", origin + IntVec3.East * 1, map, Rot4.North));
            Owned(ValidationSupport.Spawn("PowerConduit", origin + IntVec3.East * 2, map, Rot4.North));
            var generator = Owned(ValidationSupport.Spawn(GeneratorDef, origin + IntVec3.East * 3, map, Rot4.North));
            var interceptor = generator.GetComp<Comp_ShieldNet_ProjInterceptorELS>() ?? throw new InvalidOperationException("the generator has no interceptor comp");
            var generatorBattery = generator.GetComp<CompPowerBattery>() ?? throw new InvalidOperationException("the generator has no battery comp");
            generatorBattery.AddEnergy(300f);
            return new DomeRow { Battery = batteryComp, Generator = generator, Interceptor = interceptor, GeneratorBattery = generatorBattery };
        }

        // Buildings are the player's, as a player's buildings are. An unowned generator counts as hostile to no one,
        // so the port's interception check would stop no shots.
        public static ThingWithComps Owned(ThingWithComps building)
        {
            building.SetFaction(Faction.OfPlayer);
            return building;
        }

        public static int CountInjuries(Pawn pawn) => pawn.health.hediffSet.hediffs.Count(h => h is Hediff_Injury);

        // The scene needs open ground, so plants in the 24 x 5 block are removed.
        public static void ClearPlants(Map map, IntVec3 origin)
        {
            for (int dz = 0; dz < 5; dz++)
                for (int dx = 0; dx < 24; dx++)
                    foreach (var plant in map.thingGrid.ThingsAt(origin + new IntVec3(dx, 0, dz)).OfType<Plant>().ToList())
                        plant.Destroy();
        }

        // Finds a 24 x 5 block with open ground: no building or rock, and nothing but plants, which the scene clears.
        // The camera is centred three cells into the block, so the block keeps the view inside the map. Off-map black would show in the video.
        public static IntVec3? FindDemoBlock(Map currentMap)
        {
            for (int z = ViewHalfHeight; z < currentMap.Size.z - ViewHalfHeight; z++)
                for (int x = ViewHalfWidth - 3; x < currentMap.Size.x - ViewHalfWidth - 3; x++)
                    if (IsOpenBlock(currentMap, new IntVec3(x, 0, z))) return new IntVec3(x, 0, z);
            return null;
        }

        public static bool IsOpenBlock(Map currentMap, IntVec3 origin)
        {
            for (int dz = 0; dz < 5; dz++)
                for (int dx = 0; dx < 24; dx++)
                {
                    var cell = origin + new IntVec3(dx, 0, dz);
                    if (currentMap.terrainGrid.TerrainAt(cell).passability == Traversability.Impassable) return false;
                    if (currentMap.edificeGrid[cell] != null) return false;
                    if (currentMap.thingGrid.ThingsAt(cell).Any(t => !(t is Plant))) return false;
                }
            return true;
        }

        public static Faction FindHostileFaction()
        {
            var pirates = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate);
            if (pirates != null && pirates.HostileTo(Faction.OfPlayer)) return pirates;
            var other = Find.FactionManager.AllFactions.FirstOrDefault(f => f != Faction.OfPlayer && f.HostileTo(Faction.OfPlayer));
            return other ?? throw new InvalidOperationException("no hostile faction exists in this game");
        }
    }

    // Asks for the game's own frame every FrameEveryTicks game ticks, named by tick. The recorder turns the frames into the
    // video, so each frame lasts the game time it covers. The capture reads the frame itself, so the video shows the live
    // game and not a copy of the screen.
    public class DemoFrames
    {
        private int lastFrameTick = -1000;
        private string folder;
        private CombatFrameGrabber grabber;

        public void Capture(int now)
        {
            if (now - lastFrameTick < DemoScene.FrameEveryTicks) return;
            lastFrameTick = now;
            folder ??= Path.Combine(GenFilePaths.SaveDataFolderPath, "combat-frames");
            Directory.CreateDirectory(folder);
            if (grabber == null)
            {
                var host = new GameObject("ShieldCombatFrames");
                UnityEngine.Object.DontDestroyOnLoad(host);
                grabber = host.AddComponent<CombatFrameGrabber>();
            }
            grabber.Folder = folder;
            grabber.Tick = now;
            grabber.Requested = true;
        }
    }

    // Saves the game's own frame at the end of the frame, after RimWorld has drawn it. DemoFrames asks for a frame.
    public class CombatFrameGrabber : MonoBehaviour
    {
        public string Folder;
        public int Tick;
        public bool Requested;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (!Requested) continue;
                Requested = false;
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    File.WriteAllBytes(Path.Combine(Folder, "f_" + Tick.ToString("D9") + ".jpg"), shot.EncodeToJPG(85));
                }
                finally
                {
                    Destroy(shot);
                }
            }
        }
    }
}
