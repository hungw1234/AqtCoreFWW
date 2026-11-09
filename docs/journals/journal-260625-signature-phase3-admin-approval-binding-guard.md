# Signature Phase 3: Admin Approval + Binding-Guard Tightening

**Date:** 2026-06-25 (2 slices, committed to main; push deferred)
**Severity:** Critical (closes security window opened by Phase 2)
**Component:** Identity.Signature module (Enrollment domain, admin API, completion guard)
**Status:** Code DONE + shipped commits (`d1d26283` + `d956a989` + docs `31212d34`); red-before-green PROVEN for 3 teeth; code reviewer APPROVE (restart after die).

## What Shipped

**Slice 1** (`d1d26283`): Admin approval API surface.
- `ApproveSignerCertEnrollmentCommand` / `RejectSignerCertEnrollmentCommand` resolve by PublicId with 2-step tracked load (GetByPublicIdAsync is AsNoTracking → reload as tracking before mutate); ConflictError guard on already-terminal state.
- `ListPendingEnrollmentsQuery` paginated response (page + size params).
- Controller `SignerCertEnrollmentAdminController` (route `api/v1/admin/signer-cert-enrollments`); tenant from `ITenantContext.TenantId` STRICTLY (NOT `ResolveTenantId` which honors SystemAdmin X-Tenant-Id override → blocks cross-tenant approve). Policy `System.Config.Manage` + `RequiresMfa`.
- Repo new method `ListPendingAsync(tenantId, page, size)`.

**Slice 2** (`d956a989`): Binding-guard tightening + completion re-check.
- `SignerBindingGuard` now requires `IsActive && Approved` (was `IsActive`-only), so Pending cert does NOT bind.
- Tier-change logic: `HasAnyEnrollmentAsync` queries all-status (Pending/Approved/Rejected/Revoked), so revoked/rejected signer keeps enrolled tier (no silent downgrade to CCCD bootstrap).
- `PadesCompletionService` C5: re-check signer still has active + Approved enrollment before storing signed PDF; revoke/reject during async window → Fail with no blob write.

**Docs** (`31212d34`): Enrollment state machine + ADR-012 admin-approval guard.

**Security closure:** Phase 2 opened a window where a self-service Pending cert could sign before admin approval. Phase 3 closes it: Pending → NOT bound (slice 2) + now requires admin approval (slice 1) + C5 gate catches late revoke (slice 2).

## The Brutal Truth — Two Incidents

### Incident 1: Reviewer Dies Mid-Edit + Harness Silent-Approves Wrong State

A code-reviewer subagent executed Review task with Write/Edit tools enabled. **Mid-run it died with FailedToOpenSocket (API transient failure).** The harness flagged the commit to main as "intentional change" (🚩 DANGER SIGNAL). On inspection, `SignerBindingGuard.cs` was reverted to the **OLD-TIER-REVERT** logic — i.e. the security fix (tighten to `IsActive && Approved`) was reverted to the insecure `IsActive`-only state, WITH A TEMPORARY COMMENT `// OLD-TIER-REVERT-RED-PROOF-RESTORED-BY-REVIEWER-FIX`.

**Recovery:** Ran `git status`, read the file, recognized the literal marker + the comment contradicting the security invariant. Reverted the file back to the tightened logic, kept the reviewer's test fix (they had also added the vacuous-redproof fix below, which was useful). Committed to main.

**Lesson (CRITICAL):** When a subagent with Write/Edit tools dies mid-task, DO NOT trust the harness's "intentional" flag for half-finished security-critical states. Always `git status` + read the file yourself before continuing. A subagent restoration script may have partially-executed logic ("let me restore the old behavior to make the test pass") that looks like correct code but isn't. **This is a tripwire for supply-chain confusion.**

### Incident 2: EF Sentinel + DB DEFAULT Silently Coerces Insert-Inactive to Active

Test (f) "revoked-only binding tier" was initially **vacuous** — it passed even when the old tier logic was in place. Root cause: the `IsActive` EF column uses `HasSentinel(false)` + `HasDefaultValueSql("1")`. When you INSERT with `IsActive=false`, the value is the sentinel ("unset"), so the DB DEFAULT fires and coerces the row to `IsActive=1`. Therefore `GetActiveByUserAsync` returned the supposedly-"revoked" row, and both old and new tier logic landed in the same `enrolled` block → no test differentiation.

**Fix:** Seed the enrollment ACTIVE, then call `Revoke()` + a **second** `SaveChangesAsync`. Now `UPDATE` sets `IsActive=false` explicitly (no sentinel), and the column stays false. After the fix, test (f) genuinely goes **RED** when you revert to the old tier logic. This mirrors production flow: an active, approved enrollment is revoked later via command → no INSERT trick.

