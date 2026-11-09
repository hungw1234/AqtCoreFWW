#!/bin/bash
# Creates the local-cluster Secrets from .env.k8s-local (gitignored — copy
# .env.k8s-local.example and fill in NEW values; never reuse committed dev passwords).
# Idempotent: dry-run render piped to apply.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env.k8s-local"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: $ENV_FILE not found. Copy .env.k8s-local.example and fill in values." >&2
  exit 1
fi

# shellcheck disable=SC1090
. "$ENV_FILE"

for var in K8S_SA_PASSWORD K8S_VAULT_ROOT_TOKEN K8S_MINIO_ROOT_USER K8S_MINIO_ROOT_PASSWORD K8S_GRAFANA_ADMIN_PASSWORD; do
  val="${!var:-}"
  if [ -z "$val" ] || [ "$val" = "CHANGE_ME" ]; then
    echo "ERROR: $var is unset or still CHANGE_ME in .env.k8s-local" >&2
    exit 1
  fi
done

# --- aqt-data: backing-service credentials -----------------------------------
kubectl -n aqt-data create secret generic aqt-data-secrets \
  --from-literal=sa-password="$K8S_SA_PASSWORD" \
  --from-literal=vault-root-token="$K8S_VAULT_ROOT_TOKEN" \
  --from-literal=minio-root-user="$K8S_MINIO_ROOT_USER" \
  --from-literal=minio-root-password="$K8S_MINIO_ROOT_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -

# --- aqt-app: external app secret --------------------------------------------
# `aqtcorefw-secrets` is the EXTERNAL secret the chart's env section references
# (values.yaml env: db-connection-string / redis-connection-string) — by design the
# chart does not render it (External Secrets Operator pattern; locally this script
# plays that role). The chart's own `<fullname>-secret` (Vault AppRole) renders
# separately and stays empty locally — the app falls back to VAULT_TOKEN env
# (VaultConfigurationProvider.cs:102), wired via extraEnv in values-local.yaml.
CONN="Server=sqlserver.aqt-data.svc.cluster.local,1433;Database=AqtCoreFW;User Id=sa;Password=${K8S_SA_PASSWORD};TrustServerCertificate=true;MultipleActiveResultSets=true"
kubectl -n aqt-app create secret generic aqtcorefw-secrets \
  --from-literal=db-connection-string="$CONN" \
  --from-literal=redis-connection-string="redis.aqt-data.svc.cluster.local:6379" \
  --from-literal=vault-token="$K8S_VAULT_ROOT_TOKEN" \
  --from-literal=audit-admin-connection-string="$CONN" \
  --from-literal=minio-access-key="$K8S_MINIO_ROOT_USER" \
  --from-literal=minio-secret-key="$K8S_MINIO_ROOT_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -
# minio-*: Secrets cannot be referenced across namespaces — the Files module pod (aqt-app)
# needs the same MinIO root creds that aqt-data-secrets holds for the MinIO Deployment.
# audit-admin: the unsigned-backlog readiness check needs the elevated (RLS-bypass)
# AuditAdmin connection; locally both roles are the same sa login so the same string
# serves. Prod provisions a distinct elevated login here.

# --- monitoring: Grafana admin (kube-prometheus-stack grafana.admin.existingSecret) ----
kubectl get ns monitoring > /dev/null 2>&1 || kubectl create ns monitoring
kubectl -n monitoring create secret generic grafana-admin \
  --from-literal=admin-user="admin" \
  --from-literal=admin-password="$K8S_GRAFANA_ADMIN_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -

echo "[k8s-local-create-secrets] Done."
