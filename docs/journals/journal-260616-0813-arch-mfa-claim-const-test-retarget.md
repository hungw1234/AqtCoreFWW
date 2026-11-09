# Journal — Retarget stale MFA-claim compliance arch test

**Date:** 2026-06-16 08:13 ICT · **Slice:** `ARCH-MFA-CLAIM-CONST-LOCATION` (backlog T4) · **Mode:** `/cook` · **Type:** fix (test-only)

## What

`Qd742Phase03ComplianceTests.Compliance_1_6_b_MfaRequirementHandler_ValidatesMfaAtClaim` was RED.
Retargeted its reflection lookup from `MfaRequirementHandler` to `MfaClaimEvaluator` — the real,
post-refactor home of the `mfa_at` claim const. One file changed, no production code touched.

## Root cause

Refactor `cbe92117` ("emit X-Mfa-Required only when MFA step-up is the actual unmet requirement")
extracted a shared `MfaClaimEvaluator` (static) so `MfaRequirementHandler` and the new
`MfaForbidResultHandler` derive the same "MFA is fresh enough" verdict. That move pulled the
`MfaAtClaimType` const + recency logic out of `MfaRequirementHandler` — but the structural
compliance test still reflected `typeof(MfaRequirementHandler).GetField("MfaAtClaimType",
NonPublic|Static)`, which now returns null → `.NotBeNull()` fails. A refactor with an unupdated
reflection-based arch test = silent RED that survived several slices (noted-but-not-fixed in the
Phase-0 changelog entry).

## Audit-at-slice-start (INVARIANT 260522-A) paid off again

The backlog row's identifiers had **drifted** (same class as the signing red-team file-drift):
- Row said the const is `private` → it is actually **public** on `MfaClaimEvaluator`.
- Row offered "fix the test OR relocate the const" as equal options.

Empirical probe (grep + read, not trusting the row) showed the const is consumed by **3 sites**
(`MfaClaimEvaluator` + `AuthorizationController:38` + `MfaChallengeController:37`), so *relocate*
would ripple into the AuthServer host. That collapsed the "two equal options" into one correct
answer: **retarget the test, never move the const.** Confidence high enough to proceed without
asking (scout-first, ask-second).

## Key decisions

- **Fix the test, not the prod code.** The const's home (`MfaClaimEvaluator`, the shared evaluator
  both handlers use) is architecturally correct; the test was simply pointing at the wrong type.
- **`nameof(MfaClaimEvaluator.MfaAtClaimType)`** instead of a `"MfaAtClaimType"` string literal —
  a future rename now fails compile instead of silently returning null and going RED again. Guards
  against this exact stale-reflection class.
- **`BindingFlags.Public | Static`** (const is now public) — the old `NonPublic` flag was itself
  part of the breakage.
- **Kept** the handler exists+sealed assertions and the `== "mfa_at"` value contract → QĐ 742 1.6.b
  evidence stays intact.

## Lesson

A reflection-based / structural arch test does **not** move when you refactor the symbol it
inspects — it goes RED with no compiler help. When extracting a shared helper, grep the arch/
compliance suite for the moved symbol name in the same slice. Prefer `nameof(...)` over string
literals in such tests so the next refactor breaks the build (loud) rather than the assertion
(quiet, and easy to leave parked as "pre-existing").

## Verification

- Baseline: target test RED — `Expected claimTypeField not to be <null>` at line 148.
- After: `Qd742Phase03ComplianceTests` **5/5 green** (target RED→GREEN, 4 siblings unaffected).
- code-review APPROVE, 0 blocking; confirmed no production file in the diff.

## Follow-up filed

- `MFA-AT-CLAIM-LITERAL-DRY` (T4) — the `"mfa_at"` literal is triplicated (evaluator + 2 AuthServer
  controllers). Consolidation crosses the host ↔ Identity.Infrastructure boundary (needs a shared
  claim-types home), so low-priority, not done this slice.
