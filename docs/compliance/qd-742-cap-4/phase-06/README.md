# Phase 06 Compliance Evidence — Vault Finish (mục 4.4.a)

**Date:** 2026-05-10
**Phase:** 06 (Vault Finish)
**ReqID covered:** 4.4.a — "Không hardcode credential, qua giao diện cấu hình"
**Status:** SHIPPED

---

## Acceptance Evidence Map

| Acceptance Criterion | Evidence | Location |
|----------------------|----------|----------|
| Production build reads ALL secrets from Vault, fails startup if Vault unreachable | `ProductionSecretValidator` startup gate (tests pass) | `src/shared/AqtCoreFW.Infrastructure/Configuration/ProductionSecretValidator.cs` + `tests/unit/AqtCoreFW.Infrastructure.Extensions.Tests/Configuration/ProductionSecretValidatorTests.cs` (16 tests PASS) |
| Dev build with fallback (DEV_ markers) | Validator no-op in non-Production | Same files |
| Encrypted columns dual-version support | `EncryptedStringConverter` v{N}: prefix + dual-decode permanent | `src/shared/AqtCoreFW.Infrastructure/Persistence/EncryptedStringConverter.cs` + `tests/.../Persistence/EncryptedStringConverterVersioningTests.cs` (9 tests PASS) |
| Vault config never logged (sanitization) | `SecretMaskingDestructuringPolicy` Serilog hook | `src/shared/AqtCoreFW.Infrastructure/Logging/SecretMaskingDestructuringPolicy.cs` + `tests/.../Logging/SecretMaskingDestructuringPolicyTests.cs` (19 tests PASS) |
| Ops runbook for key rotation | Published | `docs/ops/vault-key-rotation-runbook.md` (~250 lines) |
| Compliance regex scan | `Qd742Phase06ComplianceTests.Compliance_4_4_a_ApplicationSettings_NoPlaintextSecretsInBaseFile` | `tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs` |

---

## Test Suite Gate Results (2026-05-10)

| Suite | Tests | Status |
|-------|-------|--------|
| `AqtCoreFW.Architecture.Tests` (incl. 9 new Phase-06 compliance) | **35/35 PASS** | ✅ |
| `AqtCoreFW.AuthServer.Tests` | **27/27 PASS** | ✅ |
| `AqtCoreFW.Infrastructure.Extensions.Tests` | **89/89 PASS + 1 SKIP** | ✅ |

Builds: `Api` 0E/0W, `AuthServer` 0E/0W, `Infrastructure` 0E/0W.

---

## Vault Wiring Inventory

### Hosts

| Host | Vault config provider | Validator | Cipher upgrader |
|------|-----------------------|-----------|-----------------|
| `Api/Program.cs` | ✅ AddVaultConfiguration (existing, line 75-87) | ✅ ValidateProductionSecrets (post-Build) | ✅ LegacyCipherUpgrader for `files.FileRecords.UploaderDisplayName` |
| `AuthServer/Program.cs` | ✅ AddVaultConfiguration (D10.1 lifted from Api) | ✅ ValidateProductionSecrets (post-Build) | ✅ LegacyCipherUpgrader for `identity.AspNetUsers.TotpSecret` |

### New types

| Type | Path | Lifetime |
|------|------|----------|
| `IKeyResolver` | Configuration/IKeyResolver.cs | abstraction |
| `VaultKeyResolver` | Configuration/VaultKeyResolver.cs | Singleton, IMemoryCache 5-min sliding |
| `VaultHealthCheck` | Configuration/VaultHealthCheck.cs | Registered on `/health/ready` |
| `ProductionSecretValidator` | Configuration/ProductionSecretValidator.cs | Static extension |
| `SecretMaskingDestructuringPolicy` | Logging/SecretMaskingDestructuringPolicy.cs | Singleton via Serilog config |
| `LegacyCipherUpgrader` | Persistence/LegacyCipherUpgrader.cs | Static helper |

### Migrations

