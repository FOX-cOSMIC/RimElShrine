using System;
using System.Collections.Generic;
using System.Linq;
using RimElShrine;
using RimElShrine.Hediffs.LimbsRegen;
using RimElShrine.Items.LimbsRegenInjector;
using RimWorld;
using Verse;

namespace ShieldValidation
{
    // Mech Serum checks: the Regenerator series on six colonists, each missing one finger. Runs only when the game starts with -shieldvalidation.
    public class MechSerumValidation : GameComponent
    {
        private const string ScenarioDefs = "mech serum: recipes, injectors and research resolve";
        private const string ScenarioApply = "mech serum: regenerator injector adds the regen effect";
        private const string ScenarioRegrow = "mech serum: a missing finger regrows under the regen effect";
        private const string ScenarioCountEnds = "mech serum: regen effect ends when its count runs out";
        private const string ScenarioCleaner = "mech serum: cleaner stops a running regen effect";
        private const string ScenarioFastBleeding = "mech serum: fast regen speeds up regrowth while a pawn has blood loss";
        private const string ScenarioFastHealthy = "mech serum: fast regen speeds up regrowth on a pawn with no blood loss";
        private const string ScenarioUsability = "mech serum: injector usability check leaves blood loss alone";
        private const string ScenarioFastLabel = "mech serum: fast regen label on a healthy pawn does not read disabled";
        private const string ScenarioCapClears = "mech serum: fast regen cap label clears after blood loss ends";
        private const string ScenarioCapEdge = "mech serum: cap label holds at exactly the cap when the fast multiplier is 1";
        private const string GroupComplete = "mech serum group complete";
        private const string FingerDefName = "Finger";
        private const string BasicResearch = "ELS_ResearchProject_BasicMechSerumProduction";
        private const string AdvancedResearch = "ELS_ResearchProject_AdvancedMechSerumProduction";
        private const string CleanerDef = "ELS_Thing_LimbsRegenCleaner";
        private const string LimitedDef = "ELS_Thing_LimbsRegenInjectorLimited";
        private const string FastLimitedDef = "ELS_Thing_LimbsRegenInjectorFastLimited";
        private const string ForeverDef = "ELS_Thing_LimbsRegenInjectorForever";
        private const string FastForeverDef = "ELS_Thing_LimbsRegenInjectorFastForever";
        private const string RegenHediffDef = "ELS_Hediff_LimbsRegen";
        private const int TicksPerSecond = 60;
        private const int CleanerAtTicks = 6000;
        private const float DeadlineFactor = 1.25f;
        private const float WindowLow = 0.8f;
        private const float WindowHigh = 1.1f;
        private const int FrameBudgetMs = 25;
        // Quick regen stops once blood loss reaches QuickRegenBloodLossMax, 0.35. The pawn with blood loss starts at 0.1, under that cap.
        // The test sets blood loss directly, because that is the state the regen code reads. It does not model a wound.
        private const float BleedingSeverity = 0.1f;

        // Patient A regrows under a Limited injector (count 3). B regrows with its count set to 1. C is cleaned while its effect runs.
        // D gets a Fast injector. D is a diagnostic only, not a required row.
        // E has blood loss and gets a Fast injector. F has none and gets a Fast injector.
        // G has blood loss and gets no injector. The usability checks of both injectors run on it.
        private sealed class Patient
        {
            public Pawn Colonist;
            public BodyPartRecord Finger;
            public int RegrownAt = -1;
            public bool BleedingAtRegrow;
        }

        private static DateTime lastClock = DateTime.MinValue;
        private int stage;
        private int stageStart;
        private bool finished;
        private Patient a;
        private Patient b;
        private Patient c;
        private Patient d;
        private Patient e;
        private Patient f;
        private Patient h;
        private Patient j;
        private float expectedTicks;
        private float expectedFastTicks;
        private bool fastQuickAtApply;
        private bool cleanerDone;
        private bool cleanerHadEffect;
        private bool capRecovered;
        private float capBloodLoss;
        private bool capShownAtEnd;
        private bool capLabelAtEnd;
        private bool capTipAtEnd;

