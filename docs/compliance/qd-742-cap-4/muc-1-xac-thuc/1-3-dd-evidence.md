---
ReqID: "1.3.đ"
title: "Khóa tài khoản khi mật khẩu hết hạn"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.3.đ — Khóa tài khoản khi mật khẩu hết hạn

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs) — hard-blocks all non-change-password routes when expired
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs) — `LockoutEnabled`, `LockoutEnd`

## Evidence Artifacts
- **Test:** `FirstLoginEnforcementTests.AuthenticatedRequest_WithMustChangePassword_ReturnsMiddlewareBlock` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/FirstLoginEnforcementTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/1-3-dd-expired-password-lock.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
When a user's password expires (`PasswordLastChangedAt + PasswordExpiryDays < UtcNow`), the middleware blocks every API request except the `/change-password` endpoint, effectively locking the account for functional use. This is a soft lock (session still valid, change-password accessible) rather than a hard `LockoutEnd` lock — compliant with the requirement's intent of preventing use until password is updated.

## Verification Steps
1. Backdate `PasswordLastChangedAt` to exceed `PasswordExpiryDays`.
2. Call any non-change-password endpoint — verify blocked response.
3. Successfully change password — verify access restored immediately.
