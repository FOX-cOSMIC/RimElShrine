using RimElShrine.Data;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimElShrine.Hediffs.LimbsRegen
{
    public class HediffWithComps_LimbsRegen : HediffWithComps
    {
        public MechserumSetting Setting => Settings.GetSetting<MechserumSetting>();
        public override string LabelInBrackets
            => base.LabelInBrackets +
            (IsQuickRegen ? (reachedMaxBloodLoss ? $"{"CannotRegenQuick".ES_Translate()}, " : $"{"RegenQuick".ES_Translate()}, ") : string.Empty) +
            (TargetMissingPartHediff is null ? string.Empty : $"{(secondsCount / targetPartRegenSecondsNeed * 100):F0}%, ") +
            (lastRegenCount < 0 ? "PermanentRegenBuffLabel".ES_Translate() : ("LastRegenCountLabel".ES_Translate() + $": {lastRegenCount}")).RawText;
        public override string TipStringExtra
        {
            get
            {
                var str = base.LabelInBrackets;
                if (TargetMissingPartHediff != null)
                {
                    str += $"{"RegenPartNoticeLabel".ES_Translate()}: ";
                    var bodyPartRecord = TargetMissingPartHediff.GetHediffBodyPartRecord();
                    str += bodyPartRecord.def.label.TryTranslate(out var result) ? result.RawText : bodyPartRecord.def.label;
                }
                str += reachedMaxBloodLoss ? $"\n\n{"CannotRegenQuickTip".ES_Translate()}" : string.Empty;
                str += (IsQuickRegen ? $"\n\n{"RegenQuickTip".ES_Translate()}" : string.Empty);
                return str;
            }
        }

        [ExposeData] public float secondsCount = 0f;

        public const int ticksToRetryAll = 60;
        [ExposeData] public int ticksToRetry = -1;
        [ExposeData] public int lastRegenCount = -1;
        [ExposeData] private int pastTicks = 0;
        private const int updateIntervalTicks = 30;
        public bool IsQuickRegen => Severity > 0.5f;

        private Hediff_MissingPart? _targetMissingPart = null;
        protected Hediff_MissingPart? TargetMissingPartHediff
        {
            get => _targetMissingPart;
            set
            {
                if (_targetMissingPart == value) return;
                _targetMissingPart = value;
                pawn.health.Notify_HediffChanged(this);
            }
        }
        protected float TargetPartMaxHealth = -1;
        protected float targetPartRegenSecondsNeed = 0f;

        public override void ExposeData()
        {
            base.ExposeData();
            this.ExposeAttributedData();
        }
        [ExposeData] private bool reachedMaxBloodLoss = false;
        public override void Tick()
        {
            base.Tick();
            if (TargetMissingPartHediff == null && ticksToRetry-- < 0)
            {
                if (TryGetMissingPart(out var missingPart, out TargetPartMaxHealth))
                {
                    ticksToRetry = -1;
                    TargetMissingPartHediff = missingPart;
                }
                else ticksToRetry = ticksToRetryAll;
            }
            if (TargetMissingPartHediff != null && TargetPartMaxHealth > 0)
            {
                targetPartRegenSecondsNeed = TargetPartMaxHealth * Setting.LimbsRegenSecondsPerHealth;
                if (targetPartRegenSecondsNeed < secondsCount)
                {
                    //re validate
                    if (TryGetMissingPart(out var regetPart, out var regetMaxHealth) && regetPart == TargetMissingPartHediff)
                    {
                        secondsCount -= TargetPartMaxHealth * Setting.LimbsRegenSecondsPerHealth;
                        //heal
                        pawn.health.RemoveHediff(TargetMissingPartHediff);
                        TargetMissingPartHediff = null;
                        lastRegenCount--;
                        if (lastRegenCount == 0) Severity = 0;
                    }
                    else
                    {
                        TargetMissingPartHediff = regetPart;
                        TargetPartMaxHealth = regetMaxHealth;
                    }
                }
                if (pastTicks++ > updateIntervalTicks)
                {
                    pastTicks -= updateIntervalTicks;
                    var incresement = updateIntervalTicks.TicksToSeconds();
                    if (IsQuickRegen)
                    {
                        // Vanilla lookup. The port's TryGetHediff would create a zero-severity bleed on a healthy pawn.
                        pawn.health.hediffSet.TryGetHediff(HediffDefOf.BloodLoss, out var bloodLossHediff);
                        var additionalIncresement = incresement * (Setting.QuickRegenFactor - 1);
                        if (bloodLossHediff is not null && (bloodLossHediff.Severity < Setting.QuickRegenBloodLossMax))
                        {
                            var loss = additionalIncresement / Setting.LimbsRegenSecondsPerHealth * Setting.QuickRegenBloodLossPercentPerHealth;
                            reachedMaxBloodLoss = (bloodLossHediff.Severity + loss) > Setting.QuickRegenBloodLossMax;
                            if (!reachedMaxBloodLoss)
                            {
                                bloodLossHediff.Severity += loss;
                                secondsCount += additionalIncresement;
                            }
                        }
                        else reachedMaxBloodLoss = true;
                    }
                    secondsCount += incresement;
                }
            }
        }
        protected bool TryGetMissingPart(out Hediff_MissingPart? missingPartHediff, out float maxHealth)
        {
            var missingPartHediffs = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            var result = missingPartHediffs.IsValid();
            maxHealth = -1;
            if (result)
            {
                missingPartHediff = missingPartHediffs[0];
                var bodyPartRecord = missingPartHediff.GetHediffBodyPartRecord();
                maxHealth = bodyPartRecord.def.GetMaxHealth(pawn);
            }
            else missingPartHediff = null;
            return result;
        }
    }
}