**Lesson:** EF sentinels + DB DEFAULT can silently coerce an insert-inactive row to active. Mutable state should be seeded via UPDATE, not INSERT, if the initial value is "exceptional." A vacuous test that "passes but shouldn't" is often a sentinel/default interaction silencing the assertion. **Red-before-green on mutable state: seed then mutate, don't seed as mutated.**

## Technical Details

### Binding Tier & C5 Gate (Design Rationale)

**Why C5 is tier-level, not per-cert:**

The cert's serial/issuer at initiate came from Mysign cert-info API and are NOT persisted in the DER blob. Thumbprint algorithms differ across enrol paths:
- VnCaRootStore.ComputeThumbprint (Mysign path)
- X509Certificate2 SHA-1 (USB path)

A per-cert rematch at completion would false-FAIL legitimate completions (wrong algo → hash mismatch). Per-cert binding already enforced at initiate in `PadesTransactionInitiator` + P3 C5 gate bounds the time-window (revoke during async signing → caught).

**Edge case accepted:** One signer has 2 Approved certs, 1 is revoked during signing of the other → completion proceeds. Signer remains trusted enrolled signer = cert rotation is valid. **Deferred:** SIGN-C5-COMPLETION-RECHECK-PER-CERT-PRECISION (T4, product decision; requires persist `enrollmentId` at initiate).

### ITenantContext vs ResolveTenantId in Admin API

Admin controller uses `ITenantContext.TenantId` directly, not `ResolveTenantId`. Why: `ResolveTenantId` honors `X-Tenant-Id` header override for SystemAdmin. Approving an enrollment in a different tenant (via header hack) is a privilege escalation. ITenantContext is request-bound, ambient, no override.

### Slice Ordering: Admin-First, Guard-Tighten-Second

Slice 1 (admin approval) ships first; Slice 2 (guard tighten) ships second. Rationale: admin surface keeps the system functional (admins can approve before guard tightens), and the security-critical change is isolated to the last slice. If Slice 2 fails, Slice 1 is still deployable as a standalone no-op feature (approvals accepted, binding unchanged).

## Verify

**Tests (integration + teeth):**
- SignerBindingGuard: 10/10 (7 existing + Pending→NotBound + Rejected→NotBound + revoked-only→NotBound via H1 fixed-vacuous test)
- PadesCompletionService: 7/7 (5 existing + C5 revoked→Fail-no-blob + C5 approved→complete)
- Architecture suite: 149/149
- Api-host build: 0E / 0W

**Red-before-green (PROVEN for all 3 teeth):**
1. **Pending binding:** Revert tier-check `IsActive && Approved` → `IsActive` → tier binding test ĐỎ → restore → GREEN
2. **Revoked-only H1:** Seed via INSERT (vacuous) → GREEN WRONG → fix seed via UPDATE → GREEN CORRECT (reverted tier-check → ĐỎ) → restore → GREEN
3. **C5 completion gate:** Revert C5 re-check → ĐỎ (revoked signer signs, blob persists wrong) → restore → GREEN

**Code review:** First attempt died mid-edit (harness approved wrong state). Re-review after incident 1 fix: APPROVE (C5 tier-level coarseness acceptable per design-rationale comment; H1 confirmed non-vacuous).

## Lessons Learned (Extract)

1. **Subagent with Write/Edit + die = audit the delta manually.** Harness "intentional" flag is a speed hint, not a proof. Security-critical state changes warrant a read before continuing.

2. **EF sentinel + DB DEFAULT silences assertions.** Vacuous test passing = check sentinel logic + DEFAULT coercion in the column definition. Mutable state: seed then mutate, not seed-as-mutated.

3. **Reviewer death mid-task can leave production code in a restored "working but insecure" state** — this is a supply-chain class of bug (intermediate state after crash looks syntactically correct but semantically wrong). Always re-verify security invariants after restoring a died reviewer.

## Next Steps

**Immediate (post-Phase-3):**
- Decide push timing (await P4 stamp composition or push independently).

**Carry-forward (T4):**
- SIGN-C5-COMPLETION-RECHECK-PER-CERT-PRECISION: scope depends on compliance precision demand.
- SIGN-ENROLLMENT-NONCE-EXPIRY-SWEEP: cleanup stale nonces (T4).

**Phase roadmap (plan 260623-1950):**
- P1: Enrollment setup (DONE; ships v2.137p+ or later)
- P2: Self-service multi-provider enroll (DONE; shipped, red-team feedback integrated)
- P3: Admin approval + binding-guard tightening (DONE, 2 incidents recovered)
- P4: Stamp composition (NEXT; PAdES visible sig on document, currently cooking)
- P5–P8: Deferred per plan 260623-1950

---

**Status:** Phase 3 COMPLETE. 3/8 phases done. P4 cooking.
