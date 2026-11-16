# CC-16 — Release CLI 0.5.2

Ships CC-7, CC-10, CC-12, CC-13, CC-14 and CC-15 (on main since v0.5.1, 2026-11-08). Version 0.5.2, alpha first. Scratch data and test output in `E:\tmp\cc-16`.

1. **Bump and changelog** — csproj to 0.5.2 (as CC-4 did for 0.5.1; the tag sets the alpha suffix), `CHANGELOG.md` lists every change since v0.5.1. Done: tests pass.
2. **Server check** — run each new command from this build against divielserver's Connapse 0.4.2 with scratch data; any command needing a route 0.4.2 lacks gets a line in the changelog naming the server version it needs. Done: every command has a result.
3. **PR** — PR into main, Codex review addressed, CI green, PM told.
4. **Alpha** — after merge, `v0.5.2-alpha.1` is tagged (by the PM/user unless allowed), the workflow publishes the GitHub and NuGet prereleases, and the published alpha passes the same checks on divielserver. Done: test log in the PM report.
5. **Release** — `v0.5.2` tagged on the tested commit, GitHub release and NuGet package published.

## Progress
- Phase 1 done: csproj 0.5.2, `CHANGELOG.md` added (CC-7, 10, 12, 13, 14, 15). Release build clean, 350 passed, 2 skipped (live-server tests). The server commits the CLI's spec was pinned to (3c9d0810, 62bff357, 0b6e7990) are all in Connapse v0.4.2. Next: run the new commands against divielserver.
