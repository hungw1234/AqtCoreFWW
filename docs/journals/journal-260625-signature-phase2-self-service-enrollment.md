# Signature Multi-Provider Enroll — Phase 2 Self-Service Enrollment Shipped (3-Slice Cadence, 1 Security Caveat)

**Date:** 2026-06-25 · **Severity:** Medium (intended design, gated by Phase 3) · **Component:** Identity.Domain, Identity.Application, Identity.Api · **Status:** Resolved (caveat documented, follow-up phase required)

## What Happened

Phase 2 (Self-Service Enrollment) of plan 260623-1950 shipped: 3 slices, 4 commits to main (NOT yet pushed):
- Slice 1 `21cc6172`: self-service signature-profile CRUD (Create/Update/MarkDefault/Delete + ListMy query + DTO). Controller `UserSignatureProfileController` /api/v1/me/signature-profiles. Caller identity JWT-pinned (anti-IDOR).
- Slice 2 `5c37b6c7`: cert enrollment via possession-proof. NET-NEW entity `SignerEnrollmentNonce` (PK Guid, UserId+TenantId bound, Challenge byte[], TTL 300s, single-use). `EnrollMySignatureCertCommand` switches on Kind: USB = raw-verify via VerifySignatureCommand(Format=Raw); Remote = dispatch SignPayloadCommand (Mysign sync-OTP, provider-key stays out of Application layer per D6 guard). Self-service ALWAYS writes ApprovalStatus=Pending. `VgcaCertSubjectParser` relocated Infrastructure→Domain (allow Application to re-assert CCCD). Consume-before-verify anti-replay; DbUpdateConcurrencyException→clean domain error.
- Slice 3 `7292f703`: read aggregate `SignatureOverviewQuery` + DTO (profiles + enrollments + NationalIdEstablished + HasApprovedEnrollment flags). Controller GET /api/v1/me/signature-overview. DRY: shared SignatureProfileDto.From factory.
- Docs close-out `5b073a37` (dependency-map + changelog + backlog).

All 3 slices code-reviewed APPROVED. Checkpoint commit + review per slice. No production code breaking changes; contracts match Phase 1 domain.

## The Brutal Truth & Security Caveat

**CRITICAL DESIGN CAVEAT — NOT A BUG, NOT A REGRESSION, BUT MUST NOT REACH PRODUCTION BEFORE PHASE 3:**

The binding guard (`SignerBindingGuard.cs:31-44`) currently filters only `IsActive` and does NOT check `ApprovalStatus==Approved`. When `SignerCertEnrollment.Create` sets IsActive=true even for Pending status, a self-service **Pending cert is bindable and signable BEFORE admin approval**. 

This is **exactly the work charted for Phase 3** (phase-03-admin-approval-binding-guard-tightening, SECURITY-CRITICAL), which will red-test the stricter guard. **Phase 2 must NOT reach production ahead of Phase 3.**

This is not a Slice-3 regression — it reflects the intentional Phase-2 scope. But shipping the looser binding without the Phase-3 approval gate active exposes a 30-day window (until Phase 3 deploys) where unapproved enrollments could sign documents. The overview's `HasApprovedEnrollment` flag already reflects the stricter intent — it exists to eventually gate UI features when Phase 3 lands.

## Technical Details

**Slice 1 (CRUD):**
- MarkDefault uses 2-flush pattern to avoid filtered-unique-index violation: first update other profiles IsDefault=false, flush; then update target IsDefault=true, flush. Single tx, two separate SaveChanges calls.
- Controller queries JWT claim `sub` for UserId + `tenant_id` for TenantId; body-binds anti-IDOR (no [FromRoute] user override).

**Slice 2 (Enrollment):**
- Entity `SignerEnrollmentNonce`: TTL 300s via EF `HasValueGenerator(e => e.ExpiresAt)` (NOW+5min at INSERT). IX_..._ExpiresAt exists, no consumer yet (T4 SWEEP non-security).
- USB path: `VerifySignatureCommand(Format=Raw)` reuses existing raw-PKCS#1 verify (no new protocol).
- Remote path: `SignPayloadCommand` to Mysign; provider-key literal never enters Application (D6 guard prevents).
- Nonce consumption: `IsTenantScoped=true`, RLS applied. Concurrent double-submit: first succeeds, second throws DbUpdateConcurrencyException→caught at Infra layer, wrapped to clean `EnrollmentNonceConsumed` domain error (500 prevented; 400 expected).
- Parser relocation: `VgcaCertSubjectParser` (was Infra) moved to Domain. No behavior change; breakage caught in Infra.Tests build (B1 lesson: "build 0E" claim must cover ALL affected test projects, not just src).

**Slice 3 (Read):**
- `SignatureOverviewQuery`: single query, LEFT JOINs profiles + enrollments. Returns overview flags: `NationalIdEstablished` (scout C1 resolved), `HasApprovedEnrollment` (currently all-false; Phase 3 will flip). DRY: shared DTO factory avoids Slice-1 dto duplication.

