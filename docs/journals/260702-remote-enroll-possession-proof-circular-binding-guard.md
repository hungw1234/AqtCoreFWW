# Remote Enrollment Possession-Proof Circular Binding Guard

**Date**: 2026-07-02 14:30
**Severity**: High
**Component**: Identity.Application (SignPayloadCommand, EnrollMySignatureCertCommand)
**Status**: Resolved

## What Happened

Remote signature cert enrollment (Viettel Mysign) was failing with error "Remote possession proof failed — the signer did not complete the sign challenge." The error message was misleading; the Mysign OTP sign SUCCEEDED, but the possession-proof verification returned fail. Debug showed the root cause: circular dependency in the binding guard. `EnrollMySignatureCertCommand.ProveRemotePossessionAsync` delegates to `SignPayloadCommand` to sign the challenge payload. `SignPayloadCommand` runs the anti-impersonation `SignerBindingGuard` AFTER the provider signs (lines 133–167). The guard checks for an ACTIVE+Approved enrollment matching the presented cert. During enrollment, the cert is not yet enrolled → guard fails closed → possession-proof fails → enrollment blocked forever.

## The Brutal Truth

The misleading error message nearly sent the diagnosis toward user/OTP problem. Three hours in, the turning point was disciplined log reading: api entry showed "Viettel Mysign Sign: success (sync)" followed immediately by audit entry "SIGN_PAYLOAD_BINDING_REJECTED". That single disconnect forced DB inspection, which revealed the circular invariant. The guard was originally designed to prevent impersonation *after* enrollment; applying it *during* enrollment is a logic breach. Shipping without this catch meant any admin user with a prior USB enrollment enrollment attempt would hit this forever. High severity, zero workaround except "enroll a different cert type first."

## Technical Details

**API Log Entry (success→rejection sequence):**
```
[14:22:11] Viettel Mysign Sign: success (sync)
[14:22:11] SIGN_PAYLOAD_BINDING_REJECTED [audit]
```

**DB Inspection (with RLS bypass):**
- User admin: SigningNationalId = `001234567890`
- Existing enrollments: USB token (serial …FB9E6CE3, status Approved)
- Incoming cert: Mysign (serial …9710405, not yet enrolled)
- Guard logic: `HasAnyEnrollmentAsync && CertMatches(Approved)` → locks into "enrolled tier" → requires cert match from Approved set → Mysign has no Approved entry yet → NotBound

**SignPayloadCommand Return:**
```csharp
// Line ~160
return Fail(FailureReasons.SIGNER_CERT_MISMATCH)
```

Enrollment status never changes from Null (no database record created).

**Tests Before Fix:** 417/417 Identity.Application.Tests; no explicit signing-guard-skip coverage.

## What We Tried

1. **Mysign provider hypothesis** — Re-read Viettel integration (OTP, signature format, verification). Mysign side was correct. Dismissed.
2. **User/cert data hypothesis** — Validated: cert chain, CCCD binding, signature hash. Data was clean. Dismissed.
3. **Log correlation** — Matched api log timestamps to DB audit entries. Caught the "sign succeeded but binding rejected" contradiction.
4. **RLS bypass inspection** — Forced reading Enrollments table directly. Revealed the user had a DIFFERENT approved enrollment (USB), triggering the guard logic. *This was the turning point.*

## Root Cause Analysis

**Circular Dependency:** `EnrollMySignatureCertCommand` (creates a binding) → `SignPayloadCommand` (verifies existing binding via guard) creates a catch-22. The guard is an anti-impersonation check designed for *post-enrollment* signing operations. During enrollment, there is no binding to verify yet; the possession-proof IS the binding-creation proof. 

**Asymmetry Proof:** USB possession-proof (`VerifySignatureCommand`) cryptographically verifies the signature and does NOT run the binding guard. Remote possession-proof was running it. Asymmetry is a bug smell.

**Guard Trigger Condition:** User with ANY prior approved enrollment (USB token in this case) → `HasAnyEnrollmentAsync() = true` → guard switches to "enrolled tier" mode → requires ALL subsequent signs to match an approved enrollment → cert under enrollment has no approved entry yet → denied.

## Lessons Learned

1. **Possession-proof vs. binding-verify are distinct gates.** A possession-proof CREATES a binding (cryptographic proof of key ownership). A binding guard VERIFIES an existing binding (anti-impersonation). Using the same guard for both is circular. If two proof paths (USB, Remote) feed the same sink (Pending enrollment), they must apply the same possession-only checks.

2. **Misleading error messages hide root cause.** "did not complete the sign challenge" pointed toward user/OTP. The actual rejection was anti-impersonation guard. Error should have been "cert not bound to active enrollment" or clearer. Discipline: always cross-reference error message with audit log and DB state.

3. **Asymmetry is a code smell.** USB and Remote have different code paths for the same operation (enrollment possession-proof). Allowed guard mismatch to hide. Code review must flag proof-path asymmetries.

## Next Steps

**Completed:**
- Added `SkipBindingGuard` bool parameter to `SignPayloadCommand` (default false, backward-compatible).
- Guarded binding-check block: `if (!cmd.SkipBindingGuard && ...)` (lines 135–166).
- `ProveRemotePossessionAsync` passes `SkipBindingGuard: true` when calling `SignPayloadCommand`.
- Files modified: `SignPayloadCommand.cs`, `EnrollMySignatureCertCommand.cs`, `SignPayloadCommandHandlerTests.cs`, `EnrollMySignatureCertCommandHandlerTests.cs`.
- New tests: +2 SignPayload tests (guard-skip behavior), +1 enroll test (SkipBindingGuard assertion).

**Safety Layers Preserved:**
- Sign is issued against caller's own `SigningNationalId` (fail-closed if absent).
- Post-proof, returned cert's CCCD must equal `user.SigningNationalId`.
- Enrollment recorded Pending for admin approval (final human gate).

**Verification:**
- Build: 0 warnings, 0 errors.
- Tests: Identity.Application.Tests 419/419 (unchanged pass + 2 new).
- Architecture: signing-compliance + tenant-isolation 23/23.
- Code review approved; security scrutiny confirmed SAFE (binding-verify remains intact post-enrollment).

---

**Commit Message:** `fix(identity): skip binding guard in remote enrollment possession-proof to prevent circular dependency`
