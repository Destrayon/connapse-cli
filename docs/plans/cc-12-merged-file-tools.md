# CC-12 — The CLI uses Connapse's merged get, upload and delete tools

Sources: Destrayon/Connapse#1231 (CON-344, merge 62bff357), its `docs/reference/mcp-tools.md` and CHANGELOG entries; CON-330's held-delete batches in `McpApprovalGate.cs` at the same commit.

Finding: the CLI never listed or called `get_documents`, `bulk_upload` or `bulk_delete`. Its only file-tool call is `delete_file` (one file); `files get` and `upload` use REST and stay there (the MCP route refuses Editors, and uploads would become base64 JSON).

1. **Several files per delete** — `files delete` takes `--file` more than once and sends one `delete_file` call with `fileIds`. Done: unit tests show one call with `fileIds`, one file still as `fileId`, Editors' REST fallback deletes each.
2. **Held deletion batches** — the CLI reads CON-330's "deletion batch <id>" answer, and `approval status` reads a batch via `GET /api/approvals/deletions/{id}`. Done: unit tests for held batch output and batch status.
3. **Guard** — a test fails if the CLI ever names a deprecated tool. Done: test passes.
4. **Live check** — run `files delete` (one and several), `files get`, `upload` against a local Connapse built from 62bff357 with scratch data. Done: outputs recorded in the PR.
5. **Ship** — tests pass, PR open, CI green.

## Progress

- Phases 1–3 done. `files delete` takes `--file` repeatedly (one `delete_file` call, `fileIds` for several, max 100; Editors' REST fallback deletes each). Held answers that name a deletion batch print the batch and say not to rerun; `approval status` falls back to `GET /api/approvals/deletions/{id}`. A test fails if the CLI source names a deprecated tool. Files: `FilesDeleteCommand.cs`, `McpToolClient.cs`, `ApprovalHelpers.cs`, `ApprovalStatusCommand.cs`, `ContainerDeleteCommand.cs`, `MemoryForgetCommand.cs`, `FakeMcpServer.cs`, `ApprovalCommandTests.cs`. 313 tests pass. Next: live check against a local Connapse at 62bff357.
- Phase 4 done. Against a local Connapse at 62bff357 (Docker, scratch container `cc12-scratch`): `upload` of 4 files, `files list`, `files get` OK; `files delete` one file → "Deleted file …", two files → "Deleted 2 of 2 file(s).", both exit 0 with a PAT made with requiresApproval=false. With a sign-in (held), `files delete` → "Held for approval (deletion batch …)" exit 3, and `approval status <batch>` → "Pending: deletion batch …" exit 3. A held multi-delete naming a missing file is refused whole by the server (exit 1). Next: tests, commit, push, PR.
