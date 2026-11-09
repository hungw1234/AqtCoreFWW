---
ReqID: "4.3.b"
title: "Hiển thị thông báo lỗi đến user"
muc: 4
phase: "05"
status: MET
generated: 2026-05-10
---

# 4.3.b — Hiển thị thông báo lỗi đến user

**Phase:** 05 | **Mục:** 4 — An toàn ứng dụng và mã nguồn

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.Infrastructure/Middleware/GlobalExceptionHandlerMiddleware.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Middleware/GlobalExceptionHandlerMiddleware.cs) — maps exception types to human-readable `ProblemDetails.title`
- Code: [`web/src/core/api/api-client.ts`](../../../../web/src/core/api/api-client.ts) — error interceptor extracts `errors[]` array and surfaces as toast/banner

## Evidence Artifacts
- **Test:** `IpRulesApiTests.Create_InvalidCidr_Returns422` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `AuditRetentionPolicyApiTests.Update_StaleRowVersion_Returns409` (project: AqtCoreFW.Audit.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Audit.Integration.Tests/AuditRetentionPolicyApiTests.cs)
- **Audit log category:** `Error`
- **Screenshot path:** `screenshots/4-3-b-user-error-messages.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
The API returns structured error responses per RFC 9457 `ProblemDetails`: 400/422 for client errors (validation failures with per-field messages), 404 for not found, 409 for concurrency conflicts, 403 for authorisation failures. The FE `api-client.ts` interceptor extracts the `errors[]` array and maps it to inline form validation messages or toast notifications. Error messages are localised (vi/en) via i18n resources and never expose internal implementation details.

## Verification Steps
1. Submit an invalid form in the admin UI — verify per-field error messages displayed inline.
2. Attempt a concurrent update with a stale row version — verify 409 with "conflict" message shown as toast.
3. Confirm error messages are in Vietnamese when locale is `vi`.

## Observations / Audit Notes
- `web/src/components/ui/error-display.tsx` citation removed Wave-2 fix-up (file not present in current codebase; error display is handled inline within feature components via the `api-client.ts` interceptor).
