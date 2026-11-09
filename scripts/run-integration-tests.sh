#!/bin/bash
# Run integration tests inside Docker — bypasses Windows Smart App Control
# Usage: bash scripts/run-integration-tests.sh [filter]
# Example: bash scripts/run-integration-tests.sh "FullyQualifiedName~Cases"

set -e

FILTER="${1:-}"
FILTER_ARG=""
if [ -n "$FILTER" ]; then
  FILTER_ARG="--filter \"$FILTER\""
fi

echo "=== Running integration tests in Docker ==="
echo "Filter: ${FILTER:-all}"

# MSYS_NO_PATHCONV prevents Git Bash from converting /app to C:/Program Files/Git/app
# Mount Docker socket so Testcontainers can create SQL Server containers
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "C:/AqtCoreFW:/app" \
  -v //var/run/docker.sock:/var/run/docker.sock \
  -w /app/src \
  -e "DOTNET_CLI_TELEMETRY_OPTOUT=1" \
  -e "TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal" \
  --network host \
  --add-host host.docker.internal:host-gateway \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "dotnet test AqtCoreFW.slnx --nologo --verbosity quiet $FILTER_ARG 2>&1 | tail -30"

echo "=== Done ==="
