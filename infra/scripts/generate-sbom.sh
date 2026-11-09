#!/usr/bin/env bash
# generate-sbom.sh — Generate a CycloneDX SBOM (JSON) for all .NET projects.
# Requires: dotnet-CycloneDX global tool  (`dotnet tool install --global CycloneDX`)
# Usage: ./infra/scripts/generate-sbom.sh [output-dir]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUTPUT_DIR="${1:-$REPO_ROOT/sbom}"

mkdir -p "$OUTPUT_DIR"

# Locate the solution file — prefer .slnx, fall back to .sln
SLN_FILE="$(find "$REPO_ROOT/src" -maxdepth 1 \( -name "*.slnx" -o -name "*.sln" \) | head -1)"

if [[ -z "$SLN_FILE" ]]; then
  echo "ERROR: No solution file found under $REPO_ROOT/src" >&2
  exit 1
fi

echo "==> Generating CycloneDX SBOM"
echo "    Solution : $SLN_FILE"
echo "    Output   : $OUTPUT_DIR"

dotnet CycloneDX "$SLN_FILE" --output "$OUTPUT_DIR" --json

echo ""
echo "==> SBOM generated at $OUTPUT_DIR/bom.json"
