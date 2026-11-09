# Secrets Management (HashiCorp Vault)

> Split out of `docs/deployment-guide.md` §5 on 2026-07-28 — that file had grown past the 800-line
> working limit and this was its single largest section (267 lines, ~30%). Content is unchanged;
> only heading levels were shifted up one. The deployment guide keeps §5 as a pointer here, and the
> numbering of §6-§9 is deliberately untouched so the `deployment-guide.md §N` references in
> `infra/helm/**/values*.yaml`, `infra/scripts/run-migrations.sh`, `infra/k8s-local/README.md` and
> the changelog stay correct.

**Related:** [`vault-key-rotation-runbook.md`](./vault-key-rotation-runbook.md) ·
[`rls-migration-login-runbook.md`](./rls-migration-login-runbook.md) ·
[`../deployment-guide.md`](../deployment-guide.md)

---

## Setup & Architecture

Vault replaces Kubernetes Secrets (which are base64-encoded, not encrypted). Secrets are:
- Stored in Vault transit engine (encrypted at rest)
- Injected by Vault Agent sidecar at pod startup
- Never written to disk
- Automatically rotated (database creds: 24h, API keys: 7d)

## Configuration in AqtCoreFW

**`appsettings.Production.json`:** Sanitized example (no secrets)
```json
{
  "Vault": {
    "Address": "https://vault.internal:8200",
    "Role": "aqtcorefw-api",
    "Namespace": "admin"
  }
}
```

**`Program.cs`:** VaultSharp configuration provider
```csharp
var vaultAddress = builder.Configuration["Vault:Address"];
var vaultRole = builder.Configuration["Vault:Role"];

var authMethod = new KubernetesAuthMethodInfo(role: vaultRole);
var vaultClientSettings = new VaultClientSettings(vaultAddress, authMethod);
var vaultClient = new VaultClient(vaultClientSettings);

// Mount secrets to configuration
builder.Configuration.AddVault(vaultClient, "secret/aqtcorefw");
```

## Vault Secret Paths

| Path | Content | Rotation |
|------|---------|----------|
| `secret/aqtcorefw/database` | SQL Server connection string, SA password | 24h (auto) |
| `secret/aqtcorefw/redis` | Redis password, connection string | 7d (manual) |
| `secret/aqtcorefw/minio` | AccessKey, SecretKey | 7d (manual) |
| `secret/aqtcorefw/rabbitmq` | Username, password | 24h (auto) |
| `secret/aqtcorefw/jwt` | RS256 signing key (PEM) | 90d (manual) |
| `secret/aqtcorefw/email` | MailKit SMTP password | 30d (manual) |
| **`secret/aqtcorefw/encryption/v{N}`** | **AES-256-GCM encryption key (Phase-06)** | **90d (manual rotation)** |
| **`secret/aqtcorefw/encryption/email-pepper-{tenantId}`** | **Per-tenant email pepper for hash (Phase-04)** | **90d (manual)** |
| **`secret/aqt/audit/hmac-v{N}`** | **HMAC-SHA256 audit log chain key (`HmacChainService` via `VaultAuditHmacKeyResolver`)** | **DEFERRED — see "Audit HMAC key" subsection** |
| **`secret/aqt/forms/external-ds/{VaultKeyId}/api-key`** | **Per-source API key for authenticated Forms external data sources (Phase-04); field: `value`** | **90d (manual)** |

## Kubernetes Auth Setup

