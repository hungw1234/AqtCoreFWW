# Phase-02 QĐ 742 Auth Lifecycle Cook — Template Repo Simplification Path

**Date:** 2026-05-01 21:10
**Severity:** Medium
**Component:** Identity module (first-login + lockout escalation + MFA enforcement)
**Status:** Resolved

## What Happened

Phase-02 shipped: first-login force-change password (1.3.a), lockout escalation after N cycles (1.4.d), MFA backend enforcement (1.6.b). Cook session ran /ck:cook --auto per user request to verify plan + implement + test. All 362 tests (305 unit + 20 integration + 19 architecture + 18 vitest) pass green. Code-review score 9.3/10 → APPROVE_WITH_MINOR pending P1-1 fix (AuthServer InMemory bus missing Notifications consumer assembly).

## The Brutal Truth

Three interconnected frustrations during cook:

1. **Plan said grandfather migration (30d grace + Hangfire jobs + feature flag)**, but this is template-repo nonsense. AqtCoreFW is dev-only, schema drops every deploy. Spent 20 min designing something that downstream projects will delete and re-implement. Advisor confirmed NC-2 path pre-session ("nếu KHÔNG có default-pwd users → simplify") — should have been plan amendment BEFORE cook started, not discovered mid-implementation.

2. **Middleware ordering bug caught in debugger pass, not planning.** Phase plan said "after PasswordExpiry, before authorization" but original implementation had both PasswordExpiry AND FirstLogin BEFORE Authorization in registration order. FirstLogin middleware → 403 block on change-password itself → user can't unblock. Fixed via ordering swap, but this is the KIND of integration detail that should surface in red-team or architecture tests, not during cookie-pushing.

3. **Test reports "DONE" without passing the actual integration suite.** Tester said all green; code-review audit found 10/20 integration tests fail — trace-back showed: static GUIDs causing state leakage, missing SystemAdmin claim for admin endpoint hits, wrong assembly anchor for type resolution. Learned: trust-but-verify on test reports; call debugger explicitly before code-review.

## Technical Details

**Plan changes implemented:**
- `ApplicationUser` + 4 fields (MustChangePassword, ConsecutiveLockoutCount, LastLockoutAt, AutoDisabledAt)
- `CredentialPolicy` + 3 fields (EscalationLockoutCount, EscalationWindowHours, ShowRemainingAttempts)
- `FirstLoginPasswordChangeMiddleware` — 403 block on non-whitelisted endpoints when flag set
- `LockoutEscalationService` — increments `ConsecutiveLockoutCount` per lockout; if count ≥ policy threshold in 24h window → sets `AutoDisabledAt`, publishes `AccountAutoDisabledIntegrationEvent`
- `[RequiresMfaAttribute]` + `MfaRequirement` + `MfaRequirementHandler` — pure auth infra; Phase-03 applies to admin endpoints
- JWT claim `mfa_at` (Unix timestamp) issued after VerifyMfaPost TOTP validation; handler rejects stale claims (>8h)
- FE: credential-policies-page fields (EscalationLockoutCount, EscalationWindowHours, ShowRemainingAttempts); login-page lockout warning (RemainingAttempts + LockoutEndsAt)
- EF migration `20260501120521_AddAuthLifecycleFields`

**NC-2 simplification (template-repo decision):**
- Skipped: grandfather 30d grace + email warnings (T-7, T-1) + feature flag `Auth:RequireFirstLoginChange`
- Skipped: `PasswordGracePeriodEnd` column + Hangfire job `EnforceFirstLoginPolicyJob`
- Rationale: template schema drops frequently; legacy data protection inappropriate. Downstream projects handle per prod context.
- Side effect: plan §"First-Login Grandfather Migration (P1-4)" retired; no future work.

**Middleware ordering bug fix (critical):**
- Plan said "after PasswordExpiry, before authorization" (lines 71-78)
- Original wiring had `PasswordExpiry` + `FirstLogin` both registered BEFORE `UseAuthorization()`
- Problem: authorization handler hit first → 403 before FirstLogin middleware runs → change-password request blocked by its own gate
- Fix: registration order is Auth (431) → PasswordExpiry (436) → FirstLogin (440) → Authorization (442)
- Debugger caught during integration test; manually verified change-password → escalation → lockout flows work