        // RimWorld creates each GameComponent with a Game argument, so this constructor must take one.
        public MechSerumValidation(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (finished || !GenCommandLine.CommandLineArgPassed("shieldvalidation")) return;
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            var map = Find.CurrentMap;
            if (map == null) return;
            if (DateTime.UtcNow - lastClock > TimeSpan.FromSeconds(2))
            {
                lastClock = DateTime.UtcNow;
                Log.Message("[ShieldValidation-diag] clock: ticks " + Find.TickManager.TicksGame + ", speed " + Find.TickManager.CurTimeSpeed + ", multiplier " + Find.TickManager.TickRateMultiplier + ", forced normal " + Find.TickManager.slower.ForcedNormalSpeed + ", deltaTime " + UnityEngine.Time.deltaTime + ", player has control " + Current.Game.PlayerHasControl + ", landing confirmation pending " + (Find.GravshipController != null && Find.GravshipController.LandingAreaConfirmationInProgress));
            }
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

        // Stage 0 checks the defs, spawns the patients, and applies the injectors. Stage 1 advances the game one tick at a time and
        // checks after every tick: the cleaner goes on at CleanerAtTicks, each finger is timed when it comes back, and the checks
        // run at the deadline. The speed setter is locked in this run, so the game would tick at real time (60 ticks a second).
        // Stage 1 calls the game's own per-tick function instead, for up to FrameBudgetMs in each frame.
        private void Run(Map map)
        {
            if (stage == 0)
            {
                Setup(map);
                stageStart = Find.TickManager.TicksGame;
                stage = 1;
                return;
            }
            var budget = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                int elapsed = Find.TickManager.TicksGame - stageStart;
                if (!cleanerDone && elapsed >= CleanerAtTicks) ApplyCleaner();
                // H's blood loss is held above the cap until it is removed below. It stands in for a wound that keeps bleeding.
                if (!capRecovered && h.Colonist.health.hediffSet.TryGetHediff(HediffDefOf.BloodLoss, out var held)) held.Severity = capBloodLoss;
                foreach (var p in new[] { a, b, c, d, e, f, h })
                {
                    if (p.RegrownAt < 0 && !p.Colonist.health.hediffSet.PartIsMissing(p.Finger))
                    {
                        p.RegrownAt = elapsed;
                        p.BleedingAtRegrow = p.Colonist.health.hediffSet.TryGetHediff(HediffDefOf.BloodLoss, out _);
                    }
                }
                // One tick after H's finger is back, with H's blood loss still held above the cap, the cap label must show. Then H's blood loss ends.
                if (!capRecovered && h.RegrownAt >= 0 && elapsed > h.RegrownAt)
                {
                    var regenNow = Regen(h.Colonist);
                    capLabelAtEnd = regenNow != null && regenNow.LabelInBrackets.Contains(CapLabelText());
                    capTipAtEnd = regenNow != null && regenNow.TipStringExtra.Contains(CapTipText());
                    capShownAtEnd = capLabelAtEnd && capTipAtEnd;
                    if (h.Colonist.health.hediffSet.TryGetHediff(HediffDefOf.BloodLoss, out var bloodLeft)) h.Colonist.health.RemoveHediff(bloodLeft);
                    capRecovered = true;
                }
                if (elapsed >= DeadlineTicks()) break;
                if (budget.ElapsedMilliseconds >= FrameBudgetMs) return;
                Find.TickManager.DoSingleTick();
            }
            FinishChecks();
            ValidationSupport.Record(GroupComplete, "finished");
            finished = true;
        }

