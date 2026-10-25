#!/usr/bin/env bash
# Regenerates src/Connapse.Client/Generated from spec/openapi.json with the Kiota version pinned in
# dotnet-tools.json. CI runs it and fails if the checked-in client differs; the nightly spec sync runs
# it after repinning the spec.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet tool restore >/dev/null
dotnet tool run kiota generate \
  --language CSharp \
  --openapi spec/openapi.json \
  --output src/Connapse.Client/Generated \
  --class-name ConnapseApiClient \
  --namespace-name Connapse.Client \
  --exclude-backward-compatible \
  --clean-output \
  --log-level Error
