# Keep the runtime validation runner in this repo

Runtime validation runs live in `tools/validation/` in this repo. This supersedes ADR 0005. Lucas chose it on 2026-11-05 and deleted the separate T3 Code project. The rules for a run are in `tools/validation/README.md`.

Updated 2026-11-07: Lucas chose to keep the harness loop that moves the mech serum group's game clock forward, as decision D3. The other option, running the game at its fastest speed, is not built. The loop stays.
