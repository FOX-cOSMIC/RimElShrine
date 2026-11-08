# Keep RimWorld 1.7 as the only supported version

Lucas decided this on 2026-11-08, answering decision D10. `RimElShrine.Shields/About/About.xml` lists only 1.7 under `supportedVersions`, and it stays that way.

## Decision

The port claims RimWorld 1.7 only. The 1.6 folder still ships in the mod, but no metadata says the port supports 1.6.

## Consequences

- Players on RimWorld 1.6 may see a version warning for this mod.
- The 1.6 build has no runtime runner, so nothing proves the 1.6 build runs in game.
- To claim 1.6 later, add `<li>1.6</li>` under `supportedVersions`, and record that choice in a new ADR.
