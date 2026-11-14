# CC-13 — CI fails when an API route has no CLI command

Modelled on the server's capability manifest test (Destrayon/Connapse #791, PR #819): a manifest names
the CLI command for every route in `spec/openapi.json`, or says why it has none, and a test fails on
anything unlisted.

1. **Manifest** — map every spec route to the CLI command that calls it, or to a reason (not a CLI job,
   or a known gap). Done: every route of the pinned spec is in exactly one list.
2. **Check and tests** — a test reads the spec and the manifest, checks each named command exists in the
   real command tree and that the CLI source calls the route, and fails on an unlisted route; a second
   test adds a fake route and sees it fail. Done: both pass locally.
3. **CI** — a named CI step runs the check before the full test run. Done: `dotnet test` passes locally.
4. **PR** — push, open the PR, report to ClaudePM. Done: CI green on the PR.

## Progress

- Phases 1–3 done. Manifest: 55 routes with a command, 35 never a CLI job, 84 known gaps, which covers all
  174 operations in the pinned spec. Files: `tests/Connapse.Cli.Tests/ApiParity/` (test, `cli-routes.json`,
  `known-cli-gaps.json`), `.github/workflows/ci.yml`, `README.md`. Verified: `dotnet test` 317 passed, 2 skipped.
  Next: push and open the PR.
