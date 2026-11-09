---
ReqID: "1.3.d"
title: "Thiết lập thời gian mật khẩu hợp lệ"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.3.d — Thiết lập thời gian mật khẩu hợp lệ

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs) — `PasswordExpiryDays` (same field governs validity window)
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/ApplicationUser.cs) — `PasswordLastChangedAt`
- UI: `admin/credential-policies`

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_4_d_CredentialPolicy_HasEscalationFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/1-3-d-password-validity-window.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Password validity is the complement of 1.3.c: `PasswordExpiryDays` defines how long a given password remains valid. `ApplicationUser.PasswordLastChangedAt` is updated on every successful password change. The middleware enforces the validity window on every authenticated request. Setting `PasswordExpiryDays=0` disables expiry (no forced rotation).

## Verification Steps
1. Confirm `CredentialPolicy.PasswordExpiryDays` persists via admin API PUT.
2. Verify `PasswordLastChangedAt` is updated after a successful password change call.
3. Confirm middleware passes for users whose `PasswordLastChangedAt` is within the expiry window.
