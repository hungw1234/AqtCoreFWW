---
ReqID: "2.3.c"
title: "Thực thi chính sách phân quyền"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.3.c — Thực thi chính sách phân quyền

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Application/Authorization/PermissionAuthorizationHandler.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Application/Authorization/PermissionAuthorizationHandler.cs) — evaluates `permissions` JWT claim against required policy
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — `AddPermissionAuthorization()` registers handler

## Evidence Artifacts
- **Test:** `IpRulesApiTests.List_RegularUserNoPermission_Returns403` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `IpRulesApiTests.Create_RegularUserNoManagePermission_Returns403` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `SecurityHealthQueriesTests.AllEndpoints_RequireSecurityHealthViewPermission` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/SecurityHealthQueriesTests.cs)
- **Audit log category:** `AppAccess`
- **Screenshot path:** `screenshots/2-3-c-rbac-enforcement.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`PermissionAuthorizationHandler` reads the `permissions` claim from the JWT and evaluates it against the policy required by each controller/action. Controllers carry `[Authorize(Policy = PermCodes.Xxx.Yyy)]` attributes; requests without the required permission code in their token receive 403 Forbidden. This enforcement is uniform across all modules via the standard authorization handler pipeline.

## Verification Steps
1. Login as `User` role (no `SECURITY.IP_RULE.VIEW` permission).
2. GET `/api/admin/ip-rules` — verify 403.
3. Login as `SystemAdmin` with correct permission — verify 200.

## Observations / Audit Notes
- `BaseApiController` citation removed Wave-2 fix-up (file not present in current codebase; authorization is applied via `[Authorize(Policy=...)]` attributes directly on controllers).
