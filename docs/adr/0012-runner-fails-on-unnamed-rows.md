# A run fails on any row its lists do not name

Lucas decided this on 2026-11-07, answering decision D5 with "Fail on unnamed rows, in a separate change". Before this decision, the runner checked only the rows named in `$required` and `$groups`. A failing row that neither list named did not fail the run, so a check added without a list entry could fail unseen.

## Decision

`tools/validation/Invoke-Scenarios.ps1` fails a run when any result row has a name that is in neither `$required` nor `$groups`. The row fails the run whatever it reads.

## Options Lucas saw

1. Fail on any row that neither list names. Chosen. The red and green runs below prove it.
2. Leave it documented. Not chosen. A failing row that no list names would still pass the run.

## Consequences

- A new check needs a row in `$required`, or a new group in `$groups`. Otherwise the run fails. `tools/validation/README.md` gives the steps.
- The control switch `-shieldvalidation-unnamedrow` writes one row that no list names, and the row reads FAIL. It exists to prove this rule.
- Red run `20261107-235413` used the unchanged runner, and it passed with the control row. That is the gap. Control run `20261107-235531` used the new runner, and it failed with the problem `row not named in the runner's lists`. Normal run `20261107-235624` used the new runner with no control switch, and it passed all 17 rows.
