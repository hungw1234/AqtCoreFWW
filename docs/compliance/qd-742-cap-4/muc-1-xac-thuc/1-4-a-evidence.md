---
ReqID: "1.4.a"
title: "Giao diện thiết lập chính sách giới hạn lần đăng nhập sai"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.4.a — Giao diện thiết lập chính sách giới hạn lần đăng nhập sai

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs) — `MaxFailedAttempts`, `LockoutMinutes`
- Code: [`src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/CredentialPoliciesController.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Presentation/Controllers/CredentialPoliciesController.cs)
- UI: `admin/credential-policies` (admin CRUD page)

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_4_d_CredentialPolicy_HasEscalationFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/1-4-a-lockout-policy-ui.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`CredentialPolicy` stores `MaxFailedAttempts` (max wrong-password attempts before lockout) and `LockoutMinutes` (how long the lockout lasts). Admins manage these via the `admin/credential-policies` CRUD UI backed by `CredentialPoliciesAdminController`. `TenantAwareUserManager.AccessFailedAsync` reads the active tenant policy and applies it on each failed login.

## Verification Steps
1. Navigate to `admin/credential-policies` — verify policy list with MaxFailedAttempts and LockoutMinutes fields.
2. Update `MaxFailedAttempts=5, LockoutMinutes=15` — verify 204 response and DB persistence.
3. Confirm `ConfigChange` audit entry emitted for the update.
