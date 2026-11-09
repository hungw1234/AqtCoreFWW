---
ReqID: "1.4.c"
title: "Tự động ngăn cản đăng nhập tự động khi vi phạm"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.4.c — Tự động ngăn cản đăng nhập tự động khi vi phạm

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/TenantAwareUserManager.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/TenantAwareUserManager.cs) — `AccessFailedAsync` applies per-tenant `MaxFailedAttempts` + `LockoutMinutes`
- Code: [`src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs`](../../../../src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs) — checks `IsLockedOut` before token issuance

## Evidence Artifacts
- **Test:** `LockoutEscalationFlowTests.TripleLockout_WithinWindow_SetsAutoDisabledAfterEscalation` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/LockoutEscalationFlowTests.cs)
- **Test:** `LockoutEscalationFlowTests.FirstLockout_SetsCounterTo1` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/LockoutEscalationFlowTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/1-4-c-auto-lockout.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
After `MaxFailedAttempts` consecutive failed logins, `TenantAwareUserManager.AccessFailedAsync` sets `LockoutEnd = UtcNow + LockoutMinutes`. The AuthServer's `AuthorizationController` rejects token issuance with a unified error message (OWASP A04 — no user-enumeration leak) until `LockoutEnd` expires. Automated brute-force tools are blocked without requiring admin intervention.

## Verification Steps
1. Fail login `MaxFailedAttempts` times consecutively.
2. Attempt login immediately after — verify locked-out error response.
3. Wait `LockoutMinutes` — verify login succeeds again.
