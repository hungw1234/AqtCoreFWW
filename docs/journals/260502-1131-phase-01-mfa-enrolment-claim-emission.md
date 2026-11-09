# Journal — Phase-01 MFA Enrolment Redirect: Token Claim Emission (QĐ 742 §1.6.b)

**Date:** 2026-05-02 11:31 ICT  
**Branch:** main (uncommitted; ready for commit)  
**Type:** Feature implementation (Phase-01 of 4-phase MFA UX nudge)  
**Status:** COMPLETED — 343 unit tests PASS, 8 MFA-specific tests PASS, zero regression  
**Blocks:** Phase-02 (FE auth-provider hook), Phase-03 (post-enrol silent renew), Phase-04 (E2E finalize)

## Problem Diagnosed

Form-login path (`LoginPost` in AuthServer) lacked enforcement of mandatory-MFA policy. Users already enrolled reached feature gates cleanly; users NOT enrolled got no signal until hitting `[RequiresMfa]` endpoint → hard 403 redirect UX.

Root cause: ROPC `/connect/token` Branch B had policy check with `Forbid("mfa_enrol_required")`, but auth-code grant (form-login via `/connect/authorize` + `CreateIdentity`) skipped it entirely. **Same policy; two code paths; divergent behavior.**

## Solution Implemented

Token-claim-driven soft signal. Backend emits `mfa_enrolment_required=true` claim into both access_token + id_token at issuance. FE auth-provider hook reads claim, redirects to enrolment flow (Phase-02). DOES NOT block login—just nudges.

**Key design:** Single evaluation + emission point = `IMfaEnrolmentEvaluator` service, called from:
1. ROPC Branch B (keeps hard `Forbid` for CLI clients—can't self-enrol)
2. `CreateIdentity` (emit claim—browser clients can enrol themselves)

Channel-appropriate divergence: CLI stays protected, SPA gets graceful flow.

## Design Deviation: Service vs Private Helper

**Plan said:** "Extract to private helper inside controller."

**I extracted:** Standalone injectable `IMfaEnrolmentEvaluator` service + interface.

**Why:** Testability. Private members cannot be mocked. Service-with-interface allows clean NSubstitute-based unit tests. DRY achieved—both ROPC + CreateIdentity consume evaluator from same source.

**Trade-off:** Extra file + DI wiring vs impossible-to-test private method. Chose testability. File count: +3 files (interface, impl, tests).

## Implementation Highlights

### 1. Fresh Policy Re-evaluation on Refresh

Refresh-token grant calls `CreateIdentity` (line 92), which re-evaluates policy from DB, NOT cached claim. Admin can flip policy Optional→Mandatory between issuance + refresh; next token reflects new state. Verified via test case 6.

### 2. Claim Serialization: Boolean as String

OpenIddict serializes `ClaimValueTypes.Boolean` as JSON `"true"` (string), not boolean primitive. Phase-02 FE must use defensive check: `=== true || === 'true'`. Documented in phase-02-fe-auth-provider-hook.md.

### 3. Audit Log Volume Guard

**Code-reviewer HIGH-1 finding:** Refresh-token grants invoke `CreateIdentity`, would log on every silent refresh (~96 entries/day/user). **Fixed:** Audit log emission gated on `!IsRefreshTokenGrantType()`. Only initial authorization-code grant logs; refresh path skips audit.

### 4. Feature Flag Isolation

`Mfa:RedirectOnEnrolmentRequired` (default `true`) bound via `IOptions<MfaOptions>`. Flip false → behavior reverts to existing `X-Mfa-Required` 403 header fallback. Zero schema change. Escape hatch if Phase-02 FE redirect has issues.

## Files Touched

### Created
- `src/host/AqtCoreFW.AuthServer/Configuration/MfaOptions.cs` (25 LOC)
- `src/host/AqtCoreFW.AuthServer/Services/MfaEnrolmentEvaluator.cs` (interface)
- `src/host/AqtCoreFW.AuthServer/Services/Impl/MfaEnrolmentEvaluatorImpl.cs` (impl, ~60 LOC)
- `tests/unit/AqtCoreFW.AuthServer.Tests/MfaEnrolmentEvaluatorTests.cs` (8 unit tests)

### Modified
- `src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs` (+45 LOC): inject evaluator, call from ROPC + CreateIdentity
- `src/host/AqtCoreFW.AuthServer/Program.cs` (+2 lines): register service
- `src/host/AqtCoreFW.AuthServer/appsettings.json` (+3 lines)
- `src/host/AqtCoreFW.AuthServer/appsettings.Development.json` (+3 lines)
- `plans/260502-1131-mfa-enrolment-redirect-fix/phase-02-fe-auth-provider-hook.md`: added note on boolean string handling

### Will Update (finalize gate)
- `docs/dependency-map.md`: AuthorizationController section
- `docs/project-changelog.md`: Phase-01 completion

## Test Results

**Unit tests:** 8 total (exceeds 6-case spec). All PASS.

1. ✅ NotEnrolled + Mandatory + GraceExpired + FlagOn → claim emitted
2. ✅ NotEnrolled + Mandatory + GraceActive → claim NOT emitted
3. ✅ NotEnrolled + OptionalPolicy → claim NOT emitted
4. ✅ AlreadyEnrolled (`TotpEnrolledAt` set) → claim NOT emitted
5. ✅ NotEnrolled + Mandatory + GraceExpired + FlagOff → claim NOT emitted (feature flag off)
6. ✅ RefreshGrant + PolicyFlipped (Optional→Mandatory) → re-evaluate + claim emitted on refresh
7. ✅ RefreshGrant + PolicyStable → claim presence preserved
8. ✅ Audit log skipped on refresh-grant (volume guard verified)

**Regression:** AuthServer 22/22, Identity.Application 222/222, Identity.Infrastructure 99/99. **Total: 343/343 PASS. Zero regression.**

## Emotional Reality

This was **exactly on spec**—no surprises, no thrashing. Design deviation (service vs helper) felt over-engineered at first, but the testability story is clean. Could've shipped private method and avoided test friction later; chose the harder-now-easier-later path.

Audit log volume guard was a nice catch by code-reviewer. Silent refresh path is invisible to users; logging it would've polluted dashboards with noise. Gating the emit was the right call.

## Unresolved & Deferred

- **T8-T11 deferred to Phase-04:** Integration test + architecture test + smoke test + dependency-map docs. Phase-01 scope = unit-level correctness. Phase-04 = behavioral E2E + finalize gate.
- **Phase-02 unblocked:** FE auth-provider hook ready to claim, Phase-04 E2E will verify end-to-end flow.

## Next Steps

1. Git commit Phase-01 changes (work context ready, not yet committed).
2. Phase-02 unblocks → FE builds auth-provider hook reading claim.
3. Phase-03 → post-enrol silent renew (refresh-token reissue with claim cleared).
4. Phase-04 → E2E + architecture test + finalize gate.
