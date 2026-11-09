---
ReqID: "6.2.b"
title: "Lưu trữ dữ liệu theo tên định dạng đối với từng loại"
muc: 6
phase: "04"
status: MET
generated: 2026-05-10
---

# 6.2.b — Lưu trữ dữ liệu theo tên định dạng đối với từng loại

**Phase:** 04 | **Mục:** 6 — Sao lưu dự phòng

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.Infrastructure/Persistence/EncryptedStringConverter.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Persistence/EncryptedStringConverter.cs) — `v{N}:` prefix format for encrypted columns
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs) — maps classified columns to converter
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/Entities/ClassificationRule.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/Entities/ClassificationRule.cs) — defines naming/format rules per classification tier
- Migration: `20260510035341_AddEmailHashColumns`, `20260510120821_EncryptRtbfPiiColumns`

## Evidence Artifacts
- **Test:** `Qd742Phase04DataClassificationComplianceTests.Compliance_4_4_Wave4_EncryptRtbfPiiColumns_MigrationExists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase04DataClassificationComplianceTests.cs)
- **Test:** `Qd742Phase06ComplianceTests.Compliance_4_4_a_EncryptedStringConverter_SupportsVersionPrefix` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase06ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/6-2-b-data-format-classification.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Classified data is stored in a well-defined versioned format per tier:
- **Restricted/Confidential columns**: stored as `v1:<base64-AES-256-GCM-ciphertext>` via `EncryptedStringConverter` — format enforced by the `v{N}:` prefix contract.
- **Hash-indexed columns** (`EmailHashGlobal`, `NormalizedEmailHashGlobal`): stored as HMAC-SHA-256 hex strings — format enforced by `EmailHashService`.
- **Plaintext columns** (Public/Internal tier): standard SQL column types per EF Core conventions.

`ClassificationRule` entities map entity+property names to their classification tier, enabling `DataClassificationService` to verify format compliance during inventory scans.

## Verification Steps
1. Inspect `AspNetUsers.Email` column — verify value starts with `v1:` prefix.
2. Inspect `AspNetUsers.EmailHashGlobal` — verify 64-char hex string (HMAC-SHA-256).
3. Run `Qd742Phase04DataClassificationComplianceTests` — all 5 tests pass.
