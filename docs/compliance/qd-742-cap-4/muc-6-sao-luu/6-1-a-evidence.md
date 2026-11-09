---
ReqID: "6.1.a"
title: "Giao diện thiết lập chính sách backup DB + cấu hình hệ thống"
muc: 6
phase: "05"
status: MET
generated: 2026-05-10
---

# 6.1.a — Giao diện thiết lập chính sách backup DB + cấu hình hệ thống

**Phase:** 05 | **Mục:** 6 — Sao lưu dự phòng

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Application/SecurityHealth/Queries/GetBackupHistoryStatusQuery.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Application/SecurityHealth/Queries/GetBackupHistoryStatusQuery.cs)
- Code: [`src/host/AqtCoreFW.Api/Controllers/Admin/SecurityHealthAdminController.cs`](../../../../src/host/AqtCoreFW.Api/Controllers/Admin/SecurityHealthAdminController.cs) — `GetBackupHistoryStatus` endpoint
- UI: `admin/security-health` — D7 backup history status panel

## Evidence Artifacts
- **Test:** `Qd742Phase05ComplianceTests.Compliance_D7_SecurityHealthAdminController_Has6Endpoints` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Test:** `Qd742Phase05ComplianceTests.Compliance_D7_QueryHandlers_AllExist` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Test:** `SecurityHealthQueriesTests.GetBackupHistoryStatus_SystemAdmin_Returns200` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/SecurityHealthQueriesTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/6-1-a-backup-policy-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
The D7 `GetBackupHistoryStatus` endpoint on `SecurityHealthAdminController` surfaces backup policy configuration (schedule, retention, last-run status) to admin dashboards. In the template-repo context, backup scheduling is delegated to the infra/DBA layer (SQL Server Agent jobs, Azure Backup, etc.); the compliance UI provides visibility into the last backup timestamp and policy status. The `admin/security-health` page renders this as a read-only status panel with color-coded health indicators.

## Verification Steps
1. GET `/api/admin/security-health/backup-history` as SystemAdmin — verify 200 with backup status payload.
2. GET as regular user — verify 403.
3. Verify `[RequiresMfa]` enforced on the endpoint.