| Module | Migration | Type |
|--------|-----------|------|
| Identity | `20260510010458_AddCipherVersionPrefix` | No-op schema marker (T-SQL cannot do AES-GCM) |
| Files | `20260510010550_AddCipherVersionPrefix` | No-op schema marker |

Actual data re-encryption runs via `LegacyCipherUpgrader.UpgradeColumnAsync` post-`MigrateAsync` in each host startup. Idempotent (`WHERE NOT LIKE 'v[0-9]%:%'`).

---

## Secret Inventory

See `plans/reports/secret-inventory-260510.md` (382 lines, 34 secrets enumerated).

Migration tier ordering:
- TIER 0 (Phase 04 blocker): `Encryption:FieldKey`, `Audit:HmacKey`, `Auth:ResourceClientSecret`
- TIER 1 (pre-prod): `ConnectionStrings:Default`
- TIER 2-3 (conditional/deferred): Bus, Cache, Search, AI, SMTP, Signing

---

## D10 Pre-Cook Decisions Summary

| ID | Decision | Outcome |
|----|----------|---------|
| D10.1 | AuthServer in scope | Same Vault wiring pattern lifted from Api |
| D10.2 | One-time migration re-encrypt | Implemented as `LegacyCipherUpgrader` (CLR-side, T-SQL cannot do GCM); converter dual-decode permanent |
| D10.3 | Vault dev container | `hashicorp/vault:latest` added to `infra/docker/docker-compose.yml` (root token `dev-only-token`, KV v2 mount `secret/`) |
| D10.4 | Drop A1 (config order) | Verified .NET IConfiguration adds-later-wins; Vault is highest-priority as registered |
| D10.5 | Vault HA + TLS deferred | Documented in runbook §1; not implemented (infra responsibility) |

---

## Template-Repo Framing

Per memory `aqtcorefw-template-dev-only.md`: AqtCoreFW is a template repo with no live production. Phase 06 deliverables are **patterns + evidence** for compliance audit (working code path, runbook, tests, dev container parity), NOT a live ops migration. Downstream projects adopting the template handle their own prod rollout per `docs/ops/vault-key-rotation-runbook.md`.

---

## Phase 06 → Phase 04 Unblock

Phase 04 (Data Classification, Cấp-4 NEW) was blocked by Phase 06 per P0-3 (PII encryption needs Vault key + per-tenant HMAC pepper from Vault). With Phase 06 shipped:
- `Encryption:FieldKey` Vault path defined → Phase 04 PII encryption can adopt
- `Encryption:EmailPepper-{tenantId}` per-tenant path schema defined → Phase 04 P1-2 unblocked
- `LegacyCipherUpgrader` pattern reusable for Phase 04 backfill of new encrypted columns

---

## Phase 04 Evidence

Phase-04 Data Classification wave evidence (PII encryption, email hashing, ClassificationRule CRUD,
multi-tenant login fixup, architecture guards) is documented in:

**`docs/compliance/qd-742-cap-4/phase-04/README.md`**

Phase-06 unblocked Phase-04 by providing:
- `Encryption:FieldKey` Vault path → Phase-04 PII encryption adopted `IKeyResolver`
- `Encryption:EmailPepper-{tenantId}` per-tenant path schema → Phase-04 P1-2 email hash
- `LegacyCipherUpgrader` pattern reusable for Phase-04 backfill of new encrypted columns

---

## References

- Plan: `plans/260428-1740-cap-4-qd-742-compliance/phase-06-vault-finish.md`
- Amendments: `plans/260428-1740-cap-4-qd-742-compliance/amendments-log.md` §D10
- Runbook: `docs/ops/vault-key-rotation-runbook.md`
- Inventory: `plans/reports/secret-inventory-260510.md`
- Code review reports: see code-reviewer agent transcripts (S2 9.0→post-fix, S3 9.2, S4 9.2→post-fix)
- Phase-04 dossier: `docs/compliance/qd-742-cap-4/phase-04/README.md`