        private void Setup(Map map)
        {
            CheckDefs();
            a = NewPatient(map);
            b = NewPatient(map);
            c = NewPatient(map);
            d = NewPatient(map);
            e = NewPatient(map);
            f = NewPatient(map);
            var setting = RimElShrine.Settings.GetSetting<MechserumSetting>();
            expectedTicks = a.Finger.def.GetMaxHealth(a.Colonist) * setting.LimbsRegenSecondsPerHealth * TicksPerSecond;
            expectedFastTicks = expectedTicks / setting.QuickRegenFactor;

            // Control runs pass -shieldvalidation-noserum, which applies no injector. The checks after the apply step must then fail.
            bool noSerum = GenCommandLine.CommandLineArgPassed("shieldvalidation-noserum");
            var limited = Injector(LimitedDef);
            bool usableBefore = limited.CanBeUsedBy(a.Colonist).Accepted;
            if (!noSerum) limited.DoEffect(a.Colonist);
            bool usableAfter = limited.CanBeUsedBy(a.Colonist).Accepted;
            var regenA = Regen(a.Colonist);
            bool applied = regenA != null && regenA.lastRegenCount == InjectorCount(LimitedDef);
            bool passApply = usableBefore && !usableAfter && applied;
            ValidationSupport.Record(ScenarioApply, (passApply ? "PASS: " : "FAIL: ") + "usable before " + usableBefore + ", usable after " + usableAfter + ", remaining " + Remaining(regenA));

            // The usability checks run on a bleeding pawn with no regen effect. Neither check may change its blood loss.
            // The pawn is missing a finger like the others, but no injector goes on it, and the checks do not read its parts.
            var usabilityPawn = NewPatient(map).Colonist;
            var usabilityBleeding = usabilityPawn.health.AddHediff(HediffDefOf.BloodLoss);
            usabilityBleeding.Severity = BleedingSeverity;
            bool usableRegen = Injector(LimitedDef).CanBeUsedBy(usabilityPawn).Accepted;
            bool usableCleaner = Injector(CleanerDef).CanBeUsedBy(usabilityPawn).Accepted;
            bool keptBlood = Math.Abs(usabilityBleeding.Severity - BleedingSeverity) < 0.0001f;
            bool passUsability = usableRegen && !usableCleaner && keptBlood;
            ValidationSupport.Record(ScenarioUsability, (passUsability ? "PASS: " : "FAIL: ") + "regen injector usable " + usableRegen + ", cleaner usable " + usableCleaner + ", blood loss before " + BleedingSeverity.ToString("0.###") + ", after " + usabilityBleeding.Severity.ToString("0.###"));

            // H is at the blood loss cap when its fast regen goes on. It is held just above the cap until its blood loss ends, so natural
            // recovery cannot take it under the cap between two ticks. Once its finger is back and one tick has passed, its blood loss ends.
            // The cap label must show during that tick and clear after it.
            h = NewPatient(map);
            var capBleeding = h.Colonist.health.AddHediff(HediffDefOf.BloodLoss);
            capBloodLoss = setting.QuickRegenBloodLossMax + 0.01f;
            capBleeding.Severity = capBloodLoss;
            if (!noSerum) Injector(FastLimitedDef).DoEffect(h.Colonist);

            // J gets a Fast injector and blood loss. Its blood loss is set to exactly the cap in FinishChecks, after the other rows, when the multiplier is 1.
            j = NewPatient(map);
            j.Colonist.health.AddHediff(HediffDefOf.BloodLoss).Severity = setting.QuickRegenBloodLossMax;
            if (!noSerum) Injector(FastLimitedDef).DoEffect(j.Colonist);

            // E has blood loss before its injector goes on, so the quick branch has blood loss to spend. F has none.
            var bleeding = e.Colonist.health.AddHediff(HediffDefOf.BloodLoss);
            bleeding.Severity = BleedingSeverity;
            if (!noSerum)
            {
                Injector(LimitedDef).DoEffect(b.Colonist);
                var regenB = Regen(b.Colonist);
                if (regenB != null) regenB.lastRegenCount = 1;
                Injector(ForeverDef).DoEffect(c.Colonist);
                Injector(FastForeverDef).DoEffect(d.Colonist);
                Injector(FastLimitedDef).DoEffect(e.Colonist);
                Injector(FastLimitedDef).DoEffect(f.Colonist);
            }
            var regenE = Regen(e.Colonist);
            fastQuickAtApply = regenE != null && regenE.IsQuickRegen;
            LogFastRegen("applied");
        }

        private void ApplyCleaner()
        {
            cleanerDone = true;
            var regenC = Regen(c.Colonist);
            cleanerHadEffect = regenC != null && regenC.secondsCount > 0f;
            Injector(CleanerDef).DoEffect(c.Colonist);
        }

