---
ReqID: "1.1.a"
title: "Có giao diện quản lý tài khoản người sử dụng"
muc: 1
phase: "03"
status: MET
generated: 2026-05-10
---

# 1.1.a — Có giao diện quản lý tài khoản người sử dụng

**Phase:** 03 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/UsersAdminController.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/UsersAdminController.cs)
- UI: `admin/users` (CRUD page — list, create, edit, disable)
- Migration: `20260501_AddApplicationUser` (Phase-02 baseline)

## Evidence Artifacts
- **Test:** `Qd742Phase03ComplianceTests.Compliance_1_6_a_ApplicationUser_HasMfaFields` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase03ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_3_a_ApplicationUser_HasMustChangePasswordField` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/1-1-a-user-management-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Admin UI at `admin/users` provides full CRUD for application user accounts: list with pagination, create with role assignment, edit profile/status, and hard/soft disable. The `UsersAdminController` enforces `[RequiresMfa]` + `IDENTITY.USERS.MANAGE` permission so only authorized admins can mutate accounts.

## Verification Steps
1. Log in as SystemAdmin with valid MFA session.
2. Navigate to `admin/users` — verify list of users rendered.
3. Create a new user; confirm record persisted and AccountManagement audit entry emitted.

## Observations / Audit Notes
Phase-03 added TotpSecret/MfaGraceUntil fields to ApplicationUser; Phase-02 added MustChangePassword + AutoDisabledAt. Both contribute to this UI's completeness.
