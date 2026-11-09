# Phase 6 Slice B: Self-Service Enrollment with Deliberate Design Reversal

**Date**: 2026-06-30 18:45
**Severity**: Medium
**Component**: Identity/Enrollment
**Status**: Resolved

## What Happened

Phase 6 Slice B shipped a complete self-service signing-certificate enrollment UI (FE multi-step modal: set-CCCD-if-missing → pick auth method Remote/USB → ceremony → done) + supporting BE endpoint `PUT /me/signing-national-id` that intentionally *reverses* a deliberate Phase 2 security restriction: **CCCD (national ID) is now user-settable instead of admin-only**.

Slice B reuses Phase 2's enroll challenge/API logic and existing VGCA USB adapter. The FE layers 3 react-query hooks; the BE endpoint adds tenant assertion + 12-digit format validation + active-enrollment lock (CCCD becomes immutable once a Pending/Approved cert binds it).

## The Brutal Truth

The frustration here is clean: Phase 2 made a deliberate call to forbid self-service CCCD-setting, citing circular-trust risk (user sets CCCD → user gets cert bound to that CCCD → trust becomes self-asserting). But that same restriction **completely blocks** self-service enrollment, since both Remote and USB enroll paths bail with `SIGNING_NATIONAL_ID_NOT_ESTABLISHED` if CCCD is unset. Slice B can't ship without either (a) admin pre-sets CCCD for every user (non-self-service) or (b) reversing the restriction. User approved (b). The guilty part: reversing a security decision always feels sketchy, even when the math checks out. But we surfaced it explicitly with the original rationale + trade-offs. No silent flips.

## Technical Details

**The reversal is safe because the claim alone is non-authoritative.** Downstream checks:
- `EnrollMySignatureCertCommand` re-asserts enrolled cert's subject-CCCD equals stored value AND requires cryptographic possession proof (phone OTP for Remote, token private-key signature for USB). A user setting CCCD to a victim's ID cannot obtain a matching certificate.
- `SignerBindingGuard` re-checks at sign time.

Guardrails added:
- 12-digit format validation
- Active-enrollment lock: CCCD immutable while cert is Pending/Approved; Rejected/Revoked states allow correction
- Idempotent no-op on repeat calls
- Plain `[Authorize]` (no MFA) per user's explicit call; residual risk low.

## What We Tried

Only one path: reverse the restriction with downstream re-validation. No half-measures (e.g., OTP gate on CCCD-set) were explored — user accepted full self-service risk + possession-proof gate.

## Root Cause Analysis

Phase 2's objection was theoretically sound *in isolation* but incompletely scoped. The objection missed that a separate cryptographic layer (possession proof + subject-cert match) makes user-set CCCD non-authoritative. The reviewer at Phase 2 didn't have visibility into downstream enroll behavior — a planning/communication gap.

## Lessons Learned

1. **Claims vs. trust grants**: Distinguish "this value is self-set" from "this value is trusted." A self-set claim is safe if downstream checks make it non-authoritative. Reverse when the downstream gate is independent + cryptographic.
2. **Surface reversals explicitly**: When reversing a prior deliberate decision, surface it to the owner with original rationale + trade-off + new evidence. Prevents quiet drift into inconsistency.
3. **Test infra fragility flag**: FE vitest suite intermittently crashes a worker under fork load on a pre-existing network-firing test (jsdom XHR ECONNREFUSED). Blast-radius runs (BE 409/149, FE 46 tests) used as gate; full suite is flaky. Follow-up: hunt the unmocked HTTP request in the test suite.

## Next Steps

- Merge pending (code-reviewer APPROVE_WITH_NITS → M1 FE step-reset race + M2 BE tenant assertion, both fixed).
- Monitor enrollment flow for CCCD-collision edge cases in staging; add alerting on repeated CCCD-mismatch rejects.
- Backlog: investigate `FE-TEST-SUITE-XHR-WORKER-CRASH` (high ROI for test stability).

## Gates Passed

- BE Identity.Application: 409/409 tests (11 new)
- Architecture: 149/149 tests
- FE: 46 tests (profile + files + digital-signature scope)
- tsc + eslint: clean
- Code review: APPROVE_WITH_NITS (fixes applied)
