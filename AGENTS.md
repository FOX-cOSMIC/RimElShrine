# Shield Expanded for RimWorld 1.7

Port of Shield Expanded from RimWorld 1.6 to 1.7. Workshop item 3575073524; author FoxMo; mod version 1.1.3. Mod folder: `RimElShrine.Shields/`. Source: `Source/RimElShrine.Shields/`. Branch `main`. `origin` is this fork, `FOX-cOSMIC/RimElShrine`; `upstream` is `ElShrineFox221/RimElShrine`.

The repo's other mods, `RimElShrine.ShuttleFuel` and `RimElShrine.MoveMonolith`, are out of scope unless Lucas says otherwise.

## Documents

- Scope and decisions: `CONTEXT.md` and the ADRs that `grill-with-docs` writes. Read them before any scope question. Domain terms belong in `CONTEXT.md`.

## Constraints

- Target RimWorld 1.7.4998 rev1299, Steam unstable branch (ADR 0002).
- Leave `RimElShrine.Shields/1.6/` and its assemblies unchanged. Build 1.7 into `RimElShrine.Shields/1.7/`, and add a `<v1.7>` block to `RimElShrine.Shields/LoadFolders.xml`.
- Give the port a new `packageId` in `About.xml`. Lucas has the original (`FoxMo.RimElShrine.Shields`) subscribed, and a shared id collides with it.
- Keep Def names, XML class names, and settings keys from 1.6. Saves and translations key on them. Record each change 1.7 forces.
- Keep the Combat Extended path: `1.6/Compatible/CombatExtended` loads only when `CETeam.CombatExtended` is active. Give 1.7 its own counterpart.
- Ship no game DLLs and no Harmony DLL. Harmony comes from the `brrainz.harmony` dependency.
- Leave the committed DLLs in `Source/references/` and the 1.6 assemblies unchanged.
- Validate in isolated savedata, in a new colony. Existing saves and the live mod list stay as they are.
- Steam Workshop: upload as Private first. Lucas switches the listing to Public after testing.
- No license file exists, so this port grants no redistribution rights. Keep FoxMo's credit and the upstream link in `About.xml` and the Workshop text.

## Build

- 1.7 build (works, 0 errors): `dotnet build Source/RimElShrine.Shields/RimElShrine.Shields.csproj -c Release -p:RimWorldVersion=1.7`. It targets net10.0 with Krafs.Rimworld.Ref 1.7.4995-unstable and Lib.Harmony.Ref 2.4.2 (ADRs 0006, 0007). Output goes to `RimElShrine.Shields/1.7/Assemblies/`.
- 1.6 build: the same projects with the default `RimWorldVersion` (1.6). It targets net472 and writes to `RimElShrine.Shields/1.6/`. Do not run it while the port is in progress, because it overwrites the 1.6 assemblies.
- Combat Extended counterpart (1.7): `dotnet build Source/RimElShrine.Shields.Compatibility/RimElShrine.Shields.CE.csproj -c Release -p:RimWorldVersion=1.7` (0 errors). It compiles against `%USERPROFILE%\.rimworld-refs\CombatExtended-1.7-Test\CombatExtended.dll`, a CE 1.7-Test build kept outside the repo (ADR 0008). The committed 1.6 `CombatExtended.dll` cannot be used, because it needs `UnityEngine.CoreModule`, which 1.7 does not provide. The CE path's runtime behavior is unproven.
- Write decompiles and run logs to `.artifacts/`, and list `.artifacts/` in `.gitignore`.
- Record the build command here once it works.

## Verification

- Compiling is not playability. A gameplay change is proven only by a run in RimWorld 1.7 that exercises it.
- Runtime validation runs live in `tools/validation/` in this repo (ADR 0009). The smoke run `tools/validation/Invoke-Smoke.ps1` passed on 2026-11-05. The scenario runner `tools/validation/Invoke-Scenarios.ps1` runs the group checks on 1.7.4998 (ADR 0009). The status of each group is in `tools/validation/README.md`. Until a scenario runs, report each gameplay change as unproven.
- Reproduce a bug red in a run before fixing it: the run fails on the bug and passes on the fix.
- Check every Harmony attribute, patched method, and reflected name against the installed 1.7 code.
- Fix the cause of each error. A disabled patch or a hidden error is not a fix. Report failures and untested cases as they are.

## Git

- Work on `main`. Push to `origin` only, as fast-forward pushes.
- The `upstream` push URL is disabled locally. Keep it disabled.
- Open pull requests only with Lucas's go.
- Commit only your own paths with `git commit -- <paths>`. Other threads may have changes in the tree; leave them.

## Routes

Before your first action on a task, match it to a row and invoke that skill yourself. In Claude, call the Skill tool. In Codex, open the skill's `SKILL.md` and follow it.

| When the task is | Use | Done when |
|---|---|---|
| Confirming a name, field, or signature that RimWorld 1.7 uses | `reverse-engineering` (Codex). In Claude, decompile with `ilspycmd`. | the decompiled source or a log line confirms it; the finding goes in `docs/game-api-findings.md` (create it on the first finding) |
| Changing error handling, a try/catch, or a Harmony patch guard | `silent-failure-hunter` agent (Claude). In Codex, check the diff yourself. | the diff shows no swallowed errors |
| Finding every caller, definition, or implementation of a C# symbol | the LSP tool (Claude only) | the references come from the language server, not from text search |
| Finding out what external documentation says | `research` | the answer cites its source and is saved as Markdown in the repo |
