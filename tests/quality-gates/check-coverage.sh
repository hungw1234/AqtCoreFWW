#!/bin/bash
# check-coverage.sh — Verify coverlet coverage meets minimum thresholds.
#
# Usage:
#   ./tests/quality-gates/check-coverage.sh <coverage.json> <min-line%> <min-branch%>
#
# Arguments:
#   coverage.json   Path to coverlet JSON summary (from --collect:"XPlat Code Coverage")
#   min-line%       Minimum line coverage percentage (0-100), e.g. 80
#   min-branch%     Minimum branch coverage percentage (0-100), e.g. 70
#
# Exit codes:
#   0  All thresholds met
#   1  One or more thresholds not met (prints which ones failed)
#   2  Usage error (wrong args)
#   3  coverage.json not found or not parseable
#
# Example:
#   ./tests/quality-gates/check-coverage.sh \
#     test-results/coverage.json 80 70
#
# CI integration (after dotnet test with coverage collector):
#   dotnet test --collect:"XPlat Code Coverage" \
#     --results-directory ./test-results \
#     --settings tests/quality-gates/coverlet.runsettings
#   # coverlet writes coverage.cobertura.xml; use reportgenerator to produce JSON:
#   dotnet tool run reportgenerator \
#     -reports:"test-results/**/coverage.cobertura.xml" \
#     -targetdir:"test-results/coverage-report" \
#     -reporttypes:JsonSummary
#   ./tests/quality-gates/check-coverage.sh \
#     test-results/coverage-report/Summary.json 80 70

set -euo pipefail

# ── argument validation ────────────────────────────────────────────────────────

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <coverage.json> <min-line%> <min-branch%>" >&2
  exit 2
fi

COVERAGE_FILE="$1"
MIN_LINE="$2"
MIN_BRANCH="$3"

if [[ ! -f "$COVERAGE_FILE" ]]; then
  echo "ERROR: Coverage file not found: $COVERAGE_FILE" >&2
  exit 3
fi

# ── parse coverage values via python (available in GitHub Actions) ─────────────

read -r ACTUAL_LINE ACTUAL_BRANCH < <(python3 - "$COVERAGE_FILE" <<'PYEOF'
import sys, json

path = sys.argv[1]
with open(path) as f:
    data = json.load(f)

# ReportGenerator JsonSummary format:
#   { "summary": { "linecoverage": 84.5, "branchcoverage": 72.1 } }
# Coverlet JSON format (assemblies map):
#   { "AqtCoreFW.Domain": { "Summary": { "LineCoverage": 84.5, "BranchCoverage": 72.1 } } }

def try_reportgen(d):
    s = d.get("summary", {})
    line = s.get("linecoverage") or s.get("LineCoverage")
    branch = s.get("branchcoverage") or s.get("BranchCoverage")
    return line, branch

def try_coverlet(d):
    line_vals, branch_vals = [], []
    for asm_data in d.values():
        if not isinstance(asm_data, dict):
            continue
        s = asm_data.get("Summary", {})
        if "LineCoverage" in s:
            line_vals.append(float(s["LineCoverage"]) * 100)
        if "BranchCoverage" in s:
            branch_vals.append(float(s["BranchCoverage"]) * 100)
    if line_vals:
        return sum(line_vals)/len(line_vals), sum(branch_vals)/len(branch_vals) if branch_vals else 0.0
    return None, None

line, branch = try_reportgen(data)
if line is None:
    line, branch = try_coverlet(data)

if line is None:
    print("0 0", file=sys.stderr)
    print("ERROR: Could not parse coverage values from JSON", file=sys.stderr)
    sys.exit(3)

print(f"{float(line):.2f} {float(branch or 0):.2f}")
PYEOF
)

# ── threshold comparison ───────────────────────────────────────────────────────

PASS=true

echo "Coverage summary:"
echo "  Line coverage:   ${ACTUAL_LINE}%  (min: ${MIN_LINE}%)"
echo "  Branch coverage: ${ACTUAL_BRANCH}%  (min: ${MIN_BRANCH}%)"

# Use awk for float comparison (bash can't do it natively)
LINE_OK=$(awk "BEGIN { print (${ACTUAL_LINE} >= ${MIN_LINE}) ? \"yes\" : \"no\" }")
BRANCH_OK=$(awk "BEGIN { print (${ACTUAL_BRANCH} >= ${MIN_BRANCH}) ? \"yes\" : \"no\" }")

if [[ "$LINE_OK" != "yes" ]]; then
  echo "FAIL: Line coverage ${ACTUAL_LINE}% is below threshold ${MIN_LINE}%" >&2
  PASS=false
fi

if [[ "$BRANCH_OK" != "yes" ]]; then
  echo "FAIL: Branch coverage ${ACTUAL_BRANCH}% is below threshold ${MIN_BRANCH}%" >&2
  PASS=false
fi

if [[ "$PASS" == "true" ]]; then
  echo "PASS: All coverage thresholds met."
  exit 0
else
  exit 1
fi
