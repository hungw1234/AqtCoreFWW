---
ReqID: "1.3.e"
title: "Mở khóa tài khoản khi đổi mật khẩu thành công"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.3.e — Mở khóa tài khoản khi đổi mật khẩu thành công

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs) — clears middleware block on successful change
- Code: [`src/modules/identity/AqtCoreFW.Identity.Application/Commands/ChangePassword/ChangePasswordCommandHandler.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Application/Commands/ChangePassword/ChangePasswordCommandHandler.cs) — sets `MustChangePassword=false`, updates `PasswordLastChangedAt`

## Evidence Artifacts
- **Test:** `FirstLoginEnforcementTests.ChangePassword_Endpoint_BypassesMiddleware` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/FirstLoginEnforcementTests.cs)
- **Test:** `FirstLoginEnforcementTests.UserWithoutMustChangeFlag_BypassesMiddlewareGate` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/FirstLoginEnforcementTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/1-3-e-unlock-after-password-change.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`ChangePasswordCommandHandler` atomically sets `MustChangePassword=false` and `PasswordLastChangedAt=UtcNow` upon successful password change. If the account was also hard-locked (`LockoutEnd` set), admin unlock clears `LockoutEnd` and resets `ConsecutiveLockoutCount`. After either path, all subsequent middleware checks pass and normal API access resumes.

## Verification Steps
1. Lock a test account via expired password.
2. POST to change-password with valid new credentials — verify 200.
3. Immediately call a protected endpoint — verify 200 (no longer blocked).
4. Confirm `MustChangePassword=false` and `PasswordLastChangedAt` updated in DB.
