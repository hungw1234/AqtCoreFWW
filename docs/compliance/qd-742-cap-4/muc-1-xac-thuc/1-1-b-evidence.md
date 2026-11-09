---
ReqID: "1.1.b"
title: "Yêu cầu xác thực khi truy cập quản trị, cấu hình"
muc: 1
phase: "02"
status: MET
generated: 2026-05-10
---

# 1.1.b — Yêu cầu xác thực khi truy cập quản trị, cấu hình

**Phase:** 02 | **Mục:** 1 — Xác thực

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.SharedKernel/Authorization/RequiresMfaAttribute.cs`](../../../../src/shared/AqtCoreFW.SharedKernel/Authorization/RequiresMfaAttribute.cs)
- UI: `admin/*` (all admin routes carry `[RequiresMfa]` + `[Authorize(Policy=...)]`)

## Evidence Artifacts
- **Test:** `Qd742Phase02ComplianceTests.Compliance_1_6_b_RequiresMfaAttribute_Exists` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/Qd742Phase02ComplianceTests.cs)
- **Test:** `Qd742Phase03ComplianceTests.Compliance_1_6_b_RequiresMfaAttribute_ExistsInSharedKernel` (project: AqtCoreFW.Architecture.Tests) — [link](../../../../tests/architecture/AqtCoreFW.Architecture.Tests/Compliance/Qd742Phase03ComplianceTests.cs)
- **Audit log category:** `AdminLogin`
- **Screenshot path:** `screenshots/1-1-b-admin-auth-gate.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
All admin/config controllers carry class-level `[RequiresMfa]` + `[Authorize(Policy=...)]` attributes. Unauthenticated requests return 401; authenticated requests without a valid `mfa_at` JWT claim within the required window return 403. The `MfaRequirementHandler` enforces this at authorization pipeline level.

## Verification Steps
1. Access any `admin/*` endpoint without a session token — verify 401.
2. Access with a valid session but no `mfa_at` claim — verify 403.
3. Access with valid session + fresh `mfa_at` — verify 200.
