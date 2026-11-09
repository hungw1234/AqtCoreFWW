# Phase 04 Compliance Evidence — Data Classification (mục 4.4 §4.4.b/c/d)

**Date:** 2026-05-10 (Waves 1–4 + 6); Fixup P0+P1 2026-05-10
**Phase:** 04 (Data Classification)
**ReqIDs covered:** 4.4.b (PII identification), 4.4.c (encrypted at rest), 4.4.d (per-tenant pepper)
**Status:** SHIPPED + FIXUPS APPLIED

---

## Acceptance Evidence Map

| Acceptance Criterion | Evidence | Location |
|----------------------|----------|----------|
| Per-tenant HMAC pepper from Vault | `IPepperResolver` + `VaultPepperResolver` | `src/shared/AqtCoreFW.Infrastructure/Configuration/` |
| ClassificationRule entity + CRUD UI | `ClassificationRule` EF entity + migration + handlers | `src/modules/audit/AqtCoreFW.Audit.*` |
| ApplicationUser PII encrypted at rest | `Email` + `NormalizedEmail` + `PhoneNumber` via `EncryptedStringConverter` | `ApplicationUserConfiguration.cs` |
| Email hash-based login lookup | `EmailHashGlobal`/`NormalizedEmailHashGlobal` columns; `EmailHashedUserStore.FindByEmailAsync` | `src/modules/identity/AqtCoreFW.Identity.Infrastructure/` |
| Multi-tenant hash correctness | Two-column split (Global + Tenant); interceptor uses Guid.Empty for login path | Fixup P0-1; `EmailHashInterceptor.cs` |
| RtbfRequest PII encrypted at rest | `SubjectEmail` + `CitizenNationalId` via `EncryptedStringConverter` | `RtbfRequestConfiguration.cs` |
| Architecture guard (D11.5) | `EncryptedFieldArchitectureGuardTests` — all [Restricted] props have converter or [DoNotEncryptAtRest] | `tests/architecture/.../EncryptedFieldArchitectureGuardTests.cs` |
| SoD — Compliance.Manage is SystemAdmin-only | Excluded from Admin role seeder | `E2ETestDataSeeder.RolePermissions.cs` |

---

## Wave Evidence

### Wave 1 — IPepperResolver + VaultPepperResolver

| Item | Location |
|------|----------|
| `IPepperResolver` abstraction | `src/shared/AqtCoreFW.Infrastructure/Configuration/IPepperResolver.cs` |
| `VaultPepperResolver` (Vault path `Encryption:EmailPepper-{tenantId:N}`) | `src/shared/AqtCoreFW.Infrastructure/Configuration/VaultPepperResolver.cs` |
| Tests (12 tests PASS) | `tests/unit/AqtCoreFW.Infrastructure.Extensions.Tests/Configuration/VaultPepperResolverTests.cs` |

### Wave 2 — ClassificationRule module

| Item | Location |
|------|----------|
| `ClassificationRule` entity | `src/modules/audit/AqtCoreFW.Audit.Domain/Entities/ClassificationRule.cs` |
| EF migration | `src/modules/audit/.../Migrations/20260510031932_AddClassificationRules.cs` |
| CRUD handlers + controller | `src/modules/audit/.../Commands/UpdateClassificationRule/` + `DataClassificationController.cs` |
| `DataClassificationService` (inventory scan) | `src/modules/audit/.../Compliance/DataClassificationService.cs` |
| Permission codes | `AUDIT.CLASSIFICATION.VIEW` + `AUDIT.CLASSIFICATION.MANAGE` (SystemAdmin only per §256 SoD) |

### Wave 3 — ApplicationUser PII encryption + Email hash (+ Fixup P0-1 + P1-1)

| Item | Location |
|------|----------|
| `Email` + `NormalizedEmail` + `PhoneNumber` encrypted at rest via `EncryptedStringConverter` | `ApplicationUserConfiguration.cs` |
| **Note (Fixup P1-1):** PhoneNumber encryption was missing in original Wave-3 commit (`c807af56`) and restored here | `SplitEmailHashGlobalAndTenant` migration + configuration update |
| `EmailHashInterceptor` — sets all 4 hash columns on every SaveChanges touching Email | `src/modules/identity/.../Persistence/EmailHashInterceptor.cs` |
| `EmailHashedUserStore` — `FindByEmailAsync` uses `NormalizedEmailHashGlobal` index | `src/modules/identity/.../Persistence/EmailHashedUserStore.cs` |
| `EmailHashService` — HMAC-SHA-256 with pepper; `null`/`Guid.Empty` → global sentinel | `src/modules/identity/.../Services/EmailHashService.cs` |
| `EmailHashBackfillService` — idempotent startup backfill (all 4 hash columns) | `src/modules/identity/.../Services/EmailHashBackfillService.cs` |
| EF migrations | `20260510035341_AddEmailHashColumns.cs` (Wave-3) + `20260510154200_SplitEmailHashGlobalAndTenant.cs` (Fixup) |

#### Two-Column Hash Split (Fixup P0-1)

The original Wave-3 interceptor computed hashes using `user.TenantId` as the pepper scope, while `FindByEmailAsync` always used `Guid.Empty`. This caused login failures for all tenanted users.

