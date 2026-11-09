---
ReqID: "2.3.a"
title: "Giao diện quản lý chính sách phân quyền theo nhóm tài khoản"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.3.a — Giao diện quản lý chính sách phân quyền theo nhóm tài khoản

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/RolesAdminController.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/RolesAdminController.cs)
- Code: [`src/shared/AqtCoreFW.SharedKernel/Authorization/PermCodes.cs`](../../../../src/shared/AqtCoreFW.SharedKernel/Authorization/PermCodes.cs) — full permission code registry
- UI: `admin/roles` (role CRUD + permission assignment page)

## Evidence Artifacts
- **Test:** `Qd742Phase05ComplianceTests.Compliance_PermissionCodes_IpRule_ViewAndManage` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Test:** `Qd742Phase05ComplianceTests.Compliance_PermissionCodes_SecurityHealth_View` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/2-3-a-role-permission-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`admin/roles` provides a UI for managing role definitions and their assigned permission codes. `RolesAdminController` exposes CRUD endpoints protected by `[RequiresMfa]` + `IDENTITY.ROLES.MANAGE`. The permission registry in `PermCodes.cs` defines all 100+ permission codes grouped by module, enabling fine-grained access policy configuration per role group.

## Verification Steps
1. Navigate to `admin/roles` — verify role list with permission assignment.
2. Add a permission code to a role — verify 204 and permission effective on next token refresh.
3. Confirm `ConfigChange` audit entry for the role update.
