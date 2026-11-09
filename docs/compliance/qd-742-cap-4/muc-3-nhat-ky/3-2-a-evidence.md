---
ReqID: "3.2.a"
title: "Giao diện quản lý chính sách nhật ký"
muc: 3
phase: "01"
status: MET
generated: 2026-05-10
---

# 3.2.a — Giao diện quản lý chính sách nhật ký

**Phase:** 01 | **Mục:** 3 — Nhật ký hệ thống

## Implementation Reference
- Code: [`src/modules/audit/AqtCoreFW.Audit.Presentation/Controllers/AuditRetentionPoliciesController.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Presentation/Controllers/AuditRetentionPoliciesController.cs) — GET/POST/PUT/DELETE endpoints
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditRetentionPolicy.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditRetentionPolicy.cs)
- UI: `admin/audit-retention-policies` (policy CRUD page, Phase-05)

## Evidence Artifacts
- **Test:** `Qd742Phase01ComplianceTests.Compliance_3_2_b_AuditRetention_PolicyEntityExists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase01ComplianceTests.cs)
- **Test:** `AuditRetentionPolicyApiTests.List_WithViewPermission_Returns200_WithApiResponseEnvelope` (project: AqtCoreFW.Audit.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Audit.Integration.Tests/AuditRetentionPolicyApiTests.cs)
- **Test:** `AuditRetentionPolicyApiTests.Create_WithManage_Returns200_AndPersistsRow` (project: AqtCoreFW.Audit.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Audit.Integration.Tests/AuditRetentionPolicyApiTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/3-2-a-audit-retention-policy-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`AuditRetentionPoliciesController` provides a full REST API (GET list, GET by id, POST, PUT, DELETE) for managing `AuditRetentionPolicy` entities. Each policy defines per-category retention rules (`RetentionDays`, `ArchiveBeforePurge`, `Category`). The `admin/audit-retention-policies` admin page (Phase-05) surfaces this as a CRUD UI. Cross-tenant access and global policy mutation are guarded at the handler level.

## Verification Steps
1. GET `/api/admin/audit-retention-policies` as SystemAdmin — verify 200 with policy list.
2. POST a new policy for `ConfigChange` category with `RetentionDays=365`.
3. Verify `ConfigChange` audit entry emitted for the policy creation.