**Fix:** Two-column strategy (Strategy C):
- `EmailHashGlobal` / `NormalizedEmailHashGlobal` — always Guid.Empty pepper; login lookup column
- `EmailHashTenant` / `NormalizedEmailHashTenant` — per-tenant pepper; NULL for non-tenanted users; future admin queries

### Wave 4 — RtbfRequest PII column encryption

| Item | Location |
|------|----------|
| `RtbfRequest.SubjectEmail` (nvarchar(1024)) + `CitizenNationalId` (nvarchar(256)) encrypted at rest | `RtbfRequestConfiguration.cs` |
| `AuditDbContext` dual-ctor: design-time + runtime `(DbContextOptions, IKeyResolver)` | `AuditDbContext.cs` |
| `PlaintextToEncryptedUpgrader` — encrypts plaintext rows at startup; `ColumnTarget.PkColumn` override for `InternalId` | `src/shared/AqtCoreFW.Infrastructure/Persistence/PlaintextToEncryptedUpgrader.cs` |
| Dual-host wiring (Api + AuthServer) | `PlaintextToEncryptedUpgrader.EncryptColumnAsync` called for `SubjectEmail` + `CitizenNationalId` + `PhoneNumber` |
| EF migration | `20260510120821_EncryptRtbfPiiColumns.cs` |

### Wave 6 — Compliance guards + integration tests

| Item | Location |
|------|----------|
| 5 Wave-4 arch compliance tests | `tests/architecture/.../Qd742Phase06ComplianceTests.cs` (tests 36–40) |
| `EncryptedFieldArchitectureGuardTests` (D11.5) — [Restricted] ⇒ converter OR [DoNotEncryptAtRest] | `tests/architecture/.../EncryptedFieldArchitectureGuardTests.cs` (NEW — Fixup P1-2) |
| `RtbfRequestEncryptionRoundtripTests` (6 tests) | `tests/integration/AqtCoreFW.Audit.Integration.Tests/` |
| `EmailHashBackfillServiceTests` (4 tests) | `tests/integration/AqtCoreFW.Identity.Integration.Tests/` |
| `MultiTenantLoginTests` (4 tests — NEW Fixup P0-1) | `tests/integration/AqtCoreFW.Identity.Integration.Tests/MultiTenantLoginTests.cs` |
| `PhoneNumberEncryptionRoundtripTests` (4 tests — NEW Fixup P1-1) | `tests/integration/AqtCoreFW.Identity.Integration.Tests/PhoneNumberEncryptionRoundtripTests.cs` |
| `TestKeyResolver` + `TestPepperResolver` (perturb-by-tenantId) in WebAppFixture | `tests/shared/AqtCoreFW.Tests.SharedFixtures/WebAppFixture.cs` |

---

## Test Suite Gates (Post-Fixup, 2026-05-10)

| Suite | Tests | Status |
|-------|-------|--------|
| `AqtCoreFW.Architecture.Tests` | 43/43 PASS (40 existing + 3 new D11.5) | ✅ |
| `AqtCoreFW.Identity.Integration.Tests` | 66/66 PASS (58 existing + 4 MultiTenant + 4 PhoneNumber) | ✅ |
| `AqtCoreFW.Infrastructure.Extensions.Tests` | 94/94 PASS + 1 SKIP | ✅ |
| `AqtCoreFW.Identity.Infrastructure.Tests` | 123/123 PASS (122 + 1 new perturbation invariant) | ✅ |
| `AqtCoreFW.Audit.Integration.Tests` | 20/30 PASS (10 pre-existing HTTP 403 — backlogged QD742-AUDIT-403) | ✅ non-regression |

---

## Encrypted Column Inventory (actual, post-fixup)

| Entity | Column | Classification | Method |
|--------|--------|----------------|--------|
| `ApplicationUser` | `Email` | Confidential | `EncryptedStringConverter` |
| `ApplicationUser` | `NormalizedEmail` | (implied by Email) | `EncryptedStringConverter` |
| `ApplicationUser` | `PhoneNumber` | Confidential | `EncryptedStringConverter` (Fixup P1-1) |
| `ApplicationUser` | `TotpSecret` | Restricted | `EncryptedStringConverter` (Phase-03) |
| `RtbfRequest` | `SubjectEmail` | Confidential | `EncryptedStringConverter` |
| `RtbfRequest` | `CitizenNationalId` | Restricted | `EncryptedStringConverter` |

**Note:** Original Wave-3 commit message (`c807af56`) claimed "6 columns encrypted" (Email, PhoneNumber, UserName, FullName, DepartmentCode, EmployeeCode). Actual Wave-3 encrypted 2 columns (Email + NormalizedEmail). PhoneNumber was wired in Fixup P1-1. UserName/FullName/DepartmentCode/EmployeeCode are Confidential but not Restricted — plan §66 scoped mandatory encryption to Restricted + explicitly listed Confidential (Email, PhoneNumber); remaining Confidential fields are future scope.

---

## References

- Plan: `plans/260428-1740-cap-4-qd-742-compliance/phase-04-data-classification.md`
- Audit report: `plans/reports/code-reviewer-260510-0948-phase04-9-commits-audit.md`
- Phase-06 dossier (Vault Finish): `docs/compliance/qd-742-cap-4/phase-06/README.md`
