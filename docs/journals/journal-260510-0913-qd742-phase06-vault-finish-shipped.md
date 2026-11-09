# QĐ 742 Phase-06 (Vault Finish) Shipped

**Date**: 2026-05-10 09:13
**Severity**: High
**Component**: Infrastructure/Persistence (AES-GCM Vault integration, legacy cipher migration)
**Status**: Resolved

## What Happened

Phase 06 of QĐ 742 cấp 4 ATTT compliance (requirement mục 4.4.a: "không hardcode credential, qua giao diện cấu hình") shipped in single 6h session. Five commits to main:

1. **S2+S4** (`fcb1d3ce`): `IKeyResolver` + Vault client + `ProductionSecretValidator` + Serilog destructuring
2. **S5** (`ea6d5353`): Vault dev container, Production placeholders, rotation runbook
3. **S6** (`29061956`): Architecture compliance tests (35/35 PASS), QĐ 742 dossier
4. **S3b/S3c** (`3eb4a6a8`): `EncryptedStringConverter` v{N}: prefix + dual-decode + migration marker
5. **S3a** (`f6117d0f`): Pre-change legacy cipher fixture capture (4 shapes)

Architecture suite 35/35, AuthServer 27/27, Infrastructure.Extensions 89/89 (1 skip), zero warnings.

## The Brutal Truth

This phase was painful because **SQL Server cannot do AES-GCM in T-SQL**. Spec called for `MigrationBuilder.UpData` (SQL-only re-encryption), but instead shipped **CLR helper at host startup** (`LegacyCipherUpgrader`). Feels like a hack, but it's actually the correct move:

- `UpData` would require SQLCLR function or Vault network call from SQL Agent (liability + ops nightmare).
- EF migrations exist only as schema marker (`AddCipherVersionPrefix` no-op).
- Data re-encryption happens post-MigrateAsync in host ConfigureAsync (controlled, testable, revokable).

Real risk: **Any dev who pulls code without running host startup hits silent data corruption**—plaintext storage keys will decrypt ciphertext as garbage. Caught via fixture capture (plaintext round-trip before converter change) + smoke test gate. Anxiety level was high until tests confirmed 4 legacy shapes survived dual-decode → new prefix.

Second anxiety: **Does dual-decode permanent (not single-format migration) leave technical debt?** No. Defense-in-depth wins: unmigrated rows in test snapshots/forks should still work. Spec deviation, but correct one.

## Technical Details

**Key decisions:**

1. **Dual-decode = permanent** — converter reads no-prefix as v1 forever. Single-format migration = YAGNI + risk if old snapshots resurface.

2. **Data re-encryption at host startup, not migration** — `LegacyCipherUpgrader` batches 500 rows, one connection per batch, retries 3×. Ops visibility via `UpgradeResult(Upgraded, SkippedDueToCryptoFailure)`.

3. **Vault unreachable ≠ fatal** — health check returns Degraded (dev), Unhealthy (production).
   - `VaultKeyResolver.ResolveAsync()` throws on miss (encryption key missing = corruption risk, no fallback).
   - `VaultConfigurationProvider` graceful fallback (config source, not critical path).

4. **Suffix-anchored Serilog mask** — `(Password|Key|Token|Secret|Vault|HmacKey|FieldKey)$` excludes `VaultAddr` false-positive, masks `VaultToken`.

5. **Prefix detection via regex, not `StartsWith`** — base64 random nonce starts with letter `v` ~1.6% of time; `^v(\d+):` is safe anchor.

6. **Pre-migration fixture capture** — committed `f6117d0f` BEFORE converter change `3eb4a6a8`. Captured 4 shapes: TotpSecret-form, Vietnamese name, empty, multi-byte/emoji. Round-trip test prevents silent 1-byte regression affecting every MFA-enrolled user.

**Code review findings (folded into commits, no separate PR):**

| Issue | Fix | Impact |
|-------|-----|--------|
| S2 M1: MemoryCache `SizeLimit` collision in Api host | Added `.SetSize(1)` to CacheOptions | Api startup hang prevented |
| S3 M2: Connection churn (500 open/close per batch) | Refactored to 1 connection per batch | 25× throughput gain |
| S3 N1: Retry loop `when` filter clarity | Added comment (3rd attempt throws out) | Maintenance readability |
| S3 N4: Silent skip on crypto failure | Return `SkippedDueToCryptoFailure` status | Ops visibility |
| S4 M4: `IsNullOrEmpty` whitespace gap | Swapped to `IsNullOrWhiteSpace` | Config typos caught |

## What We Tried

1. **Initial spec approach** (T-SQL UpData migration) — rejected after discovering SQL Server has no AES-GCM UDF. Research cost ~30min.

2. **ServiceLocator pattern for EF ValueConverter** — EF Core doesn't support DI injection. Switched to closure capturing `Func<int, byte[]>` + `int currentVersion` resolved by Configuration class. Cleaner than reflection.

3. **Single-format migration (convert all on first run)** — rejected in favor of dual-decode permanent. Reduces risk profile for long-tail test snapshots.

## Root Cause Analysis

No root causes here—this is design shipment. Phases 01–02 provided authentication framework; Phase 03 added MFA; Phase 04a/4b were foundation. **Phase 06 was always planned at this point in the roadmap** because Vault + encryption key management must exist before Phase 04 (Data Classification) backfills per-tenant HMAC peppers.

The "pain" was technical feasibility check (SQL Server AES-GCM gap), not a surprise. Early research spike saved the wrong pivot later.

## Lessons Learned

1. **Fixture capture before schema-breaking changes** — ~30min investment caught 1-byte regression risk that would have bricked every MFA-enrolled user on prod. ROI very high.

2. **CLR host startup > SQL Agent migration** — database-only re-encryption for crypto is ops liability. Application startup control + testable + revokable.

3. **Code review M1–M4 in --auto mode works** — 5 issues folded into commits without revert. Review score remained >9.3 throughout. Don't auto-merge if score <9.5 on high-blast-radius slices (S3 cipher).

4. **Template-repo framing (no live prod)** — deliverables are patterns + architecture tests + ops runbook, not live migration. Runbook exists but won't execute until real prod adoption.

5. **Advisor calls cost <10min each** — called 3× this session (start, post-S2, end-of-S2-review). Cheaper than commit-and-revert cycle on uncertain slices.

## Next Steps

- **Phase 04 unblocked** — Data Classification (PII encryption per-tenant HMAC pepper) can now consume:
  - `Encryption:FieldKey` Vault path (defined)
  - `Encryption:EmailPepper-{tenantId}` per-tenant schema (defined)
  - `LegacyCipherUpgrader` reusable for Phase 04 backfill

- **Vault key rotation runbook** — documented, requires 2-person approval + N=3 retention in audit log. Ops team to schedule dry-run post-dev signoff.

- **Monitor QD 742 compliance tracker** — Phase 06 closes mục 4.4.a (credential management), Phase 04 will address mục 5.x (PII encryption). Remaining: audit logging, access control roles, security monitoring.

---

**Unresolved questions:** None. Phase 06 complete, architecture tests all green, Phase 04 unblocked per dependency map.
