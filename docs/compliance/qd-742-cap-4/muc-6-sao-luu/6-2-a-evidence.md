---
ReqID: "6.2.a"
title: "Giao diện quản lý chính sách phân loại dữ liệu lưu trữ theo từng nhóm"
muc: 6
phase: "04"
status: MET
generated: 2026-05-10
---

# 6.2.a — Giao diện quản lý chính sách phân loại dữ liệu

**Phase:** 04 | **Mục:** 6 — Sao lưu dự phòng

## Implementation Reference
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/Entities/ClassificationRule.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/Entities/ClassificationRule.cs)
- Code: [`src/modules/audit/AqtCoreFW.Audit.Presentation/Controllers/DataClassificationController.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Presentation/Controllers/DataClassificationController.cs) — CRUD endpoints for classification rules
- Code: [`src/modules/audit/AqtCoreFW.Audit.Infrastructure/Compliance/DataClassificationService.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Infrastructure/Compliance/DataClassificationService.cs)
- Migration: `20260510031932_AddClassificationRules`
- UI: `admin/data-classification` (CRUD page — Phase-04)

## Evidence Artifacts
- **Test:** `Qd742Phase04DataClassificationComplianceTests.Compliance_4_4_Wave4_PlaintextToEncryptedUpgrader_ExistsWithPkColumnOverride` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Qd742Phase04DataClassificationComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/6-2-a-data-classification-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`ClassificationRule` entity defines data classification policies: classification tier (Public/Internal/Confidential/Restricted), applicable entity types, and handling rules (encrypt-at-rest, audit-on-access, mask-in-logs). `DataClassificationController` provides CRUD management UI at `admin/data-classification`, protected by `AUDIT.CLASSIFICATION.MANAGE` (SystemAdmin-only per §256 SoD). `DataClassificationService` performs inventory scans to enumerate entities that fall under each classification tier.

## Verification Steps
1. Navigate to `admin/data-classification` as SystemAdmin — verify rule list.
2. Create a rule classifying `ApplicationUser.Email` as `Restricted` — verify persisted.
3. Confirm `ConfigChange` audit entry emitted and `AUDIT.CLASSIFICATION.MANAGE` required (non-admin returns 403).
