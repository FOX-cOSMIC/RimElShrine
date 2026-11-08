using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimElShrine.Apparels.ApparelShield;
using RimElShrine.Buildings.ProjInterceptorNet;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShieldValidation
{
    // Raid demo for a recorded video. Runs only when the game starts with -shieldvalidation-raid, which
    // Record-CombatDemo.ps1 -Scene raid passes. The scene: one shield generator with power, and up to three colonists inside
    // its dome. Each colonist is drafted, carries a rifle, and wears an ES0 shield belt. When the dome comes up, three raiders
    // with revolvers appear outside it and attack. The colonists fire back through the dome, and the dome stops the raiders' shots.
    // The demo writes only [ShieldCombat] lines. It writes no validation-results rows, so the runner's rules do not change.
    public class RaidDemo : GameComponent
    {
        private const string Prefix = DemoScene.Prefix;
        private const int WaitForDomeTicks = 3600;
        // The fight lasts 90 seconds after the dome comes up, unless one side goes down first.
        private const int FightTicks = 5400;
        private const int FlightGraceTicks = 600;
        private const int DiagEveryTicks = 600;
        private const int MaxColonists = 3;
        private const string BeltDef = "ELS_Thing_ES0Shield";
        private const string RifleDef = "Gun_BoltActionRifle";
        private const string RevolverDef = "Gun_Revolver";
        private const string RaiderKindDef = "Mercenary_Gunner";
        private const string DefendDuty = "Defend";
        private const string AttackJobDef = "AttackStatic";
        // Each raider holds a spot within this many cells of where it spawns.
        private const float DefendRadius = 4f;

        // Offsets from the block origin. Colonists stand inside the dome, which has a radius of 11 around the generator at
        // origin + (3, 0, 0). Raiders stand 18 to 19 cells east of the generator, outside the dome, and inside the 24 x 5 block.
        private static readonly IntVec3[] ColonistOffsets =
        {
            new IntVec3(4, 0, 3),
            new IntVec3(6, 0, 3),
            new IntVec3(8, 0, 3),
        };
        private static readonly IntVec3[] RaiderOffsets =
        {
            new IntVec3(21, 0, 1),
            new IntVec3(22, 0, 3),
            new IntVec3(21, 0, 4),
        };

        private enum Stage { Setup, WaitForDome, Fight, Done }

        private Stage stage = Stage.Setup;
        private int stageStartTick;
        private int lastDiagTick;
        private Map map;
        private IntVec3 layoutOrigin;
        private DemoScene.DomeRow row;
        private Comp_ShieldNet_ProjInterceptorELS dome;
        private JobDef attackJob;
        private readonly List<Pawn> colonists = [];
        private readonly List<Comp_ApparelShield> belts = [];
        private readonly List<float> beltEnergy = [];
        private readonly List<Pawn> raiders = [];
        private readonly HashSet<Projectile> countedShots = [];
        private readonly HashSet<object> countedInterceptions = [];
        private readonly DemoFrames frames = new DemoFrames();
        private int raiderShots;
        private int colonistShots;
        // Where each colonist shot was last seen. A shot that ends at the dome's edge was stopped there, so the count should stay at 0.
        private readonly Dictionary<Projectile, UnityEngine.Vector3> colonistShotsInFlight = [];
        private int colonistShotsAtEdge;
        private int shotsIntercepted;
        private int beltAbsorbs;
        private int domeDownEvents;
        private bool domeWasUp;
        private int startInjuries;
        private int colonistInjuries = -1;

        // RimWorld creates each GameComponent with a Game argument, so this constructor must take one.
        public RaidDemo(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (stage == Stage.Done || !GenCommandLine.CommandLineArgPassed("shieldvalidation-raid")) return;
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

            var found = currentMap.mapPawns.FreeColonistsSpawned.ToList();
            if (found.Count == 0) throw new InvalidOperationException("no colonist is on the map");
            // Up to three colonists stay on the map. Any others are despawned for the run.
            foreach (var extra in found.Skip(MaxColonists)) extra.DeSpawn();
            colonists.AddRange(found.Take(MaxColonists));

            var origin = DemoScene.FindDemoBlock(currentMap);
            if (origin == null) throw new InvalidOperationException("no open 24 x 5 block for the demo layout");
            layoutOrigin = origin.Value;
            DemoScene.ClearPlants(currentMap, layoutOrigin);

            // The turret scene's dome row, with the same power. The raiders come at dome-up.
            row = DemoScene.SpawnDomeRow(currentMap, layoutOrigin);
            dome = row.Interceptor;

            for (int i = 0; i < colonists.Count; i++) DressColonist(colonists[i], layoutOrigin + ColonistOffsets[i]);
            startInjuries = colonists.Sum(c => DemoScene.CountInjuries(c));

            Find.CameraDriver.SetRootPosAndSize(row.Generator.TrueCenter(), 14f);
            Find.Selector.ClearSelection();
            Find.Selector.Select(row.Generator, false);

            Log.Message(Prefix + "raid setup: generator at " + row.Generator.Position + " (faction " + row.Generator.Faction.Name + ") with radius " + dome.Radius
                + ", colonists in the dome " + colonists.Count + " with rifles and ES0 belts, battery " + row.Battery.StoredEnergy.ToString("0")
                + " of " + row.Battery.Props.storedEnergyMax.ToString("0") + ", other colonists removed " + (found.Count - colonists.Count));
            Log.Message(Prefix + "setup done");
            stageStartTick = Find.TickManager.TicksGame;
            stage = Stage.WaitForDome;
        }

        // A colonist stands on the cell, drafted, with a rifle and an ES0 belt at full energy. Other apparel and weapons come off.
        private void DressColonist(Pawn pawn, IntVec3 cell)
        {
            pawn.DeSpawn();
            GenSpawn.Spawn(pawn, cell, map);
            pawn.equipment.DestroyAllEquipment();
            pawn.equipment.AddEquipment((ThingWithComps)ValidationSupport.Make(RifleDef));
            pawn.apparel.DestroyAll();
            var belt = (Apparel)ValidationSupport.Make(BeltDef);
            var shield = belt.GetComp<Comp_ApparelShield>() ?? throw new InvalidOperationException("the belt has no Comp_ApparelShield");
            shield.Energy = shield.EnergyMax;
            pawn.apparel.Wear(belt);
            if (belt.Wearer != pawn) throw new InvalidOperationException("the belt did not stay on " + pawn.LabelShort);
            pawn.drafter.Drafted = true;
            belts.Add(shield);
            beltEnergy.Add(shield.Energy);
            Log.Message(Prefix + "colonist " + pawn.LabelShort + " at " + pawn.Position + " drafted, with a rifle and an ES0 belt at energy " + shield.Energy.ToString("0"));
        }

        private void WaitForDome()
        {
            int now = Find.TickManager.TicksGame;
            if (now - lastDiagTick >= 300)
            {
                lastDiagTick = now;
                Log.Message(Prefix + "wait at tick " + now + ": dome powered " + dome.IsNetActive() + ", dome battery " + row.GeneratorBattery.StoredEnergy.ToString("0.0")
                    + ", dome recharging " + dome.isRecharging + " with " + dome.rechargeTicks + " ticks left, colonists drafted "
                    + colonists.All(c => c.drafter.Drafted));
            }
            if (dome.isActive)
            {
                PlaceRaiders();
                Log.Message(Prefix + "dome up at tick " + now + ", energy " + dome.energy.ToString("0") + "; raiders placed " + raiders.Count);
                domeWasUp = true;
                stageStartTick = now;
                lastDiagTick = now;
                stage = Stage.Fight;
                return;
            }
            if (now - stageStartTick > WaitForDomeTicks) throw new InvalidOperationException("the dome did not come up within a minute");
        }

        // Three raiders with revolvers, outside the dome. Each runs the game's own assault duty, so it fights the colonists it can see.
        private void PlaceRaiders()
        {
            var hostile = DemoScene.FindHostileFaction();
            var kind = DefDatabase<PawnKindDef>.GetNamed(RaiderKindDef, true);
            var defend = DefDatabase<DutyDef>.GetNamed(DefendDuty, true);
            for (int i = 0; i < RaiderOffsets.Length; i++)
            {
                var raider = PawnGenerator.GeneratePawn(kind, hostile);
                raider.equipment.DestroyAllEquipment();
                raider.equipment.AddEquipment((ThingWithComps)ValidationSupport.Make(RevolverDef));
                var cell = layoutOrigin + RaiderOffsets[i];
                GenSpawn.Spawn(raider, cell, map);
                // The assault duty sent raiders off to the map edge, out of sight of the colonists. Each raider holds its spot instead.
                raider.mindState.duty = new PawnDuty(defend, cell, DefendRadius);
                raiders.Add(raider);
                Log.Message(Prefix + "raider " + raider.LabelShort + " placed at " + raider.Position + " (faction " + hostile.Name
                    + ", hostile to the player " + hostile.HostileTo(Faction.OfPlayer) + "), weapon " + (raider.equipment.PrimaryEq?.parent.def.defName ?? "none")
                    + ", duty " + (raider.mindState.duty?.def.defName ?? "none"));
            }
        }

        // The raiders' own AI kept them out of the colonists' sight. The assault duty sent them to the map edge, and the defend
        // duty left them without a target. So each raider still up gets an attack-from-here job on the nearest colonist it can
        // hit, and it keeps that job until the colonist goes down. The shots, the dome and the belts are the game's own.
        private void AimRaiders()
        {
            attackJob ??= DefDatabase<JobDef>.GetNamed(AttackJobDef, true);
            foreach (var raider in raiders)
            {
                if (raider.Dead || raider.Downed) continue;
                var current = raider.jobs.curJob;
                if (current != null && current.def == attackJob && current.targetA.Thing is Pawn held && IsLiveTarget(raider, held)) continue;
                var target = colonists.Where(c => IsLiveTarget(raider, c)).OrderBy(c => raider.Position.DistanceToSquared(c.Position)).FirstOrDefault();
                if (target == null) continue;
                raider.jobs.StartJob(new Job(attackJob, target), JobCondition.InterruptForced);
            }
        }

        private static bool IsLiveTarget(Pawn shooter, Pawn target)
        {
            if (target.Dead || target.Downed) return false;
            var verb = shooter.TryGetAttackVerb(target, !shooter.IsColonist);
            return verb != null && verb.CanHitTarget(target);
        }

        private void Fight()
        {
            int now = Find.TickManager.TicksGame;
            AimRaiders();
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile))
            {
                if (thing is not Projectile shot || shot.Launcher is not Pawn launcher) continue;
                bool fromRaider = raiders.Contains(launcher);
                if (!fromRaider && !colonists.Contains(launcher)) continue;
                if (!countedShots.Add(shot)) continue;
                if (fromRaider)
                {
                    raiderShots++;
                    Log.Message(Prefix + "raider shot " + raiderShots + " fired at tick " + now + " from " + shot.Position + " by " + launcher.LabelShort);
                }
                else
                {
                    colonistShots++;
                    colonistShotsInFlight[shot] = shot.ExactPosition;
                    Log.Message(Prefix + "colonist shot " + colonistShots + " fired at tick " + now + " from " + shot.Position + " by " + launcher.LabelShort);
                }
            }

            // A colonist shot leaves the dome untouched. A shot that the dome stops ends just outside its edge, so a colonist shot
            // that ends there was stopped, and the count stays at 0. A shot that hits a raider ends well beyond the edge.
            var domeCentre = row.Generator.TrueCenter();
            foreach (var shot in colonistShotsInFlight.Keys.ToList())
            {
                if (shot.Spawned)
                {
                    colonistShotsInFlight[shot] = shot.ExactPosition;
                    continue;
                }
                float distance = UnityEngine.Vector3.Distance(colonistShotsInFlight[shot], domeCentre);
                if (distance <= dome.Radius + 2f)
                {
                    colonistShotsAtEdge++;
                    Log.Message(Prefix + "colonist shot ended at the dome edge at tick " + now + ", " + distance.ToString("0.0") + " cells from the dome centre");
                }
                colonistShotsInFlight.Remove(shot);
            }

            // The port keeps one entry per shot it stops, in a protected list. Count each entry once.
            if (DemoScene.InterceptionsField?.GetValue(dome) is IList entries)
            {
                foreach (var entry in entries)
                {
                    if (!countedInterceptions.Add(entry)) continue;
                    shotsIntercepted++;
                    Log.Message(Prefix + "intercept " + shotsIntercepted + " at tick " + now + ", energy " + dome.energy.ToString("0"));
                }
            }

            // A belt's energy only falls when it absorbs a hit that reached its wearer. Its recharge only raises it.
            for (int i = 0; i < belts.Count; i++)
            {
                float energy = belts[i].Energy;
                if (energy < beltEnergy[i] - 0.0001f)
                {
                    beltAbsorbs++;
                    Log.Message(Prefix + "belt absorb " + beltAbsorbs + " on " + colonists[i].LabelShort + " at tick " + now
                        + ", energy " + beltEnergy[i].ToString("0.0") + " to " + energy.ToString("0.0"));
                }
                beltEnergy[i] = energy;
            }

            if (dome.isActive != domeWasUp)
            {
                domeWasUp = dome.isActive;
                if (!domeWasUp)
                {
                    domeDownEvents++;
                    Log.Message(Prefix + "dome down at tick " + now + ", energy " + dome.energy.ToString("0"));
                }
                else Log.Message(Prefix + "dome up again at tick " + now + ", energy " + dome.energy.ToString("0"));
            }

            int colonistNow = colonists.Sum(c => DemoScene.CountInjuries(c)) - startInjuries;
            if (colonistNow != colonistInjuries)
            {
                colonistInjuries = colonistNow;
                Log.Message(Prefix + "colonist new injuries " + colonistInjuries + " at tick " + now);
            }
            if (now - lastDiagTick >= DiagEveryTicks)
            {
                lastDiagTick = now;
                Log.Message(Prefix + "fight at tick " + now + ": raiders up " + raiders.Count(r => !r.Dead && !r.Downed)
                    + ", colonists up " + colonists.Count(c => !c.Dead && !c.Downed) + ", dome up " + dome.isActive
                    + ", energy " + dome.energy.ToString("0") + ", belts " + string.Join(" ", belts.Select(b => b.Energy.ToString("0"))));
                foreach (var r in raiders)
                {
                    Log.Message(Prefix + "raider " + r.LabelShort + " at tick " + now + ": job " + (r.jobs?.curJob?.def?.defName ?? "none")
                        + ", weapon " + (r.equipment?.PrimaryEq?.parent?.def?.defName ?? "none") + ", target " + (r.jobs?.curJob?.targetA.Thing?.LabelShort ?? "none") + ", health " + (r.health.summaryHealth.SummaryHealthPercent * 100f).ToString("0")
                        + "%, at " + r.Position + ", " + (r.Dead ? "dead" : r.Downed ? "downed" : "up"));
                }
            }

            if (colonists.All(c => c.Dead || c.Downed))
            {
                Finish("colonists down");
                return;
            }
            if (raiders.All(r => r.Dead || r.Downed))
            {
                Finish("raiders down");
                return;
            }
            // The time limit ends the fight only when no counted shot is still in flight. A shot still flying after the grace period
            // ends the fight anyway, and the summary reports it.
            int elapsed = now - stageStartTick;
            if (elapsed >= FightTicks && (elapsed >= FightTicks + FlightGraceTicks || !countedShots.Any(shot => shot.Spawned)))
                Finish("time");
        }

        // Each raider's health as a percent of full health. A colonist's shot that hits a raider lowers its number.
        private string RaiderHealth() => string.Join(" ", raiders.Select(r => (r.health.summaryHealth.SummaryHealthPercent * 100f).ToString("0")));

        private void Finish(string reason)
        {
            Log.Message(Prefix + "summary: " + reason + ", raider shots fired " + raiderShots + ", shots intercepted " + shotsIntercepted
                + ", shots in flight " + countedShots.Count(shot => shot.Spawned)
                + ", colonist shots fired " + colonistShots + ", colonist shots ended at the dome edge " + colonistShotsAtEdge
                + ", raider health " + RaiderHealth()
                + ", raiders down " + raiders.Count(r => r.Dead || r.Downed) + " of " + raiders.Count
                + ", colonist new injuries " + colonistInjuries + ", colonists down " + colonists.Count(c => c.Dead || c.Downed)
                + ", belt absorbs " + beltAbsorbs + ", dome down events " + domeDownEvents + ", dome up at end " + (dome?.isActive ?? false));
            Log.Message(Prefix + "done");
            stage = Stage.Done;
        }
    }
}
