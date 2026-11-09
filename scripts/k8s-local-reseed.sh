#!/bin/bash
# Stands the local kind cluster environment up FROM ZERO. Order matters: namespaces → secrets
# → statefuls → wait ready+unsealed → vault seed → helm install.
# Prereqs: Docker Desktop kind cluster running, images built (see infra/k8s-local/README.md),
# .env.k8s-local filled in, helm on PATH.
#
# ⚠️ NOT freely re-runnable since SQL Server and Vault moved to PVCs. Step 5b mints NEW random
# encryption keys, so re-running against a SURVIVING database leaves existing rows encrypted
# under a key that no longer exists. The guard below refuses to proceed when the data PVCs are
# still present; clear them deliberately (RESET_DATA=1, or delete the PVCs by hand) to get the
# from-zero state this script expects.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

HELM="${HELM:-helm}"
RESET_DATA="${RESET_DATA:-0}"

# --- From-zero guard: keys and data must be reset together -----------------------------------
EXISTING_PVCS="$(kubectl -n aqt-data get pvc sqlserver-data vault-data \
  -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || true)"

if [ -n "$EXISTING_PVCS" ]; then
  if [ "$RESET_DATA" = "1" ]; then
    echo "[reseed] 0/7 RESET_DATA=1 — deleting data PVCs ($EXISTING_PVCS) for a true from-zero run"
    # A PVC carries the kubernetes.io/pvc-protection finalizer and will NOT delete while any pod
    # still mounts it — it just sits Terminating, and a later `apply` silently re-binds the SAME
    # volume (old data survives a "reset"). So: delete workloads, WAIT for the pods to be gone,
    # only then delete the PVCs, and verify they actually disappeared.
    # Uninstall the helm releases rather than deleting their objects directly: `helm upgrade`
    # reconciles against its own stored manifest, so objects deleted behind its back are not
    # reliably recreated on the next upgrade (it can report "deployed" while the workloads stay
    # missing). Step 6 reinstalls them from scratch.
    for rel in aqtcorefw authserver web; do
      "$HELM" uninstall "$rel" -n aqt-app --wait --timeout 5m >/dev/null 2>&1 || true
    done
    kubectl -n aqt-app delete job --all --ignore-not-found >/dev/null 2>&1 || true
    kubectl -n aqt-data delete deploy sqlserver vault --ignore-not-found >/dev/null 2>&1 || true
    echo "         waiting for sqlserver/vault pods to terminate (PVCs cannot delete while mounted)"
    kubectl -n aqt-data wait --for=delete pod -l app=sqlserver --timeout=120s >/dev/null 2>&1 || true
    kubectl -n aqt-data wait --for=delete pod -l app=vault --timeout=120s >/dev/null 2>&1 || true

    # Deleting the vault PVC also discards the unseal key, which lives on that same volume
    # (infra/k8s-local/vault-unseal.sh) — key and data can never drift apart.
    kubectl -n aqt-data delete pvc sqlserver-data vault-data --ignore-not-found --timeout=120s

    REMAINING="$(kubectl -n aqt-data get pvc sqlserver-data vault-data \
      -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || true)"
    if [ -n "$REMAINING" ]; then
      echo "ERROR: PVCs still present after delete ($REMAINING) — refusing to continue, since" >&2
      echo "       reseeding onto surviving volumes would rotate keys against retained data." >&2
      kubectl -n aqt-data get pvc >&2
      exit 1
    fi
    echo "         data PVCs deleted (verified gone)"
  else
    cat >&2 <<'GUARD'
ERROR: data PVCs already exist — refusing to reseed.

  This script mints NEW random encryption keys (step 5b). Running it against a surviving
  database would leave existing rows encrypted under a key that no longer exists.

  Which case are you in?

  (a) The stack is healthy, merely stopped or sealed after a restart/sleep.
      You do NOT need a reseed — the vault unseal sidecar restores secrets automatically.
      Just wait for the pods to become ready.

  (b) A PREVIOUS RESEED WAS INTERRUPTED (the PVCs are created at step 3 of 7, so a failure at
      the vault-seed, prod-secrets, helm or smoke step leaves them behind and lands you here).
      A half-seeded stack cannot be repaired by waiting — restart it from zero with RESET_DATA=1.

  (c) You deliberately want to wipe data AND keys together (true from-zero).

  For (b) and (c):
      RESET_DATA=1 bash scripts/k8s-local-reseed.sh
