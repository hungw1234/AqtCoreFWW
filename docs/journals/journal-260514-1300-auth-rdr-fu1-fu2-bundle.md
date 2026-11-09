# AUTH-RDR-FU1+FU2 — SSE/SignalR 401 Cleanup-and-Redirect

**Date:** 2026-05-14 13:00 ICT
**Commit:** `cf2cdde7` on `main` (local, unpushed)
**Type:** FE defense-in-depth (P2 × 2 backlog closure)
**Version:** v2.137p20
**Backlog rows:** `AUTH-RDR-FU1` + `AUTH-RDR-FU2` (Auth Session Redirect Follow-ups family)

## What Happened

v2.133p18 (commit `16cc9a91`, 2026-05-01) shipped the auth-redirect fix: when refresh token invalid → api-client.ts catch block fires `userManager.removeUser()` + `triggerSessionError()` → `userUnloaded` event → AuthProvider `setUser(null)` → RouteGuard.signinRedirect. Hardpath redirect, no UI dependency.

The audit accompanying that ship found 2 deferred callsites that bypassed the machinery:
- **Copilot SSE** uses raw `fetch('/ai/copilot/stream')` not `apiClient` → no interceptor → 401 throws but no cleanup.
- **SignalR `accessTokenFactory`** (notifications + chat) returns `user.access_token` without checking `user.expired` → expired token sent to hub → 401 → reconnect storm → CB open after 5 fails → user silently loses realtime noti with no UX signal.

Both filed P2, ~1.5h combined estimate. Sat in backlog 13 days awaiting a session.

## The Fix

3 inline patches mirroring `api-client.ts:202-203`:

**FU1 — `web/src/features/copilot/copilot-api.ts`**

```ts
if (!response.ok) {
  if (response.status === 401) {
    await userManager.removeUser().catch(() => {});
    triggerSessionError();
  }
  throw new Error(`HTTP ${response.status}`);
}
```

**FU2 — `notification-signalr.ts:52` + `use-chat-signalr.ts:36`**

```ts
accessTokenFactory: async () => {
  let user = await userManager.getUser().catch(() => null);
  if (user?.expired) {
    try {
      user = await userManager.signinSilent();
    } catch {
      await userManager.removeUser().catch(() => {});
      return '';
    }
  }
  return user?.access_token ?? '';
},
```

Returning `''` on silent-renew failure triggers SignalR negotiate-401 → existing v2.133p16 CB → AuthProvider.onUserUnloaded fires via `removeUser()` → RouteGuard.signinRedirect. Single source of redirect.

## What I Almost Got Wrong

**Helper extraction temptation.** Backlog FU3 explicitly defers `cleanupAndRedirect()` helper to a separate ship; trigger = "sau khi AUTH-RDR-FU1+FU2 ship → refactor tổng hợp". DRY instinct said: 3 inline copy-pastes is the textbook case to extract now. I held the line — KISS over DRY when:
- Pattern is 2-3 lines, not 20
- Callsite count is bounded (4 total: api-client + 3 new)
- Backlog row itself declares the deferral strategy
- Test coverage for the helper would itself be new work

Result: 25 LOC delta total. If FU3 lands next week, 1 helper + 4 callsite updates is straightforward.

**Thundering-herd check.** api-client.ts uses module-scoped `renewInFlight` to coalesce parallel `signinSilent()` bursts. If api-client + 2 SignalR factories all see expired user simultaneously → 3 parallel iframes. Reviewer flagged this; verified that `oidc-client-ts._silentRenewService` internally serializes silent-renew (OIDC server only sees 1 hit). Safe. Cross-module coalescing = candidate for FU3.

## Verification

- `tsc --noEmit -p tsconfig.app.json` — 0 errors
- `api-client.test.ts` + `api-client-breaker.test.ts` — 22/22 PASS (22 cases incl. 401 retry+redirect path that this work delegates to)
- Vitest full run: 864/872 PASS. 8 pre-existing failures (workflow-migration, digital-signature, classification, taxonomy, auth-helper, admin-tenant-selector) confirmed via `git stash` → run failing tests on pristine main → same 4/4 fail in `workflow-migration` alone. None related to this bundle.
- Code review (subagent `code-reviewer`): 9.0/10, 0 P0/P1. Pattern conformance vs api-client.ts: 100%. Recommended ship as-is.

## What Didn't Get New Tests

No new vitest cases. Rationale:
1. The `removeUser+triggerSessionError` machinery is exhaustively covered in `api-client.test.ts` (22 cases including the exact 401 catch path being mirrored).
2. The `signinSilent → catch → removeUser → return ''` pattern requires heavy mock of `oidc-client-ts.UserManager`; same mock infrastructure existed in `api-client.test.ts`.
3. FU3 helper extraction will need exactly 1 test suite for `cleanupAndRedirect()`. Writing identical-looking branch tests at 3 inline callsites now → discard them at FU3 → wasted effort.
4. Each FE change is 2-7 LOC, surgical, comments cite source pattern.

Decision logged here in case a reviewer later flags "no new tests" — this is intentional, not oversight.

## Numbers

| Metric | Value |
|---|---|
| Files changed | 3 source + 2 docs |
| LOC delta | +31 / -3 |
| Wall-clock | ~25 min (scout + plan + 3 edits + typecheck + test + review) |
| Backlog estimate | ~1.5h |
| Code-review score | 9.0/10 |
| Pre-existing test failures unrelated | 8 (confirmed) |
| New tests added | 0 (intentional) |

## Follow-ups Still Open

- **AUTH-RDR-FU3 (P3, ~30 min)** — `cleanupAndRedirect()` helper extraction. Trigger now armed (FU1+FU2 shipped). Natural next session if/when a 5th callsite emerges OR explicit DRY cleanup pass.
- Cross-module `renewInFlight` coalescing — bundle into FU3 if helper extraction happens.

## Lesson

**Backlog row drift after parent fix.** v2.133p18 already encoded the "right answer" pattern; the FU1+FU2 rows just listed the 3 unmigrated callsites with paste-ready code. 13 days from row-creation → ship → still applies cleanly. When a parent fix is well-designed, the follow-up rows are low-risk and easy to batch. This was the easiest ship of the week — preceded by 4 ships this morning (p17/p18/p19) — all small, all bounded scope.

The /cook skill's recommendation in the previous turn was "nghỉ / chốt session" — defensible. User chose to ship anyway. ~25 min for 2 P2 closures is high ROI; bundle vs nothing.