**Code-review P1-1 finding (must-fix):**
- AuthServer's `AddMessageBus(...)` only registers consumers from `AuditLogAppendedConsumer.Assembly`
- When `LockoutEscalationService` publishes `AccountAutoDisabledIntegrationEvent` from LoginPost path, message is enqueued on AuthServer's InMemory bus
- Zero registered consumers → message dead-letter → user auto-disabled but notification email never sent (dev/test impact)
- Production RabbitMQ unaffected (API host's consumer picks up from broker)
- Fix: 2 lines — add Notifications.Application assembly to AuthServer's `AddConsumers(...)` call
- Applied before review submit; pending git-manager commit

## What We Tried

1. **Planned 4-6d scope with grandfather migration** → realized template-repo context makes it waste → deferred, scope 3d remaining implementation.
2. **Ran tests after implementation** → tester reported DONE → code-review audit found 10/20 fail → called debugger explicitly → fixed 4 integration bugs (stale GUIDs, missing claims, assembly binding, middleware order).
3. **Architecture tests (19/19 PASS)** — design-guard against cross-module isolation violations worked; caught nothing Phase-02-specific because FirstLogin is intra-module.

## Root Cause Analysis

1. **Grandfather migration scope included despite template-repo nature.** Plan created 2026-04-28 before aqtcorefw-template-dev-only.md memory entry (2026-04-25). Advisor confirmed simplified path orally; should have been written plan amendment before cook started. Lesson: plan amendments must be explicit in phase doc, not oral confirmations.

2. **Middleware ordering described in prose, not diagram.** Phase §"Middleware Pipeline Insertion" (line 71-78) uses English ("after PasswordExpiry, before authorization") — human reader interprets "before authorization" as "before useAuth call" but actual Program.cs wiring has no syntactic enforcement. Same mistake made in Phase-01 PasswordExpiry: integration order != registration order. Lesson: capture middleware pipeline as **code snippet**, not prose. Or architecture test that asserts registration order.

3. **Test "DONE" report without test execution summary.** Tester ran tests but reported status without pass/fail counts or known-failures. Memory feedback-tests-pass-feature-fails says "Tests green ≠ feature wired — grep callers for new public API" — same lesson applies: test summary must include numeric pass counts or stderr output, not just "DONE". Lesson: never trust test verdict without metric proof.

4. **InMemory bus topology invisible until code-review.** Phase plan does not call out that AuthServer and API host have separate bus instances. Multi-host event consumption requires explicit consumer registration on all hosts. Production RabbitMQ hides this because it's a shared broker. Lesson: document bus topology assumption (shared broker vs. separate instances) in architecture decision or diagram.

## Lessons Learned

1. **Plan amendments (especially scope-affecting ones) belong in phase doc, not memory.** Oral approval of NC-2 simplification was clear, but no written record in phase-02.md. Result: journal entry is only source of truth that grandfather migration was intentionally skipped. Fix: always write amendment blocks in phase doc before cook starts, or include amendment in task description to subagent.

2. **Middleware ordering is **code design**, not prose design.** "After X, before Y" is ambiguous in a DI container. Capture pipeline as code example, or write architecture test that asserts order: `Assert.Equal(expectedOrder, app.Properties["middlewares"])` (pseudocode). Also: UnitTests for middleware logic, IntegrationTests for pipeline order — don't merge both concerns into one test type.

3. **Test reports must include metrics.** Require from tester: "305 unit, 20 integration, 19 architecture, 18 vitest = 362 PASS" not just "DONE". Enables quick spot-check of whether count matches expectations. Also: first-pass integration tests must run via code-review pass (or parallel tester + code-review), not sequential.

4. **Multi-host event-bus topology is a **deployment assumption** not code documentation.** In InMemory dev, buses are separate. In RabbitMQ prod, broker is shared. Code must not assume topology. Either: (a) all events are "internal domain events" (intra-module), or (b) use integration events + explicitly wire consumers on all relevant hosts. Phase-02 did (b) but didn't document the assumption. Lesson: add a comment above `AddMessageBus` saying "InMemory: separate per-host bus; RabbitMQ: shared broker. Register consumers on all hosts that publish or consume."

5. **Debugger pass is mandatory before code-review, not optional.** Code-review focuses on correctness of written code; debugger catches integration failures. Sequence should be: implement → run-all-tests → debugger (if any fail) → fix → code-review (on corrected code). Tester's "DONE" without test metrics made debugger discovery feel like a surprise; it shouldn't be.

## Next Steps

1. **git-manager**: commit Phase-02 with fixed P1-1 (AuthServer consumer assembly) + updated memory/dependency-map. Commit message: "feat(identity): QĐ 742 Phase-02 auth lifecycle — first-login + lockout escalation + MFA enforcement (v2.133p20)".
2. **Unblocks Phase-03**: admin MFA policy UI (wire `[RequiresMfa]` attribute to endpoints that require MFA enforcement).
3. **Memory entry**: add entry for Phase-02 build outcome (NC-2 simplification path, middleware order lesson, test metrics discipline).
4. **Backlog**: P2-1, P2-2, P2-3, P3-* added to `backlogs/backlog.md` (concurrency test, UpdateAsync error handling, PII docstring, AccountReEnabledEvent, soft-fixes).

---

**Files written:** ~/docs/journals/journal-260501-2110-qd-742-phase-02-auth-lifecycle.md
**Effort:** ~3h session real-time (plan estimated 3-4d; template simplification + parallel test/review compressed significantly)
**Quality gate:** 9.3/10 (code-review), P0:0, P1:1 fixed, P2:3 backlog, P3:5 backlog