```bash
# 1. Configure Vault Kubernetes auth method
vault auth enable kubernetes

vault write auth/kubernetes/config \
    kubernetes_host="https://10.0.0.1:443" \
    kubernetes_ca_cert=@/var/run/secrets/kubernetes.io/serviceaccount/ca.crt \
    token_reviewer_jwt=@/var/run/secrets/kubernetes.io/serviceaccount/token

# 2. Create policy for API
vault policy write aqtcorefw-api - <<EOF
path "secret/data/aqtcorefw/*" {
  capabilities = ["read"]
}
path "transit/encrypt/aqtcorefw" {
  capabilities = ["update"]
}
path "transit/decrypt/aqtcorefw" {
  capabilities = ["update"]
}
EOF

# 3. Create role binding
vault write auth/kubernetes/role/aqtcorefw-api \
    bound_service_account_names=aqtcorefw-api \
    bound_service_account_namespaces=gov-apps \
    policies=aqtcorefw-api \
    ttl=1h

# 4. Seed initial secrets
vault kv put secret/aqtcorefw/database \
    connection_string="Server=sql.internal;Database=AqtCoreFW;..." \
    sa_password="..."

vault kv put secret/aqtcorefw/redis \
    connection_string="redis.internal:6379" \
    password="..."

# Phase-06: Encryption Key Management
vault kv put secret/aqtcorefw/encryption/v1 \
    key="$(openssl rand -base64 32)"    # AES-256-GCM (32 bytes)

# Phase-04: Per-Tenant Email Pepper (one per active tenant)
vault kv put "secret/aqtcorefw/encryption/email-pepper-${TENANT_ID}" \
    pepper="$(openssl rand -base64 16)"  # 16-byte pepper for HMAC-SHA256
```

**Phase-06 Production Encryption Config:**
```csharp
// Program.cs — both Api + AuthServer
builder.Services.AddVaultConfiguration(builder.Configuration);  // Wires IKeyResolver + ProductionSecretValidator
builder.Services.AddScoped<ProductionSecretValidator>();       // Validates no DEV_* secrets in production
```

**Helm values override:**
```yaml
# helm/aqtcorefw-api/values.prod.yaml
config:
  Encryption__CurrentVersion: "1"
  Encryption__VaultPath: "secret/aqtcorefw/encryption"
```

**Key Rotation Runbook:** See `docs/ops/vault-key-rotation-runbook.md` for detailed rotation procedure (new key creation → deployment with CurrentVersion bump → background rekey job → retire old key).

### Audit HMAC key (`aqt/audit/hmac-v{N}`)

The audit-log chain HMAC-SHA256 key is sourced from Vault via `VaultAuditHmacKeyResolver` (`IAuditHmacKeyResolver`), separate from the AES encryption key path so it can be rotated independently in the future.

**Production setup (BEFORE first API host boot):**
```bash
vault kv put secret/aqt/audit/hmac-v1 value="$(openssl rand -base64 32)"
```

**Format:** base64-encoded 32 bytes (256 bits). The resolver decodes Base64 and rejects any other length; mismatch throws at boot.

**Active version config:**
```yaml
# helm/aqtcorefw-api/values.prod.yaml
config:
  Audit__Hmac__CurrentVersion: "1"   # only v1 supported this release
```

**Legacy fallback (one release only):** if Vault read fails (path missing, Vault unreachable) AND `Audit:HmacKey` is set in configuration, the resolver falls back to that legacy value (Base64 first, UTF-8 second) and emits a single `LogWarning` per process. The fallback path will be removed in the next release; tracked in backlog. Production deployments MUST seed the Vault path and SHOULD remove `Audit:HmacKey` from config to silence the warning.

**Rotation: DEFERRED.** Rotating the chain key in place silently breaks `HmacChainService.VerifyAsync` on every historical entry, because the current `audit.AuditLogEntries` schema has no per-entry `HmacKeyVersion` column. A future rotation playbook requires a schema migration adding that column plus a `BuildFingerprint` update; tracked as backlog `FU-HMAC-ROTATION`. Until then, treat the v1 key as long-lived and protect access tightly (read = AppRole for Api host only; write = ops break-glass).

**Drain-before-swap warning:** if the key source must be changed (legacy config → Vault, or any rotation), drain the Hangfire HMAC-compute queue first; in-flight jobs that span the swap will produce signatures that fail downstream verification. Dev DB has no compliance value — accept the chain break. Production MUST drain.

### Forms External Data Source API Keys (`secret/aqt/forms/external-ds/{VaultKeyId}/api-key`)

When a Forms external data source config sets `AuthMode`, the resolver reads a per-source API key from Vault at this path. `{VaultKeyId}` is a server-side identifier in the form template config (never client-supplied).

**Production setup (one per authenticated data source):**
```bash
vault kv put "secret/aqt/forms/external-ds/my-api-source/api-key" \
    value="<api-key-from-external-provider>"
```

**Format:** plaintext API key, stored in the KV v2 `value` field. The resolver decodes it and passes it to the external resolver; mismatch throws at runtime.

