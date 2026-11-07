# Fast injectors speed up only a bleeding pawn

Lucas decided this on 2026-11-07, answering decision D1 with "I think they only work when they can help." Before this decision, the Fast Regenerator injectors did nothing extra. Their XML sets `isFastRegen`, and no code read that flag. The 1.6 mod never read it either, so this decision changes the port's behavior from the 1.6 mod.

## Decision

A Fast injector sets the regen effect to severity 1, the top of its range. Quick regen starts above severity 0.5, so the effect runs in quick mode. Quick regen runs only while the pawn has blood loss. It adds blood loss as it works, and it stops when the pawn has no blood loss. A healthy pawn regrows at the normal speed.

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
