# Journal — QĐ 742 Cấp 4 Phase-01 Slice B

**Date:** 2026-05-01 ~01:15 ICT
**Branch:** main
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md`
**Slice:** B of A→D (after Slice A `3451b8a6`)

## What shipped

Entity + EF mapping changes — still zero DB schema impact (migrations come in Slice C):

- `src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditLogEntry.cs` — added `Category` (default `AppAccess`), `Severity` (default `Low`), `SchemaVersion` (default `2`) properties. Factory `Create(...)` extended with three optional params at the end → all 3 existing call sites (`AuditService`, `LogAuditEventCommandHandler`, unit test) remain BC.
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/Configurations/AuditLogEntryConfiguration.cs` — `Category` + `Severity` mapped via `HasConversion<int>()` (mirrors `SecurityIncidentConfiguration` precedent), `SchemaVersion` defaults to 2 in DB, two new indexes for category/severity filter patterns: `(TenantId, Category, OccurredAt)` and `(Severity, OccurredAt)`.
- `src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditRetentionPolicy.cs` — NEW aggregate. Inherits `AuditableEntity<Guid>` + `IAggregateRoot`. Fields: `TenantId?`, `Category?`, `RetentionDays`, `ArchiveBeforePurge`, `ArchivePath?`, `RowVersion`. Factory enforces positive RetentionDays and archive-path-required-when-archive-enabled invariants. Mutators: `UpdateRetention`, `EnableArchive`, `DisableArchive` — all call `MarkUpdated`.
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/Configurations/AuditRetentionPolicyConfiguration.cs` — NEW. Schema `audit`, RowVersion via `IsRowVersion()`, non-unique index `IX_AuditRetentionPolicies_TenantId_Category` (filtered uniqueness deferred — global-default uniqueness will be enforced in command handlers, Slice D).
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/AuditDbContext.cs` — `DbSet<AuditRetentionPolicy>` added; explicitly NOT in the immutable-entity list (retention policies are mutable by design — admin UI ships in Phase 05).

## Tests

- `tests/unit/AqtCoreFW.Audit.Domain.Tests/Entities/AuditLogEntryCategorySeverityTests.cs` — 8 tests: defaults, explicit category/severity overrides, schemaVersion=1 legacy replay, BC of legacy positional callers, private-set reflection guard.
- `tests/unit/AqtCoreFW.Audit.Domain.Tests/Entities/AuditRetentionPolicyTests.cs` — 15 tests: factory invariants, retention-days bounds, archive-path-required guard, archive enable/disable round-trip, RowVersion type guard.

**Results:** Audit.Domain.Tests 187/187 PASS (164 → 187, +23 new). Audit.Application.Tests 38/38 PASS (no regression from `Create` signature change). Audit.Infrastructure + Audit.Presentation build clean.

## Design decisions

1. **AuditCategory + AuditSeverity stored as `int` (not byte).** Plan called for byte-encoded storage, but `SecurityIncidentConfiguration` already maps `AuditSeverity` as `int`. Switching scale here would create cross-table heterogeneity for the same enum. Kept symmetric. Cost: 6 extra bytes/row, negligible.
2. **Default factory values match mapper fallback.** `AppAccess + Low` is exactly what `AuditCategoryMapper` returns for unknown actions. So an entry created without explicit values lands in the same bucket the mapper would assign — no surprise drift between automatic and explicit paths.
3. **`SchemaVersion = 2` shipped immediately.** No legacy v1 writers exist yet (HMAC v2 lives in Slice C). Defaulting to `2` now means every Slice B-and-later row carries the marker; Slice C verify-chain logic only needs to handle v1 for pre-Slice-C rows after backfill.
4. **AuditRetentionPolicy unique constraint deferred.** SQL Server filtered unique indexes for the (TenantId, Category) pair are workable but the global-default row (both null) sits outside any reasonable filter. Decision: keep the index non-unique, enforce uniqueness in `Create/UpdateRetentionPolicyCommandHandler` (Slice D) where the constraint is also surfaced as a meaningful FluentValidation message.
5. **`AuditRetentionPolicy` is mutable** (NOT in `AuditDbContext.IsImmutableAuditEntity`). Audit log *entries* are append-only; retention policies are admin-configurable.

## What's next (Slice C — high-risk)

- M1 EF migration `AddAuditCategoryAndSeverity_Backfill`: add the columns NULLABLE, batched UPDATE backfill (50k/batch), seed default `AuditRetentionPolicy` rows. NO trigger, NO NOT NULL flip.
- HMAC v2 in `AuditService` / `HmacAuditService` — payload includes `Category|Severity` after the existing fields. Verify-chain endpoint must handle dual schema based on `SchemaVersion`.
- Migration ordering test (`MigrationOrderingTests`) — assert M1 deploys clean even with existing rows, backfill leaves zero `Category IS NULL` rows.
- Smoke gate before Slice D: 24-48h staging watch, HMAC chain re-validates.

## Surprises / lessons

- **Optional params at the end of `Create(...)` are the cleanest BC strategy** for a 10-arg factory. Builder pattern would have been nicer but not worth a churn; named-arg call sites already handle the new params elegantly.
- **Index strategy:** `(TenantId, Category, OccurredAt)` was added on the assumption that the Phase 05 retention UI will paginate by category-per-tenant. If that turns out to be unused, the index is cheap to drop; if it's used, the query plan benefits massively.

## References

- Plan: `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md` (Slice plan A→D)
- Slice A commit: `3451b8a6`
- Mapper used by entity defaults: `src/modules/audit/AqtCoreFW.Audit.Domain/Services/AuditCategoryMapper.cs`
