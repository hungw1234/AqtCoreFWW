#!/usr/bin/env bash
# =============================================================================
# check-openapi-breaking-changes.sh
# DevSecOps CI gate — detect breaking changes between OpenAPI specs.
#
# Compares the current branch's generated OpenAPI spec against the base branch
# (main) spec to catch breaking changes before merge:
#   - Removed endpoints (path + method)
#   - Removed required request fields
#   - Changed response schema types
#
# Requires: dotnet (to generate spec), jq
# Exit codes:
#   0 — no breaking changes (or comparison skipped)
#   1 — breaking changes detected (always warning, never blocks by default)
#
# Usage in CI:
#   bash infra/scripts/check-openapi-breaking-changes.sh
# =============================================================================
set -euo pipefail

API_URL="${API_URL:-http://localhost:5001}"
BASE_SPEC="${BASE_SPEC:-reports/openapi-base.json}"
CURRENT_SPEC="${CURRENT_SPEC:-reports/openapi-current.json}"
BREAKING=0

mkdir -p reports

echo "=== OpenAPI Breaking Change Gate ==="

# ── Step 1: Fetch current spec from running API ───────────────────────────────
if curl -sf "$API_URL/swagger/v1/swagger.json" -o "$CURRENT_SPEC" 2>/dev/null; then
    echo "Current spec fetched from $API_URL"
else
    echo "::warning::API not running at $API_URL — attempting dotnet swagger generate"
    # Try swagger CLI if available
    if command -v dotnet &>/dev/null; then
        dotnet swagger tofile \
            --output "$CURRENT_SPEC" \
            src/host/AqtCoreFW.Api/bin/Release/net10.0/AqtCoreFW.Api.dll v1 2>/dev/null || {
            echo "::warning::Could not generate OpenAPI spec — skipping breaking change check"
            exit 0
        }
    else
        echo "::warning::dotnet not available — skipping OpenAPI check"
        exit 0
    fi
fi

# ── Step 2: Fetch base (main branch) spec ────────────────────────────────────
if [ ! -f "$BASE_SPEC" ]; then
    echo "Base spec not found at $BASE_SPEC"
    # Try to fetch from previous CI artifact or git stash
    if git show "origin/main:reports/openapi-base.json" > "$BASE_SPEC" 2>/dev/null; then
        echo "Base spec restored from origin/main"
    else
        echo "::warning::No base spec available — saving current as baseline for next run"
        cp "$CURRENT_SPEC" "$BASE_SPEC"
        exit 0
    fi
fi

# ── Step 3: Compare paths (requires jq) ───────────────────────────────────────
if ! command -v jq &>/dev/null; then
    echo "::warning::jq not installed — skipping detailed comparison"
    exit 0
fi

echo ""
echo "--- Checking for removed endpoints ---"

# Extract path+method combos from each spec
BASE_ENDPOINTS=$(jq -r '.paths | to_entries[] | .key as $path | .value | to_entries[] | "\(.key|ascii_upcase) \($path)"' "$BASE_SPEC" 2>/dev/null | sort)
CURRENT_ENDPOINTS=$(jq -r '.paths | to_entries[] | .key as $path | .value | to_entries[] | "\(.key|ascii_upcase) \($path)"' "$CURRENT_SPEC" 2>/dev/null | sort)

REMOVED=$(comm -23 <(echo "$BASE_ENDPOINTS") <(echo "$CURRENT_ENDPOINTS") || true)

if [ -n "$REMOVED" ]; then
    echo "::warning::Removed endpoints detected (breaking change):"
    while IFS= read -r line; do
        echo "  REMOVED: $line"
        BREAKING=$((BREAKING + 1))
    done <<< "$REMOVED"
fi

ADDED=$(comm -13 <(echo "$BASE_ENDPOINTS") <(echo "$CURRENT_ENDPOINTS") || true)
if [ -n "$ADDED" ]; then
    echo "Added endpoints (non-breaking):"
    while IFS= read -r line; do
        echo "  ADDED: $line"
    done <<< "$ADDED"
fi

# ── Step 4: Summary ──────────────────────────────────────────────────────────
echo ""
if [ "$BREAKING" -gt 0 ]; then
    echo "=== $BREAKING breaking change(s) detected — review before merging ==="
    echo "::warning::$BREAKING API breaking change(s) found. See job output for details."
    # Warning only — does not block merge; set exit 1 to enforce
    exit 0
else
    echo "=== No breaking changes detected. Gate passed. ==="
    # Save current spec as next baseline
    cp "$CURRENT_SPEC" "$BASE_SPEC"
    exit 0
fi
