---
ReqID: "1.3.b"
title: "Quy tắc đặt mật khẩu (số ký tự, loại ký tự)"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.3.b — Quy tắc đặt mật khẩu (số ký tự, loại ký tự)

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Domain/Entities/CredentialPolicy.cs) — `MinLength`, `RequireUppercase`, `RequireDigit`, `RequireSpecialChar`
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/TenantAwareUserManager.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authentication/TenantAwareUserManager.cs) — reads per-tenant `CredentialPolicy` for password validation
- UI: `admin/credential-policies` (policy CRUD page)

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_4_d_CredentialPolicy_HasEscalationFields` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/1-3-b-password-rules.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`CredentialPolicy` entity stores per-tenant password rules: `MinLength`, `RequireUppercase`, `RequireDigit`, `RequireSpecialChar`. `TenantAwareUserManager` overrides ASP.NET Core Identity's `PasswordOptions` with the active tenant policy at runtime. Password validation failures return structured 422 errors listing which rules were violated.

## Verification Steps
1. Set `MinLength=12, RequireUppercase=true` via admin UI.
2. Attempt to set a password `"simple"` — verify 422 with rule violation details.
3. Set a conforming password — verify success.
