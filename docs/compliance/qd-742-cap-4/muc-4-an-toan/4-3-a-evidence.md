---
ReqID: "4.3.a"
title: "Kiểm soát lỗi: chỉ hiển thị thông báo được kiểm soát, không lộ stack/trace"
muc: 4
phase: "05"
status: MET
generated: 2026-05-10
---

# 4.3.a — Kiểm soát lỗi: không lộ stack/trace

**Phase:** 05 | **Mục:** 4 — An toàn ứng dụng và mã nguồn

## Implementation Reference
- Code: [`src/shared/AqtCoreFW.Infrastructure/Middleware/GlobalExceptionHandlerMiddleware.cs`](../../../../src/shared/AqtCoreFW.Infrastructure/Middleware/GlobalExceptionHandlerMiddleware.cs) — catches all unhandled exceptions; returns structured `ProblemDetails` without stack trace in non-Development environments
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — `UseGlobalExceptionHandler()` registered before controllers; `ASPNETCORE_ENVIRONMENT` gates detail level

## Evidence Artifacts
- **Test:** `SecurityHealthQueriesTests.GetAuditIntegrityStatus_SystemAdmin_Returns200` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/SecurityHealthQueriesTests.cs)
- **Audit log category:** `Error`
- **Screenshot path:** `screenshots/4-3-a-error-control.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
`GlobalExceptionHandlerMiddleware` intercepts all unhandled exceptions before they propagate to ASP.NET Core's default error page. In `Production` and `Staging` environments it returns a sanitised `ProblemDetails` JSON body with a `traceId` reference (for internal log correlation) but no `detail` stack trace or internal exception message. In `Development`, full detail is shown. `Error` audit category entries are emitted for all 5xx errors to maintain the audit trail.

## Verification Steps
1. Set `ASPNETCORE_ENVIRONMENT=Production`.
2. Trigger a deliberate 500 (e.g. pass malformed data that bypasses validation).
3. Verify response body contains `type`, `title`, `status`, `traceId` but NO `stackTrace` or `exceptionMessage`.
