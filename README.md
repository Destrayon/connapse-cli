# connapse-cli

Command-line interface for [Connapse](https://www.connapse.com) — an AI-powered knowledge management platform.

The CLI works with both Connapse Cloud (multi-tenant) and self-hosted Connapse servers.

## Install

### One command, no .NET needed

**macOS / Linux**
```bash
curl -fsSL https://raw.githubusercontent.com/Destrayon/connapse-cli/main/install.sh | sh
```

**Windows (PowerShell)**
```powershell
irm https://raw.githubusercontent.com/Destrayon/connapse-cli/main/install.ps1 | iex
```

Each script downloads the self-contained binary for your platform from the [latest release](https://github.com/Destrayon/connapse-cli/releases/latest), checks its SHA-256 sum when the release publishes one, and prints `connapse --version`. On macOS and Linux it installs to `~/.local/bin`; on Windows it installs to `%LOCALAPPDATA%\Programs\connapse` and adds that folder to your user `PATH` (open a new terminal afterwards). Run the same command again to upgrade.

To install a specific release, prereleases included:
```bash
curl -fsSL https://raw.githubusercontent.com/Destrayon/connapse-cli/main/install.sh | sh -s -- --version v0.5.1-alpha.1
```
```powershell
$env:CONNAPSE_VERSION = 'v0.5.1-alpha.1'; irm https://raw.githubusercontent.com/Destrayon/connapse-cli/main/install.ps1 | iex
```

| Setting | install.sh | install.ps1 |
|---|---|---|
| Version (default: latest) | `--version <tag>` or `CONNAPSE_VERSION` | `-Version <tag>` or `CONNAPSE_VERSION` |
| Install directory | `--install-dir <dir>` or `CONNAPSE_INSTALL_DIR` | `-InstallDir <dir>` or `CONNAPSE_INSTALL_DIR` |
| Leave `PATH` alone | — (never edits shell profiles; prints a hint instead) | `-NoModifyPath` or `CONNAPSE_NO_MODIFY_PATH=1` |

Published builds: Windows x64, Linux x64 (glibc), macOS Intel and Apple Silicon. On other platforms, use the .NET tool.

### .NET global tool (requires .NET 10 SDK)

```bash
dotnet tool install -g Connapse.CLI
connapse --version
```

Upgrade later with:
```bash
dotnet tool update -g Connapse.CLI
```

### Manual download

Download the binary for your platform from the [latest release](https://github.com/Destrayon/connapse-cli/releases/latest) (`connapse-cli-win-x64.exe`, `connapse-cli-linux-x64`, `connapse-cli-osx-x64` or `connapse-cli-osx-arm64`), rename it to `connapse` (`connapse.exe` on Windows), make it executable, and put it in a directory on your `PATH`.

### Build from source

```bash
git clone https://github.com/Destrayon/connapse-cli
cd connapse-cli
dotnet build Connapse.Cli.slnx
dotnet run --project src/Connapse.Cli -- --help
```

## Quick start

```bash
# 1. Log in to your Connapse server. A browser opens for OAuth; on completion
#    the CLI stores credentials in ~/.connapse/credentials.json. To keep more than
#    one login, see "More than one login" below.
connapse auth login --server https://www.connapse.com

# 2. (Cloud only) Pick an organization as the default for subsequent commands.
connapse org list
connapse org switch my-org

# 3. Create a knowledge container.
connapse container create "my-docs" --description "Project documentation"

# 4. Upload a file or directory.
connapse upload ./docs --container my-docs --destination /guides

# 5. Wait for indexing to finish, then search.
connapse search "how do I configure X" --container my-docs --top 5
```

## Commands

All commands accept the global options `--org <slug>`, `--profile <name>` and `--verbose`. Commands that require authentication will exit with status code 2 and ask you to run `connapse auth login` if credentials are missing or invalid.

### Authentication

| Command | Description |
|---|---|
| `connapse auth login --server <url>` | Open a browser for OAuth login. |
| `connapse auth login --server <url> --with-token` | Read a pre-generated token from stdin (CI-friendly). |
| `connapse auth logout` | Clear stored credentials. |
| `connapse auth whoami` | Show the current identity, server and profile. |
| `connapse auth list` | List saved logins (profiles); `*` marks the one commands use now. |
| `connapse auth switch <profile>` | Make a saved login the one commands use by default (`default` switches back). |
| `connapse auth pat create <name> [--expires yyyy-MM-dd]` | Create a personal access token. |
| `connapse auth pat list` | List all personal access tokens. |
| `connapse auth pat revoke <id>` | Revoke a personal access token by ID. |

#### More than one login

Each profile keeps its own login and default organization, so a Connapse Cloud login and a home server
login can live side by side. Your first login is the `default` profile, in `~/.connapse/`; a named profile
lives in `~/.connapse/profiles/<name>/`.

```bash
connapse auth login --server https://www.connapse.com                     # default profile
connapse auth login --profile home --server https://connapse.home.lan     # a second login
connapse container list --profile home                                    # one command against home
connapse auth switch home                                                 # home from now on
connapse auth list
```

A command uses the profile named by `--profile`, else the `CONNAPSE_PROFILE` environment variable, else the
one `connapse auth switch` chose, else `default`. Set `CONNAPSE_HOME` to keep everything in another
directory instead of `~/.connapse`, e.g. for a CI job or a throwaway session.

### Organizations (Cloud only)

| Command | Description |
|---|---|
| `connapse org list` | List organizations you belong to. |
| `connapse org switch <slug>` | Set the default organization. |
| `connapse org current` | Print the current default organization slug. |

Organization commands are no-ops on self-hosted servers and exit 0 with a friendly message.

### Containers

| Command | Description |
|---|---|
| `connapse container create <name> [--description ...]` | Create a knowledge container. |
| `connapse container list` | List all containers in the current organization. |
| `connapse container delete <name>` | Delete an empty container (accepts name or ID). Waits for the owner's approval; see [Deletes wait for approval](#deletes-wait-for-approval). |
| `connapse container stats <name>` | Show document counts, chunks, size, and embedding info. |

### Files

| Command | Description |
|---|---|
| `connapse files list --container <name> [--path /folder]` | List files and folders in a container. |
| `connapse files get --container <name> --file <id-or-path>` | Print a file's text content. |
| `connapse files delete --container <name> --file <guid>` | Delete a file by ID. Waits for the owner's approval. |

### Content

| Command | Description |
|---|---|
| `connapse upload <path> --container <name> [--destination /] [--strategy Semantic]` | Upload a file or directory. |
| `connapse search "<query>" --container <name> [--mode Hybrid] [--top 10] [--path /x] [--min-score 0.5]` | Search inside a container. |
| `connapse reindex --container <name> [--force] [--no-detect-changes]` | Re-process all documents. |

### Memory

A fact is a subject, an attribute and a value, saved to a memory space. `--space` takes a space's name or ID, such as `team-notes`. `save` and `list` require it; if it is missing, they fail and list the spaces you can see. `recall` without `--space` searches every space you can read. Saving and forgetting need the Editor role, and a token also needs `knowledge:write`. With an Owner, Admin or agent sign-in, `forget` goes through the server's approval route and waits for the owner's approval; an Editor's `forget` goes through the REST route instead and is not held (see [Deletes wait for approval](#deletes-wait-for-approval)). Every command takes `--json`.

| Command | Description |
|---|---|
| `connapse memory save <subject> <attribute> <value> --space <name> [--valid-from] [--valid-to] [--source <doc-id>]... [--reason]` | Save a fact. |
| `connapse memory recall [query] [--space] [--subject] [--attribute] [--as-of] [--history] [--limit] [--cursor]` | Recall facts from every space you can read, or one. |
| `connapse memory list --space <name> [--query] [--subject] [--status] [--sort] [--limit] [--cursor]` | List the facts in a space. |
| `connapse memory forget <fact-id>` | Delete a fact and every corrected version of it. Waits for the owner's approval. |

### Administration

These need the Owner or Admin role. A personal access token also needs the matching scope: `admin:sources` for connections and source changes, `admin:settings` for settings, and `admin:users` for agents. Reading sources needs only `knowledge:read`. Wherever a command takes a connection, source or agent, it accepts a name or an ID. JSON options come in pairs: `--scope '{...}'` inline, or `--scope-file scope.json`.

| Command | Description |
|---|---|
| `connapse connection list [--provider S3] [--skip 0] [--take 50]` | List connections. |
| `connapse connection get <connection> [--json]` | Show a connection's settings and health. Secrets are never shown. |
| `connapse connection create <name> --provider <provider> --config <json> [--token\|--token-stdin\|--private-key-file]` | Create a connection. |
| `connapse connection update <connection> [--name ...] [--config <json>]` | Rename a connection or change its settings. |
| `connapse connection delete <connection>` | Delete a connection no source uses. |
| `connapse connection pause <connection>` | Pause syncing for its sources. |
| `connapse connection resume <connection> [--source <id>...]` | Resume syncing, for all its sources or only those given. |
| `connapse connection test <connection> [--config <json>]` | Test a connection; exits 1 if it fails. |
| `connapse connection discover <connection> [--include-personal] [--json]` | List the buckets, containers, repositories or spaces it reaches. |
| `connapse connection replace-secret <connection> [--token-stdin\|--private-key-file]` | Replace an SFTP key or custom source server token. Owner only. |
| `connapse connection forget-host-key <connection>` | Forget an SFTP server's pinned host key. |
| `connapse source list [--skip 0] [--take 50] [--json]` | List sources and their sync state. |
| `connapse source get\|stats <source>` | Show a source, or its document counts, chunks and size. |
| `connapse source create <name> --connection <connection> --scope <json>` | Create a source. The scope's `"type"` picks its kind; `source scope-schema <provider>` shows the fields. |
| `connapse source update <source> [--name] [--description] [--scope] [--sync-interval] [--enable\|--disable]` | Change a source; only the options given are sent. |
| `connapse source delete <source>` | Delete a source and its indexed documents. The data it read from is untouched. |
| `connapse source sync <source> [--apply-withheld-deletions]` | Sync now; exits 1 if the sync fails. |
| `connapse source retry-failed <source>` | Queue its failed documents for another try. |
| `connapse source scope-schema <provider>` | Print the JSON schema of each scope a provider takes. |
| `connapse settings get <category>` | Print a category (embedding, chunking, search, llm, upload) as JSON. |
| `connapse settings set <category> key=value... \| --settings <json>` | Change some settings, keeping the rest, or replace the whole category. With key=value pairs, the save is refused if someone else saved the category after it was read. |
| `connapse settings reset <category>` | Reset a category to its defaults. |
| `connapse settings test <embedding\|llm\|crossencoder\|minio> [--settings <json>]` | Check the server can reach the service; exits 1 if not. |
| `connapse settings reindex [--force] [--container <name>]` and `settings reindex-status` | Re-embed documents in the background and follow progress. |
| `connapse settings embedding-models` | List the embedding models with vectors stored. |
| `connapse agent list` | List agents: non-human callers that use API keys. |
| `connapse agent get\|create\|enable\|disable <agent>` | Show, create, enable or disable an agent. |
| `connapse agent delete <agent> --yes` | Delete an agent and revoke its keys. |
| `connapse agent key list <agent>` | List an agent's keys. |
| `connapse agent key revoke <agent> <key-id>` | Revoke one of an agent's keys. |
| `connapse agent key create <agent> --name <name> --scope knowledge:read [--expires-in-days 30]` | Create a key. The token is printed once. |

### Any API route

| Command | Description |
|---|---|
| `connapse api <method> <path> [--data <json\|@file\|->] [--content-type <type>] [--paginate]` | Call any route on the signed-in server, for routes the CLI has no command for yet. It prints the response body byte for byte as sent, and exits 1 on a non-2xx status. Redirects are reported, not followed. `--paginate` follows `skip`/`take` pages of a GET and prints all items as one array. |

For example, `connapse api GET "/api/sources?take=50" --paginate`, or `connapse api POST /api/containers --data '{"name":"docs"}'`. In Git Bash, set `MSYS_NO_PATHCONV=1`: otherwise Git Bash rewrites `/api/...` into a Windows path.

### Deletes wait for approval

`files delete`, `container delete` and `memory forget` go through the server's approval gate (its MCP route), so when your sign-in asks for approval the server holds the delete instead of running it. Sign-in tokens always ask; a personal access token asks unless it was made with approval turned off. A held delete changes nothing, prints the request ID and exits with code 3:

```
Held for approval (request 0aae0f35-ddd6-457c-a031-aae9f8c11db6).
Nothing is deleted until the server's owner approves it.
Approve or deny it at http://my-server:5001/approvals, then run this same command again to delete.
To wait for the decision: connapse approval status 0aae0f35-ddd6-457c-a031-aae9f8c11db6 --wait 50
```

Once it is approved, run the same command again and it deletes, once. The approval route only admits Owner, Admin and agent sign-ins; an Editor's delete goes through the REST route instead, which the server does not hold.

| Command | Description |
|---|---|
| `connapse approval status <request-id> [--wait <0-50>] [--json]` | Show whether a held delete was approved. Exits 0 when approved, 3 while pending, 1 when denied or expired. |

`connapse api DELETE ...` calls the REST route directly and is never held.

### Agents in Claude Code auto mode

Claude Code's auto mode classifier doesn't know your Connapse server, so it can take a container create or an upload by an agent for a change to shared infrastructure and deny it, especially when another agent handed over the task. `connapse setup claude-code` prints auto mode settings that describe the signed-in server as your own service and `connapse` as your own CLI:

```
connapse setup claude-code
```

Merge the printed `autoMode` block into `~/.claude/settings.json`; Claude Code ignores `autoMode` in a project's settings. The rule trusts CLI deletes only because the server holds them for your approval, so use it with a sign-in that asks for approval. `tests/agent-auto-mode/run.sh` runs everyday agent tasks in auto mode and counts denials.

| Command | Description |
|---|---|
| `connapse setup claude-code` | Print Claude Code auto mode settings for the signed-in server. |

### Housekeeping

| Command | Description |
|---|---|
| `connapse version` | Print the installed CLI version, and the server's version and features when signed in. |
| `connapse update` | Check for and install a newer version from GitHub. |
| `connapse update --check` | Check for a newer version without installing. |
| `connapse update --include-prerelease` | Include pre-release versions. |

### Older servers

The CLI works with the current Connapse server release and the one before it. When a command needs something your server doesn't have yet, it says which feature is missing and which server version you run, and exits with code 1:

```
Error: This server doesn't support sources (it has no GET /api/sources). It runs Connapse 0.3.x or earlier (it doesn't report its version). Upgrade the server to use this command.
```

`tests/server/smoke/run-smoke-test.sh <image> [missing-features]` runs the CLI against a released server image to check this.

## Configuration

### File locations

| File | Purpose |
|---|---|
| `~/.connapse/credentials.json` | API key, refresh token, server URL, user email. File mode 0600 on Unix. |
| `~/.connapse/config.json` | Default org slug, cached edition, last-update-check timestamp. |

On Windows these live under `%USERPROFILE%\.connapse\`.

### Environment variables

| Variable | Effect |
|---|---|
| `CONNAPSE_ORG` | Sets the target organization for all commands. Overridden by `--org`. |

### Global flags

| Flag | Effect |
|---|---|
| `--org <slug>` | Override organization for a single command. |
| `--verbose` | Print full stack traces for unexpected errors. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | User error (bad input, server-rejected request, partial failure). |
| `2` | Authentication failure — run `connapse auth login` to fix. |
| `3` | Held for the server owner's approval; nothing changed yet. See [Deletes wait for approval](#deletes-wait-for-approval). |
| `99` | Unexpected error. Re-run with `--verbose` to see the stack trace. |

## Contributing

Bug reports and pull requests are welcome at https://github.com/Destrayon/connapse-cli/issues.

### Keeping up with the API

CI fails when the server's API spec (`spec/openapi.json`, synced nightly) has a route the CLI doesn't account for. Each route goes in exactly one place under `tests/Connapse.Cli.Tests/ApiParity/`:

- `cli-routes.json`, `commands`: the CLI command that calls it, such as `"GET /api/v1/agents": "agent list"`.
- `cli-routes.json`, `notForCli`: why it is never a CLI job, such as a health probe or a browser redirect.
- `known-cli-gaps.json`: why the command doesn't exist yet. Adding the command moves the route to `commands`.

### Releases

Releases are published via a tag push to `main`:
```bash
git tag v0.5.0
git push origin v0.5.0
```

The `.github/workflows/release.yml` workflow builds four self-contained binaries, packs a NuGet global tool, and creates a GitHub Release with all five assets.

**NuGet publish uses [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), not a stored API key.** Each release run trades its GitHub OIDC token for a nuget.org key that lasts one hour, so there is no secret to expire or rotate. It needs a Trusted Publishing policy on nuget.org owned by `psummers` for repo `Destrayon/connapse-cli`, workflow `release.yml`, no environment. Don't add a `NUGET_API_KEY` secret: a stored key expires, and an expired key is what failed the 0.5.0 publish.

If the NuGet login or push fails, the job fails and no GitHub Release is created. Re-running the failed job is safe: the push uses `--skip-duplicate`, so a version that already reached nuget.org is skipped rather than published again.

## License

MIT. See [LICENSE](LICENSE).
