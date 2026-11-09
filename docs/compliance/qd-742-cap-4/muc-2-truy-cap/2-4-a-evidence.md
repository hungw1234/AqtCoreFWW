---
ReqID: "2.4.a"
title: "Giao diện thiết lập quyền cho tài khoản"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.4.a — Giao diện thiết lập quyền cho tài khoản

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/UsersAdminController.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/UsersAdminController.cs) — role assignment endpoints
- Code: [`src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/RolesAdminController.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/RolesAdminController.cs)
- UI: `admin/users/{id}/roles` (per-user role assignment page)

## Evidence Artifacts
- **Test:** `Qd742Phase05ComplianceTests.Compliance_PermissionCodes_IpRule_ViewAndManage` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/2-4-a-user-permission-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Admins assign roles to individual accounts via `admin/users/{id}/roles`. `UsersAdminController` exposes a PUT endpoint for role assignment, protected by `[RequiresMfa]` + `IDENTITY.USERS.MANAGE`. Role changes take effect on the user's next token refresh. The FE page lists all available roles with checkboxes for assignment and shows currently assigned roles.

## Verification Steps
1. Navigate to `admin/users/{id}/roles` — verify role assignment UI.
2. Assign the `Admin` role to a user — verify 204 response.
3. Re-login as that user — verify new role permissions in JWT claims.
4. Confirm `AccountManagement` audit entry emitted.
