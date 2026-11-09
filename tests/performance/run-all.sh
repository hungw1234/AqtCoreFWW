#!/usr/bin/env bash
# AqtCoreFW — k6 performance test orchestration script
# Usage: ./run-all.sh <base-url> <api-key> [type]
# Types: smoke | baseline | full
#   smoke    — smoke-test.js only (fast, CI-friendly)
#   baseline — module-specific load tests (medium, ~20 min total)
#   full     — all scripts including soak/spike/stress (long, ~2 hr)
# Example: ./run-all.sh https://staging.internal my-api-key baseline

set -euo pipefail

# ─── Arguments ────────────────────────────────────────────────────────────────

BASE_URL="${1:-}"
API_KEY="${2:-}"
TYPE="${3:-smoke}"

if [[ -z "$BASE_URL" || -z "$API_KEY" ]]; then
  echo "Usage: $0 <base-url> <api-key> [smoke|baseline|full]"
  exit 1
fi

# ─── Setup ────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULTS_DIR="${SCRIPT_DIR}/results"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"

mkdir -p "${RESULTS_DIR}"

K6_COMMON_ARGS=(
  --env "BASE_URL=${BASE_URL}"
  --env "API_KEY=${API_KEY}"
)

# ─── Runner function ──────────────────────────────────────────────────────────

run_test() {
  local script="$1"
  local label="$2"
  local out_file="${RESULTS_DIR}/${TIMESTAMP}-${label}.json"

  echo ""
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo "  Running: ${label}"
  echo "  Script : ${script}"
  echo "  Output : ${out_file}"
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

  if k6 run "${K6_COMMON_ARGS[@]}" \
       --out "json=${out_file}" \
       "${SCRIPT_DIR}/${script}"; then
    echo "  [PASS] ${label}"
  else
    echo "  [FAIL] ${label} — see ${out_file}"
    FAILED_TESTS+=("${label}")
  fi
}

# ─── Test suites ─────────────────────────────────────────────────────────────

FAILED_TESTS=()

echo "========================================================"
echo "  AqtCoreFW k6 Performance Tests"
echo "  Type      : ${TYPE}"
echo "  Base URL  : ${BASE_URL}"
echo "  Timestamp : ${TIMESTAMP}"
echo "  Results   : ${RESULTS_DIR}"
echo "========================================================"

case "${TYPE}" in

  smoke)
    run_test "smoke-test.js" "smoke"
    ;;

  baseline)
    run_test "smoke-test.js"             "smoke"
    run_test "cases-load-test.js"        "cases-load"
    run_test "forms-load-test.js"        "forms-load"
    run_test "workflow-load-test.js"     "workflow-load"
    run_test "organization-load-test.js" "organization-load"
    run_test "reporting-load-test.js"    "reporting-load"
    run_test "search-load-test.js"         "search-load"
    run_test "rules-load-test.js"          "rules-load"
    run_test "collaboration-load-test.js"  "collaboration-load"
    run_test "ai-load-test.js"             "ai-load"
    run_test "integration-load-test.js"    "integration-load"
    run_test "db-performance-test.js"      "db-performance"
    run_test "connection-pool-test.js"     "connection-pool"
    ;;

  full)
    run_test "smoke-test.js"               "smoke"
    run_test "cases-load-test.js"          "cases-load"
    run_test "forms-load-test.js"          "forms-load"
    run_test "workflow-load-test.js"       "workflow-load"
    run_test "organization-load-test.js"   "organization-load"
    run_test "reporting-load-test.js"      "reporting-load"
    run_test "search-load-test.js"         "search-load"
    run_test "rules-load-test.js"          "rules-load"
    run_test "collaboration-load-test.js"  "collaboration-load"
    run_test "ai-load-test.js"             "ai-load"
    run_test "integration-load-test.js"    "integration-load"
    run_test "db-performance-test.js"      "db-performance"
    run_test "connection-pool-test.js"     "connection-pool"
    run_test "load-test.js"              "load"
    run_test "scaled-load-test.js"       "scaled-load"
    run_test "spike-test.js"             "spike"
    run_test "soak-test.js"              "soak"
    ;;

  *)
    echo "Unknown type '${TYPE}'. Use: smoke | baseline | full"
    exit 1
    ;;

esac

# ─── Final report ─────────────────────────────────────────────────────────────

echo ""
echo "========================================================"
echo "  Results saved to: ${RESULTS_DIR}"

if [[ ${#FAILED_TESTS[@]} -eq 0 ]]; then
  echo "  Status: ALL TESTS PASSED"
  echo "========================================================"
  exit 0
else
  echo "  Status: FAILED (${#FAILED_TESTS[@]} test(s))"
  for t in "${FAILED_TESTS[@]}"; do
    echo "    - ${t}"
  done
  echo "========================================================"
  exit 1
fi
