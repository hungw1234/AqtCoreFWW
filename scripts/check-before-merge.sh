#!/usr/bin/env bash
# Local pre-merge gate for changes that do not start CI on the pull request.
#
# GitHub Actions minutes on this repo are a fixed free budget, so pull requests outside the
# high-risk paths listed in .github/workflows/ci.yml and ci-frontend.yml merge on local evidence
# and are verified by CI once on main. This script runs the same build and test commands as the
# CI jobs, scoped to the side of the repo the branch touches.
# Policy: docs/deployment-guide.md section 0 ("When CI runs").
#
# Usage: scripts/check-before-merge.sh [base-ref]   (default base: origin/main)
set -euo pipefail

BASE_REF="${1:-origin/main}"
ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

MERGE_BASE="$(git merge-base "$BASE_REF" HEAD)"
CHANGED="$( { git diff --name-only "$MERGE_BASE"; git ls-files --others --exclude-standard; } | sort -u)"

if [ -z "$CHANGED" ]; then
  echo "No changes against $BASE_REF."
  exit 0
fi

touches() { grep -qE "$1" <<<"$CHANGED"; }

RUN_BACKEND=false
RUN_FRONTEND=false
touches '^(src/|tests/|ci/|\.semgrep/|dotnet-tools\.json)' && RUN_BACKEND=true
touches '^web/' && RUN_FRONTEND=true

if [ "$RUN_BACKEND" = true ]; then
  echo "== Backend: build with warnings as errors, unit and architecture tests (CI job unit-tests)"
  dotnet build ci/unit.slnx -c Release /warnaserror
  dotnet test ci/unit.slnx -c Release --no-build

  echo "== Backend: vulnerable package scan (CI job quality-gates)"
  VULN_OUT="$(dotnet list src/AqtCoreFW.slnx package --vulnerable --include-transitive 2>&1 || true)"
  if grep -qiE '(High|Critical)' <<<"$VULN_OUT"; then
    echo "$VULN_OUT"
    echo "High or critical vulnerable package found." >&2
    exit 1
  fi
fi

if [ "$RUN_FRONTEND" = true ]; then
  echo "== Frontend: type check, unit tests, production build (CI workflow CI (frontend))"
  (
    cd web
    pnpm install --frozen-lockfile
    pnpm exec tsc --noEmit -p tsconfig.app.json
    pnpm exec tsc --noEmit -p tsconfig.test.json
    pnpm run lint
    pnpm exec vitest run
    pnpm run build
  )
fi

if [ "$RUN_BACKEND" = false ] && [ "$RUN_FRONTEND" = false ]; then
  echo "Only docs, plans, scripts or config outside the build changed: no build or test to run."
fi

echo "Local pre-merge checks passed."
