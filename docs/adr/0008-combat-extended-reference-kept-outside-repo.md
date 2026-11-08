# Compile the 1.7 Combat Extended counterpart against a CE 1.7-Test build kept outside the repo

Superseded in part by ADR 0016 (2026-11-08). The reference is now the DLL built from the clone at `%USERPROFILE%\.rimworld-refs\CombatExtended-1.7-Test-src`, not the older DLL named below. The rule that the build stays outside the repo still holds.

The committed 1.6 CombatExtended.dll needs UnityEngine.CoreModule, which the 1.7 references do not provide. The 1.7 counterpart compiles against CombatExtended.dll built from the 1.7-Test branch (commit bcdd75dc95) with Krafs.Rimworld.Ref 1.7.4995-unstable. That DLL lives at %USERPROFILE%\.rimworld-refs\CombatExtended-1.7-Test\ and is not committed, because it is a third-party test build. Its behavior in RimWorld 1.7 is unproven.
