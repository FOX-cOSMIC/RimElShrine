using RimElShrine;
using RimElShrine.Hediffs.LimbsRegen;
using RimWorld;
using Verse;

namespace RimElShrine.Items.LimbsRegenInjector
{
    public class Comp_LimbsRegenInjector : CompUseEffect
    {
        protected CompProperties_LimbsRegenInjector Props => (CompProperties_LimbsRegenInjector)props;
        protected HediffDef ELS_Hediff_LimbsRegen => DefDatabase<HediffDef>.GetNamed(nameof(ELS_Hediff_LimbsRegen));
        public override void DoEffect(Pawn user)
        {
            var pawn = user;
            if (pawn != null)
            {
                if (Props.isCleaner)
                {
                    var hediff = pawn.health.GetOrAddHediff(ELS_Hediff_LimbsRegen);
                    if (hediff != null) hediff.Severity = 0;
                    if (PawnUtility.ShouldSendNotificationAbout(pawn))
                    {
                        Messages.Message("LimbsRegenInjector_ClearRegenEffectMsg".ES_Translate(pawn), pawn, MessageTypeDefOf.PositiveEvent);
                    }
                }
                else
                {
                    if (pawn.health.AddHediff(ELS_Hediff_LimbsRegen) is HediffWithComps_LimbsRegen hediff)
                    {
                        hediff.lastRegenCount = Props.lastRegenCount;
#if RIMWORLD_17
                        // Quick regen starts above 0.5 severity. A Fast injector sets the top severity, so the effect runs in quick mode on any pawn, bleeding or not.
                        if (Props.isFastRegen) hediff.Severity = 1f;
#endif
                    }
                    if (PawnUtility.ShouldSendNotificationAbout(pawn))
                    {
                        Messages.Message("LimbsRegenInjector_ApplyRegenEffectMsg".ES_Translate(pawn), pawn, MessageTypeDefOf.PositiveEvent);
                    }
                }
            }
        }
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            var disabledReason = Props.isCleaner ? "LimbsRegenInjector_DisabledReason_EffectNotExisted" : "LimbsRegenInjector_DisabledReason_EffectExisted";
            var result = true;
            if (p != null)
            {
                // Vanilla lookup, which only reads. The port's TryGetHediff sets the pawn's blood loss to 0 when the regen effect is absent.
                var existed = p.health.hediffSet.TryGetHediff(ELS_Hediff_LimbsRegen, out _);
                result = !(existed ^ Props.isCleaner);
            }
            if (!result) return disabledReason.ES_Translate();
            return result;
        }
    }
}
