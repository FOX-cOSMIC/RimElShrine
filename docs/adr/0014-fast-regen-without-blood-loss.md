# Fast injectors work on a pawn with no blood loss

Lucas decided this on 2026-11-08, answering decision D9. Lucas said that fast regen is supposed to grow back missing body parts, and that it should also work with no blood loss or no bleeding, so that it brings the body back to complete health. This reverses decision D1 from 2026-11-07, which ADR 0011 records. D1 chose that quick regen runs only on a pawn that is already bleeding.

## Decision

A Fast injector still sets the regen effect to severity 1. Quick regen then speeds up regrowth on any pawn that is missing a body part, bleeding or not. Each quick step adds blood loss, as the injector text says. A pawn with no blood loss starts a bleed at zero, and its first quick step raises that bleed. Quick regen stops speeding up when blood loss reaches `QuickRegenBloodLossMax`, which is 0.35 by default.

## Choices I made

- Fast regen keeps its cost. Lucas's words describe a pawn with no blood loss. They do not make the speed-up free. The injector text says fast regeneration "may cause additional blood loss symptoms." To make the speed-up free, set the default of `QuickRegenBloodLossPercentPerHealth` in `Source/RimElShrine.Shields/MechserumSetting.cs` to 0.
- The cap still ends the speed-up. A pawn at the cap regrows at the normal speed.
- The label stays `Fast Regen`. A healthy Fast pawn now regrows fast, so the label is accurate. This settles the label question in D9.
- The 1.6 build keeps its `#if RIMWORLD_17` guard. Nothing in 1.6 reads the Fast flag, so the 1.6 behavior does not change.
- The usability check keeps the vanilla read from ADR 0011. Decision D4 still holds.
- The code uses a new helper, `QuickRegenBloodLoss`, instead of the port's `CommonUtility.TryGetHediff`. When the requested definition is absent, that helper sets the pawn's blood loss to zero, even when the pawn is bleeding. The new helper never sets an existing bleed to zero.

## Consequences

- A healthy Fast pawn regrows a finger inside the fast window, 7,680 to 10,560 ticks. The validation row also checks that the pawn has blood loss when the finger returns.
- At the default settings, a finger adds about 0.027 blood loss. That stays well under the cap of 0.35.
- The validation row for a healthy Fast pawn changes from "does nothing" to "speeds up regrowth". The label row drops its test for blood loss. Both row names changed.
- The Workshop text and the in-game Fast descriptions say the speed-up works with or without blood loss.

## Proof

- Red run `20261108-200958` on commit `614106d`, on RimWorld 1.7.5000 rev1275. The healthy-pawn row fails. The finger came back at 28,833 ticks, outside the fast window of 7,680 to 10,560, with no blood loss. The other 20 rows pass, including the bleeding-pawn row at 9,633 ticks.
- Green run `20261108-202332` on commit `140d470`, with the 1.7 assemblies that commit rebuilt. All 18 scenario rows and 3 completion rows pass. The healthy pawn's finger came back at 9,633 ticks, with blood loss when it came back.
- The 1.6 build has no runtime runner. Its proof is a build with 0 errors on commit `140d470`. Nothing in 1.6 raises the regen severity above 0.5, so the fast path does not run there.
