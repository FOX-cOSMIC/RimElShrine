# Record the raid scene and the game's raid AI, decided on 2026-11-08

Lucas decided two settings for the recorded combat demo on 2026-11-08. The two questions were numbered D1 and D2, which repeat decisions D1 and D2 from 2026-11-07. This record names the choices by topic, so the numbers do not clash.

## Decision

Lucas chose the raid scene, `Record-CombatDemo.ps1 -Scene raid`. Three colonists stand inside the dome, each with a rifle and an ES0 belt, and three raiders with revolvers stand outside it. Lucas asked for the belts on the colonists and for the colonists to fire through the dome they stand in.

Lucas chose to try the game's own raid AI. The raiders then run the game's raid lord, and the trial worked, so the raid scene uses the raid lord by default (`-RaidAI game`). The scripted aim stays available as `-RaidAI scripted`.

## Consequences

- The runs for both scenes are listed under Recordings in `tools/validation/README.md`.
- The scenes write only `[ShieldCombat]` lines and no validation rows. The runner's rules do not change.
- In two test runs, `20261108-110251` and `20261108-110839`, the raiders on the game's duties found no target. `docs/game-api-findings.md` records that.
