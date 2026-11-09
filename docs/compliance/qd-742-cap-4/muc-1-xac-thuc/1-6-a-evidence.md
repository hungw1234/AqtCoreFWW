---
ReqID: "1.6.a"
title: "Giao diện quản lý chính sách MFA"
muc: 1
phase: "03"
status: MET
generated: 2026-05-10
---

# 1.6.a — Giao diện quản lý chính sách MFA

**Phase:** 03 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/MfaPolicy.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/MfaPolicy.cs)
- Code: [`src/host/AqtCoreFW.Api/Controllers/Admin/MfaPoliciesAdminController.cs`](../../../../src/host/AqtCoreFW.Api/Controllers/Admin/MfaPoliciesAdminController.cs)
- UI: `admin/mfa-policies` (CRUD page — list by role, toggle mandatory, set grace days, set allowed factors)

## Evidence Artifacts
- **Test:** `Qd742Phase03ComplianceTests.Compliance_1_6_a_MfaPolicy_EntityExists` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase03ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_6_a_MfaPolicy_EntityExists` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_6_a_MfaPolicyDto_HasAllFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_6_a_ApplicationUser_HasMfaFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/1-6-a-mfa-policy-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`MfaPolicy` entity stores per-role MFA configuration: `Role`, `IsMandatory`, `GraceDays`, `AllowedFactors` (TOTP | EmailOtp bitmask). The `admin/mfa-policies` UI backed by `MfaPoliciesAdminController` allows SystemAdmins to create/edit/delete per-role policies. The `MfaEmergencyDisableRequest` entity provides a 2-admin approval flow for emergency MFA disable (Phase-03 P1-1 safeguard).

## Verification Steps
1. Navigate to `admin/mfa-policies` — verify role-scoped policy list.
2. Create a policy with `IsMandatory=true, GraceDays=7, AllowedFactors=Totp`.
3. Confirm `ConfigChange` audit entry emitted and policy retrievable via GET.