        private void FinishChecks()
        {
            var regenA = Regen(a.Colonist);
            bool passRegrow = InWindow(a.RegrownAt) && regenA != null && regenA.lastRegenCount == InjectorCount(LimitedDef) - 1;
            ValidationSupport.Record(ScenarioRegrow, (passRegrow ? "PASS: " : "FAIL: ") + Timing(a.RegrownAt) + ", remaining " + Remaining(regenA));

            bool endedB = Regen(b.Colonist) == null;
            bool passCount = InWindow(b.RegrownAt) && endedB;
            ValidationSupport.Record(ScenarioCountEnds, (passCount ? "PASS: " : "FAIL: ") + Timing(b.RegrownAt) + ", effect removed " + endedB);

            bool endedC = Regen(c.Colonist) == null;
            bool stillMissing = c.RegrownAt < 0;
            bool passCleaner = cleanerHadEffect && endedC && stillMissing;
            ValidationSupport.Record(ScenarioCleaner, (passCleaner ? "PASS: " : "FAIL: ") + "effect running before the cleaner " + cleanerHadEffect + ", effect removed " + endedC + ", finger still missing " + stillMissing);

            bool bleedingAtRegrow = e.BleedingAtRegrow;
            bool passFast = fastQuickAtApply && InFastWindow(e.RegrownAt) && bleedingAtRegrow;
            ValidationSupport.Record(ScenarioFastBleeding, (passFast ? "PASS: " : "FAIL: ") + "quick regen on after the injector " + fastQuickAtApply + ", " + FastTiming(e.RegrownAt) + ", blood loss when the finger came back " + bleedingAtRegrow);

            // F starts with no blood loss. A Fast pawn with none must regrow in the fast window, and it must have blood loss when the finger
            // comes back. That blood loss is the cost of fast regen.
            bool passHealthy = Regen(f.Colonist) != null && InFastWindow(f.RegrownAt) && f.BleedingAtRegrow;
            ValidationSupport.Record(ScenarioFastHealthy, (passHealthy ? "PASS: " : "FAIL: ") + FastTiming(f.RegrownAt) + ", blood loss when the finger came back " + f.BleedingAtRegrow);

            // F's label must show fast regen on, and it must not show the blood loss cap.
            var regenLabel = Regen(f.Colonist);
            string label = regenLabel == null ? string.Empty : regenLabel.LabelInBrackets;
            string tip = regenLabel == null ? string.Empty : regenLabel.TipStringExtra;
            string fastText = "RegenQuick".ES_Translate().RawText;
            string capText = "CannotRegenQuick".ES_Translate().RawText;
            string capTip = "CannotRegenQuickTip".ES_Translate().RawText;
            bool capShown = label.Contains(capText) || tip.Contains(capTip);
            bool passLabel = regenLabel != null && label.Contains(fastText) && !capShown;
            ValidationSupport.Record(ScenarioFastLabel, (passLabel ? "PASS: " : "FAIL: ") + "label [" + label + "], blood loss cap shown " + capShown);

            // H's blood loss has ended. Its label must not show the cap, and its blood loss must stay gone.
            var regenH = Regen(h.Colonist);
            string labelH = regenH == null ? string.Empty : regenH.LabelInBrackets;
            string tipH = regenH == null ? string.Empty : regenH.TipStringExtra;
            bool capShownH = labelH.Contains(capText) || tipH.Contains(capTip);
            bool bloodLeftH = h.Colonist.health.hediffSet.TryGetHediff(HediffDefOf.BloodLoss, out _);
            bool passCap = capShownAtEnd && capRecovered && regenH != null && labelH.Contains(fastText) && !capShownH && !bloodLeftH;
            ValidationSupport.Record(ScenarioCapClears, (passCap ? "PASS: " : "FAIL: ") + "cap shown while held above the cap " + capShownAtEnd + " (label " + capLabelAtEnd + ", tip " + capTipAtEnd + "), label [" + labelH + "], blood loss cap shown " + capShownH + ", blood loss ended " + capRecovered + ", blood loss left " + bloodLeftH);

            LogFastRegen("at the end");

            // J is at exactly the cap and the multiplier is 1, so a quick step adds nothing. The cap label must still show. This runs
            // last, so the multiplier change reaches no other row. The multiplier goes back afterwards.
            var regenJ = Regen(j.Colonist);
            var settingJ = RimElShrine.Settings.GetSetting<MechserumSetting>();
            float savedFactor = settingJ.QuickRegenFactor;
            bool clearBefore = false;
            bool bloodBefore = false;
            bool exactAtCap = false;
            bool capAtEdge = false;
            if (regenJ != null)
            {
                clearBefore = !regenJ.LabelInBrackets.Contains(CapLabelText()) && !regenJ.TipStringExtra.Contains(CapTipText());
                if (j.Colonist.health.hediffSet.TryGetHediff(HediffDefOf.BloodLoss, out var edgeBleeding))
                {
                    bloodBefore = true;
                    edgeBleeding.Severity = settingJ.QuickRegenBloodLossMax;
                    exactAtCap = edgeBleeding.Severity == settingJ.QuickRegenBloodLossMax;
                }
                try
                {
                    settingJ.QuickRegenFactor = 1f;
                    regenJ.Tick();
                    capAtEdge = regenJ.LabelInBrackets.Contains(CapLabelText()) && regenJ.TipStringExtra.Contains(CapTipText());
                }
                finally
                {
                    settingJ.QuickRegenFactor = savedFactor;
                }
            }
            bool passEdge = regenJ != null && clearBefore && bloodBefore && exactAtCap && capAtEdge;
            ValidationSupport.Record(ScenarioCapEdge, (passEdge ? "PASS: " : "FAIL: ") + "clear before " + clearBefore + ", blood loss before " + bloodBefore + ", exactly at the cap " + exactAtCap + ", cap label and tip after the tick " + capAtEdge + ", label [" + (regenJ == null ? "no regen effect" : regenJ.LabelInBrackets) + "]");
        }

