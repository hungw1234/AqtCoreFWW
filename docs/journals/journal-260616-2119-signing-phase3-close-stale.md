# Signing Foundation Phase 3 (Detached Data) — Closed as Already-Shipped

**Date:** 2026-06-16 ~21:19 ICT · **Flow:** `/cook phase 3` (code mode, audit-at-slice-start) · **Scope:** BE Identity comments + docs · **Commit:** `8455c3bb` (5 ahead of origin/main, not pushed)

## Verdict
Phase 3 was **already shipped by the earlier security-remediation slice (C1)** — not implemented this session. The audit-at-slice-start (INVARIANT 260522-A) ground-truthed the plan's pre-red-team Phase-3 body against the actual code and found all four success criteria met **and test-backed**. The session produced doc-comment + plan/changelog edits only — zero logic, zero contract change.

## What the audit found (empirical, not plan-trust)
- **`cert==signer` for detached signing is enforced at verify-at-ingest**, not at a separate guard call in the handler. `InProcessIdentitySigningClient.VerifySignatureAsync` (lines ~75-96) calls `SignerBindingGuard.VerifyAsync` whenever the verify request carries a `SignerUserId` and the signature is cryptographically valid; a non-bound verdict is mapped to `IsValid=false` with a `signer-binding-failed` reason → the Forms handler turns it into a `GOV_FRM_022` ValidationError. The guard **returns a verdict and never throws** for a binding failure — exactly the red-team H6 contract the plan demanded (a thrown guard would escape the Result contract → 500).
- **The anonymous public-form path already skips binding** — `SubmitPublicFormCommandHandler` supplies no `SignerUserId`, and the client's `request.SignerUserId is Guid` check double-guards the skip. `SubmitFormCommandHandler` (authed) sets `SignerUserId = SubmittedBy`. This is precisely the "wire into BOTH handlers, skip anon" the plan flagged the original draft for missing — already correct.
- **No canonicalizer fork** — both handlers call the single BE `FormDataCanonicalizer` (Mode-1 canonical-JSON / Mode-2 doc-hash).
- **Tests back the close:** `InProcessIdentitySigningClientTests` (h) `BindingGuardReturnsNotBound_FailsClosedWithBindingError` + (i) `NoSignerUserId_BindingGuardNotCalled`; `SignerBindingGuardTests` (integration); `FormDigitalSignatureSubmitTests`.

## The one thing NOT built — and why that is correct
The plan's residual step was "route detached signing through `ISignatureOrchestrator`." Deliberately **not** built:
- The orchestrator is an **async request → execute → link → poll** machine that produces a durable underlying record (DocFlow / PendingTransaction) you then poll for completion.
- A detached form-field signature is **complete-at-submit and verified synchronously at ingest** — there is no async lifecycle, no underlying record, nothing to poll.
- Routing it through the orchestrator would mean inventing a `SigningRequest` row + a new `SigningUnderlyingKind` for something already finished: pure ceremony, zero functional payoff. It also **contradicts the phase's own risk note** ("consolidation, not redesign — resist re-architecting verify-at-ingest — keep diff minimal").
- Therefore `DetachedDataSigningStrategy` correctly stays **routing-only** (implements `ISigningStrategy`, not `ISigningExecutionStrategy`): nothing routes a detached request through the orchestrator because the detached flow never enters it.

## Decisions / lessons
- **A plan phase can be wholly satisfied by an earlier, more security-critical slice under a different seam than the phase imagined.** Phase 3 pictured detached signing as an orchestrator-driven request; the shipped architecture made it a verify-at-ingest concern that the C1 remediation already bound. Audit the seam that actually exists, not the one the plan names.
- **"Route it through the unified layer" is not free.** When the unified layer is an async-request machine and the artifact is synchronous-complete, the routing is ceremony. The plan's own risk section was the tell.
- User chose "close + light cleanup" over building the ceremony — consistent with YAGNI and rule §3 (do not silently build a plan step that the ground truth makes redundant).

## Changes
- `DetachedDataSigningStrategy.cs` — comment "execution implemented in Phase 3" → explains it is routing-only by design (verify-at-ingest, never orchestrator-driven). Plan-phase references removed.
- `ISigningStrategy.cs` — removed the per-phase execution-surface enumeration + "red-team C5" label; explains the routing-vs-execution split is intentional (some artifacts are verified synchronously and never enter the orchestrator), per the no-plan-refs-in-code convention.
- `plan.md` + `phase-03-*.md` (gitignored, local) — Phase 3 → Done with a close banner.
- `project-changelog.md` — close entry.

## Verification
Identity.Application build 0W/0E (covers the `ISigningStrategy` cref). Infrastructure edit is a pure XML-doc comment — inert to IL; Infra.dll rebuild skipped to avoid stopping the running API/Auth hosts. No new tests (criteria pre-covered by the tests listed above). No DB migration.

## Carry-forward
- Unchanged from Phase 2a: **Phase 2 remaining** (form-gen signer-anchor render — couples to 2b), **Phase 2b** (multi-signer BP transport — net-new), **Phase 4** (in-proc sandbox), **Phase 5** (remote PDF assembler), **Phase 6** (post-upload cert-bind verify oracle). Dependency order now: 0✓ → 1✓ → {2a✓, **3✓**} → 2b → 4 → 5 → 6.
- 5 commits unpushed on `main` (Phase 1, Phase 2a ×2, Phase 3) — push timing is the user's call.
- `web/vitest-output*.txt` still untracked (gitignore pattern `62cf359c` doesn't match `*-final.txt`) — housekeeping pass pending.
