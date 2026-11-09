# ARCH-FU Scope Correction — JWT Nonce Whitelist vs Speculative Refactor

**Date**: 2026-05-17 09:00 ICT  
**Severity**: Medium  
**Component**: Architecture (SourceCodeScanner, Rule1 enforcement)  
**Status**: Resolved  
**Commit**: `fac04940` (v2.137p35)  

---

## What Happened

Backlog ARCH-FU claimed "`ApiResponse.cs:14` direct `Guid.NewGuid()` + 54 callers, ~2-4h refactor". User pre-confirmed REFACTOR path. Code session planned to inject `IGuidGenerator` across static factory methods.

**Before touching code**, ran premise verification per L4 habit from this morning (grep-verify reviewer claims):

1. Located failing test: `Rule1_GuidNewGuid_DirectCall_Banned_In_AllowlistedModules` @ `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/SourceLevelRulesTests.cs:27`
2. Ran test: **ONE violation only** → `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/Signing/DocumentSigningWorkflow.cs:115` `var nonce = Guid.NewGuid();` (JWT cryptographic nonce)
3. Read `SourceCodeScanner.FilterByAllowlist` logic: enforces 14 module-name strings (masterdata, identity, audit, files, rules, etc.). **ApiResponse.cs lives in SharedKernel** → implicit pass. **Not in allowlist scope.**
4. Backlog's "54 callers" premise = speculative guess about `ApiResponse.Success/Failure` static factories. **Unverified.**

**Real scope**: 1 file, 1 line, JWT semantics. Match existing `FileSecurityService.cs` whitelist pattern (cryptographic randomness, not entity ID).

---

## The Brutal Truth

We almost burned 3 hours refactoring the **wrong file** based on unverified backlog text, even with "user confirmation" attached. The confirmation was of the REFACTOR path, not the premise. Honest premise verification before scope lock is not optional.

Backlog author (possibly me in a previous session) made a high-confidence guess about which file violated Rule1. **Guess was wrong.** Rule1 doesn't enforce SharedKernel foundation files; it enforces allowlisted modules. `ApiResponse` was never in scope.

This is frustrating because the rigor was there (L4 from morning, L1 from memory) — it just needed to run 10 minutes earlier instead of after committing to refactor.

---

## Technical Details

**Test failure (pre-fix):**
```
Rule1_GuidNewGuid_DirectCall_Banned_In_AllowlistedModules FAILED
Violation found:
  File: src/modules/identity/AqtCoreFW.Identity.Infrastructure/Services/Signing/DocumentSigningWorkflow.cs:115
  Code: var nonce = Guid.NewGuid();
  Reason: Direct Guid.NewGuid() call in allowlisted module (identity)
```

**Rule1 logic snapshot** (`SourceCodeScanner.cs:~210`):
- `IsAllowlistedModule`: checks file path contains one of 14 module names
- `FilterByAllowlist`: keeps only violations in allowlisted modules
- **SharedKernel files never match module-name list** → implicit pass on Rule1

**Existing whitelist precedent** (`SourceCodeScanner.DefaultWhitelistFragments`):
```csharp
"FileSecurityService.cs" // "cryptographically secure random token generation"
```

Same semantic class: cryptographic randomness, not entity ID sequences.

---

## What We Tried

1. ✗ Planned full `IGuidGenerator` injection into `ApiResponse.Success/Failure` — would require refactoring all 54 static-factory call sites
2. ✓ Verified premise first → found real violation is 1 file, JWT nonce
3. ✓ Whitelist single file with semantic rationale comment

---

## Root Cause Analysis

**Backlog scope claim lacked premise verification.** The original ticket author (unknown session) read the Rule1 test name (`...GuidNewGuid...Banned...`), saw `ApiResponse.cs` contains `Guid.NewGuid()`, and wrote "fix this via IGuidGenerator injection" without:
- Running the test to see actual violations
- Reading Rule1 filter logic (allowlist-module-only, not foundation-wide)
- Distinguishing cryptographic randomness (JWT nonce) vs entity ID (ApiResponse tracking)

**User confirmation** ("yes, refactor") confirmed the proposed solution path, not the problem diagnosis. This is a communication gap, not user error.

---

## Lessons Learned

1. **Premise verification is load-bearing, not optional.** Even with user pre-confirmation, run the failing test and read the filter logic before committing 2-4h estimates. L4 from morning's ship reconfirmed: "verify reviewer file-content claims via grep" — extends to backlog claims too.

2. **"User confirmed REFACTOR" ≠ "premise is correct".** Confirmation is of the solution, not the problem. Always re-surface with AskUserQuestion if premise verification contradicts backlog. Preserves trust; avoids wasted effort.

3. **Foundation-vs-module enforcement distinction is real.** Rule1 uses `FilterByAllowlist(14 module strings)` — doesn't enforce SharedKernel. If future ARCH-FU2 extends Rule1 to foundation files via `IsFoundationFile`, `ApiResponse.cs:14` becomes a *real* 2-4h refactor. File that ticket separately when that forcing function appears.

4. **8-16x scope compression via verification.** Estimated 2-4h → actual 15min (7 LOC whitelist). Unusual ratio because the entire 2-4h was premise-driven; verification revealed zero need for the refactor. Whitelist + semantic comment was correct fix per JWT-nonce semantics.

5. **Micro-ship skips subagent sweep.** ~7 LOC test-infra whitelist (no prod code change) with single failing test passing locally + Architecture 97/97 + SharedKernel.Tests 199/199 full suite green = sufficient validation. Code-reviewer on a one-line whitelist is YAGNI.

---

## Next Steps

**ARCH-FU RESOLVED.** Whitelist entry + comment merged to origin/main.

**ARCH-FU2 (conditional future ticket):**  
If scope ever expands Rule1 to enforce SharedKernel foundation files (unlikely, but conditional):
- `src/shared/AqtCoreFW.SharedKernel/Api/ApiResponse.cs:14` becomes real violation
- Requires `IGuidGenerator` injection on `ApiResponse.Success/Failure` static factories
- Estimate 2-4h refactor + regression sweep
- **DO NOT file until forcing function appears** (Rule1 foundation-enforcement feature request)

**Carry memory L1 forward:** Premise verification on backlog claims is now habit before scope lock, not just on code-review claims. Pattern started with morning's P7-FU1.2 ship (scout → premise check → replan), amplified by P4-2B scope-collapse (memory ≠ truth), now reconfirmed here.

---

## Test Results

- SharedKernel.Tests: 199/199 PASS (Rule1 FAIL → PASS for DocumentSigningWorkflow)
- Architecture: 97/97 PASS (regression-clean)
- Build: 0E/0W

---

## Files Changed

- `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/SourceCodeScanner.cs` — 1 entry in `DefaultWhitelistFragments`, 4-line semantic comment
- 0 prod code changes

