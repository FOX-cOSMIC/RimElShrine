using System;
using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using RimElShrine.Buildings.ProjInterceptorNet;
using RimElShrine.Compatibility;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShieldValidation
{
    // Combat Extended checks for the port's dome. This assembly loads only when CETeam.CombatExtended is active
    // (ShieldValidation/LoadFolders.xml), and it runs only with -shieldvalidation. The shots are real CE bullets, spawned
    // and launched in the order CE's own code uses. Rules: tools/validation/README.md.
    public class CombatExtendedValidation : GameComponent
    {
        private const string CounterpartScenario = "combat extended: CE shots use the CE counterpart";
        private const string HostileScenario = "combat extended: a hostile CE shot is intercepted by the dome";
        private const string EnergyScenario = "combat extended: the dome loses the CE energy cost of a shot";
        private const string FriendlyScenario = "combat extended: a friendly CE shot is not intercepted";
        private const string GroupComplete = "combat extended group complete";
        private static readonly string[] EarlierGroups =
        {
            "network group complete",
            "equipment group complete",
            "mech serum group complete",
        };
        private const string CounterpartTypeName = "RimElShrine.Compatibility.Buildings.ProjInterceptorNet.Comp_ShieldNet_ProjInterceptorELS_CompatibleCE";
        private const string BulletDefName = "Bullet_22LR_FMJ";
        private const string HostileKind = "Mercenary_Gunner";
        private const string FriendlyKind = "Colonist";

        // The shots start inside the 24 x 5 demo block, 22 cells from its west edge. The dome row sits at x 0 to 3, so a
        // shot starts outside the dome's radius and reaches it before anything else.
        private const int ShotStartX = 22;
        // The friendly shot flies on another row, so its path does not cross the hostile shot's path.
        private const int FriendlyRowOffset = 4;
        // The shot is launched from a standing height. A flat shot at height zero lands where it starts.
        private const float ShotHeight = 1.5f;
        private const int FrameBudgetMs = 20;
        private const int ActivateTicks = 3000;
        // Ticks the dome must stay up before a shot, so the charge per tick is measured on at least one quiet tick.
        private const int ReadyTicks = 60;
        private const int ShotTicks = 1200;
        // Tolerance in energy units, for rounding. A bracket from the last two ticks in flight covers the shot's own decay.
        private const float Tolerance = 0.02f;

        private enum Stage { Setup, WaitForDome, Hostile, WaitForDomeAgain, Friendly }

        private sealed class Flight
        {
            public BulletCE Shot;
            public int StartTick;
            public float StartEnergy;
            // Energy cost of the shot on the last two ticks it was in flight. A shot loses speed in flight, so the cost falls.
            public float PrevCost;
            public float LastCost;
            // Position on the last tick the shot was in flight.
            public Vector2 LastPos;
            public bool Removed;
            // Energy the dome lost during the flight: the charge it would have gained, less the change it did gain.
            public float Observed;
        }

        private readonly HashSet<string> recorded = new HashSet<string>();
        private Stage stage = Stage.Setup;
        private bool finished;
        private int stageStart;
        private int activeSince = -1;
        private DemoScene.DomeRow row;
        private Comp_ShieldNet_ProjInterceptorELS dome;
        private IntVec3 origin;
        private Flight flight;

        // Sampled on every game tick. The charge per tick is measured on a quiet tick, one with no shot in flight.
        private int sampleTick = -1;
        private float sampleEnergy;
        private bool sampleFlying;
        private float chargePerTick;
        private bool haveCharge;

        // RimWorld creates each GameComponent with a Game argument, so this constructor must take one.
        public CombatExtendedValidation(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (finished || !GenCommandLine.CommandLineArgPassed("shieldvalidation")) return;
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            var map = Find.CurrentMap;
            if (map == null) return;
            try
            {
                Run(map);
            }
            catch (Exception e)
            {
                ValidationSupport.Record(GroupComplete, "FAIL: exception in stage " + stage + ": " + e.GetType().Name + ": " + e.Message);
                finished = true;
            }
        }

        public override void GameComponentTick()
        {
            if (finished || dome == null) return;
            int now = Find.TickManager.TicksGame;
            bool flying = flight != null && flight.Shot.Spawned;
            if (flying)
            {
                flight.PrevCost = flight.LastCost;
                flight.LastCost = CostNow(flight.Shot);
                flight.LastPos = flight.Shot.TrueCenter().ToVector2();
            }
            // The tick a shot leaves the map already carries the interception, so only ticks after a quiet tick count.
            else if (!sampleFlying && dome.isActive && sampleTick >= 0 && now - sampleTick == 1)
            {
                chargePerTick = dome.energy - sampleEnergy;
                haveCharge = true;
            }
            sampleFlying = flying;
            sampleTick = now;
            sampleEnergy = dome.energy;
        }

        // Advances the game one tick at a time, as the mech serum group does, so the dome comes up and the shots fly whether
        // or not the game is paused or running at a set speed. Each tick samples the dome in GameComponentTick.
        private void Run(Map map)
        {
            var budget = System.Diagnostics.Stopwatch.StartNew();
            while (!finished)
            {
                Step(map);
                if (finished || budget.ElapsedMilliseconds >= FrameBudgetMs) return;
                Find.TickManager.DoSingleTick();
            }
        }

        private void Step(Map map)
        {
            int now = Find.TickManager.TicksGame;
            switch (stage)
            {
                case Stage.Setup:
                    // The other groups go first. Their colonists share this map, and a shot that misses the dome flies on across it.
                    if (!ValidationSupport.GroupsCompleted(EarlierGroups)) return;
                    Setup(map);
                    stage = Stage.WaitForDome;
                    stageStart = now;
                    return;

                case Stage.WaitForDome:
                    if (!dome.isActive)
                    {
                        if (now - stageStart > ActivateTicks)
                        {
                            FailAll("the dome did not come up within " + ActivateTicks + " ticks, energy " + dome.energy.ToString("0.###"));
                        }
                        return;
                    }
                    if (activeSince < 0) activeSince = now;
                    if (!haveCharge || now - activeSince < ReadyTicks) return;
                    flight = Launch(HostileLauncher(), origin + new IntVec3(ShotStartX, 0, 0), now);
                    stage = Stage.Hostile;
                    return;

                case Stage.Hostile:
                    if (!EndFlightIfDone(now)) return;
                    CheckHostile();
                    stage = Stage.WaitForDomeAgain;
                    stageStart = now;
                    return;

                case Stage.WaitForDomeAgain:
                    if (!dome.isActive)
                    {
                        if (now - stageStart > ActivateTicks)
                        {
                            Row(FriendlyScenario, "FAIL: the dome did not come back up within " + ActivateTicks + " ticks, energy " + dome.energy.ToString("0.###"));
                            Finish();
                        }
                        return;
                    }
                    flight = Launch(FriendlyLauncher(), origin + new IntVec3(ShotStartX, 0, FriendlyRowOffset), now);
                    stage = Stage.Friendly;
                    return;

                case Stage.Friendly:
                    if (!EndFlightIfDone(now)) return;
                    CheckFriendly();
                    Finish();
                    return;
            }
        }

        private void Setup(Map map)
        {
            var block = DemoScene.FindDemoBlock(map);
            if (block == null) throw new InvalidOperationException("no open 24 x 5 block for the test layout");
            origin = block.Value;
            DemoScene.ClearPlants(map, origin);
            row = DemoScene.SpawnDomeRow(map, origin);
            dome = row.Interceptor;
            // Control runs pass -shieldvalidation-cenopower, which drains the generator's own battery. The dome then never comes up,
            // and the shot rows must fail.
            if (GenCommandLine.CommandLineArgPassed("shieldvalidation-cenopower"))
            {
                row.GeneratorBattery.DrawPower(row.GeneratorBattery.StoredEnergy);
            }
            // Control runs pass -shieldvalidation-ceniff, which turns the dome's IFF off. The friendly shot is then intercepted,
            // and the friendly row must fail.
            if (GenCommandLine.CommandLineArgPassed("shieldvalidation-ceniff")) dome.IFFOn = false;
            CheckCounterpart();
        }

        // Row 1: the rules the dome picks for a CE bullet are the port's CE counterpart, not the vanilla ones.
        private void CheckCounterpart()
        {
            var item = typeof(ProjectileCE).Assembly.GetCompatibleItem<ICompatibleProjInterceptorELS>();
            string found = item == null ? "none" : item.GetType().FullName;
            bool pass = item != null && item.GetType().FullName == CounterpartTypeName;
            Row(CounterpartScenario, (pass ? "PASS: " : "FAIL: ") + "rules for ProjectileCE: " + found);
        }

        // Row 2 and row 3. The shot must stop at the dome's edge, and the dome must lose the energy the CE rule gives.
        private void CheckHostile()
        {
            float low = Math.Min(flight.PrevCost, flight.LastCost);
            float high = Math.Max(flight.PrevCost, flight.LastCost);
            float tolerance = Math.Max(Tolerance, high * 0.01f);
            var centre = row.Generator.TrueCenter().ToVector2();
            float distance = Vector2.Distance(flight.LastPos, centre);
            float radius = dome.Radius;
            bool stopped = flight.Removed && flight.Observed > Tolerance && distance <= radius + 3f;
            Row(HostileScenario, (stopped ? "PASS: " : "FAIL: ") + "removed " + flight.Removed + ", energy taken " + flight.Observed.ToString("0.####")
                + ", last seen " + distance.ToString("0.##") + " cells from the dome centre, radius " + radius);
            // The dome can take no more than it holds, so a shot whose cost is above the energy before it cannot be measured.
            bool enough = flight.StartEnergy > high + Tolerance;
            bool exact = enough && flight.Removed && flight.Observed > Tolerance && flight.Observed >= low - tolerance && flight.Observed <= high + tolerance;
            string expected = high - low > 1e-6f ? low.ToString("0.####") + " to " + high.ToString("0.####") : low.ToString("0.####");
            string note = enough ? string.Empty : ", energy before the shot " + flight.StartEnergy.ToString("0.###") + " is below the cost";
            Row(EnergyScenario, (exact ? "PASS: " : "FAIL: ") + "energy taken " + flight.Observed.ToString("0.####") + ", CE cost " + expected
                + " (building factor " + dome.Setting.BuildingDmgGlobalFactor.ToString("0.##") + ")" + note);
        }

        // Row 4: a shot from a friendly launcher takes no energy. The IFF branch must let it pass.
        private void CheckFriendly()
        {
            bool none = Math.Abs(flight.Observed) <= Tolerance;
            Row(FriendlyScenario, (none ? "PASS: " : "FAIL: ") + "energy taken " + flight.Observed.ToString("0.####") + ", removed " + flight.Removed);
        }

        // Returns true when the flight is over: the shot is gone, or its wait has run out. The energy taken is read here.
        private bool EndFlightIfDone(int now)
        {
            bool removed = !flight.Shot.Spawned;
            if (!removed && now - flight.StartTick < ShotTicks) return false;
            flight.Removed = removed;
            flight.Observed = chargePerTick * (now - flight.StartTick) - (dome.energy - flight.StartEnergy);
            Log.Message("[ShieldValidation-diag] combat extended flight: removed " + removed + ", ticks " + (now - flight.StartTick)
                + ", energy " + flight.StartEnergy.ToString("0.###") + " to " + dome.energy.ToString("0.###")
                + ", charge per tick " + chargePerTick.ToString("0.#####") + ", observed loss " + flight.Observed.ToString("0.####")
                + ", last seen " + flight.LastPos + ", destination " + flight.Shot.Destination);
            return true;
        }

        private Flight Launch(Thing launcher, IntVec3 cell, int now)
        {
            var target = row.Generator.TrueCenter();
            var shot = (BulletCE)ThingMaker.MakeThing(BulletDef());
            // CE spawns the projectile before launching it, the same as CE's own verbs do.
            GenSpawn.Spawn(shot, cell, row.Generator.Map);
            var from = new Vector2(cell.x + 0.5f, cell.z + 0.5f);
            var to = new Vector2(target.x, target.z);
            // Vector2.up rotated by the shot rotation gives the direction of the shot, so this is the angle that points it at the dome.
            float rotation = Mathf.Atan2(-(to.x - from.x), to.y - from.y) * Mathf.Rad2Deg;
            shot.Launch(launcher, from, 0f, rotation, ShotHeight);
            var f = new Flight
            {
                Shot = shot,
                StartTick = now,
                StartEnergy = dome.energy,
                LastPos = shot.TrueCenter().ToVector2(),
            };
            f.LastCost = CostNow(shot);
            f.PrevCost = f.LastCost;
            Log.Message("[ShieldValidation-diag] combat extended shot: def " + shot.def.defName + ", launcher " + launcher.LabelShort
                + ", from " + from + " toward " + to + ", rotation " + rotation.ToString("0.0") + ", destination " + shot.Destination
                + ", dome radius " + dome.Radius + ", energy " + dome.energy.ToString("0.###") + ", max " + dome.MaxEnergy.ToString("0.###"));
            return f;
        }

        // Energy the dome would take for this shot, with the same rule the port's dome uses: the CE energy cost times the
        // building factor. Kinetic mode uses the speed and mass of the bullet. Otherwise the cost is the bullet's damage.
        private float CostNow(BulletCE shot)
        {
            var setting = dome.Setting;
            float cost;
            if (setting.UseKineticEnergy)
            {
                var speed = shot.def.projectile.SpeedTilesPerTick * 60 * setting.NormalKineticEnergyFactor;
                cost = Mathf.Pow(speed, 2) / 2f * shot.def.BaseMass;
            }
            else
            {
                cost = shot.DamageAmount;
            }
            return cost * setting.BuildingDmgGlobalFactor;
        }

        // A small, non-explosive CE bullet. An explosive shell breaks up into fragments, which CE then has to draw and tick, and
        // its cost (damage) can be larger than the dome's energy, so the dome would empty and the energy row could not hold.
        private static ThingDef BulletDef()
        {
            var def = DefDatabase<ThingDef>.GetNamed(BulletDefName, true);
            if (!typeof(BulletCE).IsAssignableFrom(def.thingClass) || def.projectile.flyOverhead || def.projectile.explosionRadius > 0f)
            {
                throw new InvalidOperationException(BulletDefName + " is not a plain ground Combat Extended bullet");
            }
            return def;
        }

        // The hostile launcher is a raider of a faction hostile to the player. It is not spawned, which CE allows for a launcher.
        private static Pawn HostileLauncher()
        {
            var hostile = DemoScene.FindHostileFaction();
            return PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(HostileKind, true), hostile);
        }

        // The friendly launcher is a colonist of the player's faction. It is not spawned either.
        private static Pawn FriendlyLauncher()
        {
            return PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(FriendlyKind, true), Faction.OfPlayer);
        }

        // Records a row once. The dependent rows fail together when the dome never comes up.
        private void Row(string scenario, string text)
        {
            if (recorded.Add(scenario)) ValidationSupport.Record(scenario, text);
        }

        private void FailAll(string reason)
        {
            Row(HostileScenario, "FAIL: " + reason);
            Row(EnergyScenario, "FAIL: " + reason);
            Row(FriendlyScenario, "FAIL: " + reason);
            Finish();
        }

        private void Finish()
        {
            ValidationSupport.Record(GroupComplete, "finished");
            finished = true;
        }
    }
}