**Resolver behavior (fail-closed):** On Vault miss, unreachable, or `IVaultClient` not wired, the resolver returns no options and logs a single warning. Static/unauthenticated external data sources are unaffected. Authenticated sources do not fall back to static paths.

**Rotation:** Manual; update the value in Vault and redeploy (or the resolver will read the updated key on the next refresh).

### Per-Tenant HMAC Chain Signing & Recovery

**Overview:** Each tenant (and the system-wide NULL tenant) maintains an independent HMAC-SHA256 chain. Entries are ordered by a signer-assigned `SequenceId` (assigned during signing, not by InsertionOrder), enabling deterministic chain ordering regardless of INSERT timing. When unsigned entries accumulate, a backfill operation (run as `SystemAdmin`) re-signs all rows in the chain per-tenant.

**Background Job — `SignTenantChainAsync(Guid? tenantId)`:**
Runs every 5 minutes on the `audit` Hangfire queue (higher priority than generic background work). For each tenant:
1. Reads unsigned entries ordered by SequenceId (ascending).
2. Opens a **raw `SqlConnection`** (bypass pooling/RLS) with a dedicated **least-privilege** login (`AuditAdmin` connection string — falls back to `Default` if not configured).
3. Sets RLS context: `sp_set_session_context N'TenantId'` to the target tenant GUID (or NULL for system).
4. Sets session mode: `sp_set_session_context N'audit_mutate_mode'` to `'signature'` (enables the audit immutability trigger to write signatures).
5. Computes HMAC-SHA256 over each entry's canonical JSON + previous signature → `HmacSignature` column.
6. On success, persists. On failure, rolls back (SqlException clears session context automatically).
7. Clears session context in `finally` block.
8. Different tenants run in parallel (per-tenant serialization via SQL Server `sp_getapplock` on resource `audit-hmac:{tenantKey}` — no `[DisableConcurrentExecution]` to allow parallelism).

**Backfill (Re-Sign Existing Chain) — `POST /api/v1/audit/chain/backfill`:**
**Authorization:** `[Authorize(Policy=System.Config.Manage)]` (SystemAdmin role) + RLS class `Audit.Log.View`.

**Operation:**
- Returns **202 Accepted** immediately with a Hangfire job ID (async, not inline).
- Enqueues `IBackgroundAuditJobEnqueuer.EnqueueBackfillChain(Guid triggeredByUserId, Guid? triggeredByTenantId, string triggeredByUserName, string? triggeredByIp, string? correlationId)` → `HangfireAuditJobEnqueuer.EnqueueBackfillChain(…)`. Captures caller identity + IP + correlation ID at the HTTP boundary for forensic audit trail.
- Background job iterates every tenant (including NULL system tenant) via an **elevated connection** (`ConnectionStrings:AuditAdmin`, db_owner or higher — needed to read across tenants under RLS).
- Re-computes EVERY entry's SequenceId (1..N per tenant) + HMAC in one transaction per tenant.
- Writes ONE audit.AuditLogEntries record (type `chain-backfill`) with captured actor, IP, correlation ID, and enriched `DataSnapshot`: `{ rowsReSignedTotal, callerIp, correlationId, tenantChains: [{ tenantId, rows, preHead, postHead }, ...] }`.

**Health Check — `audit-unsigned-backlog`:**
New health endpoint that monitors unsigned entries. Unhealthy if:
- Unsigned-row count > `MaxUnsignedCount` (default 100), OR
- Oldest unsigned entry's age > `MaxOldestUnsignedAge` (default 10 minutes).

Configuration section: `Audit:HealthChecks:UnsignedBacklog`.

Uses the same **elevated connection** (`AuditAdmin`) to read across tenants.

**Configuration:**
```yaml
# appsettings.Production.json
ConnectionStrings:
  Default: "Server=...;Initial Catalog=AqtCoreFW;..."              # Least-privilege runtime login
  AuditAdmin: "Server=...;Integrated Security=true;..."            # db_owner-capable admin login
Audit:
  HealthChecks:
    UnsignedBacklog:
      MaxUnsignedCount: 100          # Threshold for Unhealthy status
      MaxOldestUnsignedAge: "00:10:00"  # TimeSpan (10 minutes)
```

