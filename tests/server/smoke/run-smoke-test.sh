#!/usr/bin/env bash
# Runs the version smoke test (CON-129): starts a released Connapse server image with Postgres and
# MinIO (capped together at 2 CPUs and 4 GB, see docker-compose.yml), signs in as the seeded admin,
# and runs VersionSmokeTests, which drives the real CLI against it. Everything it starts is removed
# at the end, pass or fail.
#
#   tests/server/smoke/run-smoke-test.sh <image> [missing-features]
#
#   run-smoke-test.sh ghcr.io/destrayon/connapse:v0.3.3-alpha.1 sources,connections
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
export CONNAPSE_SMOKE_IMAGE="${1:?usage: run-smoke-test.sh <image> [missing-features]}"
missing="${2:-}"

compose=(docker compose -f "$here/docker-compose.yml")
home=$(mktemp -d)
cleanup() { "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true; rm -rf "$home"; }
trap cleanup EXIT

docker pull -q "$CONNAPSE_SMOKE_IMAGE" >/dev/null
"${compose[@]}" up -d --wait postgres minio
"${compose[@]}" up -d web

base=http://127.0.0.1:5112
for _ in $(seq 1 90); do curl -fs "$base/health" >/dev/null && break; sleep 2; done
curl -fs "$base/health" >/dev/null || { "${compose[@]}" logs web | tail -50; echo "the server didn't come up" >&2; exit 1; }

# The seeded admin's token, saved where the CLI keeps its sign-in, in a throwaway home directory.
for _ in $(seq 1 30); do
  token=$(curl -fs -X POST "$base/api/v1/auth/token" -H 'Content-Type: application/json' \
    -d '{"email":"smoke-test@example.com","password":"Smoke-Test-Passw0rd!"}' || true)
  [ -n "$token" ] && break; sleep 2
done
[ -n "$token" ] || { echo "couldn't sign in as the seeded admin" >&2; exit 1; }
mkdir -p "$home/.connapse"
python3 - "$token" "$base" "$home/.connapse/credentials.json" <<'PY'
import json, sys
t = json.loads(sys.argv[1])
json.dump({"apiKey": t["accessToken"], "apiBaseUrl": sys.argv[2], "userEmail": "smoke-test@example.com",
           "refreshToken": t.get("refreshToken")}, open(sys.argv[3], "w"))
PY

# The CLI the test runs, framework-dependent: the project publishes self-contained, which `dotnet x.dll` can't start.
dotnet build "$here/../../../src/Connapse.Cli/Connapse.Cli.csproj" -c Release -o "$home/cli" -p:SelfContained=false -v q -nologo

CONNAPSE_SMOKE_CLI="$home/cli/connapse.dll" CONNAPSE_SMOKE_HOME="$home" CONNAPSE_SMOKE_EXPECT_MISSING="$missing" \
  dotnet test "$here/../../../Connapse.Cli.slnx" --filter "Category=ServerSmoke" --logger "console;verbosity=normal"
