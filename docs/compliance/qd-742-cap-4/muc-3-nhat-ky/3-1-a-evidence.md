---
ReqID: "3.1.a"
title: "Cung cấp chức năng ghi nhật ký"
muc: 3
phase: "01"
status: MET
generated: 2026-05-10
---

# 3.1.a — Cung cấp chức năng ghi nhật ký

**Phase:** 01 | **Mục:** 3 — Nhật ký hệ thống

## Implementation Reference
- Code: [`src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditLogEntry.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Domain/Entities/AuditLogEntry.cs)
- Code: [`src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/AuditLogRepository.cs`](../../../../src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/AuditLogRepository.cs)
- Code: [`src/shared/AqtCoreFW.SharedKernel/Application/Behaviors/AuditBehavior.cs`](../../../../src/shared/AqtCoreFW.SharedKernel/Application/Behaviors/AuditBehavior.cs) — MediatR pipeline behavior that auto-logs all `ICommand` operations
- Migration: `20260501010000_AddAuditLogBaseline` (Phase-01)

## Evidence Artifacts
- **Test:** `Qd742Phase01ComplianceTests.Compliance_3_1_a_AuditEvent_ImmutabilityChainExists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase01ComplianceTests.cs)
- **Audit log category:** `AppAccess`, `AdminLogin`, `AccountManagement`, `ConfigChange`, `Error`
- **Screenshot path:** `screenshots/3-1-a-audit-log-function.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`AuditBehavior<TRequest, TResponse>` is a MediatR pipeline behavior registered for all modules. It intercepts every `ICommand` dispatch, creates an `AuditLogEntry` with category, actor, tenant, target entity, description, severity, and HMAC signature, then persists via `AuditLogRepository`. The HMAC chain (each entry's signature covers the previous entry's hash) provides tamper-evidence per §3.1.a immutability requirement.

## Verification Steps
1. Execute any state-changing command (e.g. create a user).
2. Query `GET /api/audit/logs` — verify corresponding entry present with correct category and actor.
3. Attempt direct SQL UPDATE on `audit.AuditLogEntries` — verify trigger `tr_AuditLog_NoMutate` raises error.
