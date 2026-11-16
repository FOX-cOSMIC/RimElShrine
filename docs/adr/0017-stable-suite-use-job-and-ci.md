# Stabilise the suite, cover the use job, and make the build reproducible

Lucas decided these on 2026-11-15, when he asked to run the tests and finish the project properly.

## Decisions

- D12 (answering D11): the row `mech serum: fast regen speeds up regrowth on a pawn with no blood loss` passes when the pawn has blood loss at any tick while its finger regrows. It no longer checks only the tick the finger returns. Natural recovery can clear the blood loss before that tick, which made the row fail in 2 of 5 Combat Extended runs.
- D13: a new row orders a colonist to use a Fast injector through the game's use job, with the arguments the right-click menu passes. Until now every row called the injector's effect directly.
- D14: this work goes through one branch and one pull request on `FOX-cOSMIC/RimElShrine`, with review rounds, and is merged after a clean round. The full-send request is Lucas's go for that pull request, as AGENTS.md requires.
- D15: Lucas first chose a GitHub Actions workflow that builds the 1.7 mod on each push and pull request. The GitHub CLI token had no `workflow` scope, so the push was refused. Asked to grant the scope, Lucas chose on 2026-11-15 to skip CI instead. The repo has no CI, and the reproducible build below stays, so a workflow can be added later.
- D16: the merged branches `docs-mod-explainer`, `fast-regen-bleeding`, and `fix/usability-check-keeps-blood-loss` are deleted locally and on `origin`. `git cherry main <branch>` showed every commit of each branch already in `main`.

## Out of scope

Runtime tests for the 1.6 build, Combat Extended weapons, armour and health, and the Workshop upload.

## Choices I made

- Test colonists that are generated with a missing part are discarded. Run `20261115-214152` gave one colonist the test finger already missing, and the use-job row failed on it.
- The 1.7 DLLs are now reproducible. The SDK wrote the git commit into the informational version and the SourceLink map, and the embedded PDB held this machine's folder path, so every commit changed the DLL bytes. The three projects set `IncludeSourceRevisionInInformationalVersion` to false. The Shields project maps its source folder with `PathMap` and sets `EnableSourceLink` to false. A decompile diff shows the version string as the only code-level change. To reverse, remove those properties.
- A clone of the branch in another folder builds byte-identical 1.7 DLLs with SDK 10.0.301 (round 1 review). Another SDK can give other bytes.
- The installed game moved to 1.7.5005 rev1082 on 2026-11-10. ADR 0002 targets the installed build, so the proof below is on 1.7.5005.

## Proof

All on RimWorld 1.7.5005 rev1082.

| Run | Setting | Verdict | What it shows |
|---|---|---|---|
| `20261115-214411` | build with no fast regen blood loss | FAIL | Only the healthy Fast row fails: `blood loss during regrowth False`. |
| `20261115-214507` | `-shieldvalidation-noserum` | FAIL | The use-job row fails, with no job ordered. |
| `20261115-214604`, `20261115-214655` | standard | PASS | 22 rows. |
| `20261115-214753`, `20261115-214923`, `20261115-215021` | `-CombatExtended` | PASS | 27 rows. |
| `20261115-215331` | standard, final DLLs | PASS | 22 rows. |
| `20261115-215426` | `-CombatExtended`, final DLLs | PASS | 27 rows. |
| `20261116-100133` | standard, after round 1 (use job with the menu's arguments) | PASS | 22 rows. |
| `20261116-100243` | `-CombatExtended`, after round 1 | PASS | 27 rows. |
