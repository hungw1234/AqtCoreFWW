---
ReqID: "2.3.b"
title: "Phân loại nhóm tài khoản (≥3 nhóm: thông thường / quản trị sử dụng / quản trị phát triển vận hành)"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.3.b — Phân loại nhóm tài khoản (≥3 nhóm)

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.SharedKernel/Authorization/PermCodes.cs`](../../../../src/shared/AqtCoreFW.SharedKernel/Authorization/PermCodes.cs)
- Code: [`src/host/AqtCoreFW.Api/E2ETestDataSeeder.RolePermissions.cs`](../../../../src/host/AqtCoreFW.Api/E2ETestDataSeeder.RolePermissions.cs) — seeds 3+ role groups: `User`, `Admin`, `SystemAdmin`

## Evidence Artifacts
- **Test:** `Qd742Phase05ComplianceTests.Compliance_PermissionCodes_IpRule_ViewAndManage` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/2-3-b-account-groups.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
The system defines at least 3 distinct account groups per QĐ 742 mandate:
- **User** (thông thường) — end-user permissions only; no admin access.
- **Admin** (quản trị sử dụng) — module-level management; no system configuration.
- **SystemAdmin** (quản trị phát triển vận hành) — full access including credential policies, MFA policies, IP rules, Vault config, and compliance data. SoD: `Compliance.Manage` is SystemAdmin-only.

Additional tenant-scoped roles can be defined via `admin/roles`.

## Verification Steps
1. Confirm seeder creates `User`, `Admin`, `SystemAdmin` roles with distinct permission sets.
2. Log in as `Admin` — verify `admin/credential-policies` returns 403 (SystemAdmin-only).
3. Log in as `SystemAdmin` — verify access to all admin endpoints.
