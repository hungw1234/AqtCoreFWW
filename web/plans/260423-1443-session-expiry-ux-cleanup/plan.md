---
title: "Session expiry UX cleanup + 401 hardening"
description: "Fix spurious 'Phiên sắp hết hạn' popup, add 401 silent-renew retry, bump countdown default 60→120s"
status: pending
priority: P2
effort: 1.5h
branch: main
tags: [frontend, auth, ux, session, backend-seed]
created: 2026-04-23
---

# Plan — Session Expiry UX Cleanup + Hardening

## Problem
Modal "Phiên sắp hết hạn" pops up during active user work even though silent renew is configured. Cause: redundant poll-based warning (fires 60s before expiry) racing with `automaticSilentRenew` (fires 180s before expiry). Poll is legacy — must remove. Also missing defense for 401 edge case (silent renew invisibly missed — API 401 → abrupt redirect).

## Scope — 3 Phases

| # | Phase | Area | Effort | Status | Deps |
|---|-------|------|--------|--------|------|
| A | [Remove redundant token-expiry poll](./phase-01-remove-token-expiry-poll.md) | FE | 15m | pending | none |
| B | [401 defense-in-depth retry](./phase-02-api-client-401-retry.md) | FE | 45m | pending | A |
| C | [Seeder countdown 60→120s](./phase-03-session-seeder-countdown.md) | BE | 5m | pending | none |

**Dependency chain:** A → B (clean slate before new logic). C independent (parallelizable with A/B).

## Success Criteria (plan-level)
- No popup during active normal usage (user confirmed scenario)
- Silent renew failure → modal with clear "Đăng nhập lại" CTA
- API 401 → automatic silent retry once before surfacing error (invisible to user if token renewable)
- Idle modal countdown default 120s (from 60s) — more humane
- `SessionExpiryReason` union reduced to `'idle' | 'renew-failed'`
- All FE unit tests green; BE unchanged behavior except seed value

## Files Touched (summary)
**Phase A (modify 4):** `auth-provider.tsx`, `session-expiry-modal.tsx`, `vi.json`, `en.json`
**Phase B (create 2, modify 2):** `session-error-handler.ts` (new), `api-client.test.ts` (new), `auth-provider.tsx`, `api-client.ts`
**Phase C (modify 1):** `SystemParamSeeder.cs`

**File ownership:** Phase A + B both touch `auth-provider.tsx` → sequential (A first).

## Verification Gate (before declaring DONE)
1. `yarn lint && yarn typecheck && yarn test` — all green
2. Manual: login → idle 5 min while typing — NO popup
3. Manual: kill auth server mid-session → silent renew fail → modal "renew-failed"
4. BE: `dotnet build` on `AqtCoreFW.SystemParams` project → no errors
5. DB inspect: new install shows `session_countdown_seconds=120`

## Unresolved Questions
- (B) Race window: if 2 parallel 401s arrive while `signinSilent()` in flight — should we queue retries or fail-fast second? **Proposal:** single in-flight promise; parallel 401s share result. Confirm acceptable.
- (B) Should response interceptor clear `isRedirectingToLogin` flag before retry path, or replace entirely? **Proposal:** replace — new logic supersedes, drop old flag.
- (C) Runtime DB already has row with value "60"? Seeder idempotent (INSERT ... WHERE NOT EXISTS) so existing deployments NOT auto-upgraded. Need manual admin UI update or migration? **Decision pending user.**
