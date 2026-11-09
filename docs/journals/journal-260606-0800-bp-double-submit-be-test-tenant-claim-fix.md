# BP Double-Submit + Idempotency BE Test + Tenant Claim Fix

**Date**: 2026-06-06 08:00  
**Severity**: Medium  
**Component**: AntiDoubleSubmitMiddleware, IdempotencyMiddleware, BpMultiRunHttpTests  
**Status**: Resolved (NOT pushed, main ahead=2)  

## What Happened

Executed backlog row `BP-DOUBLE-SUBMIT-TOKEN-BE-TEST` (AC6 descope carry-forward from v2.137p157 BP multi-run ship). Row claimed: "WebApplicationFactory harness lacks AntiDoubleSubmit middleware & Redis wiring → descoped test (~0.5d effort to wire)."

Empirical scout DISPROVED the premise. Middleware was **already wired unconditionally** at `Program.cs:674`; TestContainers Redis active at `ApiFactory.cs:128-135`; real `TwoTierCacheService` (L1 IMemoryCache + L2 Redis) installed via DI. Task reframed: assertion-only, not infrastructure.

## The Brutal Truth

The row's descope justification was an **unverified assumption**. Five minutes of empirical scouting revealed the harness was production-like. Lesson: never trust a descope "blocked-on" claim without checking the actual code. Cost of 5-min audit << cost of shipping with stale framing.

Worse: code-review post-implementation surfaced **two production tenant-scoping bugs** the original row never named. Both live, both in existing code, both missed by existing unit tests.

## Technical Details

### Bug 1: AntiDoubleSubmitMiddleware.cs:44 (LOW severity, silently broken in prod)

Middleware reads tenant claim as `context.User.FindFirst("tenantId")` (camelCase), but AuthServer issues it as `tenant_id` (snake_case per OAuth2 convention, verified at `AuthorizationController.cs:431`, `MfaChallengeController.cs:122`). Per-tenant cache key lookup fails; every tenant collapses into shared `"anon"` namespace, defeating documented per-tenant scoping.

Impact: X-Request-Token collision across tenants improbable (random GUID), but cache entry shared within tenant cluster.

### Bug 2: IdempotencyMiddleware.cs:38 (HIGHER severity, data-leak risk)

**Identical bug** in sibling middleware. On cache-hit, returns cached **response body** (24h TTL, caller-chosen deterministic keys). "Anon" namespace collapse = potential **cross-tenant data leak** on idempotent POST (e.g., create transaction, clerk calls same endpoint, second clerk gets first's response).

### Test Fact Regression Lock

Initial idempotency Fact 3 asserted `defIdB != defIdA` (two tenants, same key). Passes trivially if middleware never engages (vacuous lock). Added sub-assertion `replayId == defIdA` (same-tenant replay) to **prove middleware actually fires** before claiming isolation. Same-tenant 409 Fact already non-vacuous (Fact 1 proves middleware fires), but idempotency needed engagement proof.

## What We Tried

1. **Audit-at-slice-start** → grepped `Program.cs`, `ApiFactory.cs`, TestContainers config → confirmed middleware + Redis fully wired.
2. **Refactored test harness** → extracted `CreateAndActivateBpAsync()`, `BuildWorkflowDefPayload()`, token/key POST helpers (DRY; easier to reason about facts).
3. **Wrote 4 new facts** (3 double-submit: same-token→409+count==1, distinct→2 runs, 2-tenant isolation; +1 idempotency with engagement proof).
4. **Code-review surfaced tenant bugs** → grep validated claim-naming across AuthServer + all middleware consumers.
5. **Applied one-liner fixes** to both middlewares: `tenant_id ?? tenantId ?? "anon"` (legacy fallback preserves existing unit tests green).

## Root Cause Analysis

**Why the descope was stale:** p157 builder descended into Docker redis-check assumption without verifying the ApiFactory actually wires it. Assumption cost: 1d of uncertainty. Reality: Redis pre-wired.

**Why tenant bugs survived:** Middlewares use camelCase claim name; AuthServer uses snake_case (OAuth2 std). No existing integration test validates the **claim-name match** end-to-end (unit tests mock `IHttpContextAccessor.User` manually, no auth flow). Idempotency bug is adjacent severity escalation: double-submit is race (high collision bar), idempotency is cache-hit (100% deterministic on same key).

**Why code-review caught them:** Reviewer traced the claim-read path and noticed the name divergence against AuthServer; not a "find by keyword" scan. Validates the lesson: adjacent bug sits one step away from the flagged one.

## Lessons Learned

1. **Audit a descope "blocked-on" claim empirically before designing around it.** The row's framing was a hypothesis, not ground truth. Five-minute scout invalidated 0.5d of assumed work.

2. **A passing test that can pass for the wrong reason is not a lock.** Initial idempotency Fact was green but didn't prove the middleware engaged. Added engagement proof (same-tenant replay assertion) to make the test **fail if middleware disengages**.

3. **Tenant claim-naming MUST be validated end-to-end, not inferred from unit tests.** Unit mocks hide the OAuth2 claim-naming convention. Integration test (WebApplicationFactory + real AuthServer OR realistic HttpContext.User) catches this. Current unit tests for both middlewares are now accidentally passing because they mock the `User` directly.

4. **Adjacent bugs (idempotency twin of double-submit) warrant same-session review.** Both use tenant claims; both were wrong. Same-session scope avoidance leaves orphan bugs.

## Next Steps

1. **Commits (queued, NOT pushed):** `708aa03c` (fix(security) code: 2-liner tenant fixes) + `1ecd6c2a` (docs: changelog v2.137p158 + backlog close).
2. **Validation:** Build 0E/0W; BpMultiRunHttpTests 5/5 pass (1 pre-existing AC3 skip) on real SQL2022+Redis; AntiDoubleSubmitMiddlewareTests 14/14; IdempotencyMiddlewareTests 3/3 (verified fallback preserves green).
3. **Gap acknowledged:** Idempotency slice (2nd fix + refactor + 4th fact) added after code-review, not re-reviewed (same pattern, proportionate).
4. **Backlog:** Close row `BP-DOUBLE-SUBMIT-TOKEN-BE-TEST`; carry forward: residual 28 non-tested Geo provinces.

## Metrics

- Lines changed: 2 prod (tenant claim); ~180 test (4 facts + DRY refactor).
- Build: 0E/0W; test pass: 5+14+3=22/22.
- Prod impact: Zero on double-submit (improbable collision); idempotency leak close. No migration/entity/contract/FE/depmap change.
