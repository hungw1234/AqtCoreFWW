#!/bin/bash
# Generates REAL (random 32-byte base64) encryption/HMAC/pepper secrets and publishes them to the
# in-cluster Vault, replacing the AAAA... dev markers that infra/docker/vault-init.sh seeds — the
# precondition for running the stack with ASPNETCORE_ENVIRONMENT=Production, where
# ProductionSecretValidator rejects dev markers.
#
# Two distinct Vault surfaces are written (they serve different readers):
#   1. aqt/* runtime paths        — VaultKeyResolver / VaultAuditHmacKeyResolver / pepper resolver
#      (aqt/encryption/v1, aqt/audit/hmac-v1, aqt/encryption/email-pepper-<tenant>)
#   2. secret/data/aqtcorefw      — VaultConfigurationProvider config path; keys use __ separators
#      (Encryption__FieldKey, Audit__HmacKey, Auth__ResourceClientSecret) and exist so the Production startup validator sees
#      non-marker values. Runtime crypto does NOT read these (legacy fallback only).
#
# ⚠️ KEY-ROTATION WARNING: data already encrypted in the DB with the OLD key becomes unreadable.
# Run this only right before a FULL reseed drill (scripts/k8s-local-reseed.sh) so the fresh DB is
# encrypted with the new key from the first row. Never run against a DB you want to keep.
#
# Secret values are never echoed. Idempotent: KV v2 POST creates a new version per run.
set -euo pipefail

VAULT_NS=aqt-data
VAULT_SVC=vault

# Root token comes from the cluster secret (created by k8s-local-create-secrets.sh) — not from args,
# so the value never lands in shell history.
VAULT_TOKEN="$(kubectl -n "$VAULT_NS" get secret aqt-data-secrets -o jsonpath='{.data.vault-root-token}' | base64 -d)"
if [ -z "$VAULT_TOKEN" ]; then
  echo "ERROR: could not read vault-root-token from aqt-data-secrets" >&2
  exit 1
fi

gen_key() { openssl rand -base64 32; }

# All writes run the `vault` CLI already inside the vault pod (probed 2026-07-23: image has
# busybox wget with NO --post-data, but ships /bin/vault) — no port-forward needed and values
# never transit the host network. VAULT_TOKEN is passed via env, not argv, so it does not show
# in `ps` inside the pod.
VAULT_POD="$(kubectl -n "$VAULT_NS" get pod -l app=vault -o jsonpath='{.items[0].metadata.name}')"

seed() {
  local path="$1"
  shift
  # -c vault: the pod also runs the `unseal` sidecar, so the target container must be explicit.
  kubectl -n "$VAULT_NS" exec -i -c vault "$VAULT_POD" -- env \
    VAULT_ADDR=http://127.0.0.1:8200 VAULT_TOKEN="$VAULT_TOKEN" \
    vault kv put -mount=secret "$path" "$@" > /dev/null
  echo "  ✓ ${path}"
}

FIELD_KEY="$(gen_key)"
HMAC_KEY="$(gen_key)"

echo "[gen-prod-secrets] Seeding runtime aqt/* paths (real random keys)..."
seed "aqt/encryption/v1"  "value=${FIELD_KEY}"
seed "aqt/audit/hmac-v1"  "value=${HMAC_KEY}"

# Per-tenant email peppers — same tenant list as vault-init.sh (must cover every seed TenantId,
# else email-hash backfill fails the whole batch). Each tenant gets its own random pepper.
TENANTS="00000000000000000000000000000000 00000000000000000000000000000001 a1b2c3d400000000000000000000b7c1 a1b2c3d400000000000000000000b7c2"
echo "[gen-prod-secrets] Seeding per-tenant email peppers..."
for tid in $TENANTS; do
  seed "aqt/encryption/email-pepper-${tid}" "value=$(gen_key)"
done

echo "[gen-prod-secrets] Seeding config path secret/data/aqtcorefw (validator-visible keys)..."
# Auth__ResourceClientSecret: introspection client secret shared by Api (presents it) and AuthServer
# (seeds/rotates the client with it). Required outside Development; the dev value is rejected.
RESOURCE_CLIENT_SECRET="$(gen_key)"
seed "aqtcorefw" "Encryption__FieldKey=${FIELD_KEY}" "Audit__HmacKey=${HMAC_KEY}" \
  "Auth__ResourceClientSecret=${RESOURCE_CLIENT_SECRET}"

# --- OpenIddict signing + encryption certificates (AuthServer non-Development branch) ----------
# Self-signed local CA stand-ins. Non-Development AuthServer refuses to boot without them
# (Program.LoadRequiredCertificate fail-fast). Mounted via extraVolumes in
# infra/helm/authserver/values-local.yaml at /certs. PFX password goes into the same Secret and
# reaches the app as env — never appsettings.
echo "[gen-prod-secrets] Generating OpenIddict signing/encryption PFX certs..."
CERT_DIR="$(mktemp -d)"
trap 'rm -rf "$CERT_DIR"' EXIT
PFX_PASSWORD="$(openssl rand -base64 24)"

make_pfx() {
  local name="$1" cn="$2"
  # "//CN=" (double slash): Git Bash on Windows path-mangles a leading "/CN=..." into
  # "C:/Program Files/Git/CN=..."; the MSYS double-slash escape yields the intended
  # "/CN=..." there and collapses to the same on real POSIX shells (probed 2026-07-23).
  openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes \
    -keyout "$CERT_DIR/$name.key" -out "$CERT_DIR/$name.crt" \
    -subj "//CN=$cn" > /dev/null 2>&1
  openssl pkcs12 -export -out "$CERT_DIR/$name.pfx" \
    -inkey "$CERT_DIR/$name.key" -in "$CERT_DIR/$name.crt" \
    -passout pass:"$PFX_PASSWORD"
}
make_pfx signing    aqt-authserver-signing-local
make_pfx encryption aqt-authserver-encryption-local

kubectl -n aqt-app create secret generic authserver-openiddict-certs \
  --from-file=signing.pfx="$CERT_DIR/signing.pfx" \
  --from-file=encryption.pfx="$CERT_DIR/encryption.pfx" \
  --from-literal=pfx-password="$PFX_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f - > /dev/null
echo "  ✓ k8s Secret authserver-openiddict-certs (aqt-app)"

echo "[gen-prod-secrets] Done. Values were NOT printed. Reminder: run a FULL reseed drill now —"
echo "                   any existing DB rows encrypted with the old key are unreadable."
