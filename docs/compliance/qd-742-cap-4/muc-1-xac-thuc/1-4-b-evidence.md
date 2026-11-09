---
ReqID: "1.4.b"
title: "Cảnh báo khi vi phạm chính sách"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.4.b — Cảnh báo khi vi phạm chính sách

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/TenantAwareUserManager.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/TenantAwareUserManager.cs) — `ShowRemainingAttempts` flag drives warning response
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs) — `ShowRemainingAttempts` field
- UI: `auth/login` — displays remaining-attempts warning in login error response

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_4_d_CredentialPolicy_HasEscalationFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `LockoutEscalationFlowTests.FirstLockout_SetsCounterTo1` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/LockoutEscalationFlowTests.cs)
- **Audit log category:** `AdminLogin`
- **Screenshot path:** `screenshots/1-4-b-login-violation-warning.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
When `CredentialPolicy.ShowRemainingAttempts=true`, the login error response includes `remainingAttempts` in the JSON body, warning the user how many attempts remain before lockout. The warning is conditional per-tenant policy — tenants may disable it for security-hardened environments. The FE login page renders this as a dismissable alert.

## Verification Steps
1. Set `ShowRemainingAttempts=true` in credential policy.
2. Submit an incorrect password — verify response body includes `remainingAttempts` field.
3. Set `ShowRemainingAttempts=false` — verify response no longer includes the field.