GUARD
    exit 1
  fi
fi

echo "[reseed] 1/7 namespaces"
kubectl apply -f infra/k8s-local/namespaces.yaml

echo "[reseed] 2/7 secrets"
bash scripts/k8s-local-create-secrets.sh

echo "[reseed] 3/7 backing services"
# The unseal sidecar's script ships as a ConfigMap (same verbatim-mount pattern as
# vault-init.sh) so infra/k8s-local/vault-unseal.sh stays the single source of truth.
kubectl -n aqt-data create configmap vault-unseal-script \
  --from-file=vault-unseal.sh=infra/k8s-local/vault-unseal.sh \
  --dry-run=client -o yaml | kubectl apply -f -
kubectl apply \
  -f infra/k8s-local/sqlserver.yaml \
  -f infra/k8s-local/vault.yaml \
  -f infra/k8s-local/redis.yaml \
  -f infra/k8s-local/rabbitmq.yaml \
  -f infra/k8s-local/minio.yaml \
  -f infra/k8s-local/clamav.yaml

echo "[reseed] 4/7 wait for sqlserver + vault (cold-start can take minutes)"
kubectl -n aqt-data wait --for=condition=ready pod -l app=sqlserver --timeout=300s
kubectl -n aqt-data wait --for=condition=ready pod -l app=vault --timeout=120s
# Pod-ready is NOT enough: the vault readiness probe answers 200 while still sealed (so the
# sidecar can reach it to unseal). Seeding a sealed vault fails, so gate on seal state.
echo "[reseed] 4b/7 wait for vault to be UNSEALED (file storage boots sealed)"
for i in $(seq 1 60); do
  # `vault status -format=json` is pretty-printed ("sealed": false), so flatten before matching.
  # It also exits 2 when sealed — a status code, not a failure — hence no reliance on exit here.
  if kubectl -n aqt-data exec deploy/vault -c vault -- \
       env VAULT_ADDR=http://127.0.0.1:8200 vault status -format=json 2>/dev/null \
       | tr -d '\n\r\t ' | grep -q '"sealed":false'; then
    echo "         vault unsealed"
    break
  fi
  if [ "$i" = 60 ]; then
    echo "ERROR: vault still sealed after 120s. Sidecar logs:" >&2
    kubectl -n aqt-data logs deploy/vault -c unseal --tail=30 >&2 || true
    exit 1
  fi
  sleep 2
done

echo "[reseed] 5/7 vault seed (re-uses infra/docker/vault-init.sh verbatim)"
kubectl -n aqt-data create configmap vault-init-script \
  --from-file=vault-init.sh=infra/docker/vault-init.sh \
  --dry-run=client -o yaml | kubectl apply -f -
kubectl -n aqt-data delete job vault-seed --ignore-not-found
kubectl apply -f infra/k8s-local/vault-seed-job.yaml
kubectl -n aqt-data wait --for=condition=complete job/vault-seed --timeout=120s
kubectl -n aqt-data logs job/vault-seed | tail -n 12

# P2 Production posture: overwrite the AAAA... dev markers with real random keys (KV v2 POST
# creates a new version — MUST run after vault-seed or the dev job would win). Also mints the
# OpenIddict PFX cert Secret the authserver chart mounts. Fresh-DB safe: this script only runs
# inside a from-zero reseed, so no existing rows were encrypted with the old key.
echo "[reseed] 5b/7 real prod-posture secrets (replaces dev markers)"
bash scripts/k8s-local-generate-prod-secrets.sh

echo "[reseed] 6/7 helm install app charts (db-migrate-job runs as a pre-install hook;"
echo "         sqlserver is already ready per step 4, so the hook does not race cold-start)"
"$HELM" upgrade --install aqtcorefw infra/helm/aqtcorefw -n aqt-app \
  -f infra/helm/aqtcorefw/values-local.yaml --wait --timeout 10m
# Alias Service `api` — web/nginx.conf proxies to the compose hostname api:8080
kubectl apply -f infra/k8s-local/api-alias-service.yaml
"$HELM" upgrade --install authserver infra/helm/authserver -n aqt-app \
  -f infra/helm/authserver/values-local.yaml --wait --timeout 5m
"$HELM" upgrade --install web infra/helm/web -n aqt-app \
  -f infra/helm/web/values-local.yaml --wait --timeout 5m

echo "[reseed] 7/7 smoke"
bash infra/k8s-local/smoke-k8s.sh

echo "[reseed] Done."