        // Diagnostic for the Fast injector. It logs what the game does with the Fast flag, and is not a required row.
        private void LogFastRegen(string when)
        {
            var regen = Regen(d.Colonist);
            float severity = regen == null ? -1f : regen.Severity;
            bool quick = regen != null && regen.IsQuickRegen;
            bool bloodLoss = d.Colonist.health.hediffSet.TryGetHediff(HediffDefOf.BloodLoss, out _);
            Log.Message("[ShieldValidation-diag] fast regen " + when + ": severity " + severity.ToString("0.###") + ", quick " + quick + ", blood loss hediff " + bloodLoss + ", finger back at " + d.RegrownAt + " ticks");
        }

        private int DeadlineTicks()
        {
            return (int)(expectedTicks * DeadlineFactor);
        }

        private bool InWindow(int at)
        {
            return at >= 0 && at >= WindowLow * expectedTicks && at <= WindowHigh * expectedTicks;
        }

        private bool InFastWindow(int at)
        {
            return at >= 0 && at >= WindowLow * expectedFastTicks && at <= WindowHigh * expectedFastTicks;
        }

        private string FastTiming(int at)
        {
            if (at < 0) return "finger still missing at the deadline";
            return "finger back at " + at + " ticks, fast window " + (int)(WindowLow * expectedFastTicks) + " to " + (int)(WindowHigh * expectedFastTicks);
        }

        private string Timing(int at)
        {
            if (at < 0) return "finger still missing at the deadline";
            return "finger back at " + at + " ticks, window " + (int)(WindowLow * expectedTicks) + " to " + (int)(WindowHigh * expectedTicks);
        }

        private static string Remaining(HediffWithComps_LimbsRegen regen)
        {
            return regen == null ? "no effect" : regen.lastRegenCount.ToString();
        }

        private static string CapLabelText() => "CannotRegenQuick".ES_Translate().RawText;
        private static string CapTipText() => "CannotRegenQuickTip".ES_Translate().RawText;

