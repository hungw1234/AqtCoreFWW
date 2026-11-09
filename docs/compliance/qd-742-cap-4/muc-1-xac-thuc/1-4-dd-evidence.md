---
ReqID: "1.4.đ"
title: "Tự động vô hiệu hóa tài khoản khi vi phạm"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.4.đ — Tự động vô hiệu hóa tài khoản khi vi phạm

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/LockoutEscalationService.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/LockoutEscalationService.cs) — `OnLockoutAsync` increments `ConsecutiveLockoutCount`; sets `AutoDisabledAt` when escalation threshold reached
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs) — `EscalationLockoutCount`, `EscalationWindowHours`
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs) — `AutoDisabledAt`, `ConsecutiveLockoutCount`

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_4_d_CredentialPolicy_HasEscalationFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_4_d_ApplicationUser_HasAutoDisableFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_LockoutEscalationService_Exists` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `LockoutEscalationFlowTests.TripleLockout_WithinWindow_SetsAutoDisabledAfterEscalation` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/LockoutEscalationFlowTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/1-4-dd-auto-disable.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Beyond the per-attempt lockout (1.4.c), the escalation service permanently disables accounts that experience `EscalationLockoutCount` lockouts within `EscalationWindowHours`. `AutoDisabledAt` is set and an `AccountAutoDisabledIntegrationEvent` is published for downstream notification. Admin must explicitly unlock the account — no automatic re-enable. This satisfies the "tự động vô hiệu hóa" requirement for repeated violation.

## Verification Steps
1. Configure `EscalationLockoutCount=3, EscalationWindowHours=24`.
2. Trigger 3 separate lockout cycles within 24h.
3. Verify `AutoDisabledAt` set, subsequent login returns disabled error.
4. Verify `AccountAutoDisabledIntegrationEvent` published in outbox.
