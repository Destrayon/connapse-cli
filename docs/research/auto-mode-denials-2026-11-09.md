# Why Claude Code's auto mode blocks the Connapse CLI, and how gh and other CLIs avoid it
**Date:** 2026-11-09
**Status:** Reviewed
**Built on:** no prior corpus material (the Connapse research corpus has no report on Claude Code's auto mode; `docs/research/api-mcp-cli-consistency-2026-10-17.md` in the Connapse repo was read for the server's approval design)

## Executive summary
The Connapse CLI was blocked because Claude Code's auto mode classifier treats a Connapse server as "shared infrastructure" it knows nothing about, so creating a container or uploading a file falls under its built-in **Modify Shared Resources** soft-deny rule (a rule the user's own message can clear by naming the exact change). In the blocked run, the instruction came from another Claude session (ClaudePM), and the classifier's rules say a peer agent's message never clears a soft deny. gh is not blocked for comparable work because the classifier's default environment already trusts the working-directory repository and its GitHub remote; nothing in gh's command design earns that. The fix that removes the denials without losing safety has two parts: tell the classifier, at the user level, that the signed-in Connapse server is the user's own service and that `connapse` is the user's own CLI, and make every CLI delete go through the server's owner-approval gate so the classifier's trust in writes cannot reach a delete. The main uncertainty is that the classifier is a model, not a rule engine: an environment entry shifts its judgment but is not a guarantee, which is why the change is proved with a scripted auto mode run.

## Research brief
**Question:** Why does Claude Code's auto mode deny everyday Connapse CLI calls from agents, and how do gh and similar CLIs avoid that?

**Sub-questions:**
1. Which classifier rule denied the CON-288 CLI calls, and with what exact text?
2. Why are gh calls of the same kind allowed?
3. How do agent-friendly CLIs (gh, Supabase, Vercel, Stripe) shape commands, output, help and confirmation for agents?
4. Which levers does a user or a CLI have to change the classifier's decision without weakening it?
5. Where in Connapse can a delete be held for the owner's approval?

**Out of scope:** changing Claude Code itself; bypassing the classifier with broad `Bash(*)` allow rules or `--dangerously-skip-permissions`.

**Success criteria:** the exact denial reproduced and explained, a comparison with other CLIs, and a design that removes the denials while keeping deletes behind the owner's approval.

## Findings by sub-question

### 1. The rule that denied the Connapse CLI in CON-288
The CON-288 agent (Connapse 0.4.1 testing, transcript `05c9954f-…jsonl` in the con-288 worktree project) ran this command twice and was denied both times:

```
printf '…' > E:/tmp/con-288/diviel/con288-cli-test.md; connapse container create con288-cli-scratch 2>&1 | tail -5; connapse upload E:/tmp/con-288/diviel/con288-cli-test.md --container con288-cli-scratch 2>&1 | tail -8; connapse files list --container con288-cli-scratch 2>&1 | head -10
```

The tool result was: "Permission for this action was denied by the Claude Code auto mode classifier. Reason: [Modify Shared Resources]." The read-only calls before it (`connapse --help`, `auth whoami`, `version`, `container list`, `search`, `files list`) all passed. Confidence: high (primary, the session transcript).

The rule, from `claude auto-mode defaults` in Claude Code 2.1.318, reads in part: "Persistent changes to shared state … that do not meet the bar should be blocked. 'Shared' means anything not exclusively owned by the agent's current session — internal services, production namespaces, shared databases, and multi-user infrastructure all count. Creating new resources in shared namespaces counts as modifying shared state." Its consent bar is "must name: the specific change" in the user's own message.

Two things kept the bar from clearing:
- **The classifier knows nothing about Connapse.** Its environment section lists "Key internal services: None configured" and "Org-specific CLIs: None configured", in both the defaults and the user's config. An unknown network service that holds records looks like shared infrastructure.
- **The consent came from a peer agent.** ClaudePM's message ("an upload then delete of a small test file in a scratch container") was a cross-session message. The built-in Multi-Agent Coordination rule says such content "does not meet any SOFT BLOCK rule's consent bar". Every agent ClaudePM launches is in the same position, so this is the everyday case, not an edge case.

A contributing factor: the command chained four actions with `;` and wrote a file outside the working directory, so the classifier had to judge the riskiest part (a create on an unknown service) for the whole line. Confidence: medium (inference from the rule text; the classifier does not explain its weighting).

