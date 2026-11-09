---
ReqID: "1.3.c"
title: "Thiết lập thời gian yêu cầu thay đổi mật khẩu"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.3.c — Thiết lập thời gian yêu cầu thay đổi mật khẩu

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs) — `PasswordExpiryDays` field
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Middleware/FirstLoginPasswordChangeMiddleware.cs) — checks `PasswordLastChangedAt` + `PasswordExpiryDays`
- UI: `admin/credential-policies` (policy CRUD — expiry days field)

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_4_d_CredentialPolicy_HasEscalationFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/1-3-c-password-expiry-config.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`CredentialPolicy.PasswordExpiryDays` (0 = disabled) controls mandatory password rotation interval. The middleware compares `ApplicationUser.PasswordLastChangedAt + PasswordExpiryDays` against the current UTC timestamp; if expired, the user is redirected to change-password regardless of MFA state. Per-tenant policy allows different expiry windows per environment.

## Verification Steps
1. Set `PasswordExpiryDays=90` via admin UI — verify persisted.
2. Backdate a test user's `PasswordLastChangedAt` to 91 days ago.
3. Login with that user — verify middleware block redirecting to change-password.
