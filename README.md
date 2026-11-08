# RimElShrine Mods

This repo holds the RimElShrine mods. The main project is the Shield Expanded 1.7 port in `RimElShrine.Shields/`. The port is a RimWorld 1.7 version of Shield Expanded, a 1.6 mod by FoxMo (Workshop item 3575073524). It has its own package ID, `foxcosmic.rimelshrine.shields17`, so it loads beside the original. It needs Harmony.

- `RimElShrine.Shields/` is the port's mod folder. Its `1.7/` folder holds the 1.7 build, and `1.6/` holds the 1.6 build.
- `Source/` holds the source code for the mods in this repo.
- `pictures/` holds Workshop preview pictures.
- `tools/validation/` runs checks inside RimWorld. Its README gives the status.
- `docs/` holds the explainer, the decision records in `docs/adr/`, and the API findings.
- `CONTEXT.md` defines the terms this project uses.

The port has no license, so it grants no redistribution rights. FoxMo's credit and the upstream link stay in `About.xml` and the Workshop text.

`RimElShrine.ShuttleFuel/` and `RimElShrine.MoveMonolith/` are other mods in this repo. The port does not cover them.
