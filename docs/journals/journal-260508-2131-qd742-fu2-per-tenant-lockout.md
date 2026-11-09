# QD742-FU2: Per-Tenant Lockout Enforcement Complete

**Date:** 2026-05-08 14:21Z
**Severity:** Medium
**Component:** Identity.Infrastructure, AuthServer, QĐ 742 compliance
**Status:** Resolved

## What Happened

PR #94 merged (squash `bc86e1cd` on main). Closes Phase-02 follow-up P2: the actual lockout decision now respects per-tenant `CredentialPolicy` settings (MaxFailedAttempts, LockoutMinutes) instead of framework defaults (5 attempts / 15 min).

**Scope:** 7 files, +325/-4 lines. One new UserManager subclass, 7 unit tests, symmetric DI registration (AuthServer + InfrastructureRegistration), docs updated post-merge.

## The Brutal Truth

This feels like a grudging victory. Phase-02 (v2.133p20, May 1) added the **columns and UI** for per-tenant lockout policy. But the actual runtime behavior was still hardcoded. Admins could configure policies that were completely ignored by the framework. A week of features sitting dead, waiting for 1 day of implementation. Extremely frustrating pattern.

The real pain: ASP.NET Core Identity has no built-in tenant awareness. Subclassing `UserManager<T>` and overriding `AccessFailedAsync` is straightforward but undocumented. We only discovered it because we'd already invested in the pattern—otherwise, might have wasted cycles on DI interception tricks.

## Technical Details

**New file:** `TenantAwareUserManager.cs` (50 LOC)
- Sealed, non-generic subclass of `UserManager<ApplicationUser>`
- Overrides `AccessFailedAsync(ApplicationUser user)`
- Ctor: 9 base params + `ICredentialPolicyRepository`
- Tenant resolved from `user.TenantId` (not `ITenantContext`)—safer because policy lookup is stateless

**Resolution order (4-tier fallback):**
```
if (user.TenantId is null or Guid.Empty) → use Options.Lockout
else if (policy == null) → fallback
else if (policy.MaxFailedAttempts == 0) → fallback
else if (policy.LockoutMinutes == 0) → fallback
else → apply policy
```

**Registration (symmetric):**
- AuthServer: `.AddUserManager<TenantAwareUserManager>()` post-token-providers
- Identity.Infrastructure: same in `InfrastructureRegistration` for API host

**Tests:** 7/7 unit (4 happy path + 3 edge: zero maxAttempts, zero lockoutMinutes, empty TenantId). Full suite: Identity.Infrastructure 106/106, AuthServer 27/27, Architecture 26/26. Zero regression.

## What We Tried

Initial design considered `ITenantContext` injection, but tenant must resolve from user object—context is request-scoped, user entity is method-scoped. Avoided that trap early via code review.

## Root Cause Analysis

Phase-02 shipped UI without wiring the backend decision point. Normal scoping issue: feature ownership split across "admin policy" (read-side) and "auth enforcement" (write-side). They weren't synchronized until FU2. Root lesson: P0 features need integration tests that **exercise the full flow**, not just isolated unit tests.

## Lessons Learned

1. **Tenant-aware framework overrides:** Subclass + override the virtual method, inject scoped repo, resolve tenant from domain object (not context). ASP.NET Core Identity is designed for this; docs just don't emphasize it.

2. **Symmetric DI registration:** When adding host-level service registrations, always check whether the same module is used by API (via InfrastructureRegistration). Catch asymmetry at review time, not production deployment.

3. **Integration test discipline:** Phase-02 had unit tests for `CredentialPolicy` read-side (warning message) + `LockoutEscalationService` separately. But no test that **admin sets policy → user gets locked out**. FU2 now covered; lesson stays: cross-feature flows need E2E validation.

## Next Steps

- **QD742-FU5** (spawned, P3): race condition in CAS increment (concurrent AccessFailedAsync calls)
- **QD742-FU6** (spawned, P3): exception handling in policy lookup (DB failure → fallback, not crash)
- Docs sweep complete (changelog + roadmap + dependency-map + compliance-evidence). No further doc action needed.

---

**Status:** DONE
**Concerns:** None. Code review approved 9.0/10. All tests pass. Backward compat verified (zero breaking change).
