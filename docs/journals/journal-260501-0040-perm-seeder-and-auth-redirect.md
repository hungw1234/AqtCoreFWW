# PermCodes Drift + OIDC Silent Renew UX Collapse (v2.133p18)

**Date**: 2026-05-01 00:40 ICT
**Severity**: High
**Component**: Identity seeding (BE), Authentication redirect (FE)
**Status**: Resolved + Follow-up backlog queued

## What Happened

Two back-to-back bugs caught same session:

**Bug 1**: User reports 3 toast "Không có quyền truy cập" when clicking "Phiên bản & Di chuyển" tab in workflow detail (`/admin/workflow/{guid}`). T5-10 added 5 new `WORKFLOW.MIGRATION.*` permissions to controllers with `[Authorize(Policy=...)]` but **forgot to add them to `E2ETestDataSeeder.Permissions.cs::BuildPermissionList()`**. Permission rows missing in DB → 403 even for SystemAdmin with ALL grant.

**Bug 2**: User reports "401 redirect doesn't work — blank page for 4 minutes, 13+ failed 401 calls." Backend logs show `invalid_grant ID2003` from `/connect/token`. Silent token refresh dead on arrival.

## The Brutal Truth

**PermCodes drift is recurring pain pattern**: This is the SECOND time this exact bug happens in same codebase (precedent: v2.133p9 `TenantBackfillSeeder` regression). We KNEW this pattern needed architecture tests and didn't enforce it fast enough. Worse: bonus finding `FORMS.SUBMIT.APPROVE/REJECT` **also missing** from seeder, likely since T4-21. That's two bugs hiding in plain sight.

**OIDC silent renew UX is fragile minefield**: We've been relying on notification toast countdown to signal redirect. That's **backwards**. One strict-mode unmount during React dev, one race condition between `getNotificationInstance()` and `setSessionErrorHandler`, and the user gets a blank page watching 401s fire silently. The session is dead but UI doesn't know.

## Technical Details

### Bug 1 - Missing Permission Seeds

**Discovery**: 
- PermCodesSeederCoverageTests added (`src/tests/AqtCoreFW.Tests.Integration/Identity/PermCodesSeederCoverageTests.cs`)
- Test: iterate `PermCodes.All` + verify each exists in seeded rows
- **Test FAILED on first run** → found 7 missing perms: `WORKFLOW.MIGRATION.CREATE/READ/UPDATE/DELETE/EXPORT` (new) + `FORMS.SUBMIT.APPROVE/REJECT` (buried in v4 refactor)

**Root cause**: 
- T5-10 modified `WorkflowMigrationsController` + `WorkflowVersioningController` with `[Authorize(Policy="WORKFLOW.MIGRATION.READ")]` etc.
- **No corresponding BuildPermissionList() update** 
- Same pattern: `PermCode` static class updated, controller decorated, seeder forgotten

### Bug 2 - Token Expiry Redirect

**DevTools timeline**:
- `t=0s`: Page load, token already expired (user opened tab 15min later)
- `t=0.2s`: API call → 401 `invalid_grant ID2003`
- `t=0.5s`: `api-client.ts::triggerSessionError()` fires → `removeUser()`
- `t=0.7s`: Toast countdown starts "Redirect in 3s..."
- `t=1.2s`: Strict-mode hydration re-renders AuthProvider
- `t=1.5s`: `setSessionErrorHandler` from old effect scope nuked by cleanup
- `t=4s`: Toast expires, **no redirect** (handler was unmounted)
- Loop repeats on retry

**Investigation findings**:
- `automaticSilentRenew` **only triggers `silentRenewError` if token near-expiry**. Expired-on-load = no event.
- `RouteGuard` checks `isAuthenticated` (state) not `user.expired` (getter) → re-render race.
- Toast UI countdown = unreliable side-effect carrier. Should never be UX signal for redirect.

**Pattern audit**: Found 4 total callsites using `signinSilent`/expired patterns:
- `api-client.ts::triggerSessionError()` — P1, FIXED
- `handleExtend` in profile flow — P1, FIXED (silent-swallow behavior)
- `SoD violations export` raw fetch call — P1, deferred (edge-case, low traffic)
- `Copilot SSE` event factory — P2, deferred (AI module alpha)
- Generic SignalR retry cleanup helper — P3, deferred (refactor scope)

## What We Tried

1. **PermCodes**: First guess was "User's grant missing" → checked admin role, all grants set. Realized must be seeding gap. Added architecture test immediately.

2. **OIDC Redirect**: 
   - Tried: Check if `automaticSilentRenew` configured (it is)
   - Tried: Increase toast countdown (fragile band-aid)
   - **Real fix**: `removeUser()` in catch → fires `userUnloaded` event → AuthProvider sets `user=null` → RouteGuard signinRedirect. Single source of truth, no toast race dependency.

## Root Cause Analysis

**PermCodes**: Dispatcher list update (PermCodes.cs, controller decorator, seeder) must be atomic. We have 3 places now, no enforcement. Architecture test fills gap but **should've been in CI since v2.133p9**, not discovered twice in wild.

**OIDC**: We misunderstood OIDC library contracts. `userUnloaded` event is the ONLY reliable redirect signal. Toast countdown is presentational, not control logic. Tried to make UI do infrastructure's job.

## Lessons Learned

1. **Recurring pattern needs enforcement NOW**: PermCodes+Seeder mismatch cost us 2 incidents in same codebase. Architecture test ROI = insanely high (1 test, <100 LOC, <300ms, caught 2 bugs + 1 buried bug).

2. **Event-driven auth state > UI side-effects**: Every `signinSilent` call must trigger `removeUser()` on error, letting event loop handle redirect. Toast = FYI only, not control signal.

3. **Strict-mode uncovers async handler brittleness**: `setSessionErrorHandler` in effect without dependency cleanup would've stayed hidden in production. Strict-mode revealed pattern infect. Apply same skepticism to useEffect handlers in critical paths.

## Next Steps

- **Merged P1 fixes**: PermCodes seeder + api-client OIDC redirect (commits `7161f3b1`, `16cc9a91`)
- **Backlog AUTH-RDR-FU1** (P1): Fix `handleExtend` silent-swallow pattern in profile
- **Backlog AUTH-RDR-FU2** (P1): SoD violations export raw fetch → `removeUser()` on 401
- **Backlog AUTH-RDR-FU3** (P2): Copilot SSE + SignalR cleanup audit
- **Follow-up task**: Review all callers of `signinSilent` + session invalidation paths (Grep `invalid_grant|401|removeUser`), apply same pattern
- **CI gate**: Architecture test suite now required in Cook finalize (already added to feedback).

## Emotional Reality

Mid-level frustration: PermCodes pattern is stupid (3-way dispatcher) but recurring twice before guardrails = team oversight. OIDC UX bug is humbling — we tried to solve in UI what belongs in event-driven state machine. Good news: fixes are simple once root cause identified. Bad news: 4 more similar callsites still vulnerable, pattern will repeat unless we close in AUTH-RDR-FU backlog.

**Win**: Architecture test catches BOTH bugs on first run, including buried FORMS.SUBMIT permissions. This validates test-driven guards.
