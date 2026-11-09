#!/bin/sh
# vault-unseal.sh — reconciles the local Vault's seal state, forever.
#
# Runs as a sidecar next to the file-storage Vault (infra/k8s-local/vault.yaml). File storage
# starts SEALED on every pod restart, so unsealing is a continuous concern, not a one-shot Job.
#
# Idempotent across every state it can observe:
#   uninitialized        -> operator init, persist unseal key + force the well-known root token
#   initialized + sealed -> unseal with the persisted key
#   unsealed             -> sleep and re-check (handles later re-seal / restart)
#
# LOCAL-ONLY: 1 key share, threshold 1, unseal key kept in a namespace Secret beside the vault.
# Production requires split key custody and a human unseal ceremony — see docs/deployment-guide.md.
#
# Required env: VAULT_ADDR, EXPECTED_ROOT_TOKEN
set -eu

: "${VAULT_ADDR:?VAULT_ADDR must be set}"
: "${EXPECTED_ROOT_TOKEN:?EXPECTED_ROOT_TOKEN must be set}"

# The unseal key lives on the SAME PVC as the data it unlocks, so the two can never drift apart:
# deleting the volume discards both together, which is exactly the local reset semantics we want.
# (The hashicorp/vault image ships NO curl — probed 2026-07-25 — so the Kubernetes-Secret route
# would need busybox wget, whose applet here has no --post-data. A file on the volume is simpler
# and has strictly better failure behaviour.)
KEY_FILE=/vault/file/unseal-key

log() { echo "[vault-unseal] $*"; }

# Prints the stored unseal key, or nothing when it has not been written yet.
read_stored_key() {
  [ -f "$KEY_FILE" ] && cat "$KEY_FILE" || true
}

# Persists the unseal key, then reads it back to PROVE it landed. Losing this key while the PVC
# keeps an initialized vault is unrecoverable (the vault can never be unsealed again), so a
# silent write failure is the worst outcome in this script — verify, never assume.
store_key() {
  key_plain="$1"
  ( umask 077; printf '%s' "$key_plain" > "$KEY_FILE" ) 2>/dev/null || true

  if [ "$(read_stored_key)" != "$key_plain" ]; then
    log "FATAL: could not persist the unseal key to $KEY_FILE."
    log "       Refusing to continue — an initialized vault whose key is lost is unrecoverable."
    return 1
  fi
  log "unseal key persisted to $KEY_FILE (read-back verified)"
}

# --- Wait for the vault process in the sibling container to accept connections --------------
# Any parseable status means the listener is up — including exit code 2 (sealed), which is the
# normal first state for file storage. Only a connection failure prints no JSON at all.
until vault status -format=json 2>/dev/null | grep -q '"sealed"'; do
  log "waiting for vault to listen on $VAULT_ADDR ..."
  sleep 2
done

