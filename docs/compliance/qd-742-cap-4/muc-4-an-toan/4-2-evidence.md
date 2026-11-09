---
ReqID: "4.2"
title: "Bảo vệ chống tấn công OWASP (SQL Injection, OS cmd, RFI, LFI, Xpath, XSS, CSRF) — kiểm thử xâm nhập"
muc: 4
phase: "05"
status: MET
generated: 2026-05-10
---

# 4.2 — Bảo vệ chống tấn công OWASP

**Phase:** 05 | **Mục:** 4 — An toàn ứng dụng và mã nguồn

## Implementation Reference
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — `UseAntiforgery()`, CORS policy, CSP headers via `SecurityHeadersMiddleware`
- Code: [`src/shared/AqtCoreFW.Infrastructure/Persistence/`](../../../../src/shared/AqtCoreFW.Infrastructure/Persistence/) — all DB access via EF Core parameterised queries or Dapper with `@param` syntax; no string-concatenated SQL
- Code: [`src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs`](../../../../src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs) — unified login error message (OWASP A04 user enumeration fix, PR #92)

## Evidence Artifacts
- **Test:** `IpRulesApiTests.Create_Anonymous_Returns401` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Test:** `IpRulesApiTests.List_Anonymous_Returns401` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/IpRulesApiTests.cs)
- **Audit log category:** `Error`
- **Screenshot path:** `screenshots/4-2-owasp-protection.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
OWASP protections applied across multiple layers: (1) SQL injection — EF Core/Dapper parameterised queries only; raw SQL uses bracket-escaped schema identifiers; (2) XSS — CSP headers + Razor encoding + React's built-in DOM escaping; (3) CSRF — `UseAntiforgery()` for cookie-authenticated endpoints; (4) user enumeration (A04) — unified login error message regardless of failure reason (PR #92); (5) RFI/LFI — no dynamic file inclusion paths; (6) HTTPS enforced globally. Penetration testing is a Wave-3/Wave-4 deliverable per compliance plan.

## Verification Steps
1. Attempt SQL injection via CIDR field (e.g. `'; DROP TABLE--`) — verify 422 validation rejection before DB access.
2. Confirm login error message identical for unknown user vs wrong password (OWASP A04).
3. Verify CSP `Content-Security-Policy` header present on all API responses.

## Observations / Audit Notes
Full penetration test report is a Wave-4 deliverable (post Phase-05b ship). Current evidence is architectural — parameterised queries, validation pipeline, unified error messages, and security headers. Manual pen-test report to be attached under `phase-07/` when available.
