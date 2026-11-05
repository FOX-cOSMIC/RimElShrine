using System;
using RimElShrine.Apparels.ApparelShield;
using RimWorld;
using Verse;

namespace ShieldValidation
{
    // Shield Equipment checks: an ES0 shield belt on a colonist. Runs only when the game starts with -shieldvalidation.
    public class EquipmentValidation : GameComponent
    {
        private const string BeltDef = "ELS_Thing_ES0Shield";
        private const float HitAmount = 2f;
        private const int RechargeTicks = 120;
        private const string ScenarioWorn = "equipment: colonist wears the ES belt";
        private const string ScenarioAbsorb = "equipment: ES belt absorbs a ranged hit and loses energy";
        private const string ScenarioEmpty = "equipment: ES belt with no energy lets a hit through";
        private const string ScenarioRecharge = "equipment: ES belt recharges while worn";
        private const string GroupComplete = "equipment group complete";

        private int stage;
        private int stageStart;
        private bool finished;
        private Pawn pawn;
        private Apparel belt;
        private Comp_ApparelShield shield;

        // RimWorld creates each GameComponent with a Game argument, so this constructor must take one.
        public EquipmentValidation(Game game)
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

        // Stage 0 puts the belt on a colonist with full energy. Stage 1 runs the hit checks. Stage 2 waits RechargeTicks
        // game ticks, then checks that the belt gained energy at its own rate while worn.
        private void Run(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (stage == 0)
            {
                Setup(map);
                stage = 1;
                return;
            }
            if (stage == 1)
            {
                CheckWorn();
                CheckAbsorb();
                CheckEmpty();
                stageStart = now;
                stage = 2;
                return;
            }
            if (now - stageStart < RechargeTicks) return;
            CheckRecharge(now - stageStart);
            ValidationSupport.Record(GroupComplete, "finished");
            finished = true;
        }

        private void Setup(Map map)
        {
            var cell = ValidationSupport.FindFreeBlock(map, 1, 1);
            if (cell == null) throw new InvalidOperationException("no free cell for the test colonist");
            pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Colonist", true), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, cell.Value, map);

            belt = ValidationSupport.Make(BeltDef) as Apparel;
            if (belt == null) throw new InvalidOperationException("the belt def does not make Apparel");
            shield = belt.GetComp<Comp_ApparelShield>();
            if (shield == null) throw new InvalidOperationException("the belt has no Comp_ApparelShield");
            shield.Energy = shield.EnergyMax;

            // Control runs pass -shieldvalidation-nowear, which leaves the belt off the colonist. The checks must then fail.
            if (GenCommandLine.CommandLineArgPassed("shieldvalidation-nowear")) return;
            pawn.apparel.DestroyAll();
            pawn.apparel.Wear(belt);
        }

        private void CheckWorn()
        {
            bool worn = belt.Wearer == pawn && pawn.apparel.WornApparel.Contains(belt);
            ValidationSupport.Record(ScenarioWorn, (worn ? "PASS: " : "FAIL: ") + "belt worn by the colonist: " + worn);
        }

        private void CheckAbsorb()
        {
            float before = shield.Energy;
            var dinfo = new DamageInfo(DefDatabase<DamageDef>.GetNamed("Bullet", true), HitAmount);
            shield.PostPreApplyDamage(ref dinfo, out bool absorbed);
            float expected = HitAmount * shield.Props.energyLossPerDamage * shield.Setting.DmgGlobalFactor;
            float lost = before - shield.Energy;
            bool pass = absorbed && shield.Energy >= 0f && Math.Abs(lost - expected) < 0.0001f;
            ValidationSupport.Record(ScenarioAbsorb, (pass ? "PASS: " : "FAIL: ") + "absorbed " + absorbed + ", energy lost " + lost.ToString("0.####") + ", expected " + expected.ToString("0.####"));
        }

        private void CheckEmpty()
        {
            shield.Energy = 0f;
            var dinfo = new DamageInfo(DefDatabase<DamageDef>.GetNamed("Bullet", true), HitAmount);
            shield.PostPreApplyDamage(ref dinfo, out bool absorbed);
            bool pass = !absorbed && shield.Energy == 0f;
            ValidationSupport.Record(ScenarioEmpty, (pass ? "PASS: " : "FAIL: ") + "absorbed " + absorbed + ", energy " + shield.Energy.ToString("0.####"));
        }

        private void CheckRecharge(int ticks)
        {
            float energy = shield.Energy;
            float expected = Math.Min(shield.EnergyMax, ticks * shield.EnergyGainPerTick);
            bool pass = expected > 0f && Math.Abs(energy - expected) <= Math.Max(0.01f, expected * 0.02f);
            ValidationSupport.Record(ScenarioRecharge, (pass ? "PASS: " : "FAIL: ") + "energy " + energy.ToString("0.####") + " after " + ticks + " ticks, expected " + expected.ToString("0.####"));
        }
    }
}
