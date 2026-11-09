---
ReqID: "3.2.c"
title: "Lưu ≥5 thông tin: (i) thời điểm, (ii) phân nhóm, (iii) mô tả thao tác/lỗi, (iv) đối tượng, (v) mức độ quan trọng"
muc: 3
phase: "01"
status: MET
generated: 2026-05-10
---

# 3.2.c — Lưu ≥5 thông tin trong mỗi bản ghi nhật ký

**Phase:** 01 | **Mục:** 3 — Nhật ký hệ thống

## Implementation Reference
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditLogEntry.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditLogEntry.cs) — `OccurredAt`, `Category`, `Description`, `EntityType`+`EntityId`, `Severity`
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/ValueObjects/AuditSeverity.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/ValueObjects/AuditSeverity.cs)

## Evidence Artifacts
- **Test:** `Qd742Phase01ComplianceTests.Compliance_3_2_c_AuditEvent_HasSeverityColumn` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase01ComplianceTests.cs)
- **Test:** `Qd742Phase01ComplianceTests.Compliance_3_1_a_AuditEvent_ImmutabilityChainExists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase01ComplianceTests.cs)
- **Audit log category:** all 5
- **Screenshot path:** `screenshots/3-2-c-audit-entry-fields.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`AuditLogEntry` stores all 5 mandatory fields per QĐ 742 §3.2.c:
- `OccurredAt` (DateTimeOffset UTC) → (i) thời điểm
- `Category` (AuditCategory enum) → (ii) phân nhóm
- `Description` (string) → (iii) mô tả thao tác/lỗi
- `EntityType` + `EntityId` → (iv) đối tượng tác động
- `Severity` (AuditSeverity enum: Low/Medium/High/Critical) → (v) mức độ quan trọng

Additionally stores `ActorId`, `TenantId`, `IpAddress`, and `HmacSignature` for complete audit trail. The architecture test verifies `Severity` column exists by reflection.

## Verification Steps
1. Run `Qd742Phase01ComplianceTests.Compliance_3_2_c_AuditEvent_HasSeverityColumn` — verify passes.
2. Perform any tracked command (e.g. update IP rule).
3. Inspect resulting `AuditLogEntry` — verify all 5 mandatory fields populated.
