# Changelog

## 0.5.2

Changes since v0.5.1 (2026-11-08).

### New commands

- **Memory** (CC-7, #22): `connapse memory save`, `recall`, `list` and `forget` save and recall remembered facts. `save` and `list` need `--space` (a memory space's name or ID); the error lists the spaces you can see.
- **Approval status** (CC-10, #24): `connapse approval status <request-id> [--wait <0-50 seconds>]` reports an owner's decision on a held delete. For a single request it exits 0 when approved, 3 while pending, and 1 when denied or expired. For a deletion batch it exits 3 while the batch is pending or still running after approval, 0 only once every item is deleted or skipped, and 1 otherwise.
- **Agent setup** (CC-10, #24): `connapse setup claude-code` prints the Claude Code auto mode settings that let an agent use the CLI without denials.
- **Approval request, code tools, memory timeline** (CC-14, #27): `connapse approval request` asks the owner to approve up to 50 deletions in one batch; `connapse code symbols|references|impact|map` query a container's code index; `connapse memory timeline` lists facts by time with `--from`/`--to`.

### Changed

- **Deletes wait for the owner's approval** (CC-10, #24): `files delete`, `container delete` and `memory forget` go through the server's approval gate. A held delete prints its request ID and exits with the new code **3**. Two cases are not held: a personal access token created with approval turned off (for any role), and Editors, whom the approval route refuses and who still delete directly.
- **Several files in one delete** (CC-12, #25): `files delete` takes `--file` more than once (up to 100) and sends one request. A held delete of several files becomes one deletion batch, which `approval status` reads.

### Fixed

- **`connapse api -d @file`** (CC-15, #28) sends the file as the request body. It used to be expanded into command-line arguments.
- **Git Bash folder paths** (CC-15, #28): `upload --destination`, `files list --path` and `search --path` refuse a folder Git Bash rewrote into a Windows path (such as `C:/Program Files/Git/docs`) and say to set `MSYS_NO_PATHCONV=1`.

### Internal

- CI fails when a server API route has neither a CLI command nor a written reason for having none (CC-13, #26).
