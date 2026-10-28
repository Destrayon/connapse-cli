# connapse-cli

Command-line interface for [Connapse](https://www.connapse.com) — an AI-powered knowledge management platform.

The CLI works with both Connapse Cloud (multi-tenant) and self-hosted Connapse servers.

## Install

### Option 1: .NET global tool (requires .NET 10 SDK)

```bash
dotnet tool install -g Connapse.CLI
connapse --version
```

Upgrade later with:
```bash
dotnet tool update -g Connapse.CLI
```

### Option 2: Self-contained binary (no .NET required)

Download the binary for your platform from the [latest release](https://github.com/Destrayon/connapse-cli/releases/latest):

| Platform | Asset |
|---|---|
| Windows (x64) | `connapse-cli-win-x64.exe` |
| Linux (x64) | `connapse-cli-linux-x64` |
| macOS (Intel) | `connapse-cli-osx-x64` |
| macOS (Apple Silicon) | `connapse-cli-osx-arm64` |

On Linux or macOS:
```bash
chmod +x connapse-cli-linux-x64
sudo mv connapse-cli-linux-x64 /usr/local/bin/connapse
```

On Windows, add the `.exe` to a directory in your `PATH`.

### Option 3: Build from source

```bash
git clone https://github.com/Destrayon/connapse-cli
cd connapse-cli
dotnet build Connapse.Cli.slnx
dotnet run --project src/Connapse.Cli -- --help
```

## Quick start

```bash
# 1. Log in to your Connapse server. A browser opens for OAuth; on completion
#    the CLI stores credentials in ~/.connapse/credentials.json.
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

All commands accept the global options `--org <slug>` and `--verbose`. Commands that require authentication will exit with status code 2 and ask you to run `connapse auth login` if credentials are missing or invalid.

### Authentication

| Command | Description |
|---|---|
| `connapse auth login --server <url>` | Open a browser for OAuth login. |
| `connapse auth login --server <url> --with-token` | Read a pre-generated token from stdin (CI-friendly). |
| `connapse auth logout` | Clear stored credentials. |
| `connapse auth whoami` | Show the current identity and server. |
| `connapse auth pat create <name> [--expires yyyy-MM-dd]` | Create a personal access token. |
| `connapse auth pat list` | List all personal access tokens. |
| `connapse auth pat revoke <id>` | Revoke a personal access token by ID. |

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
| `connapse container delete <name>` | Delete a container (accepts name or ID). |
| `connapse container stats <name>` | Show document counts, chunks, size, and embedding info. |

### Files

| Command | Description |
|---|---|
| `connapse files list --container <name> [--path /folder]` | List files and folders in a container. |
| `connapse files get --container <name> --file <id-or-path>` | Print a file's text content. |
| `connapse files delete --container <name> --file <guid>` | Delete a file by ID. |

### Content

| Command | Description |
|---|---|
| `connapse upload <path> --container <name> [--destination /] [--strategy Semantic]` | Upload a file or directory. |
| `connapse search "<query>" --container <name> [--mode Hybrid] [--top 10] [--path /x] [--min-score 0.5]` | Search inside a container. |
| `connapse reindex --container <name> [--force] [--no-detect-changes]` | Re-process all documents. |

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
| `connapse settings set <category> key=value... \| --settings <json>` | Change some settings, keeping the rest, or replace the whole category. |
| `connapse settings reset <category>` | Reset a category to its defaults. |
| `connapse settings test <embedding\|llm\|crossencoder\|minio> [--settings <json>]` | Check the server can reach the service; exits 1 if not. |
| `connapse settings reindex [--force] [--container <name>]` and `settings reindex-status` | Re-embed documents in the background and follow progress. |
| `connapse settings embedding-models` | List the embedding models with vectors stored. |
| `connapse settings clear-summaries` | Clear container and document summaries so they are written again. |
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

### Housekeeping

| Command | Description |
|---|---|
| `connapse version` | Print the installed CLI version. |
| `connapse update` | Check for and install a newer version from GitHub. |
| `connapse update --check` | Check for a newer version without installing. |
| `connapse update --include-prerelease` | Include pre-release versions. |

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
| `99` | Unexpected error. Re-run with `--verbose` to see the stack trace. |

## Contributing

Bug reports and pull requests are welcome at https://github.com/Destrayon/connapse-cli/issues.

### Releases

Releases are published via a tag push to `main`:
```bash
git tag v0.5.0
git push origin v0.5.0
```

The `.github/workflows/release.yml` workflow builds four self-contained binaries, packs a NuGet global tool, and creates a GitHub Release with all five assets.

**NuGet publish requires the `NUGET_API_KEY` secret.** Create a scoped API key at [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys) (glob pattern `Connapse.CLI*`) and set it on the repo:

```bash
gh secret set NUGET_API_KEY --repo Destrayon/connapse-cli
```

If the secret is missing the release workflow still succeeds — the NuGet publish step is skipped, but binary assets are still attached to the release.

## License

MIT. See [LICENSE](LICENSE).
