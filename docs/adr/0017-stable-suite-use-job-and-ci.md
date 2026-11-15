# Stabilise the suite, cover the use job, and build in CI

Lucas decided these on 2026-11-15, when he asked to run the tests and finish the project properly.

## Decisions

- D12 (answering D11): the row `mech serum: fast regen speeds up regrowth on a pawn with no blood loss` passes when the pawn has blood loss at any tick while its finger regrows. It no longer checks only the tick the finger returns. Natural recovery can clear the blood loss before that tick, which made the row fail in 2 of 5 Combat Extended runs.
- D13: a new row orders a colonist to use a Fast injector through the game's use job, the path a player takes from the right-click menu. Until now every row called the injector's effect directly.
- D14: this work goes through one branch and one pull request on `FOX-cOSMIC/RimElShrine`, with review rounds, and is merged after a clean round. The full-send request is Lucas's go for that pull request, as AGENTS.md requires.
- D15: a GitHub Actions workflow builds the 1.7 mod on each push and pull request. It uses the NuGet reference packages, so it needs no game. It does not build the Combat Extended part, which needs a local clone (ADR 0016), and it does not run the game.
- D16: the merged branches `docs-mod-explainer`, `fast-regen-bleeding`, and `fix/usability-check-keeps-blood-loss` are deleted locally and on `origin`. `git cherry main <branch>` showed every commit of each branch already in `main`.

## Out of scope

Runtime tests for the 1.6 build, Combat Extended weapons, armour and health, and the Workshop upload.
