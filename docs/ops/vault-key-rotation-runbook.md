# Vault Key Rotation Runbook

**Purpose:** Operational procedure for rotating encryption keys + secret material stored in HashiCorp Vault for AqtCoreFW.

**Compliance:** QĐ 742 cấp 4 mục 4.4.a — không hardcode credential, qua giao diện cấu hình.

**Audience:** Ops engineers + tenant DBAs.

**Template-repo note:** AqtCoreFW is a template repository with no live production environment. This runbook describes the pattern downstream projects must implement when adopting AqtCoreFW. Procedures assume a production Vault HA cluster + AppRole auth + TLS — those are infrastructure responsibilities (D10.5), not application scope.

---

## 1. Vault Topology Requirements

| Component | Production requirement |
|-----------|------------------------|
| Vault cluster | HA with ≥3 nodes, Raft or Consul backend |
| TLS | Required, certificate from internal CA or Let's Encrypt |
| Auth method | AppRole (per-host RoleId/SecretId), Token auth disabled |
| Audit log | File audit device + syslog forwarder |
| Seal | Auto-unseal via cloud KMS or transit (NEVER use Shamir in prod) |
| Backup | Daily snapshot to encrypted storage, 30-day retention |

The dev `docker-compose.yml` Vault service (`hashicorp/vault:latest` dev mode, root token `dev-only-token`) is for local development + integration tests ONLY. It runs in-memory; restarting the container loses all secrets. Bootstrap script `vault-init` re-seeds dev keys on each startup.

---

## 2. Secret Path Convention

All AqtCoreFW secrets live under `secret/data/aqt/*` in KV v2 mount `secret/`:

| Path | Content | Consumer |
|------|---------|----------|
| `secret/data/aqt/db/connection-string-default` | Primary DB conn string | Api + AuthServer |
| `secret/data/aqt/encryption/v{N}` | AES-256 field encryption key version N (32 bytes base64) | EncryptedStringConverter via VaultKeyResolver |
| `secret/data/aqt/audit/hmac-v{N}` | HMAC-SHA256 audit log chain key (base64 32B) | HmacChainService via VaultAuditHmacKeyResolver |
| `secret/data/aqt/encryption/email-pepper-{tenantId}` | Per-tenant email PII salt (Phase 04) | UserStore PII pipeline |
| `secret/data/aqt/redis/connection-string` | Redis conn string with password | InfrastructureRegistration |
| `secret/data/aqt/bus/rabbitmq-{key}` | RabbitMQ host/vhost/credentials | MessagingRegistration |
| `secret/data/aqt/signing/{providerKey}` | VN CA digital signature provider keys (Phase 05b) | IDigitalSignatureProvider impls |
| `secret/data/aqt/email/host` | SMTP relay hostname for alert notifications (`Email__Host`) | MailKitEmailSender via AlertEvaluationJob |
| `secret/data/aqt/email/password` | SMTP password (`Email__Password`); omit for an unauthenticated relay | MailKitEmailSender |

**Email keys are optional by design.** They are absent from `ProductionSecretValidator.RequiredKeys`
so a deployment that never uses an EMAIL alert rule still boots. The cost: nothing fails at startup
when they are missing. Outside Development, an EMAIL alert rule that breaches with `Email:Host` unset
logs Critical (naming the key) and does not deliver. Seed both keys before enabling any EMAIL alert
rule; leave `password` unset if the relay does not authenticate.

The application config keys map by replacing `:` with `/` and prefixing `aqt/`. Example: `Encryption:FieldKey` → reads `aqt/encryption/v{CurrentVersion}` value.

---

## 3. Key Rotation Procedure

### Pre-rotation checklist

- [ ] Two ops engineers present (2-person approval per QĐ 742 §4.4.a)
- [ ] Change-window approved by service owner
- [ ] Current key version verified: query `secret/data/aqt/encryption/v{N}` returns expected value hash
- [ ] Backup snapshot of Vault completed within last 24h
- [ ] Application healthy on `/health/ready` (Vault health check green)

### Rotation steps (encryption key v{N} → v{N+1})

1. **Generate new key material** (use `openssl rand -base64 32` — 32-byte AES-256 key).
2. **Write v{N+1} to Vault** via authorized Vault token:
   ```bash
   vault kv put secret/aqt/encryption/v$((N+1)) value="<base64-of-new-key>"
   ```
3. **Bump app config** (do NOT delete v{N} yet):
   ```json
   { "Encryption": { "CurrentVersion": <N+1> } }
   ```
   Push via deploy pipeline. Each application instance restart will pick up the new version.
4. **Restart application instances** rolling. Each restart:
   - Reads new `CurrentVersion` from config
   - On next encrypted write, prefixes cipher with `v{N+1}:` (per `EncryptedStringConverter`)
   - On read, dual-decode resolves prefix → fetches v{N+1} or v{N} key from Vault as needed
5. **Verify in production**:
   - Spot-check new MFA enrolment row → cipher starts with `v{N+1}:`
   - Existing v{N}-prefixed and no-prefix legacy rows still decrypt correctly
   - Log error rate normal, no `KeyNotFoundException` spikes
6. **Optional — re-encrypt existing rows** to v{N+1} via `LegacyCipherUpgrader.UpgradeColumnAsync` post-deploy hook. This is OPTIONAL — dual-decode keeps v{N} rows readable indefinitely. Reasons to re-encrypt:
   - Compliance audit requirement (every cipher must use latest key)
   - Want to remove v{N} from Vault for reduced blast radius
7. **Retain v{N}** in Vault. Do NOT delete until all rows are confirmed re-encrypted to v{N+1}+.

### Retention policy (N=3 minimum)

