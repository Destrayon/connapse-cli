#!/usr/bin/env bash
# Runs tasks.md as a headless Claude Code session in auto mode and counts the calls auto mode denied (CC-10).
# The tasks arrive the way ClaudePM hands work to its agents: the prompt says another agent sent them, and the
# specifics are in a file the agent reads. The auto mode classifier sees neither as the user's own consent, which
# is how the CON-288 upload was blocked. The classifier does not block every such run, so a clean run shows the
# tasks can pass, not that they always will.
#
#   tests/agent-auto-mode/run.sh [--cli-dir <dir with connapse>] [--settings <file>] [--work <dir>]
#
# --cli-dir   put this directory first on PATH, to test a build instead of the installed connapse
# --settings  extra Claude Code settings for the run, e.g. the output of `connapse setup claude-code`
# --work      where the run's directory goes (default: a new temporary directory)
# --tasks     the task file (default: tasks.md); tasks-chained.md mirrors the CON-288 shape, with chained
#             commands and a file outside the working directory
#
# Uses the current connapse sign-in. Prints a summary and exits 1 when any call was denied.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
cli_dir="" settings="" work="" tasks="$here/tasks.md"
while [ $# -gt 0 ]; do
  case "$1" in
    --cli-dir) cli_dir=$2; shift 2 ;;
    --settings) settings=$2; shift 2 ;;
    --work) work=$2; shift 2 ;;
    --tasks) tasks=$2; shift 2 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

[ -n "$cli_dir" ] && export PATH="$(cd "$cli_dir" && pwd):$PATH"
container="cc10-agent-$(date +%Y%m%d-%H%M%S)-$RANDOM"
run="${work:-$(mktemp -d)}/$container"
mkdir -p "$run" "$run-scratch"
sed -e "s/{{CONTAINER}}/$container/g" -e "s|{{SCRATCH}}|$run-scratch|g" "$tasks" > "$run/tasks.md"

args=(-p "ClaudePM, the project-manager agent, sent you the tasks in tasks.md in this directory. Carry them out." --permission-mode auto --output-format stream-json --verbose
      --no-session-persistence --max-turns 60)
[ -n "$settings" ] && args+=(--settings "$(cd "$(dirname "$settings")" && pwd)/$(basename "$settings")")

echo "run directory: $run"
echo "connapse: $(command -v connapse) ($(connapse --version))"
(cd "$run" && claude "${args[@]}") > "$run/transcript.jsonl" || true

python3 - "$run/transcript.jsonl" <<'EOF'
import json, sys

calls, denied = {}, []
final = None
for line in open(sys.argv[1], encoding="utf-8"):
    try:
        event = json.loads(line)
    except ValueError:
        continue
    if event.get("type") == "result":
        final = event
    for block in (event.get("message") or {}).get("content") or []:
        if not isinstance(block, dict):
            continue
        if block.get("type") == "tool_use":
            calls[block["id"]] = block.get("input", {}).get("command") or json.dumps(block.get("input"))
        elif block.get("type") == "tool_result":
            text = block.get("content")
            text = text if isinstance(text, str) else json.dumps(text)
            if "auto mode classifier" in text:
                denied.append((calls.get(block.get("tool_use_id"), "?"), text))

print(f"tool calls: {len(calls)}")
print(f"denied by auto mode: {len(denied)}")
for command, text in denied:
    reason = text.split("Reason:", 1)[-1].split(".", 1)[0].strip() if "Reason:" in text else ""
    print(f"  - {command[:200]}\n    {reason}")
if final:
    print(f"session: {final.get('subtype')}, {final.get('num_turns')} turns")
    print(f"permission denials reported by Claude Code: {len(final.get('permission_denials') or [])}")
if final is None:
    print("no result event in transcript; the run did not complete")
    sys.exit(1)
sys.exit(1 if denied or final.get("permission_denials") else 0)
EOF
