# Authenticated Form Submission Consent Contract Fix

**Date**: 2026-07-08 17:05
**Severity**: High (silently rejected PII submissions despite consent)
**Component**: Forms.Presentation (FormSubmissionsController), Forms.Application (SubmitFormRequest)
**Status**: Resolved

## What Happened

The authenticated form submission endpoint silently rejected all PII forms with consent, returning 422 `GOV_FRM_020` ("requires consent") even when the user checked the consent checkbox and submitted it. The FE had the checkbox, collected consent, sent `consentGiven: true` in the request body, but the BE DTO had no corresponding field — System.Text.Json dropped it unlogged, the handler always saw `ConsentGiven=false`, and the gate `RequiresConsent && hasPiiFields && !ConsentGiven` always fired.

**The reframe:** Backlog row `AUTH-SUBMIT-NO-CONSENTGIVEN` was filed as a T4 "product-shape question" (is authenticated PII+consent intentionally public-only?). Scout revealed it wasn't a design ambiguity — it was a live contract break. The FE at `web/src/features/forms/form-submit-modal.tsx:72` + `form-submission-api.ts:162` already implemented the full flow: consent checkbox validator forces it, consent collector sends `consentGiven` in the POST body. The BE `SubmitFormRequest` DTO at `FormSubmissionsController.cs:161` simply had no `ConsentGiven` field. Re-tiered T4→T3 and fixed inline.

**Fix applied:** Mirror the public-path DTO — added `bool ConsentGiven = false` to `SubmitFormRequest` between `Data` and `Signature` fields; wired `request.ConsentGiven` into the command in the `Submit` action. 2 production lines, 0 breaking changes. Public path (`PublicSubmitRequest`) already had the field and worked correctly; authenticated path was dead-code.

**Outcome:** Authenticated users can now submit PII forms with consent. FE checkbox flows through, handler gate passes, `ConsentRecord` is created linked to the submitter (`DataSubjectId=request.SubmittedBy` from JWT). All 43 Forms.Integration tests pass (includes 2 new authenticated-path consent tests).

## The Brutal Truth

The frustrating part is that this was **entirely discoverable by reading both sides of the contract** and **nobody did**. The row filer saw the BE DTO missing the field and framed it as "maybe we don't want authenticated consent?" instead of "let me check if the FE already sends this." The authenticated form submission feature had been shipping since P6 Phase-2 with zero consent integration on that path — a silent gap that nobody caught because nobody tested the unhappy path (PII+no consent = rejection).

Five minutes of FE scout work (`grep -n consentGiven web/src/features/forms/`) would have reframed this immediately: DTO field = 1 line, wiring = 1 line, problem solved. Instead it went to backlog as a "product decision" and sat there until the audit invariant (INVARIANT 260522-A) triggered: backlog rows older than 5 days are snapshots, verify empirically before opening.

## Technical Details

**Code locations:**
- `SubmitFormRequest.cs` (new field location): Between `Data` (line 10) and `Signature` (line 14), add `public bool ConsentGiven { get; set; } = false;`
- `FormSubmissionsController.Submit` (line 104): `ConsentGiven: request.ConsentGiven,` into the command builder
- `SubmitFormCommand` (line 28, already existed): `public bool ConsentGiven { get; set; }` — no change needed
- `SubmitFormCommandHandler.cs` (line 52, already existed): gate `if (submitRequest.RequiresConsent && hasPiiFields && !command.ConsentGiven)` — no change needed

**Parity with public path:**
```csharp
// PublicSubmitRequest (always had this):
public bool ConsentGiven { get; set; } = false;

// SubmitFormRequest (now matches):
public bool ConsentGiven { get; set; } = false;
```

**Test coverage (2 new authenticated-path tests):**
- `SubmitFormCommandHandler_AuthenticatedWithPii_ConsentGiven_Success`: Authenticated user submits PII form, consents, checks gate, returns 200. Asserts `ConsentRecord` created with `DataSubjectId=submitting user PublicId`.
- `SubmitFormCommandHandler_AuthenticatedWithPii_NoConsent_422`: Authenticated user submits PII form, skips consent, hits gate, returns 422 `GOV_FRM_020`. Asserts zero `ConsentRecord`.

**Test run green flags:**
- Forms.Integration: 43/43 (2 new + 41 existing, fresh Testcontainers)
- Forms.Application: 340/340 (unchanged)
- Identity.Application: 426/426 (unchanged)
- Build: 0 errors, 0 warnings

## What We Tried

1. **Assumed product design needed:** The row framing ("is this intentional?") seemed plausible — authenticated admins might not need citizen consent. Spent ~15 min reading design docs and convo history before deciding to verify empirically instead of speculating.

2. **FE scout:** Grepped `consentGiven` in `web/src/` — found collector at form-submit-modal, validator forcing it true, POST body including it. Confirmed FE side fully wired. Row reframed immediately.

3. **Checked public path as parity:** Public DTO had `ConsentGiven`, command had it, handler used it. Authenticated side missing only the DTO field — the whole handler stack was already there.

## Root Cause Analysis

1. **DTO shape evolution:** The public form submission API was designed first (or refactored later) to include `ConsentGiven`. The authenticated API evolved separately without equivalent scope. No reviewer caught the asymmetry because the field is optional (defaults to false) — the unsigned submission works fine, the signed submission (another code path) also works. Only the test case "authenticated + PII + consent checkbox" falls into the dead zone.

2. **Backlog row framing by single-sided scout:** The filer read the BE DTO and asked "why is this field missing?" instead of "does the FE consumer already send it?" The answer determines whether it's a design question (add field intentionally) or a bug (field already sent but dropped). Single-sided investigation → speculative framing → time wasted in decision-making instead of fix.

3. **Test coverage gap:** The FE tests likely assert "form submits successfully with consent checkbox checked" (they probably test against the public endpoint). The authenticated path has no equivalent test. Adding one surfaced the contract break immediately.

## Lessons Learned

1. **Audit invariant (INVARIANT 260522-A) pays off:** Backlog rows > 5 days old are stale. A 5-min empirical check (FE consumer scout + DTO grep) flips the category from "needs decision" to "bug fix" and cuts scope from meeting-dependent to implementable.

2. **Scout both sides of the contract before framing.** When a DTO field is missing, the question "why?" has two evidence paths: (A) ask the designer (slow, speculative), (B) check if the client already sends it (fast, decisive). Do (B) first.

3. **Feedback tie-in:** This validates existing guidance (`verify-fe-consumer-before-be-build` and `feedback-fe-dto-must-match-be-controller`). The DTO contract must be visible to both sides; silent mismatches are only caught in testing.

4. **Symmetric test coverage for split paths.** Authenticated and public form submission are divergent paths. Consent logic appears in both; feature parity expectations should be enforced in tests (both paths should have identical consent assertions). This is a class-design issue for the test fixture — perhaps a shared `ConsentAtomicityTests` base with path-specific ctor injection.

## Next Steps

1. **Close row:** `AUTH-SUBMIT-NO-CONSENTGIVEN` → Closes with this fix. No follow-up design needed; contract is symmetric now.

2. **Feedback file:** Add to MEMORY feedback: "Scout FE consumer before accepting backlog row framing" (links `verify-fe-consumer`, `feedback-fe-dto-must-match-be-controller`, INVARIANT-260522-A).

3. **Optional test framework hardening:** Refactor consent tests to have a shared base (`ConsentAtomicityTestsBase`) and enforce both-path parity. Low priority (tests are green now).

---

**Status:** RESOLVED — `plan/260708-1621-auth-submit-consentgiven-contract-fix/plan.md` (2-line fix, 2 tests, integrated)
