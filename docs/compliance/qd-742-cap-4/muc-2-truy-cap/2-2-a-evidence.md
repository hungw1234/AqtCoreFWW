---
ReqID: "2.2.a"
title: "Giao diện quản lý chính sách giới hạn IP quản trị"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.2.a — Giao diện quản lý chính sách giới hạn IP quản trị

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/IpFilterRule.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/IpFilterRule.cs)
- Code: [`src/host/AqtCoreFW.Api/Controllers/Admin/IpRulesAdminController.cs`](../../../../src/host/AqtCoreFW.Api/Controllers/Admin/IpRulesAdminController.cs)
- UI: `admin/ip-rules` (CRUD page — List, GetById, Create, Update, Delete)

## Evidence Artifacts
- **Test:** `Qd742Phase05ComplianceTests.Compliance_2_2_a_IpFilterRule_EntityExists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Test:** `Qd742Phase05ComplianceTests.Compliance_2_2_a_IpFilterRule_HasRequiredFields` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Test:** `Qd742Phase05ComplianceTests.Compliance_2_2_a_IpRulesAdminController_Has5Endpoints` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase05ComplianceTests.cs)
- **Test:** `IpRulesApiTests.Create_ValidCidr_Returns201` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/2-2-a-ip-rules-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`IpFilterRule` entity stores CIDR notation rules (`Cidr`, `IsBlock`, `IsEnabled`, `Description`, `TenantId`). The `admin/ip-rules` page backed by `IpRulesAdminController` (5 endpoints: List, GetById, Create, Update, Delete) provides full management UI. The controller requires `[RequiresMfa]` + `SECURITY.IP_RULE.MANAGE` permission. Per-tenant rules allow each tenant to maintain their own IP allowlist/blocklist.

## Verification Steps
1. Navigate to `admin/ip-rules` as SystemAdmin with MFA — verify CRUD UI.
2. POST a new rule with valid CIDR `192.168.1.0/24` — verify 201.
3. POST with invalid CIDR — verify 422 validation error.