# NOTE ON ERROR HANDLING IN THIS FUNCTION: the caller invokes it as `initialize || ...`, and a
# function in a `||` context runs with `set -e` DISABLED throughout its body — a `return 1` from
# a helper is silently discarded and execution continues. So every step that must not be skipped
# is checked explicitly here. Getting this wrong once already produced the worst outcome the
# script exists to prevent: an initialized vault whose unseal key was never stored.
initialize() {
  log "vault is uninitialized — running operator init (1 share / threshold 1, local-only)"
  if ! init_out="$(vault operator init -key-shares=1 -key-threshold=1 -format=json)"; then
    log "operator init failed; will retry"
    return 1
  fi

  # `-format=json` is PRETTY-printed, so the key sits on its own line inside the array and a
  # line-based pattern spanning `"unseal_keys_b64": [ "…"` never matches (probed 2026-07-25 —
  # this silently produced an empty key and left an initialized-but-unopenable vault).
  # Flatten to one line first, then extract.
  init_flat="$(printf '%s' "$init_out" | tr -d '\n\r\t ')"

  unseal_key="$(printf '%s' "$init_flat" \
    | sed -n 's/.*"unseal_keys_b64":\["\([^"]*\)".*/\1/p' | head -n 1)"
  init_root="$(printf '%s' "$init_flat" \
    | sed -n 's/.*"root_token":"\([^"]*\)".*/\1/p' | head -n 1)"

  # The vault is ALREADY initialized at this point — `operator init` is not undoable. From here
  # on, the unseal key exists only in this shell variable, so persisting it is the single most
  # urgent step: without it on disk the volume becomes permanently unopenable.
  if [ -z "$unseal_key" ] || [ -z "$init_root" ]; then
    log "FATAL: could not parse operator init output — the vault is now initialized but its"
    log "       unseal key could not be recovered from that output. Reset the volume:"
    log "       RESET_DATA=1 bash scripts/k8s-local-reseed.sh"
    return 1
  fi

  store_key "$unseal_key" || return 1

  if ! vault operator unseal "$unseal_key" > /dev/null; then
    log "unseal after init failed; will retry from the stored key"
    return 1
  fi
  log "vault unsealed after init"

  # `operator init` mints its own root token, which does NOT match the well-known value in
  # aqt-data-secrets that vault-init.sh / generate-prod-secrets.sh / the app all use. Mint a
  # non-expiring root token with that exact ID so no consumer needs to change.
  if ! VAULT_TOKEN="$init_root" vault token create \
      -id="$EXPECTED_ROOT_TOKEN" -policy=root -ttl=0 > /dev/null; then
    log "FATAL: could not install the well-known root token — consumers would all get 403."
    return 1
  fi
  log "well-known root token installed"

  # Dev-mode auto-enabled kv-v2 at secret/; file storage does not. Without this, every write
  # from vault-init.sh 404s and the app reports: Could not load secrets from Vault {"errors":[]}
  # Check the exit code separately: a 403 also produces no match, and treating that as
  # "not enabled" would mask an auth failure as a successful enable.
  secrets_list="$(VAULT_TOKEN="$EXPECTED_ROOT_TOKEN" vault secrets list -format=json 2>/dev/null)"
  if [ -z "$secrets_list" ]; then
    log "could not list secrets engines (auth or connectivity); will retry"
    return 1
  fi
  if ! printf '%s' "$secrets_list" | tr -d '\n\r\t ' | grep -q '"secret/"'; then
    if ! VAULT_TOKEN="$EXPECTED_ROOT_TOKEN" vault secrets enable -path=secret kv-v2 > /dev/null; then
      log "FATAL: could not enable kv-v2 at secret/ — seeding would 404."
      return 1
    fi
    log "kv-v2 secrets engine enabled at secret/"
  fi
}

unseal_existing() {
  stored_key="$(read_stored_key)"
  if [ -z "$stored_key" ]; then
    # Unrecoverable by design: an initialized file-storage vault whose only unseal key is gone
    # can never be opened. Keep logging instead of exiting so the operator can read this on a
    # running pod (an exit here would CrashLoop the sidecar and scroll the cause away).
    log "STUCK: vault is initialized but $KEY_FILE holds no unseal key."
    log "       The volume's data is unreachable without it. Reset the volume:"
    log "       RESET_DATA=1 bash scripts/k8s-local-reseed.sh"
    return 1
  fi
  vault operator unseal "$stored_key" > /dev/null
  log "vault unsealed from stored key"
}

# --- Reconcile loop -------------------------------------------------------------------------
# `vault status` uses exit codes as signal, NOT just failure: 0 = unsealed, 2 = sealed,
# 1 = genuine error. A sealed vault therefore "fails" while printing perfectly good JSON, so
# the exit code must be captured separately instead of treated as an error. The output is also
# pretty-printed (`"sealed": true`), so matching needs to tolerate whitespace after the colon.
json_flag() {
  # json_flag <field> <status-json> -> prints "true"/"false"/"" for that boolean field.
  # Flatten first: the JSON is pretty-printed, and every field must be matched independently
  # of how it is wrapped across lines.
  printf '%s' "$2" | tr -d '\n\r\t ' \
    | sed -n "s/.*\"$1\":\(true\|false\).*/\1/p" \
    | head -n 1
}

while true; do
  status="$(vault status -format=json 2>/dev/null)" || true

  initialized="$(json_flag initialized "$status")"
  sealed="$(json_flag sealed "$status")"

  # `|| true` keeps the reconciler alive under `set -e`: a failed attempt must be retried on the
  # next tick, not kill the sidecar (a dead sidecar cannot unseal after the next restart).
  if [ -z "$initialized" ] || [ -z "$sealed" ]; then
    log "unreadable vault status; retrying"
  elif [ "$initialized" = "false" ]; then
    initialize || log "init attempt failed; retrying"
  elif [ "$sealed" = "true" ]; then
    unseal_existing || true
  fi
  # sealed=false -> steady state, nothing to do

  sleep 10
done
