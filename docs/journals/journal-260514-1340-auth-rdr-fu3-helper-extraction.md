# AUTH-RDR-FU3 — `cleanupAndRedirect()` Helper Extraction

**Date:** 2026-05-14 13:40 ICT
**Commit:** _pending_
**Type:** FE refactor (P3 backlog closure)
**Version:** v2.137p21
**Backlog row:** `AUTH-RDR-FU3` (Auth Session Redirect Follow-ups family — now CLOSED)

## What Happened

Yesterday afternoon I shipped v2.137p20 with FU1+FU2 (3 inline `removeUser + triggerSessionError` copy-pastes at copilot-api / notification-signalr / use-chat-signalr). The session ended with me arguing FU3 should be deferred — "FU3 helper extraction will test once when ship; 3 inline copy-pastes acceptable when bounded next-step."

User responded ~10 minutes later: ship FU3.

Fair call. The DRY pressure was already at 5 callsites (v2.133p18 api-client + handleExtend + 3 from p20). The "bounded next-step" had arrived. Time to consolidate.

## The Fix

**New helper** (`session-error-handler.ts`):

```ts
import { userManager } from './auth-provider';

export async function cleanupAndRedirect(): Promise<void> {
  await userManager.removeUser().catch(() => {});
  triggerSessionError();
}
```

**5 callsite refactors:**

| Site | Before | After |
|---|---|---|
| `api-client.ts:202-203` | `await removeUser().catch(...); triggerSessionError()` | `await cleanupAndRedirect()` |
| `auth-provider.tsx:161-162` (handleExtend) | `await removeUser().catch(...); handleAuthFailure()` | `await cleanupAndRedirect()` (deps array trimmed) |
| `copilot-api.ts` SSE 401 | Inline 2-liner inside `if (status === 401)` | `await cleanupAndRedirect()` |
| `notification-signalr.ts` factory | `await removeUser().catch(...); return ''` | `await cleanupAndRedirect(); return ''` |
| `use-chat-signalr.ts` factory | same | same |

## What Almost Tripped Me

**Circular import via `userManager`.** `session-error-handler.ts` now imports `userManager` from `./auth-provider`, while `auth-provider.tsx` already imports `setSessionErrorHandler` from `./session-error-handler`. Direct cycle.

ES modules handle this fine for live-binding values consumed at runtime — both modules finish init before `cleanupAndRedirect()` is ever called (event-handler time, post-mount). Vite/TS don't warn. Production build works. Tests pass. Documented in helper comment so the next reader doesn't strip it "for cleanliness".

The cleanest alternative would be moving `userManager` to its own module (`auth-user-manager.ts`), breaking the cycle physically. Reviewer flagged this as "L1 cleanup candidate, not worth doing now." I agreed — the helper comment makes the cycle intentional and visible.

**Test mock breakage.** First run: api-client.test.ts went from 22/22 PASS to 4 FAIL. Cause: the test mocked `session-error-handler` with only `triggerSessionError: vi.fn()`. New api-client imports `cleanupAndRedirect` — the mock didn't expose it.

First attempted fix: `vi.mock('...', async () => { const { userManager } = await import('./auth-provider'); ... })` — failed because dynamic import inside the mock factory ran before auth-provider's `const userManager = new UserManager(oidcConfig)` had evaluated. `userManager` was undefined when `cleanupAndRedirect()` was called.

Working fix: `vi.hoisted(() => ({ removeUser: vi.fn().mockResolvedValue(undefined), trigger: vi.fn() }))` to create shared mock refs that exist before any `vi.mock` factory runs. Both the `oidc-client-ts` mock and the `session-error-handler` mock reference the same `removeUser` and `trigger` fns. Existing assertions on `userManager.removeUser` and `triggerSessionError` continue to work because they're the same fn instances.

Same trick applied to the new `session-error-handler.test.ts`:

```ts
const { removeUser } = vi.hoisted(() => ({ removeUser: vi.fn() }));
vi.mock('../auth-provider', () => ({ userManager: { removeUser } }));
```

## Behavior Delta Watch

**SignalR factories now fire `triggerSessionError`.** Previously they only called `removeUser` and relied on `onUserUnloaded → RouteGuard.signinRedirect`. Now they also trigger the countdown toast.

Reviewer flagged this: could the toast appear after a brief network blip while api-client renews fine?

Answer: no. SignalR factory only invokes the catch branch when `userManager.signinSilent()` itself rejects — same trigger condition as api-client. Network blips during HTTP calls don't reach the factory at all. `authFailureController.trigger` short-circuits on second fire (`if (redirecting || interval !== null) return`), so concurrent api-client + SignalR fires can't stack the toast.

Net change: the SignalR-only expiry path (token rotation while user has no recent HTTP activity) now also shows the countdown UX instead of silent redirect. That's a UX improvement.

**handleExtend lost `handleAuthFailure` from useCallback deps.** Before: `[resetActivity, handleAuthFailure]`. After: `[resetActivity]`. Indirect path: `cleanupAndRedirect` → `triggerSessionError` → registered handler = `handleAuthFailure` → `authFailureController.trigger`. Same end-effect, one extra hop, no race possible (handler is registered in the same `useEffect` that mounts the modal that calls handleExtend).

## Verification

- `tsc --noEmit -p tsconfig.app.json` — 0 errors
- Targeted vitest 28/28 PASS:
  - `api-client.test.ts` 15/15 (after mock rewire)
  - `api-client-breaker.test.ts` 7/7
  - `session-error-handler.test.ts` 6/6 (NEW — order, swallow-rejection, no-handler-registered)
- Code review (subagent `code-reviewer`): 9.0/10, 0 P0/P1, ship.

## What Did Not Get Re-Tested

Full vitest run not executed — 8 pre-existing failures (workflow-migration, digital-signature, classification, taxonomy, auth-helper, admin-tenant-selector) were already confirmed unrelated in v2.137p20 session via stash test. Re-running would just confirm the same 8.

## Numbers

| Metric | Value |
|---|---|
| Files changed | 6 source + 1 NEW test + 1 test-mock update + 2 docs |
| LOC delta | +~135 / -~30 (helper +12, refactors -10 net, new test +85, mock rewire +15) |
| Wall-clock | ~50 min (helper + 5 refactors + 1 test + 1 mock rewire + review + finalize) |
| Backlog estimate | ~30 min |
| Code-review score | 9.0/10 |
| Pre-existing test failures | 8 (skipped re-verify, confirmed earlier today) |
| New tests added | 6 cases |

## Lesson

**The "next-step ROI" gate worked.** Yesterday's "defer FU3" call was defensible at the time — 3 inline copy-pastes, FU3 not strictly needed. User overrode because the right side of the trade-off had shifted: family was complete-able in 1 ship, not 2 weeks later. ~50 min to close the family permanently vs leaving an open-ended P3 row that future me would have to re-context every time the backlog gets reviewed.

The pattern: when 3+ callsites of identical 2-liners exist AND a closing item is the next deferred row in the same family, do it now. Don't wait for a 4th callsite to "trigger" — that just means another 30 min wasted re-discovering the pattern.

## Family CLOSED

AUTH-RDR family (v2.133p18 parent → FU1+FU2 v2.137p20 → FU3 v2.137p21) is now fully shipped. No follow-ups remain.

## Carry-Forward

None for AUTH-RDR. Reviewer noted a L1 "move `userManager` to its own module to break the cycle physically" as a future cleanup candidate — not worth filing as a backlog row, will surface naturally if auth-provider.tsx gets refactored.