Keep last 3 key versions in Vault to allow rollback within 30 days. Older versions can be archived to cold storage with the same crypto-shredding policy.

### Audit HMAC key (`aqt/audit/hmac-v{N}`)

The audit log chain HMAC key follows the same path-version convention (`v{N}`) but **rotation is DEFERRED in this release**. Rotating the chain key in place silently invalidates `HmacChainService.VerifyAsync` on every historical entry because `audit.AuditLogEntries` has no per-entry `HmacKeyVersion` column. The rotation procedure above (encryption keys) **MUST NOT** be applied to `aqt/audit/hmac-v1` until a schema migration adding `HmacKeyVersion smallint NOT NULL` plus a `BuildFingerprint` update ships (tracked as backlog `FU-HMAC-ROTATION`). See `deployment-guide.md` §5 → "Audit HMAC key" for production seeding and the one-release legacy `Audit:HmacKey` config fallback.

---

## 4. Rollback Procedure

**Scope:** rollback safe ONLY before any v{N+1} writes hit production.

If rollback needed within first 1-5 minutes of rolling restart (no v{N+1} writes yet):

1. Bump app config back: `Encryption:CurrentVersion = N`
2. Push config change → rolling restart
3. Verify cipher prefix on new writes is `v{N}:`
4. Investigate root cause; do NOT delete v{N+1} from Vault yet (debug evidence)

**If v{N+1} writes have occurred**, rollback requires re-encrypting v{N+1} rows back to v{N} via `LegacyCipherUpgrader` running with reverse direction (custom logic required). DO NOT bump config alone — application will refuse to read v{N+1} ciphers without v{N+1} key in resolver.

---

## 5. Production Secret Validator Gate

`ProductionSecretValidator` runs at startup of both Api and AuthServer hosts. Required keys:

- `ConnectionStrings:Default`
- `Encryption:FieldKey`
- `Audit:HmacKey`

If any are null/whitespace/`DEV_*`-prefixed/known-dev-marker (`AAAA...=`), the host **refuses to start** with a clear error message naming the failing key.

This is the ENFORCEMENT of "không hardcode credential" — there is no Production deployment path that silently uses dev placeholders.

To deploy to Production:

1. Vault must be reachable (TLS + AppRole token)
2. Required keys must be populated under `secret/data/aqt/*`
3. App config (`appsettings.Production.json`) sets `Vault:Enabled=true`

The validator runs BEFORE `MigrateAsync` so a missing connection string yields a clear error instead of a SQL stack trace.

---

## 6. Logger Sanitization

`SecretMaskingDestructuringPolicy` (registered in Serilog pipeline of both hosts) masks property values whose key matches:

```regex
(Password|Key|Token|Secret|Vault|HmacKey|FieldKey)$
```

Case-insensitive, suffix-anchored. So `VaultAddr` is NOT masked (informational), but `VaultToken` IS masked.

Allowlist (NOT masked): `KeyVersion` (int, not a secret).

Test it locally:
```bash
docker compose logs api | grep -i password
# Should show "***" not actual values
```

---

## 7. Vault Auth Token Bootstrap

The bootstrap token (`VAULT_TOKEN` env var on application hosts) must:

- NEVER appear in process arguments, container labels, or stdout/stderr
- Be passed via secret-management system (Kubernetes Secret, Docker secrets, AWS SSM Parameter Store, etc.)
- Have minimal scope: read-only on `secret/data/aqt/*`
- Be rotated quarterly via AppRole `secret-id` re-issue (RoleId is stable per host)

Example AppRole policy (`vault policy write aqt-app -`):
```hcl
path "secret/data/aqt/*" {
  capabilities = ["read"]
}
path "secret/metadata/aqt/*" {
  capabilities = ["list"]
}
```

---

## 8. Dev Container Operations

### Start

```bash
cd infra/docker
docker compose up -d vault vault-init
```

`vault-init` seeds dev keys idempotently. Re-running is safe.

### Verify

```bash
curl -H "X-Vault-Token: dev-only-token" http://localhost:8200/v1/secret/data/aqt/encryption/v1
```

Should return JSON with `data.data.value` = base64 32-byte key.

### Reset

```bash
docker compose rm -f vault
docker compose up -d vault vault-init
```

In-memory storage is wiped on restart, then re-seeded by `vault-init`.

---

## 9. Audit Trail Requirements

Every key rotation MUST be logged in:

1. Vault audit device (file + syslog forwarder)
2. Operations changelog (e.g., internal Confluence / Notion)
3. Compliance dossier (`docs/compliance/qd-742-cap-4/phase-06/key-rotations.csv`)

Format:
```csv
timestamp,key_path,old_version,new_version,operator_a,operator_b,ticket_id
```

---

## 10. Emergency Response

**Vault unreachable in Production:**

- All hosts fail health probes (`/health/ready` returns Unhealthy)
- New requests fail on Vault-dependent paths (encryption ops)
- Existing instances continue serving from in-memory key cache (5-min TTL) until cache expires
- Page on-call ops + service owner immediately

**Suspected key compromise:**

1. Generate new key version v{N+1} (procedure §3)
2. Force re-encrypt all data via `LegacyCipherUpgrader`
3. Once verified, rotate Vault root token + AppRole secret-ids
4. Audit Vault access logs for unauthorized reads
5. File security incident per `docs/runbooks/security-incident-runbook.md`

---

## 11. Cross-References

- `docs/dependency-map.md` — module relationships
- `docs/runbooks/security-incident-runbook.md` — incident response
- `plans/260428-1740-cap-4-qd-742-compliance/phase-06-vault-finish.md` — implementation plan
- `plans/reports/secret-inventory-260510.md` — secret consumer inventory
