using System;
using System.IO;
using System.Linq;
using RimElShrine.Buildings.ProjInterceptorNet;
using RimWorld;
using Verse;

namespace ShieldValidation
{
    // Runs the shield network checks in a new colony. It runs only when the game starts with -shieldvalidation.
    // Results go to validation-results.txt in the run's save folder, and each row is also logged as [ShieldValidation].
    public class NetworkValidation : GameComponent
    {
        private const int SettleTicks = 600;
        private const int AllocateCount = 2;
        private const string GeneratorDef = "ELS_Thing_Building_ShieldNet_Shield";
        private const string TerminalDef = "ELS_Thing_Building_ShieldNet_Terminal";
        private const string ServerDef = "ELS_Thing_Building_ShieldNet_ServerUnit";
        private const string ScenarioJoin = "network: parts join one shield network";
        private const string ScenarioAllocate = "network: terminal allocation reaches the network";
        private const string ScenarioNoPower = "network: no power, no upgrade value";

        private static bool started;
        private int stage;
        private int stageStart;
        private bool finished;
        private ThingWithComps generator;
        private ThingWithComps terminal;
        private ThingWithComps server;
        private ThingWithComps battery;
        private Comp_ShieldNet_ProjInterceptorELS shieldComp;
        private Comp_ShieldNet_Terminal terminalComp;
        private Comp_ShieldNet_ServerUnit serverComp;

        // RimWorld creates each GameComponent with a Game argument, so this constructor must take one.
        public NetworkValidation(Game game)
        {
        }

        private static DateTime lastNote = DateTime.MinValue;

        public override void GameComponentUpdate()
        {
            if (finished || !GenCommandLine.CommandLineArgPassed("shieldvalidation")) return;
            // Diagnostic lines use their own prefix, so the runner does not count them as result markers.
            if (DateTime.UtcNow - lastNote > TimeSpan.FromSeconds(10))
            {
                lastNote = DateTime.UtcNow;
                Log.Message("[ShieldValidation-diag] waiting: state " + Current.ProgramState + ", map " + (Find.CurrentMap != null) + ", events " + LongEventHandler.AnyEventNowOrWaiting);
            }
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            var map = Find.CurrentMap;
            if (map == null) return;
            try
            {
                Run(map);
            }
            catch (Exception e)
            {
                Record("run complete", "FAIL: exception in stage " + stage + ": " + e.GetType().Name + ": " + e.Message);
                finished = true;
            }
        }

        // Called on every frame of play. Stage 0 places the parts and powers them. Stage 1 checks them, then takes the power away.
        // Stage 2 checks again. Each stage waits SettleTicks game ticks, so the game's own network updates run first.
        private void Run(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (stage == 0)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                Setup(map);
                stageStart = now;
                stage = 1;
                return;
            }
            if (now - stageStart < SettleTicks) return;
            if (stage == 1)
            {
                CheckJoin();
                CheckAllocate();
                battery.DeSpawn();
                stageStart = now;
                stage = 2;
                return;
            }
            CheckNoPower();
            Record("run complete", "finished");
            finished = true;
        }

        private void Setup(Map map)
        {
            var origin = FindOrigin(map);
            if (origin == null) throw new InvalidOperationException("no free row of 9 cells for the test layout");
            var o = origin.Value;
            battery = Spawn("Battery", o, map, Rot4.East);
            Spawn("PowerConduit", o + IntVec3.East * 2, map, Rot4.North);
            generator = Spawn(GeneratorDef, o + IntVec3.East * 3, map, Rot4.North);
            Spawn("PowerConduit", o + IntVec3.East * 5, map, Rot4.North);
            terminal = Spawn(TerminalDef, o + IntVec3.East * 6, map, Rot4.North);
            Spawn("PowerConduit", o + IntVec3.East * 7, map, Rot4.North);
            server = Spawn(ServerDef, o + IntVec3.East * 8, map, Rot4.North);

            var batteryComp = battery.GetComp<CompPowerBattery>();
            if (batteryComp == null) throw new InvalidOperationException("the battery has no CompPowerBattery");
            // Control runs pass -shieldvalidation-nopower, which leaves the battery empty. The allocation check must then fail.
            if (!GenCommandLine.CommandLineArgPassed("shieldvalidation-nopower")) batteryComp.AddEnergy(1000f);

            shieldComp = generator.GetComp<Comp_ShieldNet_ProjInterceptorELS>();
            terminalComp = terminal.GetComp<Comp_ShieldNet_Terminal>();
            serverComp = server.GetComp<Comp_ShieldNet_ServerUnit>();
            if (shieldComp == null || terminalComp == null || serverComp == null)
                throw new InvalidOperationException("a shield part is missing its network comp");
        }

