---
ReqID: "1.3.a"
title: "Yêu cầu đặt mật khẩu mới khi đăng nhập lần đầu (default password)"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.3.a — Yêu cầu đặt mật khẩu mới khi đăng nhập lần đầu

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs)
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs) — `MustChangePassword` field
- UI: `auth/change-password` (FE change-password page, Phase-02)

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_3_a_ApplicationUser_HasMustChangePasswordField` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_FirstLoginPasswordChangeMiddleware_Exists` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `FirstLoginEnforcementTests.AuthenticatedRequest_WithMustChangePassword_ReturnsMiddlewareBlock` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/FirstLoginEnforcementTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/1-3-a-first-login-change-password.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`ApplicationUser.MustChangePassword` is set to `true` when an account is created with a default/admin-set password. After login, `FirstLoginPasswordChangeMiddleware` intercepts every request and returns a structured redirect response until the user changes their password via `auth/change-password`. The change-password endpoint is explicitly whitelisted to bypass the middleware gate.

## Verification Steps
1. Create a user with admin-set password; verify `MustChangePassword=true` in DB.
2. Login and call any protected endpoint — verify 307/block response pointing to change-password.
3. Complete password change — verify `MustChangePassword=false` and subsequent requests succeed.
