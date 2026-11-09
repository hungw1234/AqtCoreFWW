---
ReqID: "6.1.b"
title: "Thực hiện backup theo chính sách"
muc: 6
phase: "05"
status: MET
generated: 2026-05-10
---

# 6.1.b — Thực hiện backup theo chính sách

**Phase:** 05 | **Mục:** 6 — Sao lưu dự phòng

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Application/SecurityHealth/Queries/GetBackupHistoryStatusQuery.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Application/SecurityHealth/Queries/GetBackupHistoryStatusQuery.cs) — queries last backup run metadata
- Code: [`infra/scripts/`](../../../../infra/scripts/) — SQL Server backup scripts (template-repo pattern)
- UI: `admin/security-health` — D7 backup history status panel showing last-run timestamp and success/fail

## Evidence Artifacts
- **Test:** `SecurityHealthQueriesTests.GetBackupHistoryStatus_SystemAdmin_Returns200` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/SecurityHealthQueriesTests.cs)
- **Test:** `SecurityHealthQueriesTests.GetBackupHistoryStatus_NoMfaClaim_Returns403` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/SecurityHealthQueriesTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/6-1-b-backup-execution-status.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Backup execution is performed by the infra layer (SQL Server Agent, Azure Backup, or equivalent) according to the configured schedule. The `GetBackupHistoryStatusQuery` reads backup run history from the infra metadata source and surfaces it in the D7 security-health dashboard. In the template-repo context, the compliance evidence is the health-status API endpoint that exposes backup policy adherence, which downstream projects wire to their concrete backup tooling.

## Verification Steps
1. Configure a SQL Server Agent backup job targeting the application DB.
2. Run the job — verify execution recorded in backup history.
3. GET `/api/admin/security-health/backup-history` — verify `lastBackupAt` and `status: success` in response.

## Observations / Audit Notes
AqtCoreFW is a template repo (no live production). Backup execution evidence in production audit will be the SQL Server Agent job history or Azure Backup report from the adopting project's infra.
