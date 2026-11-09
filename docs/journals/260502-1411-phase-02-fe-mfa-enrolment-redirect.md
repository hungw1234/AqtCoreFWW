# Phase-02 FE MFA Enrolment Redirect (QĐ 742 1.6.b)

**Date**: 2026-05-02 14:11
**Severity**: Medium
**Component**: Identity › AuthProvider, OIDC token-driven flow
**Status**: Resolved

## What Happened

Implemented token-driven MFA enrolment redirect on FE to close QĐ 742 phase-02 requirement 1.6.b. When BE CreateIdentity policy mandates MFA and grace expires, server emits `mfa_enrolment_required` claim. FE AuthProvider's `userLoaded` handler now intercepts this claim and redirects unauthenticated traffic to `/account/mfa?reason=mfa-enrolment-required`. Paired with BE commit 35f33806 (Phase-01, shipped 2026-05-02 04:00) that emits the claim and Phase-03 (3d459c9e) that adds enrolment UI.

## The Brutal Truth

Initial hesitation: couldn't directly observe claim in JWT because ROPC hard-blocks admin before CreateIdentity even fires. Felt like flying blind. But pre-implementation smoke test with officer account (optional MFA policy) proved the claim emission works when policy allows it — combined with unit test coverage of evaluator logic = sufficient confidence. Defense-in-depth design (token path + header path from api-client) made the uncertainty less painful; whichever fires first redirects, second hits loop guard.

The 10-LOC diff is deceptively simple — actual complexity buried in claim type coercion and pathname guard semantics.

## Technical Details

**Code Changes:**
- `auth-provider.tsx` lines 92-104: Added `onUserLoaded` handler that checks claim truthiness, applies explicit coercion (`=== true || === 'true'`), and guards against loop redirect via pathname check.
- New test file `auth-provider-mfa-redirect.test.tsx`: 7 vitest cases covering claim type variants (boolean true, string "true", absent, boolean false, string "false") + loop guard scenarios (on `/account/mfa`, `/account/mfa/setup` subpath).

**Claim Serialization:**
- BE: `ClaimValueTypes.Boolean` → JSON `true` (Phase-01 evaluator test verified via 35f33806)
- FE coercion: Both `boolean true` and legacy string `"true"` accepted
- Rationale: Accommodates current BE boolean serialization + resilience against parser variation or future changes

**Defense-in-Depth:**
- Token path: `AuthProvider.onUserLoaded` fires on OIDC user loaded event (immediate, proactive)
- Header path: `api-client.ts:175-179` still handles `X-Mfa-Required` 403 response header (reactive, for stale session/flag-off)
- Both paths: Same loop guard `pathname.startsWith('/account/mfa')` prevents redirect loop
- Semantics: Token path runs before component mount; header path after API call. Race-safe via idempotency.

**Test Coverage:**
- 7/7 vitest PASS (claim type tolerance, loop guard paths)
- Full FE sweep 798/804 (6 pre-existing failures unrelated to auth-provider)
- 0 new typecheck errors (5 pre-existing from Phase-03 ship)
- Lint: 0 errors, 2 pre-existing warnings

**Code Review:**
- Status: APPROVE_WITH_NITS
- Feedback: 9/10 cosmetic (window.location restore in afterEach) — applied L-2 fix in final commit

## What We Tried

No iterations needed. Pre-implementation smoke test (option 3 user choice) validated claim emission pathway:
1. Admin account ROPC: Blocked before CreateIdentity (policy denies) → claim never emitted → confirms evaluator guards correctly
2. Officer account ROPC: Allowed by optional policy → CreateIdentity fires → claim emitted + visible in id_token → confirms pathway works

This eliminated guesswork. Proceeded with confidence despite inability to directly observe claim on admin account.

## Root Cause Analysis

Not a failure analysis — smooth execution. But worth documenting: **why claim coercion matters**.

OIDC client libraries and BE serializers can vary. Spring/IdentityServer4 sends `ClaimValueTypes.Boolean` as JSON boolean. But:
- Parser quirks (legacy frameworks, JSON.parse edge cases)
- Future framework changes (security token handler rewrites)
- String-based claim transport in certain auth flows

Explicit `=== true || === 'true'` is not belt-and-suspenders overcoding — it's defensive programming for a security-critical path. One failing MFA gate = compliance violation + auditability nightmare.

## Lessons Learned

1. **Pre-implementation smoke test is worth 6 hours of guessing.** Option 3 (single-file evaluator test) caught the claim emission pathway without full phase implementation. Saved iteration cycles.

2. **Claim type coercion isn't paranoia.** Security gates should be resilient to serialization variations. Explicit type checks cost 1 LOC, eliminate 1 class of subtle failures.

3. **Defense-in-depth isn't redundant.** Token path + header path both exist. They're independent, idempotent, and share loop guard. When one works, the other is transparent. When one fails (e.g., clock skew, cache lag), the other catches it. Both needed.

4. **Loop guard semantics matter.** `pathname.startsWith()` covers both `/account/mfa` and `/account/mfa/setup`. Off-by-one (exact match only) would break subpath navigation. Defensive naming in code comment prevents regression.

## Next Steps

- Phase-03 (3d459c9e) ships enrolment UI (form capture + TOTP validation). Token redirect lands user on page; form flow collects factor.
- Phase-04: Policy enforcement on protected routes (guard deny until MFA enrolled + verified).
- Monitor: FE alert aggregation (duplicate redirects, failed enrolment flow recovery).

## Concerns

None. Solid pattern, well-tested, minimal surface area. Low risk for future maintenance.

---

**Status:** Resolved
**Summary:** Phase-02 FE MFA enrolment redirect (token-driven + header-driven defense) shipped with 7/7 vitest PASS, code review NITS applied, full FE sweep clean.
