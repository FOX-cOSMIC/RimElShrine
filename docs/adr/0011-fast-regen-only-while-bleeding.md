# Fast injectors speed up only a bleeding pawn

Lucas decided this on 2026-11-07, answering decision D1 with "I think they only work when they can help." Before this decision, the Fast Regenerator injectors did nothing extra. Their XML sets `isFastRegen`, and no code read that flag. The 1.6 mod never read it either, so this decision changes the port's behavior from the 1.6 mod.

## Decision

A Fast injector sets the regen effect to severity 1, the top of its range. Quick regen starts above severity 0.5, so the effect runs in quick mode. Quick regen runs only while the pawn has blood loss. It adds blood loss as it works, and it stops when the pawn has no blood loss. A healthy pawn regrows at the normal speed. In this record, a bleeding pawn means a pawn with blood loss, the state the regen code reads. The validation sets blood loss directly, and it does not model a wound.

Two changes make this hold.

- `Comp_LimbsRegenInjector.DoEffect` sets the severity inside an `#if RIMWORLD_17` block. The 1.7 build defines that symbol in `Source/RimElShrine.Shields/RimElShrine.Shields.csproj`. The 1.6 build compiles the old code.
- `HediffWithComps_LimbsRegen.Tick` reads blood loss with the vanilla `HediffSet.TryGetHediff`. The port's `TryGetHediff` helper creates a zero-severity blood loss hediff when a pawn has none. Without this change, a healthy pawn would get quick regen through that phantom hediff. This change has no guard. Quick regen never runs in the 1.6 build, because nothing there raises severity above 0.5, so the 1.6 build behaves as before.

## Options Lucas saw

1. Make the flag work for every pawn, and give a healthy pawn blood loss when quick regen starts. Not chosen, because it adds blood loss to pawns that have none.
2. Only apply the flag, so quick regen works on a pawn that is already bleeding. Chosen.
3. Leave the injectors as they are, and say so in the Workshop text. Not chosen, because the Fast injectors would stay useless.

## Consequences

- The quick factor is 3 by default, set by `QuickRegenFactor`. In the validation run, a bleeding pawn regrows a finger at 9,633 ticks. A normal injector takes 28,833 ticks. Run `20261107-175804` measured both.
- Quick regen adds blood loss on each regrowth step. The amount per health point is set by `QuickRegenBloodLossPercentPerHealth`. Quick regen stops once blood loss reaches the cap set by `QuickRegenBloodLossMax`, which is 0.35 by default.
- A healthy pawn regrows at the normal speed, and no blood loss appears. Run `20261107-175804` checks this.
- The port now differs from the 1.6 mod on the Fast injectors. The 1.6 build keeps its old behavior, because its code is compiled without the guard. Removing the guard changes the 1.6 build the next time someone builds it.
- A separate bug remains. The injector's usability check, `Comp_LimbsRegenInjector.CanBeUsedBy`, uses the same helper. Checking a bleeding pawn sets that pawn's blood loss to zero, which cures the bleeding. The 1.6 build has the same bug. This change does not fix it, and decision D4 asks Lucas how to handle it.
- Red and green runs for this change are recorded in `tools/validation/README.md`.

Updated 2026-11-07: Lucas chose to leave the usability check unchanged in both builds, as decision D4. Checking a bleeding pawn still cures its bleed, in 1.6 and 1.7 alike.

Updated 2026-11-08: Lucas reopened D4 and chose option 1, fix the usability check in both builds, and he invoked full send for it. The 2026-11-07 wording, that the check cures the bleeding, overstated the effect. The check set blood loss to 0 while it ran, and an open wound refilled it. `Comp_LimbsRegenInjector.CanBeUsedBy` now reads the regen effect with the vanilla `HediffSet.TryGetHediff`, which only reads. A pawn with no blood loss now shows the normal fast label, not `Disable Fast`, because only blood loss at the cap sets that label. The source change is in shared code, so the 1.6 build compiles it. The 1.6 assemblies are not rebuilt in this change, because AGENTS.md says to leave them unchanged. Red run `20261108-142312` fails on the unfixed build, and green run `20261108-142616` passes all 19 rows on the fixed build.

Updated 2026-11-08, after review round 1 of commit `76ef5cb`: the cap flag was set only while a finger regrew, so it could stay set after the blood loss ended. It is now recomputed from blood loss on every tick, so it also clears when no finger is regrowing. Red run `20261108-143801` fails the new cap row on the unfixed build, and green run `20261108-144040` passes all 20 rows on the fixed build.

Updated 2026-11-08, review round 2 of commit `3a14f9d`: at a fast multiplier of 1, the slider minimum, a quick step adds no blood loss, so the cap test missed blood loss at exactly the cap. The test now counts blood loss at or above the cap. That restores the rule from commit `76ef5cb`. The same review found that the cap row did not check the label while the pawn was at the cap. The row now asserts the label and the tip one tick after the finger is back. Mutation run `20261108-144559` fails the row when the flag always clears. Green run `20261108-144736` passes all 20 rows at that point. The multiplier-1 edge has its own row. Red run `20261108-145304` fails it on the rule from commit `3a14f9d`, and green run `20261108-145423` passes all 21 rows on the final code. Review round 4 asked for the row to check its preconditions, and to restore the multiplier in a `finally` block. Green run `20261108-150040` passes all 21 rows with those preconditions recorded.
