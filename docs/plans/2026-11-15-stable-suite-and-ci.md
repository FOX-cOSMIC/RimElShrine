# Plan: stable suite, use-job row, and reproducible build

Spec: `docs/specs/2026-11-15-stable-suite-and-ci.md`.

1. In `tools/validation/ShieldValidation/MechSerumValidation.cs`, add `BleedingDuringRegrow` to `Patient`. Set it on any tick where the finger is still missing and the pawn has blood loss. Row F uses it instead of `BleedingAtRegrow`.
2. Add patient K. In `Setup`, spawn a `ELS_Thing_LimbsRegenInjectorFastLimited` next to K and call `CompUsable.TryStartUseJob(K, LocalTargetInfo.Invalid, Props.ignoreOtherReservations)`, as the right-click menu does. Skip the order under `-shieldvalidation-noserum`. In the tick loop, record the tick the regen effect appears, whether it is quick, and whether the injector is gone. The row passes when the effect appeared, is quick, the injector is used up, and the finger is back within the fast window counted from that tick.
3. Add the row name to the runner's required list in `Invoke-Scenarios.ps1`.
4. Red run for check 2: comment out the blood loss line in `HediffWithComps_LimbsRegen.cs`, build, run, then restore the source and rebuild.
5. Red run for check 3, then green runs for checks 4 and 5.
6. CI workflow: dropped on 2026-11-15 (D15). Make the 1.7 build reproducible instead.
7. Update `tools/validation/README.md` (status, known issues) and ADR 0017 with the runs.
8. Branch, PR, review rounds, merge.
