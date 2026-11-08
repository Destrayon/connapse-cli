# CC-7: `connapse memory` saves, recalls, lists and forgets facts

Server side: Connapse PR #1164 (CON-282) added `memory.remember`, `memory.recall`, `memory.forget`
web routes. The pinned spec (`spec/pin.json`) predates it.

1. **Repin the spec** — `spec/openapi.json` at Connapse `main`, client regenerated. Done when the
   generated client has `Api.Memory.Facts.GetAsync` (recall), `Spaces[id].Facts.PostAsync` (remember)
   and `Facts[id].DeleteAsync` (forget), and the solution builds.
2. **Commands** — `connapse memory save|recall|list|forget` (plus `spaces`). Done when each command
   calls its route and prints a readable or `--json` result.
3. **Tests** — CommandHost tests for each command: request sent, output, errors. Done when they pass.
4. **Verify and ship** — Release build with CI flags, full tests, README, PR. Done when the PR is open.
5. **No default space** — Connapse won't create a default memory space (CON-291), so `save` and `list`
   require `--space`; a live-server try with a stored login. Done when the PR is updated and pushed.

## Progress

- Phase 1: spec repinned to Connapse `main` 3c9d0810 (includes #1164), client regenerated; it also picks
  up CON-279's file routes. Added `src/Connapse.Client/MemorySpacesByName.cs` (a string indexer for
  spaces). The Release build with CI flags is clean. Next: write the commands.
- Phase 2: `src/Connapse.Cli/Commands/MemoryCommand.cs` and `Commands/Memory/*` (save, recall, list,
  forget, helpers); registered in `RootCommandBuilder`. Writes add an Editor/knowledge:write hint on 403.
  The Release build is clean. Next: CommandHost tests.
- Phase 3: `tests/Connapse.Cli.Tests/Commands/MemoryCommandTests.cs`, 15 tests (paths, queries, bodies,
  output, `--json`, 403 hint, 400/404 problems, bad fact ID). All pass. Next: README, CI build, PR.
- Phase 4: README "Memory" section. Release build with CI flags: 0 warnings; full tests 261 passed,
  2 skipped. Committed, pushed, PR opened. Next: wait for review.
- Phase 5: `--space` has no default; `save` and `list` fail without it and list up to 20 visible spaces`n  (MemoryHelpers.RequireSpaceAsync). README, help, tests and MemorySpacesByName.cs use `team-notes`. CI-flag build clean,`n  265 passed, 2 skipped. No stored login for connapse.diviel.dev (only divielserver:5001), so no live test. Next: review.
