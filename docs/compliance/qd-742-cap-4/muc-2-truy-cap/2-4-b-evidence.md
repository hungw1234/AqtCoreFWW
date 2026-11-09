---
ReqID: "2.4.b"
title: "Thực thi chính sách phân quyền tài khoản"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.4.b — Thực thi chính sách phân quyền tài khoản

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs`](../../../../src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs) — embeds user's role permissions into `permissions` JWT claim at token issuance
- Code: [`src/modules/identity/AqtCoreFW.Identity.Application/Authorization/PermissionAuthorizationHandler.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Application/Authorization/PermissionAuthorizationHandler.cs)

## Evidence Artifacts
- **Test:** `IpRulesApiTests.List_RegularUserNoPermission_Returns403` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `IpRulesApiTests.Update_RegularUserNoPermission_Returns403` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `IpRulesApiTests.Delete_RegularUserNoPermission_Returns403` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Audit log category:** `AppAccess`
- **Screenshot path:** `screenshots/2-4-b-account-permission-enforcement.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
At token issuance, `AuthorizationController` queries the user's assigned roles and flattens all permission codes into the `permissions` JWT claim array. `PermissionAuthorizationHandler` enforces these claims at every request. Per-account overrides (additional or revoked permissions beyond role defaults) are supported via `UserClaim` entries. This ensures the JWT is the authoritative, tamper-evident source of truth for each account's access rights.

## Verification Steps
1. Assign no roles to a user — verify all protected endpoints return 403.
2. Assign `Admin` role — verify module-level endpoints return 200, SystemAdmin-only endpoints still 403.
3. Revoke the role — re-login and verify 403 restored without further config.
