using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace ShieldValidation
{
    // Helpers shared by the scenario groups. Results go to validation-results.txt in the run's save folder,
    // and each row is also logged as [ShieldValidation], which the runner counts against the file.
    public static class ValidationSupport
    {
        private static readonly HashSet<string> completedGroups = new HashSet<string>();

        // True when every named group has written its completion row. A group can wait on the others, so that its own
        // events (the Combat Extended shots, for one) do not reach the colonists another group is checking.
        public static bool GroupsCompleted(params string[] names) => names.All(completedGroups.Contains);
        public static bool IsFree(Map map, IntVec3 cell)
            => cell.InBounds(map) && cell.Walkable(map) && !cell.Filled(map) && !map.thingGrid.ThingsAt(cell).Any();

        // Finds the lowest-left corner of a free width x height block.
        public static IntVec3? FindFreeBlock(Map map, int width, int height)
        {
            for (int z = 6; z < map.Size.z - height - 6; z++)
            {
                for (int x = 6; x < map.Size.x - width - 6; x++)
                {
                    bool free = true;
                    for (int dz = 0; dz < height && free; dz++)
                        for (int dx = 0; dx < width && free; dx++)
                            free = IsFree(map, new IntVec3(x + dx, 0, z + dz));
                    if (free) return new IntVec3(x, 0, z);
                }
            }
            return null;
        }

        public static Thing Make(string defName)
        {
            var def = DefDatabase<ThingDef>.GetNamed(defName, true);
            return ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        }

        public static ThingWithComps Spawn(string defName, IntVec3 cell, Map map, Rot4 rot)
            => (ThingWithComps)GenSpawn.Spawn(Make(defName), cell, map, rot);

        public static void Record(string scenario, string result)
        {
            if (scenario.EndsWith(" group complete", StringComparison.Ordinal)) completedGroups.Add(scenario);
            var line = DateTime.UtcNow.ToString("O") + " | " + scenario + " | " + result;
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "validation-results.txt"), line + Environment.NewLine);
            Log.Message("[ShieldValidation] " + line);
        }

        private static bool unnamedControlWritten;

        // Control runs pass -shieldvalidation-unnamedrow. The row it writes is named by no runner list and reads FAIL,
        // so the runner must fail the run. It is written once per game session.
        public static void WriteUnnamedControlRow()
        {
            if (unnamedControlWritten || !GenCommandLine.CommandLineArgPassed("shieldvalidation-unnamedrow")) return;
            unnamedControlWritten = true;
            Record("control: unnamed row", "FAIL: deliberate control row that no runner list names");
        }
    }
}
