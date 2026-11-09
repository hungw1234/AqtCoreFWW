#!/bin/bash
# Wraps the repo's Grafana dashboards into a sidecar-discoverable ConfigMap and applies it.
# The source files in infra/grafana/ are API-import format ({"dashboard": {...}}); the
# kube-prometheus-stack sidecar expects the BARE dashboard model — this script unwraps at
# apply time so the committed sources stay in one canonical format (DRY: no second copy).
# Idempotent: dry-run render piped to apply. Re-run after editing a dashboard JSON.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

for name in aqtcorefw-dashboard slo-dashboard; do
  python - "$REPO_ROOT/infra/grafana/$name.json" "$TMP_DIR/$name.json" <<'PYEOF'
import json, sys
src, dst = sys.argv[1], sys.argv[2]
doc = json.load(open(src, encoding="utf-8"))
# Unwrap the API-import envelope; pass bare models through unchanged.
model = doc.get("dashboard", doc)
json.dump(model, open(dst, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
PYEOF
done

kubectl -n monitoring create configmap aqtcorefw-grafana-dashboards \
  --from-file="$TMP_DIR/aqtcorefw-dashboard.json" \
  --from-file="$TMP_DIR/slo-dashboard.json" \
  --dry-run=client -o yaml \
  | kubectl label --local -f - grafana_dashboard=1 -o yaml \
  | kubectl apply -f -

echo "[grafana-dashboards] Applied (sidecar picks up within ~1min)."