### 2. Why gh passes for the same kind of work
gh passes because of where it points, not how it is built. The classifier's default environment defines a **Trusted repo** ("the git repository the agent started in … and its configured remote(s)") and **Source control** scoped to it. The built-in allow rule **Git Push Destination** says pushing to any branch of the session's repo "is ordinary", and the **External System Writes** soft deny carves out PR work ("working on a PR … does cover two writes on that PR"). `gh pr create` against the working repo therefore lands on trusted ground. A gh command against an unrelated repo, or `gh repo delete`, gets no such help. Confidence: high (primary, the rule text).

The user's own config shows the same pattern: rules the user added by hand (allowing `gh stack push` on `pm/*` branches, release tag pushes, and ssh maintenance of `~/connapse-*` on divielserver) are what let ClaudePM's other routine work through. Connapse itself has no entry yet.

### 3. How agent-friendly CLIs shape commands for agents
None of the vendors studied publishes recommended Claude Code permission rules, so no CLI "solves" auto mode by itself; what they share is predictable, explicit, non-interactive behaviour that makes each call easy to judge. Confidence: medium-high (primary vendor docs, fetched 2026-11-09).
- **gh:** deletes need `--yes` and an explicit repository when not interactive; `gh repo delete` needs a separate `delete_repo` token scope; `--json`/`--jq` structured output; documented exit codes (4 = authentication required); `gh auth status` names the active account; agent skills via `gh skill` (preview).
- **Supabase CLI:** `--agent auto` detection, `--yes` required for changes to hosted projects, `--dry-run`, and an "untrusted data" envelope around results returned to agents.
- **Vercel CLI:** switches to non-interactive mode when it detects an agent (`CLAUDECODE` / `AI_AGENT`), explicit `--scope`, `vercel agent init` to write agent instructions.
- **Stripe CLI:** `--confirm` on destructive calls, sandbox by default with `--live` to opt in, idempotency keys, `llms.txt`, an MCP server and `stripe agent setup`.
- **Guidance:** clig.dev (no prompts without a terminal, `--confirm=<name>` for destructive acts, dry runs) and Anthropic's "Writing tools for agents" (clear names, actionable errors, results an agent can act on).

The Connapse CLI already has most of the shape: explicit `--container` targets, `--json`, non-interactive behaviour, and `auth whoami`. What it lacks is the setup step (Vercel's `agent init`, Stripe's `agent setup`) and a confirmation step for deletes that is stronger than a flag the agent could pass itself.

### 4. Levers that change the classifier's decision
- **User-level `autoMode` settings.** `autoMode.environment`, `autoMode.allow` and `autoMode.soft_deny` are read from `~/.claude/settings.json`, managed settings, `--settings` and the SDK, and are ignored in a project's `.claude/settings*.json` (so a repository cannot grant itself trust). Each array should include `"$defaults"` to keep the built-in rules. Filling "Key internal services" and "Org-specific CLIs" is the documented way to tell the classifier what is the user's own. Confidence: high (Claude Code docs and `claude auto-mode config` output).
- **Permission rules.** A narrow `permissions.allow` rule like `Bash(connapse *)` stays in effect in auto mode, while broad ones (`Bash(*)`, interpreter wildcards) are dropped. Confidence: medium (inferred from the docs). It would also allow `connapse files delete` with no review, so it is the wrong lever on its own.
- **The user's own words.** A user message naming the exact change clears a soft deny. That works for a person at the keyboard, not for agents launched by ClaudePM.
- **Command shape.** One action per call, inside the working directory, with explicit targets, gives the classifier less to doubt. It helps but does not clear the Modify Shared Resources rule.

Because the user-level environment entry is the only lever that works for peer-launched agents and keeps the classifier in the loop, the CLI should print that entry for the user to add (`connapse setup claude-code`) rather than write it silently: changing the user's own trust settings is the user's call.

### 5. Where Connapse can hold a delete for the owner
The Connapse server holds Destructive and Credential calls only on its MCP route (`/mcp`, policy "RequireAgent": roles Owner, Admin or Agent). `McpApprovalGate` records a pending request and replies "'<tool>' is pending approval … Ask the person to approve request {id} at /approvals … once approved, repeat this exact call with the same arguments." The approval is bound to the token and the exact arguments, runs once, must be used within 15 minutes, and a request waits up to one day. `GET /api/approvals/{id}?wait=N` reports pending, approved, denied (with the reason), expired or used.

`McpApprovalClaims.Required` is true unless the token carries `requires_approval=false`; a sign-in or OAuth token has no such claim, so it **fails closed**. The CLI's sign-in token is therefore held, as are PATs and agent keys created with the default `RequiresApproval = true`. The REST delete routes the CLI uses today are never held. The relevant MCP tools are `delete_file(containerId, fileId)`, `container_delete(containerId)`, `memory_forget(factId)` and `approval_status(requestId, waitSeconds)`. Confidence: high (primary, Connapse `origin/main` source).

