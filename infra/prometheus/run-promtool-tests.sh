#!/usr/bin/env sh
# Render the Helm-templated alert rules and run the promtool unit tests against them.
#
# This is the executable form of the procedure documented at the top of alert-rules-test.yml.
# Neither helm nor promtool needs to be installed — both run from Docker images. Run from the
# repo root or from this directory; the script locates the repo root itself.
#
#   sh infra/prometheus/run-promtool-tests.sh
#
# Exit code is promtool's: 0 = every fixture passed, non-zero = a rule fired when it should not
# have (or stayed silent when it should have fired). The rendered alert-rules.yml is a build
# artifact — gitignored, regenerated on every run, never committed.
set -eu

# Resolve repo root from this script's location so the helm mount is always the repo.
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)

# On Git Bash / MSYS, path mangling rewrites container paths like /src into C:/...;
# MSYS_NO_PATHCONV (Git for Windows) and MSYS2_ARG_CONV_EXCL (MSYS2) disable it.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

RENDERED="$SCRIPT_DIR/alert-rules.yml"
INTERMEDIATE="$SCRIPT_DIR/prometheusrule.rendered.yaml"
# The intermediate is not gitignored — make sure a mid-script failure never leaves it behind.
trap 'rm -f "$INTERMEDIATE"' EXIT

# 1) Render the PrometheusRule template. The release name MUST be `aqtcorefw` — the alert
#    expressions select on service="<fullname>" and the fixtures pin that exact rendering
#    (see the header of alert-rules-test.yml for why any other name fails by design).
echo "==> [1/3] helm template (release=aqtcorefw, prometheusrule only)"
docker run --rm -v "$REPO_ROOT:/src" -w /src alpine/helm template aqtcorefw infra/helm/aqtcorefw \
  --set metrics.prometheusRule.enabled=true -s templates/prometheusrule.yaml \
  > "$INTERMEDIATE"

# 2) Extract spec.groups into the flat rule-file shape promtool expects. Neither the helm nor
#    the prometheus image ships python, so the extraction is a tiny awk: everything from the
#    `  groups:` line onward, de-indented by the two spaces that nest it under `spec:`. This
#    survives exactly because the template emits `spec:\n  groups:` as literal YAML — if the
#    template's shape changes, promtool will reject the result loudly rather than pass silently.
echo "==> [2/3] extract spec.groups -> alert-rules.yml"
awk '
  /^spec:/ { inspec=1; next }
  inspec && /^[^ ]/ { inspec=0 }
  inspec { sub(/^  /, ""); print }
' "$INTERMEDIATE" > "$RENDERED"
rm -f "$INTERMEDIATE"

if ! grep -q '^groups:' "$RENDERED"; then
  echo "ERROR: extraction produced no top-level 'groups:' — template shape changed?" >&2
  exit 1
fi

# 3) Run the promtool unit tests against the freshly rendered rules.
echo "==> [3/3] promtool test rules alert-rules-test.yml"
docker run --rm -v "$SCRIPT_DIR:/w" -w /w --entrypoint promtool \
  prom/prometheus:latest test rules alert-rules-test.yml
