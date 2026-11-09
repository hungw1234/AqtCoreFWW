---
ReqID: "1.1.c"
title: "Yêu cầu xác thực khi truy cập sử dụng"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.1.c — Yêu cầu xác thực khi truy cập sử dụng

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs`](../../../../src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs)
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — `AddAuthentication` + OpenIddict bearer middleware
- UI: `auth/login` (OIDC login page served by AuthServer)

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_3_a_ApplicationUser_HasMustChangePasswordField` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_FirstLoginPasswordChangeMiddleware_Exists` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Audit log category:** `AppAccess`
- **Screenshot path:** `screenshots/1-1-c-user-login-gate.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
All API routes not marked `[AllowAnonymous]` require a valid OpenIddict Bearer JWT token. Unauthenticated requests return 401. The AuthServer issues tokens only after credential verification; `FirstLoginPasswordChangeMiddleware` additionally intercepts post-login requests when `MustChangePassword=true` and redirects to the change-password flow.

## Verification Steps
1. Call any non-anonymous API endpoint without Authorization header — verify 401.
2. Obtain a valid token via OIDC login flow — verify subsequent requests succeed.
3. Confirm `AppAccess` audit entry emitted on first protected resource access.