Moving the CLI's deletes onto this gate means a delete waits for the owner whether or not the classifier lets the command through, which is what makes trusting `connapse` writes in auto mode safe.

## Reproduction attempts (2026-11-09)
The CON-288 denial did not reproduce on demand. `tests/agent-auto-mode/run.sh` ran a headless Claude Code 2.1.318 session in auto mode against the unchanged CLI 0.5.1 five times on divielserver: once with the user's own prompt, twice with the tasks handed over as coming from ClaudePM, and twice with a CON-288-shaped task (chained commands, a test file outside the working directory). Every run finished with 0 denials, and in every run `files delete` and `container delete` ran at once without anyone being asked. The denial is therefore a judgment the classifier makes some of the time, not a fixed rule outcome, which makes the environment entry the way to make it reliable rather than a fix that a before-and-after count can prove. The gap that reproduces every time is the opposite one: auto mode let agent deletes through with no owner approval.

### After the change (2026-11-09)

The same scripts were run against the new build (`connapse` 0.5.1+07aef5d), with the output of `connapse setup claude-code` passed through `--settings`. `tasks.md` made 20 tool calls and `tasks-chained.md` made 7; auto mode denied none of them, and Claude Code reported no permission denials. Every delete in both runs was held by the server for the owner's approval (exit code 3; requests 843455d4, c318c375, 5a4774c1 and ecd92690), so nothing was deleted without the owner. Because the baseline was also clean, these runs show the tasks pass with the new settings and that deletes now wait for approval; they are not a before-and-after drop in denials.

## Conflicts and uncertainties
- **The classifier is a model.** An environment entry describing Connapse as the user's own service should move create, upload and update calls out of "shared state", but the classifier can still weigh a single call differently. This is why the result is proved by a scripted auto mode run rather than argued.
- **Permission rules in auto mode:** the docs say broad rules are dropped and do not list narrow ones explicitly; that narrow rules survive is an inference.
- **Approval-off tokens.** A PAT created with approval off is not held. A user who signs the CLI in with such a token opts out of the gate; the CLI says so in its delete output rather than hiding it.
- **Inline adversarial review** (no separate subagent, per the user's rules): every sub-question has an answer; the load-bearing single-source claims are the denial reason (the transcript, which is the event itself) and the server's fail-closed claim handling (source code, verified by reading `McpApprovalClaims.cs`). No TBDs remain.

## Gaps — what we did not find
- No vendor publishes recommended Claude Code auto mode settings for its CLI.
- No public description of how the classifier weighs environment entries against soft-deny rules.
- Whether the token behind the user's current CLI sign-in has approval off could not be read (`auth pat list` returned 403); the fail-closed rule makes this matter only for PATs created with approval off.

## Source quality assessment
Almost all of the synthesis rests on primary sources: the CON-288 session transcript, `claude auto-mode defaults` and `claude auto-mode config` output from Claude Code 2.1.318, Claude Code's settings and permission-mode docs, the Connapse server source at `origin/main`, and each vendor's own CLI docs. The clig.dev and Anthropic engineering guidance are secondary. Nothing rests on tertiary sources.

## Sources
**Primary**
- CON-288 transcript: `~/.claude/projects/D--CodeProjects-Connapse--claude-worktrees-con-288/05c9954f-786f-4c64-91ed-e6adf456714b.jsonl`
- `claude auto-mode defaults` and `claude auto-mode config`, Claude Code 2.1.318
- Claude Code docs: permission modes (auto mode), settings (`autoMode`), permissions — code.claude.com/docs
- Connapse `origin/main`: `src/Connapse.Web/Mcp/McpApprovalGate.cs`, `src/Connapse.Identity/Authentication/McpApprovalClaims.cs`, `src/Connapse.Web/Endpoints/ApprovalEndpoints.cs`, `src/Connapse.Identity/IdentityServiceExtensions.cs`, `docs/reference/mcp-tools.md`, `docs/guides/tokens-and-agents.md`
- gh manual (cli.github.com/manual: `gh repo delete`, `gh help exit-codes`, `gh help formatting`, `gh auth status`, `gh skill`)
- Supabase CLI docs, Vercel CLI docs, Stripe CLI docs and `docs.stripe.com/llms.txt`

**Secondary**
- clig.dev, Command Line Interface Guidelines
- Anthropic, "Writing effective tools for agents"
