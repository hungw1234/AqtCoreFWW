---
ReqID: "2.1.b"
title: "Hiển thị thông báo + đóng phiên hết hạn + yêu cầu login lại"
muc: 2
phase: "05"
status: MET
generated: 2026-05-10
---

# 2.1.b — Hiển thị thông báo + đóng phiên hết hạn + yêu cầu login lại

**Phase:** 05 | **Mục:** 2 — Kiểm soát truy cập

## Implementation Reference
- Code: [`web/src/core/api/api-client.ts`](../../../../web/src/core/api/api-client.ts) — 401 interceptor triggers silent renew then redirects to login
- Code: [`web/src/features/auth/auth-config.ts`](../../../../web/src/features/auth/auth-config.ts) — `silent_redirect_uri`, `automaticSilentRenew`
- UI: `auth/login` — session-expired banner shown when OIDC silent renew fails

## Evidence Artifacts
- **Test:** `SecurityHealthQueriesTests.GetLoginPolicyStatus_SystemAdmin_Returns200` (project: AqtCoreFW.Identity.Integration.Tests) — [link](../../../../tests/integration/AqtCoreFW.Identity.Integration.Tests/SecurityHealthQueriesTests.cs)
- **Audit log category:** `AppAccess`
- **Screenshot path:** `screenshots/2-1-b-session-expired-notification.png` (Wave-3 deliverable, placeholder)

## Behavior Summary
When the access token expires, the FE OIDC client attempts a silent renew via `silent_redirect_uri`. If the renew fails (refresh token also expired or revoked), the 401 interceptor in `api-client.ts` clears the user session and redirects to `auth/login` with a `?reason=session_expired` query parameter. The login page renders a localised "phiên làm việc đã hết hạn" banner from this parameter.

## Verification Steps
1. Let both access and refresh tokens expire (or revoke the refresh token server-side).
2. Call any protected API from the FE — verify redirect to login page.
3. Confirm login page displays session-expired notification banner.