In **development/testing**, `AuditAdmin` falls back to `Default` (no separate login needed).

**Verifying Chain Health:**
```sql
-- Count unsigned entries per tenant
SELECT TenantId, COUNT(*) AS UnsignedCount, MIN(OccurredAt) AS OldestUnsigned
FROM audit.AuditLogEntries
WHERE HmacSignature IS NULL OR HmacSignature = ''
GROUP BY TenantId
ORDER BY TenantId;

-- Expected: 0 rows (all signed) or very recent rows (<1 min old) still in the compute window
```

**Recovery After Key Rotation or Fault:**
When the Vault path is missing or the `AuditAdmin` connection is misconfigured, the background job throws on key retrieval or connection open. Hangfire retries 10 times, then parks the job in the `Failed` state.

1. **Repair the key source** (seed Vault → `vault kv put secret/aqt/audit/hmac-v1 value="<base64-key>"`) or configure `ConnectionStrings:AuditAdmin`.
2. **Open** `https://{authserver-host}/hangfire/failed` (auth: `SystemAdmin` required in all environments).
3. **Filter** to `HangfireAuditJobEnqueuer.EnqueueBackfillChain` jobs (or `SignTenantChainAsync` if requeuing mid-signing).
4. **Bulk-select** all (order does not matter for backfill, but per-tenant signing serializes anyway).
5. **Click "Requeue jobs"**. The job is idempotent (re-computing signatures over identical data yields identical HMACs) so re-execution is safe.
6. **Monitor** `/health/ready` and structured logs — unsigned-entry count should drop to 0 within seconds.

**Verification — chain converges:**
```sql
SELECT COUNT(*) AS Pending
FROM audit.AuditLogEntries
WHERE HmacSignature IS NULL OR HmacSignature = '';
-- Expected: 0 after replay (or only rows < 5s old still in normal compute window).
```

### Dev Vault re-seed after restart

The dev `vault` container in `infra/docker/docker-compose.yml` uses in-memory storage (`VAULT_DEV_LISTEN_ADDRESS`) — every container restart wipes all KV paths. The `vault-init` sidecar only runs once on `docker compose up`; restarting just the `vault` service leaves the API in a `permission denied` loop on every HMAC compute / encryption read.

Re-seed manually with the existing init script (idempotent — KV v2 versions on re-write):

```bash
docker compose -f infra/docker/docker-compose.yml run --rm vault-init
```

This re-uses the same `curlimages/curl` image + bind-mounted `vault-init.sh` declared in the compose file. After it exits, the API recovers on the next resolver call (5-minute cache TTL, or restart API for instant clear). For production this never applies — Vault HA stores secrets on disk + replicates across cluster nodes.

> Not auto-restarted on `vault` healthcheck flip per YAGNI — restart is rare (only on full container recreate). Backlog `P3b-FU2` upgrades this to a healthcheck-dependent sidecar if pain recurs.

**Phase-04 Data Classification Config:**
```yaml
# helm/aqtcorefw-api/values.prod.yaml
config:
  DataClassification__RlsEnabled: "true"
  DataClassification__AuditRestrictedFieldAccess: "true"
```

Role-based access gate: Compliance.Manage permission required for `/admin/data-classification` UI. SystemAdmin-only. SQL RLS enforced at database layer via SESSION_CONTEXT.

## Helm Values for Vault Integration

```yaml
# helm/aqtcorefw-api/values.prod.yaml
vault:
  enabled: true
  address: "https://vault.internal:8200"
  role: "aqtcorefw-api"
  secretPath: "secret/aqtcorefw"
  syncInterval: "60s"

podAnnotations:
  vault.hashicorp.com/agent-inject: "true"
  vault.hashicorp.com/agent-inject-secret-database: "secret/data/aqtcorefw/database"
  vault.hashicorp.com/agent-inject-template-database: |
    {{- with secret "secret/data/aqtcorefw/database" -}}
    export ConnectionStrings__Default="{{ .Data.data.connection_string }}"
    {{- end }}
  vault.hashicorp.com/agent-inject-secret-redis: "secret/data/aqtcorefw/redis"
  vault.hashicorp.com/role: "aqtcorefw-api"
```
