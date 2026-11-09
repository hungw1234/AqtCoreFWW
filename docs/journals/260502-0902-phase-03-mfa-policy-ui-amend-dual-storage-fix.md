# Journal — Phase-03 MFA Policy UI AMEND: Dual-Storage Sync Bugs

**Date:** 2026-05-02 09:02 ICT  
**Branch:** main (fold + amend; commits `59cce7d2` + `de142e04`)  
**Type:** Critical bug fix + regression guard + architectural decision documentation  
**Version:** v2.133p22 → v2.133p22a (amended)

## Context

Phase-03 MFA Policy UI (QĐ 742 ATTT cấp 4 §1.6.a–b) shipped without code-reviewer + tester gates. Post-hoc review by code-reviewer revealed **2 critical non-functional bugs** blocking MFA validate path + emergency disable, and **false-confidence test reports** masking the issues.

## Critical Bugs Found

### C1 — TOTP Dual-Storage Path Mismatch (§1.6.b non-functional)

**Symptom:** Enrolled users could not validate TOTP at login.

**Root cause:** Write path inconsistency:
- `CompleteTotpEnrolAsync` → wrote pending secret to `ApplicationUser.TotpSecret` (EF column, encrypted at-rest backup)
- Validate path (`MfaTotpValidator.ValidateAsync`) → read from `AspNetUserTokens` via `UserManager.GetAuthenticatorKeyAsync` (ASP.NET Core Identity standard store)
- Neither write path synchronized the ASP.NET Core token store, so validators found **no key to validate against**

**Fix approach:** Grep confirmed `UserManager.SetAuthenticatorKeyAsync` is not public (only `IUserAuthenticatorKeyStore` can call it). Solution: dual-write via public `UserManager.SetAuthenticationTokenAsync`:

```csharp
await _userManager.SetAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", base32Secret);
await _userManager.SetTwoFactorEnabledAsync(user, true);
// Keep existing TotpSecret backup for audit/rotation tooling
await _userManager.UpdateAsync(user);
```

### C2 — Emergency Disable Mirror Bug (§1.6.b.iii no-op)

**Symptom:** `ApproveEmergencyDisableCommand` cleared `TotpSecret` only; disable was functionally a no-op because validate path read from untouched `AspNetUserTokens` + `TwoFactorEnabled` flag.

**Root cause:** Incomplete dual-clear. Fix:

```csharp
await _userManager.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
await _userManager.SetTwoFactorEnabledAsync(user, false);
await _userManager.UpdateAsync(user);
// Deliberately NOT calling ResetAuthenticatorKeyAsync — Reset generates new random key
// Emergency disable must leave NOTHING usable
```

## Test Report vs Reality Gap

Tester reported **"ALL_PASS 463 tests"** but feature was end-to-end broken. Root cause:

- Tests injected `mfa_at` claim via `X-Test-MfaAt` header, **bypassing real Identity validation entirely**
- Test assertions checked structural presence (entity field exists), not behavioral (enrol → validate succeeds with real key)
- No `MfaFlowTests.EnrolThenValidate_RealFlow_BothStorageBackendsAgree` regression guard existed

**Lesson:** Tests passing ≠ feature working. Security features require behavioral e2e tests through primary auth flow, not stub-injected state.

## Secondary Bugs & Fixes

| ID | Issue | Fix |
|----|-------|-----|
| H1 | `MfaTempTokenService.Validate` missing cache `Size=1` | Added size param to prevent unlimited growth |
| H3 | Architecture sweep missed `MfaPoliciesAdminController` (lives in API host, not module Presentation) | Extended `AssemblyFixtures.PresentationAssemblies` to include `AqtCoreFW.Api` |

## Amendments Applied

1. **`git reset --soft 5db66dcf`** — fold both rogue commits into working tree (preserves code + test changes, clears commit log)
2. **Dual-write sync** — `SetAuthenticationTokenAsync` + `SetTwoFactorEnabledAsync(true)` in enrol flow
3. **Dual-clear sync** — `RemoveAuthenticationTokenAsync` + `SetTwoFactorEnabledAsync(false)` in emergency disable
4. **H1 fix** — cache size bound
5. **H3 fix** — architecture sweep scope
6. **ADR-007 NEW** — documented dual-storage write/clear/validate contract and decision rationale (Option C: dual-write vs A: drop backup or B: custom IUserAuthenticatorKeyStore)
7. **Regression guard NEW** — `MfaFlowTests.EnrolThenValidate_RealFlow_BothStorageBackendsAgree` asserts both backends agree + ValidateTotpAsync succeeds post-enrol
8. **Changelog entry** — documented amendments + secondary fixes
9. **Re-test:** 37/37 PASS including new canary
10. **Re-review:** AMEND_WITH_MINOR (8.5/10); 2 of 3 doc concerns were reviewer stale-read, third fixed (changelog mention of dropped `AuthServerHost`)
11. **`git commit --amend`** (fold ADR + all working-tree fixes), push `3d459c9e` origin/main

## Key Discoveries (Future Hardening)

**Test injection bypass risk:** When tests hook auth claims via test-only middleware (`X-Test-MfaAt` header), they evade auth validation. Mandatory: behavioral tests through real IIdentityService paths, not claim injection shortcuts.

**Subagent self-commit risk:** Both implementation and tester subagents committed without orchestration gate. Added memory `feedback-tester-no-self-commit.md`: subagents MUST NOT self-commit; only lead can execute `git commit`.

**Dual-storage sync contract:** Kept encrypted `TotpSecret` column (instead of dropping) for audit/key-rotation tooling future. Trade-off: two-row sync risk mitigated by regression test enforcing contract agreement on every validate.

**`git reset --soft` + amend gotcha:** Edit modifies working tree, NOT index. Must `git add -A` before `--amend` or fixes get dropped. First attempt: ADR + fixes dropped because missed re-add; caught and recovered via re-amend.

## Finalize Verification

| # | Artifact | Status | Evidence |
|---|----------|--------|----------|
| 1 | `src/modules/identity/features/mfa-enrol/CompleteTotpEnrolCommand.cs` | ✅ | Dual-write: `SetAuthenticationTokenAsync` + `SetTwoFactorEnabledAsync(true)` |
| 2 | `src/modules/identity/features/emergency-disable/ApproveEmergencyDisableCommand.cs` | ✅ | Dual-clear: `RemoveAuthenticationTokenAsync` + `SetTwoFactorEnabledAsync(false)` |
| 3 | `docs/adr/adr-007-dual-storage-mfa-sync-contract.md` | ✅ | NEW ADR documenting write/clear/validate contract + rationale |
| 4 | Test suite | ✅ | 37/37 PASS; `MfaFlowTests.EnrolThenValidate_RealFlow_BothStorageBackendsAgree` regression guard added |
| 5 | docs/project-changelog.md | ✅ | Amendments + H1/H3 secondary fixes documented |
| 6 | memory/feedback-tester-no-self-commit.md | ✅ | NEW — subagent commit discipline rule |
| 7 | Git commit `3d459c9e` | ✅ | Amend pushed to origin/main |

## Unresolved Questions

1. Should `MfaService.EnrollTotpAsync` be marked `[Obsolete]` to prevent other code paths from using stale write path? Current state: only `CompleteTotpEnrolCommand` consumes; no grep hits elsewhere. Deferred: add to MFA module polish checklist.
2. Hangfire background job for `TotpSecret` → `AspNetUserTokens` audit-log backfill for pre-Phase-03 users? Optional. Not required for QĐ 742 compliance; pre-2026-05-01 users won't have TotpSecret set anyway.
