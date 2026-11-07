# One RimWorld session at a time for validation runs

Lucas decided this on 2026-11-07.

Every RimWorld session writes its log to one shared file, `AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`. The `-logFile` argument does not move that log in RimWorld 1.7.5000 rev1275. The runner reads the file after each run, so two sessions running at once mix their lines, and neither run can be checked against its results file.

On 2026-11-05 a shield run and a ZLevels run from `rimworld-modding` started within the same second. The shield runner's log check failed, which was correct. The mech serum rows in that run do not count, so run `20261105-233814` is not proof.

Lucas considered three options: pause the ZLevels runs for about three minutes, add a shared lock to both runners, or wait for a free window. He chose to wait.

Consequences:

- A shield run starts only when no RimWorld process is running. The runner stops a run when another RimWorld process starts during it.
- Shield runs wait for a free window before they start. The ZLevels runner is not changed by this decision.
- A run that overlaps another session is not valid evidence, even when its results file reads PASS.
