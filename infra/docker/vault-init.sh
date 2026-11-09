#!/bin/sh
# vault-init.sh — Seeds dev encryption keys + HMAC + per-tenant email peppers into Vault KV v2.
# Idempotent: KV v2 POST creates new versions on re-run — safe to re-execute.
# Runs inside `curlimages/curl` (alpine /bin/sh — POSIX only, no bash-isms).
#
# Required env: VAULT_ADDR, VAULT_TOKEN
# Tenants seeded match OpenIddictDataSeeder.cs dev seeder:
#   - 00000000000000000000000000000000 (Guid.Empty — global / cross-tenant login lookup)
#   - 00000000000000000000000000000001 (devTenantId — admin/officer/viewer dev users)

set -eu

: "${VAULT_ADDR:?VAULT_ADDR must be set}"
: "${VAULT_TOKEN:?VAULT_TOKEN must be set}"

# Fixed dev keys — 32 bytes base64. NEVER use in production.
DEV_AES_KEY="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
# Distinct last char from DEV_AES_KEY so a stray grep/log dump can tell them apart.
DEV_AUDIT_HMAC_KEY="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB="
DEV_PEPPER="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="

# Dev tenants (Guid "N" format — 32 hex chars, no hyphens).
# MUST cover every TenantId present in dev/E2E seed data — backfill fails the entire
# batch if any user's tenant pepper is missing (see EmailHashBackfillService).
#   - 0000...0000  Guid.Empty — cross-tenant login lookup (FindByEmailAsync global pepper)
#   - 0000...0001  devTenantId — admin/officer/viewer dev users (OpenIddictDataSeeder)
#   - a1b2c3d4...b7c1  TenantSeedProfiles.BoTaiChinh — BTC tenant (10k test users)
#   - a1b2c3d4...b7c2  TenantSeedProfiles BNV tenant — demo tenant seeded by reset-all
DEV_TENANTS="00000000000000000000000000000000 00000000000000000000000000000001 a1b2c3d400000000000000000000b7c1 a1b2c3d400000000000000000000b7c2"

seed() {
  path="$1"
  value="$2"
  curl -sS -f -X POST \
    -H "X-Vault-Token: $VAULT_TOKEN" \
    -d "{\"data\":{\"value\":\"$value\"}}" \
    "$VAULT_ADDR/v1/secret/data/$path" > /dev/null
  echo "  ✓ $path"
}

echo "[vault-init] Seeding dev encryption key v1..."
seed "aqt/encryption/v1" "$DEV_AES_KEY"

echo "[vault-init] Seeding dev audit HMAC key v1..."
seed "aqt/audit/hmac-v1" "$DEV_AUDIT_HMAC_KEY"

echo "[vault-init] Seeding dev email peppers..."
for tid in $DEV_TENANTS; do
  seed "aqt/encryption/email-pepper-$tid" "$DEV_PEPPER"
done

echo "[vault-init] Done."
