#!/usr/bin/env bash
# Runs the capability-route test (CON-21): builds the Connapse server at pin.json's commit, starts it
# with Postgres and MinIO (capped, see docker-compose.yml), signs in as the seeded admin, and runs
# CapabilityRoutesTests, which sends every route in that commit's capability list through
# `connapse api`. Everything it starts is removed at the end, pass or fail.
#
#   tests/server/run-route-test.sh <connapse-checkout-at-the-pinned-commit>
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
src=$(cd "${1:?usage: run-route-test.sh <connapse-checkout>}" && pwd)
pin=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commit"])' "$here/pin.json")
[ "$(git -C "$src" rev-parse HEAD)" = "$pin" ] || { echo "$src is not at the pinned commit $pin" >&2; exit 1; }

compose=(docker compose -f "$here/docker-compose.yml")
home=$(mktemp -d)
cleanup() { "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true; rm -rf "$home"; }
trap cleanup EXIT

docker build -q -t connapse-route-test:pinned "$src" >/dev/null
"${compose[@]}" up -d --wait postgres minio
"${compose[@]}" up -d web

base=http://127.0.0.1:5111
for _ in $(seq 1 90); do curl -fs "$base/health" >/dev/null && break; sleep 2; done
curl -fs "$base/health" >/dev/null || { "${compose[@]}" logs web | tail -50; echo "the server didn't come up" >&2; exit 1; }

# The seeded admin's token, saved where the CLI keeps its sign-in, in a throwaway home directory.
for _ in $(seq 1 30); do
  token=$(curl -fs -X POST "$base/api/v1/auth/token" -H 'Content-Type: application/json' \
    -d '{"email":"route-test@example.com","password":"Route-Test-Passw0rd!"}' || true)
  [ -n "$token" ] && break; sleep 2
done
[ -n "$token" ] || { echo "couldn't sign in as the seeded admin" >&2; exit 1; }
mkdir -p "$home/.connapse"
python3 - "$token" "$base" "$home/.connapse/credentials.json" <<'PY'
import json, sys
t = json.loads(sys.argv[1])
json.dump({"apiKey": t["accessToken"], "apiBaseUrl": sys.argv[2], "userEmail": "route-test@example.com",
           "refreshToken": t.get("refreshToken")}, open(sys.argv[3], "w"))
PY

# The CLI the test runs, framework-dependent: the project publishes self-contained, which `dotnet x.dll` can't start.
dotnet build "$here/../../src/Connapse.Cli/Connapse.Cli.csproj" -c Release -o "$home/cli" -p:SelfContained=false -v q -nologo

CONNAPSE_ROUTE_TEST_CLI="$home/cli/connapse.dll" CONNAPSE_ROUTE_TEST_HOME="$home" CONNAPSE_ROUTE_TEST_CAPABILITIES="$src/tests/Connapse.Integration.Tests/Capabilities/capabilities.json" \
  dotnet test "$here/../../Connapse.Cli.slnx" --filter "Category=ServerRoutes" --logger "console;verbosity=normal"
