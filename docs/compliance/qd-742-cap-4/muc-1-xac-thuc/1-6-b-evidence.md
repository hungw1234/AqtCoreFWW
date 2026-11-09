---
ReqID: "1.6.b"
title: "Tích hợp các bước MFA khi chính sách kích hoạt"
muc: 1
phase: "03"
status: MET
generated: 2026-05-10
---

# 1.6.b — Tích hợp các bước MFA khi chính sách kích hoạt

**Phase:** 03 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authorization/MfaRequirementHandler.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authorization/MfaRequirementHandler.cs) — validates `mfa_at` JWT claim
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authorization/MfaPolicyProvider.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Authorization/MfaPolicyProvider.cs) — resolves `RequiresMfa:{hours}` policies dynamically
- Code: [`src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/RecoveryCodeService.cs`](../../../../src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/RecoveryCodeService.cs) — generates 16 BCrypt-hashed recovery codes
- UI: `auth/mfa/enrol` + `auth/mfa/verify` (TOTP enrollment and verification flow)

## Evidence Artifacts
- **Test:** `Qd742Phase03ComplianceTests.Compliance_1_6_b_RecoveryCodeService_Generates16CodesWithBCrypt` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase03ComplianceTests.cs)
- **Test:** `Qd742Phase03ComplianceTests.Compliance_1_6_b_RequiresMfaAttribute_ExistsInSharedKernel` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase03ComplianceTests.cs)
- **Test:** `Qd742Phase03ComplianceTests.Compliance_1_6_b_MfaPolicyProvider_ExistsAndResolvesRequiresMfaPrefix` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase03ComplianceTests.cs)
- **Test:** `Qd742Phase03ComplianceTests.Compliance_1_6_b_MfaRequirementHandler_ValidatesMfaAtClaim` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase03ComplianceTests.cs)
- **Test:** `MfaFlowTests.CompleteEnrol_ValidTotpCode_Returns200WithRecoveryCodes` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/MfaFlowTests.cs)
- **Audit log category:** `AccountManagement`
- **Screenshot path:** `screenshots/1-6-b-mfa-enrol-flow.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
When an `MfaPolicy` with `IsMandatory=true` applies to a user's role, all `[RequiresMfa]`-decorated controllers require a valid `mfa_at` claim in the JWT. `MfaPolicyProvider` resolves `RequiresMfa:{hours}` policies at runtime; `MfaRequirementHandler` rejects requests where the `mfa_at` Unix timestamp is older than `MaxAgeHours`. TOTP enrollment produces 16 BCrypt-hashed recovery codes (work factor ≥ 10) per NIST SP 800-63B. Grace period (`GraceDays`) permits delayed enrollment without immediate enforcement.

## Verification Steps
1. Enrol TOTP via `auth/mfa/enrol` — verify QR URI returned and 16 recovery codes in response.
2. Call a `[RequiresMfa]`-decorated endpoint without `mfa_at` claim — verify 403.
3. Complete TOTP verification — verify `mfa_at` claim present in refreshed token and endpoint returns 200.
