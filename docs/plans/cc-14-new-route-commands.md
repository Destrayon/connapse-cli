# CC-14 — CLI commands for the server's new routes

Source: the nightly API sync (#23, spec at Destrayon/Connapse@0b6e7990) added eight routes with no CLI command, so the route check failed.

1. **Approval routes** — `GET /api/approvals/{id}` and `GET /api/approvals/deletions/{id}` are already what `approval status` calls; list them. New `approval request` sends `POST /api/approvals/deletions`. Done: unit tests.
2. **Code tools** — new `code symbols|references|impact|map` over `/api/code/*`. Done: unit tests.
3. **Memory timeline** — new `memory timeline` over `GET /api/memory/timeline`. Done: unit tests.
4. **Ship** — manifest updated, route check and all tests pass, README documents the commands, PR into main.

## Progress

- Phases 1–4 done. No route needed a known-gaps entry. Files: `Commands/CodeCommand.cs`, `Commands/Code/*`, `Commands/Approval/ApprovalRequestCommand.cs`, `ApprovalHelpers.cs`, `ApprovalCommand.cs`, `Commands/Memory/MemoryTimelineCommand.cs`, `MemoryCommand.cs`, `RootCommandBuilder.cs`, `cli-routes.json`, three test files, README. Release build clean; 341 tests pass, 2 live-server tests skipped. Next: PR review.
