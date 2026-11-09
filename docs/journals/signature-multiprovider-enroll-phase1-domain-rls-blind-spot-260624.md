# Signature Multi-Provider Enrollment Phase 1: Domain + RLS Blind Spot

**Date:** 2026-06-24 · **Commit:** `3c4f3ac8` · **Severity:** Medium (caught in review) · **Component:** Identity module, SignerCertEnrollment + UserSignatureProfile · **Status:** Resolved

## What Shipped

Phase 1 of plan `260623-1950` (multi-provider signature cert enrollment + admin-approval lifecycle). Domain & persistence layer complete; P2–P8 deferred.

**Entities:**
- `SignerCertEnrollment`: ALTER ADD Kind (EnrollmentKind: Remote/USB), ApprovalStatus (CertApprovalStatus: Pending/Approved/Rejected), ApprovedBy, ApprovedAt, RejectionReason, PublicId. Domain methods: Approve/Reject (Reject also sets IsActive=false). Factory signature broke: now requires explicit Kind + ApprovalStatus (approval-gated contract; sole caller EnrollSignerCertCommand passes Approved for admin path).
- `UserSignatureProfile` (NEW aggregate): tenant-scoped, single-default unique index, FK to User (NoAction), image columns nullable (upload deferred P4/P5).
- 3 enums (EnrollmentKind, CertApprovalStatus, SignatureVisualType).

**Repositories:**
- `SignerCertEnrollmentRepository.HasAnyEnrollmentAsync` (no IsActive filter — captures ALL statuses; approval-gate consequence for P3).
- `UserSignatureProfileRepository.GetByPublicIdAsync` (M1).

**Handlers:**
- `UpdateUserCommandHandler`: guard SigningNationalId change when user has any enrollment (finding H5).

**Migrations:**
1. `AddEnrollmentKindAndApproval`: nullable→backfill→not-null; existing rows: Approved if IsActive=1, Rejected if IsActive=0, Kind=Remote (historical assumption). PublicId=newid().
2. `AddUserSignatureProfile`: table + RLS policy.

## The Brutal Truth

We shipped green. Code-review returned CHANGES_REQUESTED and was RIGHT. The arch test suite (145/145 tests) was BLIND to a tenant-isolation hole. That stings.

The implementer ran the full test suite locally, all 682 tests green, and reported ready. The code-reviewer caught what the automation missed: a new tenant-scoped table (`UserSignatureProfiles`) with NO RLS policy applied in the migration. The SharedInfrastructure `TenantTableIsolationGuardTests` only reflects over `shared-infra.DbContext`; it never touches the module's `IdentityDbContext`. Architecture coverage = false confidence.

## Technical Details

**Critical finding (architecture blind spot):**
```
UserSignatureProfiles: TenantId + PII (profile image metadata, user FK).
Migration: table + unique index + FK. 
Missing: RLS policy ALTER TABLE ... ENABLE ROW LEVEL SECURITY;
Result: table reads unfiltered; multi-tenant data exposure if someone queries outside the ORM layer.
```

**Where arch-suite failed:**
- `docs/identity/dependency-map.md` lists Identity as owner of `UserSignatureProfiles`.
- `SharedInfraTenantTableIsolationGuardTests` (line 47–80) iterates `DbContext.Model.GetEntityTypes()` only from `SharedInfrastructure.DbContext`.
- `IdentityDbContext` ≠ SharedInfrastructure.DbContext. The new entity never reached the guard.

**Fix applied (post-review):**
- Migration `AddUserSignatureProfile` now calls `RlsMigrationHelper.AddTenantIsolationPolicy(mb, nameof(UserSignatureProfile))`.
- Teeth test (`SignatureProfileRlsMigrationTests`) inserted before migration sequence to prove SQL policy fires.

**Secondary findings (TDD failures that got caught after "green"):**
- HIGH-1: H5 guard (SigningNationalId lock) had only a false-returning stub test. Could be inverted silently. Added 3 positive + 1 skip test.
- HIGH-2: Migration backfill "teeth" tests were phantom (seeded via factory, never ran the actual SQL UPDATE statement). Renamed honestly + documented the coverage gap (will retrofit in P2 when multi-provider backfill is real).

## What We Tried

1. **Architect skip:** Assumed arch-suite coverage was hermetic. It wasn't. The suite guards the shared-infrastructure layer but is BLIND to module-local DbContext tables.
2. **TDD shortcut:** The H5 guard test used a stub (return false) that satisfied the test's syntax but proved nothing. Implementer didn't catch because the test "passed" and moved on.
3. **Migration sanity:** "All migrations apply cleanly" is not "all tenant-scope invariants held." Migrations need active teeth tests.

## Root Cause Analysis

1. **Architecture assumption decay:** The guard was designed against one DbContext (SharedInfra). When modules gained their own DbContexts (Identity, Collaboration, etc.), the guard became PARTIAL coverage but the comment never updated. Implementer trusted "145/145 green" as gospel.

2. **Test-driven incompleteness:** RED-first TDD worked for happy-path domain logic (MarkDefault, H1 idempotence). But for security invariants (RLS, authz guards), a passing stub test ≠ proven invariant. The H5 test passed because it didn't actually invoke the guard's real path.

3. **Teeth test as documentation debt:** Migration backfill tests that use factories (not raw SQL) are false-green. They seed the happy path but don't exercise the UPDATE statement itself. This is a known pattern in this codebase—documented but NOT yet automated to fail.

## Lessons Learned

**Verify-the-verifier:** "All tests green" from the implementer is not a gate. An independent code-review caught a suite-invisible tenant-isolation hole in 30 seconds of grep. The suite passed; the contract failed. This is the pattern from `monday-260623-module-audit-low-findings-fix.md` (2 findings false-positive when implementer trusted audit; 4 real ones hidden until grepped). **Always have a human read the code.**

**RLS coverage must be module-aware:** The shared-infra arch guard is correct but incomplete. We need a peer guard that reflects over each module's DbContext, asserts all TenantId-bearing entities have RLS policies. `IdentityDbContext.Model.GetEntityTypes()` must include the `UserSignatureProfile` reflection + policy check. This is a follow-up task.

**Stub tests for invariants are non-tests:** H5 guard test was honest (false-return stub) but inert. Invariant-guard tests must exercise the actual constraint path. Same lesson from `monday-260623-collab-read-receipts-fullstack.md` (teeth-test tautology caught in review). Red-first only works if the test is actually RED when the constraint is violated.

## Next Steps

1. **Immediate (this slice):** RLS policy landed in migration. H5 + backfill tests filled. Code-review approved (1 NIT pending, unblocking).
2. **Follow-up (P2 entry):** Add arch guard `ModuleDbContextTenantIsolationGuardTests` (new test class in ArchTests). Reflect over `IdentityDbContext`, `CollaborationDbContext`, etc.; assert all TenantId entities have RLS. This closes the blind spot.
3. **Migration teeth test retrofit (P2):** When P2 backfills multi-provider enrollments, the teeth test will run real SQL (no stub). Document today's phantom test as a coverage gap.

## Verify

- Build: 0E / 0W
- Identity.Domain.Tests: 133/133
- Identity.Application.Tests: 331/331
- Identity.Integration.Tests: 318/318 (RLS policy proven in smoke)
- Arch tests: 145/145
- Code review: CHANGES_REQUESTED (3 findings) → APPROVED (post-fix, 1 NIT pending)

**P2 entry blockers:** None. RLS + guards in place. 3 blocker-spikes already validated (Mysign sync-OTP feasible, PDF-XObject pivot, raw-PKCS#1 reuse). Viettel sandbox confirm deferred to P2 smoke.
