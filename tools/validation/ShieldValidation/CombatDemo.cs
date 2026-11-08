using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using RimElShrine.Buildings.ProjInterceptorNet;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShieldValidation
{
    // Combat demo for a recorded video. Runs only when the game starts with -shieldvalidation-combat, which
    // Record-CombatDemo.ps1 passes. The scene: one shield generator with power and one drafted colonist inside its
    // dome. A hostile mini-turret, outside the dome, is placed when the dome comes up, and then it fires.
    // The demo writes only [ShieldCombat] lines. It writes no validation-results rows, so the runner's rules do not change.
    public class CombatDemo : GameComponent
    {
        private const string Prefix = "[ShieldCombat] ";
        private const string GeneratorDef = "ELS_Thing_Building_ShieldNet_Shield";
        private const int WaitForDomeTicks = 3600;
        private const int FightTicks = 3600;
        // A shot that is still flying at the time limit gets this long to resolve. It flies in well under a second.
        private const int FlightGraceTicks = 600;
        // At root size 14 on a 16:9 window, the camera reaches about 25 cells to each side and 14 up and down.
        private const int ViewHalfWidth = 25;
        private const int ViewHalfHeight = 14;
        private const int FrameEveryTicks = 6;
        private static readonly FieldInfo InterceptionsField = typeof(Comp_ShieldNet_ProjInterceptorELS)
            .GetField("interceptions", BindingFlags.Instance | BindingFlags.NonPublic);

        private enum Stage { Setup, WaitForDome, Fight, Done }

        private Stage stage = Stage.Setup;
        private int stageStartTick;
        private int lastDiagTick;
        private Map map;
        private IntVec3 layoutOrigin;
        private ThingWithComps generator;
        private Comp_ShieldNet_ProjInterceptorELS dome;
        private CompPowerBattery domeBattery;
        private Building_TurretGun turret;
        private Pawn colonist;
        private readonly HashSet<Projectile> countedShots = [];
        private readonly HashSet<object> countedInterceptions = [];
        private int shotsFired;
        private int shotsIntercepted;
        private int startInjuries;
        private int injuries = -1;
        private int domeDownEvents;
        private bool domeWasUp;
        private int lastFrameTick = -1000;
        private string framesFolder;
        private CombatFrameGrabber grabber;

        // RimWorld creates each GameComponent with a Game argument, so this constructor must take one.
        public CombatDemo(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (stage == Stage.Done || !GenCommandLine.CommandLineArgPassed("shieldvalidation-combat")) return;
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            map ??= Find.CurrentMap;
            if (map == null) return;
            try
            {
                if (stage == Stage.WaitForDome || stage == Stage.Fight) CaptureFrame(Find.TickManager.TicksGame);
                switch (stage)
                {
                    case Stage.Setup:
                        Setup(map);
                        break;
                    case Stage.WaitForDome:
                        WaitForDome();
                        break;
                    case Stage.Fight:
                        Fight();
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Message(Prefix + "FAIL exception in stage " + stage + ": " + e.GetType().Name + ": " + e.Message);
                Finish("failed");
            }
        }

        private void Setup(Map currentMap)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;

            // Only one colonist stays on the map, so the turret has one target. The others are despawned for the run.
            var colonists = currentMap.mapPawns.FreeColonistsSpawned.ToList();
            if (colonists.Count == 0) throw new InvalidOperationException("no colonist is on the map");
            colonist = colonists[0];
            startInjuries = CountInjuries();
            foreach (var other in colonists.Skip(1)) other.DeSpawn();

            var origin = FindDemoBlock(currentMap);
            if (origin == null) throw new InvalidOperationException("no open 24 x 5 block for the demo layout");
            var o = origin.Value;
            layoutOrigin = o;
            // The scene needs open ground, so plants in its block are removed.
            for (int dz = 0; dz < 5; dz++)
                for (int dx = 0; dx < 24; dx++)
                    foreach (var plant in currentMap.thingGrid.ThingsAt(o + new IntVec3(dx, 0, dz)).OfType<Plant>().ToList())
                        plant.Destroy();

            // Battery, two conduits, and the generator sit in one row. The turret and its conduit line come at dome-up.
            var battery = Owned(ValidationSupport.Spawn("Battery", o, currentMap, Rot4.East));
            var batteryComp = battery.GetComp<CompPowerBattery>();
            if (batteryComp == null) throw new InvalidOperationException("the battery has no CompPowerBattery");
            batteryComp.AddEnergy(batteryComp.Props.storedEnergyMax);
            Owned(ValidationSupport.Spawn("PowerConduit", o + IntVec3.East * 1, currentMap, Rot4.North));
            Owned(ValidationSupport.Spawn("PowerConduit", o + IntVec3.East * 2, currentMap, Rot4.North));
            generator = Owned(ValidationSupport.Spawn(GeneratorDef, o + IntVec3.East * 3, currentMap, Rot4.North));
            dome = generator.GetComp<Comp_ShieldNet_ProjInterceptorELS>();
            if (dome == null) throw new InvalidOperationException("the generator has no interceptor comp");
            domeBattery = generator.GetComp<CompPowerBattery>();
            if (domeBattery == null) throw new InvalidOperationException("the generator has no battery comp");
            // The shield counts as powered only while its own battery holds energy. A grid with no producer never
            // charges that battery, so the scene starts it with some energy.
            domeBattery.AddEnergy(300f);

            // The colonist stands inside the dome, unarmed and drafted, so it stays where it is.
            colonist.DeSpawn();
            GenSpawn.Spawn(colonist, o + new IntVec3(3, 0, 3), currentMap);
            colonist.equipment.DestroyAllEquipment();
            colonist.drafter.Drafted = true;

            Find.CameraDriver.SetRootPosAndSize(generator.TrueCenter(), 14f);
            Find.Selector.ClearSelection();
            Find.Selector.Select(generator, false);

            Log.Message(Prefix + "setup: generator at " + generator.Position + " (faction " + generator.Faction.Name + ") with radius " + dome.Radius
                + ", colonist " + colonist.LabelShort + " at " + colonist.Position
                + ", battery " + batteryComp.StoredEnergy.ToString("0") + " of " + batteryComp.Props.storedEnergyMax.ToString("0")
                + ", other colonists removed " + (colonists.Count - 1));
            Log.Message(Prefix + "setup done");
            stageStartTick = Find.TickManager.TicksGame;
            stage = Stage.WaitForDome;
        }

        private void WaitForDome()
        {
            int now = Find.TickManager.TicksGame;
            if (now - lastDiagTick >= 300)
            {
                lastDiagTick = now;
                Log.Message(Prefix + "wait at tick " + now + ": dome powered " + dome.IsNetActive() + ", dome battery " + domeBattery.StoredEnergy.ToString("0.0")
                    + ", dome recharging " + dome.isRecharging + " with " + dome.rechargeTicks + " ticks left, colonist drafted " + colonist.drafter.Drafted);
            }
            if (dome.isActive)
            {
                PlaceTurret();
                Log.Message(Prefix + "dome up at tick " + now + ", energy " + dome.energy.ToString("0") + "; turret placed at " + turret.Position
                    + " (faction " + turret.Faction.Name + ", hostile to the player " + turret.Faction.HostileTo(Faction.OfPlayer) + ")");
                domeWasUp = true;
                stageStartTick = now;
                stage = Stage.Fight;
                return;
            }
            if (now - stageStartTick > WaitForDomeTicks) throw new InvalidOperationException("the dome did not come up within a minute");
        }

        // The conduit line runs east from the generator to the turret, 20 cells from the battery.
        private void PlaceTurret()
        {
            for (int i = 5; i <= 19; i++) Owned(ValidationSupport.Spawn("PowerConduit", layoutOrigin + IntVec3.East * i, map, Rot4.North));
            var hostile = FindHostileFaction();
            var turretDef = DefDatabase<ThingDef>.GetNamed("Turret_MiniTurret", true);
            var turretThing = ThingMaker.MakeThing(turretDef, turretDef.MadeFromStuff ? GenStuff.DefaultStuffFor(turretDef) : null);
            turretThing.SetFaction(hostile);
            turret = (Building_TurretGun)GenSpawn.Spawn(turretThing, layoutOrigin + IntVec3.East * 20, map, Rot4.West);
        }

        private void Fight()
        {
            int now = Find.TickManager.TicksGame;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile))
            {
                if (thing is Projectile shot && shot.Launcher == turret && countedShots.Add(shot))
                {
                    shotsFired++;
                    Log.Message(Prefix + "shot " + shotsFired + " fired at tick " + now + " from " + shot.Position);
                }
            }
            // The port keeps one entry per shot it stops, in a protected list. Read it by reflection, and count new entries.
            if (InterceptionsField?.GetValue(dome) is IList entries)
            {
                foreach (var entry in entries)
                {
                    if (!countedInterceptions.Add(entry)) continue;
                    shotsIntercepted++;
                    Log.Message(Prefix + "intercept " + shotsIntercepted + " at tick " + now + ", energy " + dome.energy.ToString("0"));
                }
            }
            if (dome.isActive != domeWasUp)
            {
                domeWasUp = dome.isActive;
                if (!domeWasUp)
                {
                    domeDownEvents++;
                    Log.Message(Prefix + "dome down at tick " + now + ", energy " + dome.energy.ToString("0"));
                    Finish("dome down");
                    return;
                }
            }
            int count = CountInjuries() - startInjuries;
            if (count != injuries)
            {
                injuries = count;
                Log.Message(Prefix + "colonist new injuries " + injuries + " at tick " + now);
            }
            if (colonist.Dead || colonist.Downed)
            {
                Finish("colonist dead or downed");
                return;
            }
            // The time limit ends the fight only when no counted shot is still in flight, so every shot the summary counts
            // has resolved. If one is still flying after the grace period, the fight ends anyway and the summary reports it.
            int elapsed = now - stageStartTick;
            if (elapsed >= FightTicks && (elapsed >= FightTicks + FlightGraceTicks || !countedShots.Any(shot => shot.Spawned)))
                Finish("time");
        }

        // Frames come from the game's own render, every FrameEveryTicks game ticks, and are named by tick. The recorder
        // turns them into the video, so each frame lasts the game time it covers. The capture reads the frame itself, so
        // the video shows the live game and not a copy of the screen.
        private void CaptureFrame(int now)
        {
            if (now - lastFrameTick < FrameEveryTicks) return;
            lastFrameTick = now;
            framesFolder ??= Path.Combine(GenFilePaths.SaveDataFolderPath, "combat-frames");
            Directory.CreateDirectory(framesFolder);
            if (grabber == null)
            {
                var host = new GameObject("ShieldCombatFrames");
                UnityEngine.Object.DontDestroyOnLoad(host);
                grabber = host.AddComponent<CombatFrameGrabber>();
            }
            grabber.Folder = framesFolder;
            grabber.Tick = now;
            grabber.Requested = true;
        }

        // Buildings are the player's, as a player's buildings are. An unowned generator counts as hostile to no one,
        // so the port's interception check would stop no shots.
        private static ThingWithComps Owned(ThingWithComps building)
        {
            building.SetFaction(Faction.OfPlayer);
            return building;
        }

        private int CountInjuries() => colonist.health.hediffSet.hediffs.Count(h => h is Hediff_Injury);

        // Finds a 24 x 5 block with open ground: no building or rock, and nothing but plants, which the scene clears.
        // The camera is centred three cells into the block, so the block keeps the view inside the map. Off-map black would show in the video.
        private static IntVec3? FindDemoBlock(Map currentMap)
        {
            for (int z = ViewHalfHeight; z < currentMap.Size.z - ViewHalfHeight; z++)
                for (int x = ViewHalfWidth - 3; x < currentMap.Size.x - ViewHalfWidth - 3; x++)
                    if (IsOpenBlock(currentMap, new IntVec3(x, 0, z))) return new IntVec3(x, 0, z);
            return null;
        }

        private static bool IsOpenBlock(Map currentMap, IntVec3 origin)
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

        private void Finish(string reason)
        {
            Log.Message(Prefix + "summary: " + reason + ", shots fired " + shotsFired + ", shots intercepted " + shotsIntercepted
                + ", shots in flight " + countedShots.Count(shot => shot.Spawned)
                + ", colonist new injuries " + injuries + ", dome down events " + domeDownEvents + ", dome up at end " + (dome?.isActive ?? false));
            Log.Message(Prefix + "done");
            stage = Stage.Done;
        }

        private static Faction FindHostileFaction()
        {
            var pirates = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate);
            if (pirates != null && pirates.HostileTo(Faction.OfPlayer)) return pirates;
            var other = Find.FactionManager.AllFactions.FirstOrDefault(f => f != Faction.OfPlayer && f.HostileTo(Faction.OfPlayer));
            return other ?? throw new InvalidOperationException("no hostile faction exists in this game");
        }
    }

    // Saves the game's own frame at the end of the frame, after RimWorld has drawn it. CombatDemo asks for a frame.
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
