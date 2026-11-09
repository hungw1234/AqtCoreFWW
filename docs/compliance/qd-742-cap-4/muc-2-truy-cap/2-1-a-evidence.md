---
ReqID: "2.1.a"
title: "Thiết lập timeout đóng phiên"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.1.a — Thiết lập timeout đóng phiên

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`src/host/AqtCoreFW.AuthServer/appsettings.json`](../../../../src/host/AqtCoreFW.AuthServer/appsettings.json) — `OpenIddict:AccessTokenLifetime`, `RefreshTokenLifetime`
- Code: [`src/host/AqtCoreFW.Api/Program.cs`](../../../../src/host/AqtCoreFW.Api/Program.cs) — OIDC session cookie lifetime + sliding expiration
- UI: `admin/security-health` → `GetLoginPolicyStatus` endpoint surfaces active timeout values

## Evidence Artifacts
- **Test:** `SecurityHealthQueriesTests.GetLoginPolicyStatus_SystemAdmin_Returns200` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/SecurityHealthQueriesTests.cs)
- **Audit log category:** `ConfigChange`
- **Screenshot path:** `screenshots/2-1-a-session-timeout-config.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
Session timeout is controlled by OpenIddict access-token and refresh-token lifetimes configured in `appsettings.json`. Access tokens expire after a configurable window (default 60 min); refresh tokens expire after a longer window (default 14 days sliding). The `GetLoginPolicyStatus` D7 endpoint surfaces the current effective timeout values to admin dashboards. OIDC silent renew handles transparent refresh within the window.

## Verification Steps
1. Obtain a token and wait past `AccessTokenLifetime`.
2. Call a protected endpoint — verify 401 Unauthorized.
3. Verify `GetLoginPolicyStatus` returns configured timeout values.