## What We Tried

1. **Self-service idempotent design:** Admin `EnrollSignerCertCommand` (Phase 1) intentionally NOT modified; self-service already matches idempotent semantics. INVARIANT 260522-A probe at Slice 2 start found admin-dedup was a non-issue; avoided scope creep.

2. **Nonce consumption race:** M1 lesson — concurrent double-submit threw unhandled DbUpdateConcurrencyException (500) → wrapped to clean domain error (400) at Infra layer.

3. **Concurrent MarkDefault:** Slice 1 two-flush pattern tested with Testcontainers + concurrent UpdateAsync; verified unique-index does not fire mid-flush.

4. **Parser relocation fallout:** B1 lesson — moved `VgcaCertSubjectParser` from Infra to Domain; Application tests green, but Infra.Tests broke (parser consumed in test fixture). Rebuild caught it; not invisible to CI.

## Root Cause Analysis

**No root cause for Phase 2 itself; caveat is intentional design gate:**

The binding guard issue is NOT a defect; it's the intended boundary. The architecture allows self-service Pending → admin → Approved → signer-usable. Phase 2 ships the self-service→Pending path. Phase 3 gates binding on Approved status. This sequencing exists because Phase 2 approval workflows are async (Mysign OTP callback, VGCA cert validation); shipping the binding gate before approval is wired would force stubs or false-green tests.

**Lessons here are process-level:**

- Reviewer-driven (code-review + manual teeth) caught the caveat; it's not hidden, just gated. INVARIANT 260522-A probe confirmed caveat is documented, not silent.
- Idempotent self-service design (borrowing admin handler behavior) is correct KISS; no need to fork approval logic.

## Lessons Learned

1. **Layer-relocation coverage.** When moving code between assemblies (Infra→Domain), rebuild ALL affected test projects, not just src. "build 0E" doesn't mean "projects I touched are clean" — it means "full solution compiles." Lesson: explicit checklist — "identify all test projects that import the moved type → rebuild them separately."

2. **Concurrent nonce anti-replay.** DbUpdateConcurrencyException (row-version mismatch on second consume attempt) is a transport error, not a domain error. Wrap at Infra boundary, return clean domain `EnrollmentNonceConsumed` (not 500). Current code does this correctly.

3. **KISS idempotent design.** Reusing admin handler logic for self-service (both write Pending / IsActive=true) is simpler than forking. Self-service CANNOT auto-approve (intentional literal `ApprovalStatus.Pending`); admin path is unchanged. This keeps Phase 2 and Phase 3 decoupled.

4. **Caveat ≠ bug.** Documenting a design gate (binding requires approval; approval happens Phase 3) is not hiding a regression. It's saying "feature is intentionally incomplete." The overview flags (`HasApprovedEnrollment`) already signal intent.

## Next Steps

1. **Immediate (done):** All 3 slices merged to main. Code-review APPROVED x3. Tests green: Application.Tests 363/363, arch 149/149 (module-RLS + D6 + AuditCategory + query [AuditCategory] not required), Api-host build 0E/0W.

2. **Pre-production gate:** Phase 2 must NOT be deployed to production until Phase 3 (admin-approval-binding-guard-tightening, SECURITY-CRITICAL) is also completed and deployed. This is a hard sequencing requirement, not a "nice to have."

3. **Phase 3 teeth-test:** Red-test the binding guard to verify Pending certs are NOT bindable (will fail with current main branch code). Flip binding check to `IsActive && ApprovalStatus==Approved`, watch test green.

4. **Follow-up (T4, non-security):** SIGN-ENROLLMENT-NONCE-EXPIRY-SWEEP (background job to clean expired nonces; IX_..._ExpiresAt has no consumer yet).

5. **Spike follow-ups (resolved-positive, not yet E2E live):** C1 NationalIdEstablished scout, C3 USB raw-PKCS#1 spike — both positive. P7 smoke will exercise them end-to-end.

## Verify (final state)

- Build: 0E, 0W
- Identity.Application.Tests: 363/363 ✓
- Identity.Domain.Tests: included in Application.Tests count ✓
- Identity.Integration.Tests: signing-specific 52+ ✓
- Architecture guard suite: 149/149 ✓ (module-RLS + D6 + AuditCategory; queries need no [AuditCategory] as they're not IBaseCommand)
- Api-host build: 0E, 0W ✓
- Code review: APPROVED x3 (Slice 1, 2, 3)

---

**Emotional reality:** Phase 2 is clean — no blind spots caught at review, no burning edge cases. The caveat feels odd to surface so explicitly, but it's the right call: ship Phase 2 knowing Phase 3 is the sequencing gate, not a surprise. The team nailed idempotent design (reuse admin logic, no fork) and kept the scope tight (read-only OverviewQuery, credential binding deferred). Merging to main was anticlimactic in the best way. Now the hard part: shipping Phase 3 before any production deployment of Phase 2.