        private void CheckJoin()
        {
            var net = shieldComp.ShieldNet;
            bool sameNet = net != null && ReferenceEquals(net, terminalComp.ShieldNet) && ReferenceEquals(net, serverComp.ShieldNet);
            int shields = net == null ? 0 : net.shields.Count;
            int terminals = net == null ? 0 : net.terminals.Count;
            int servers = net == null ? 0 : net.serverUnits.Count;
            bool members = shields == 1 && terminals == 1 && servers == 1;
            string detail = "shields " + shields + ", terminals " + terminals + ", server units " + servers + ", one net for all: " + sameNet;
            Record(ScenarioJoin, (sameNet && members ? "PASS: " : "FAIL: ") + detail);
        }

        private void CheckAllocate()
        {
            bool allocated = serverComp.TryAllocateResource(ShieldUpgradeResource.MaxRadius, AllocateCount, out int remained);
            var net = terminalComp.ShieldNet;
            int resources = net == null ? -1 : net.GetUpgradeResources(ShieldUpgradeResource.MaxRadius);
            float value = net == null ? -1f : net.GetUpgradeValue(ShieldUpgradeResource.MaxRadius);
            float expected = ShieldUpgradeResource.MaxRadius.ToUpgradeValue(AllocateCount);
            bool pass = allocated && remained == 0 && resources == AllocateCount && Math.Abs(value - expected) < 0.0001f;
            string detail = "allocated " + AllocateCount + ", network holds " + resources + ", value " + value.ToString("0.###") + " expected " + expected.ToString("0.###");
            Record(ScenarioAllocate, (pass ? "PASS: " : "FAIL: ") + detail);
        }

        private void CheckNoPower()
        {
            var net = terminalComp.ShieldNet;
            float value = net == null ? -1f : net.GetUpgradeValue(ShieldUpgradeResource.MaxRadius);
            bool pass = value == 0f;
            Record(ScenarioNoPower, (pass ? "PASS: " : "FAIL: ") + "upgrade value with no power " + value.ToString("0.###"));
        }

        private static IntVec3? FindOrigin(Map map)
        {
            for (int z = 6; z < map.Size.z - 6; z++)
            {
                for (int x = 6; x < map.Size.x - 14; x++)
                {
                    bool free = true;
                    for (int dx = 0; dx <= 8 && free; dx++) free = IsFree(map, new IntVec3(x + dx, 0, z));
                    for (int dx = 3; dx <= 4 && free; dx++) free = IsFree(map, new IntVec3(x + dx, 0, z + 1));
                    if (free) return new IntVec3(x, 0, z);
                }
            }
            return null;
        }

        private static bool IsFree(Map map, IntVec3 cell)
            => cell.InBounds(map) && cell.Walkable(map) && !cell.Filled(map) && !map.thingGrid.ThingsAt(cell).Any();

        private static ThingWithComps Spawn(string defName, IntVec3 cell, Map map, Rot4 rot)
        {
            var def = DefDatabase<ThingDef>.GetNamed(defName, true);
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            return (ThingWithComps)GenSpawn.Spawn(thing, cell, map, rot);
        }

        private static void Record(string scenario, string result)
        {
            var line = DateTime.UtcNow.ToString("O") + " | " + scenario + " | " + result;
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "validation-results.txt"), line + Environment.NewLine);
            Log.Message("[ShieldValidation] " + line);
        }
    }
}