        private static Patient NewPatient(Map map)
        {
            var cell = ValidationSupport.FindFreeBlock(map, 1, 1);
            if (cell == null) throw new InvalidOperationException("no free cell for a test colonist");
            var pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Colonist", true), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, cell.Value, map);
            var finger = pawn.RaceProps.body.AllParts.First(part => part.def.defName == FingerDefName);
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, finger);
            return new Patient { Colonist = pawn, Finger = finger };
        }

        private static Comp_LimbsRegenInjector Injector(string defName)
        {
            var thing = ValidationSupport.Make(defName) as ThingWithComps;
            var comp = thing == null ? null : thing.GetComp<Comp_LimbsRegenInjector>();
            if (comp == null) throw new InvalidOperationException(defName + " has no regen injector comp");
            return comp;
        }

        private static int InjectorCount(string defName)
        {
            return DefDatabase<ThingDef>.GetNamed(defName, true).comps.OfType<CompProperties_LimbsRegenInjector>().First().lastRegenCount;
        }

        private static HediffWithComps_LimbsRegen Regen(Pawn pawn)
        {
            return pawn.health.hediffSet.TryGetHediff(DefDatabase<HediffDef>.GetNamed(RegenHediffDef, true), out Hediff hediff) ? hediff as HediffWithComps_LimbsRegen : null;
        }

        // Checks that the recipes, injectors, and research resolve as the Workshop describes. Writes one row.
        private static void CheckDefs()
        {
            var problems = new List<string>();
            CheckRecipe("ELS_Recipe_Make_MechSerumHealer", "MechSerumHealer", problems);
            CheckRecipe("ELS_Recipe_Make_MechSerumResurrector", "MechSerumResurrector", problems);
            if (DefDatabase<ThingDef>.GetNamedSilentFail("ELS_Thing_EmptyInjector") == null) problems.Add("ELS_Thing_EmptyInjector is missing");
            CheckInjector(CleanerDef, true, -1, false, BasicResearch, problems);
            CheckInjector(LimitedDef, false, 3, false, BasicResearch, problems);
            CheckInjector(FastLimitedDef, false, 3, true, BasicResearch, problems);
            CheckInjector(ForeverDef, false, -1, false, AdvancedResearch, problems);
            CheckInjector(FastForeverDef, false, -1, true, AdvancedResearch, problems);
            var basic = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(BasicResearch);
            var advanced = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(AdvancedResearch);
            if (basic == null) problems.Add(BasicResearch + " is missing");
            if (advanced == null) problems.Add(AdvancedResearch + " is missing");
            else if (basic != null && !advanced.prerequisites.Contains(basic)) problems.Add("advanced research does not need basic research");
            ValidationSupport.Record(ScenarioDefs, problems.Count == 0
                ? "PASS: both serum recipes make their product from the empty serum, five regen injectors load with their flags, research tiers chain"
                : "FAIL: " + string.Join("; ", problems));
        }

        private static void CheckRecipe(string recipeDef, string productDef, List<string> problems)
        {
            var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDef);
            if (recipe == null) { problems.Add(recipeDef + " is missing"); return; }
            if (!recipe.products.Any(p => p.thingDef != null && p.thingDef.defName == productDef)) problems.Add(recipeDef + " does not make " + productDef);
            if (!recipe.ingredients.Any(i => i.filter.AllowedThingDefs.Any(t => t.defName == "ELS_Thing_EmptyInjector"))) problems.Add(recipeDef + " does not use the empty serum");
        }

        private static void CheckInjector(string defName, bool cleaner, int count, bool fast, string research, List<string> problems)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) { problems.Add(defName + " is missing"); return; }
            var props = def.comps.OfType<CompProperties_LimbsRegenInjector>().FirstOrDefault();
            if (props == null) { problems.Add(defName + " has no regen injector comp"); return; }
            if (props.isCleaner != cleaner || props.lastRegenCount != count || props.isFastRegen != fast) problems.Add(defName + " has the wrong flags");
            var recipeResearch = def.recipeMaker == null ? null : def.recipeMaker.researchPrerequisite;
            if (recipeResearch == null || recipeResearch.defName != research) problems.Add(defName + " does not need " + research);
        }
    }
}
