---
ReqID: "3.2.b"
title: "Cấu hình thời gian lưu trữ nhật ký"
muc: 3
phase: "01"
status: MET
generated: 2026-05-10
---

# 3.2.b — Cấu hình thời gian lưu trữ nhật ký

**Phase:** 01 | **Mục:** 3 — Nhật ký hệ thống

## Implementation Reference
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditRetentionPolicy.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditRetentionPolicy.cs) — `RetentionDays`, `Category`, `ArchiveBeforePurge`
- Code: [`src/modules/audit/AqtCoreFW.Audit.Infrastructure/Jobs/AuditLogPurgeJob.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Infrastructure/Jobs/AuditLogPurgeJob.cs) — Hangfire recurring job that enforces retention policy
- Migration: `20260501010000_AddAuditLogBaseline` (includes `AuditRetentionPolicies` table)

## Evidence Artifacts
- **Test:** `Qd742Phase01ComplianceTests.Compliance_3_2_b_AuditRetention_PolicyEntityExists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase01ComplianceTests.cs)
- **Test:** `AuditRetentionPolicyApiTests.Update_FreshRowVersion_Returns204_AndPersistsChange` (project: AqtCoreFW.Audit.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Audit.Integration.Tests/AuditRetentionPolicyApiTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/3-2-b-log-retention-config.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`AuditRetentionPolicy.RetentionDays` specifies how long entries of a given `AuditCategory` are kept. `AuditLogPurgeJob` runs on a configurable Hangfire schedule, resolves the effective retention window per bucket (per-tenant policy takes precedence over global default), and purges or archives entries beyond the threshold. `ArchiveBeforePurge=true` moves entries to an archive table before deletion, satisfying audit trail preservation requirements.

## Verification Steps
1. Set `RetentionDays=30` for `AppAccess` category.
2. Manually invoke purge job — verify entries older than 30 days removed (or archived).
3. Confirm `SecurityHealthQueriesTests.GetAuditRetentionStatus_SystemAdmin_Returns200` passes.
