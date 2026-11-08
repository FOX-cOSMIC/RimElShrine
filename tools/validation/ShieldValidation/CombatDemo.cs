using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimElShrine.Buildings.ProjInterceptorNet;
using RimWorld;
using Verse;

namespace ShieldValidation
{
    // Turret combat demo for a recorded video. Runs only when the game starts with -shieldvalidation-combat, which
    // Record-CombatDemo.ps1 passes. The scene: one shield generator with power and one drafted colonist inside its
    // dome. A hostile mini-turret, outside the dome, is placed when the dome comes up, and then it fires.
    // The demo writes only [ShieldCombat] lines. It writes no validation-results rows, so the runner's rules do not change.
    public class CombatDemo : GameComponent
    {
        private const string Prefix = DemoScene.Prefix;
        private const int WaitForDomeTicks = 3600;
        private const int FightTicks = 3600;
        // A shot that is still flying at the time limit gets this long to resolve. It flies in well under a second.
        private const int FlightGraceTicks = 600;

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
        private readonly DemoFrames frames = new DemoFrames();

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
                if (stage == Stage.WaitForDome || stage == Stage.Fight) frames.Capture(Find.TickManager.TicksGame);
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
            startInjuries = DemoScene.CountInjuries(colonist);
            foreach (var other in colonists.Skip(1)) other.DeSpawn();

            var origin = DemoScene.FindDemoBlock(currentMap);
            if (origin == null) throw new InvalidOperationException("no open 24 x 5 block for the demo layout");
            layoutOrigin = origin.Value;
            DemoScene.ClearPlants(currentMap, layoutOrigin);

            // The turret and its conduit line come at dome-up.
            var row = DemoScene.SpawnDomeRow(currentMap, layoutOrigin);
            generator = row.Generator;
            dome = row.Interceptor;
            domeBattery = row.GeneratorBattery;

            // The colonist stands inside the dome, unarmed and drafted, so it stays where it is.
            colonist.DeSpawn();
            GenSpawn.Spawn(colonist, layoutOrigin + new IntVec3(3, 0, 3), currentMap);
            colonist.equipment.DestroyAllEquipment();
            colonist.drafter.Drafted = true;

            Find.CameraDriver.SetRootPosAndSize(generator.TrueCenter(), 14f);
            Find.Selector.ClearSelection();
            Find.Selector.Select(generator, false);

            Log.Message(Prefix + "setup: generator at " + generator.Position + " (faction " + generator.Faction.Name + ") with radius " + dome.Radius
                + ", colonist " + colonist.LabelShort + " at " + colonist.Position
                + ", battery " + row.Battery.StoredEnergy.ToString("0") + " of " + row.Battery.Props.storedEnergyMax.ToString("0")
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
            for (int i = 5; i <= 19; i++) DemoScene.Owned(ValidationSupport.Spawn("PowerConduit", layoutOrigin + IntVec3.East * i, map, Rot4.North));
            var hostile = DemoScene.FindHostileFaction();
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
            if (DemoScene.InterceptionsField?.GetValue(dome) is IList entries)
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
            int count = DemoScene.CountInjuries(colonist) - startInjuries;
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

        private void Finish(string reason)
        {
            Log.Message(Prefix + "summary: " + reason + ", shots fired " + shotsFired + ", shots intercepted " + shotsIntercepted
                + ", shots in flight " + countedShots.Count(shot => shot.Spawned)
                + ", colonist new injuries " + injuries + ", dome down events " + domeDownEvents + ", dome up at end " + (dome?.isActive ?? false));
            Log.Message(Prefix + "done");
            stage = Stage.Done;
        }
    }
}
