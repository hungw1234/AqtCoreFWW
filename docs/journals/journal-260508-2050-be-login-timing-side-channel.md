# BE-LOGIN-TIMING-SIDE-CHANNEL P3 OWASP A07 Fix

**Date:** 2026-05-08 20:50
**Severity:** Medium (OWASP A07 / QĐ 742 1.4)
**Component:** AuthorizationController.cs + PasswordVerifyTimingDecoy.cs (AuthServer)
**Status:** Shipped

## What Happened

PR #92 (commit `4bf47527`, 20:30) unified login error messages to defeat username enumeration via **message oracle**. PR #93 closes the residual **timing oracle** — not-found branch previously skipped PBKDF2 verification (~100-300ms delta), still leaking user existence via response-time analysis.

Attack: Attacker sends `POST /account/login` for known/unknown emails, measures latency. Unknown user returns fast (no hash verify), known user returns slow (PBKDF2 work-factor paid). Delta: ~2-3× timing gap, measurable over HTTPS + network jitter.

## The Fix

**New service:** `PasswordVerifyTimingDecoy.cs` (~40 LOC)
```csharp
public interface IPasswordVerifyTimingDecoy { void Consume(string dummyHash); }
// Implementation calls IPasswordHasher<ApplicationUser>.VerifyHashedPassword 
// against cached Lazy<string> decoy hash, discards result
```

**Injection points:**
1. `AuthorizationController` ctor param #10: `IPasswordVerifyTimingDecoy`
2. Both not-found branches (browser L272, ROPC L138): Call `Consume(...)` **before** audit log
3. Both branches now pay PBKDF2 cost → indistinguishable timing

**Cold-start mitigation:**
- `Program.cs` post-Build: `_ = app.Services.GetRequiredService<IPasswordVerifyTimingDecoy>().Consume("")`
- Forces Lazy initialization at startup, closes ~100-300ms first-request window per process

## Key Decisions

1. **DI seam over inline static:** User requested unit-test coverage → chose dependency seam. Trade-off: 1 extra class + registration vs mockability. Justified by test bundle (5 new cases).

2. **Static decoy hash + DI'd hasher:** Asymmetric by design. Hash init is one-shot cost (~10ms), verify cost mirrors user-found path. Repo-wide grep verified zero `PasswordHasherOptions` customization, so hardcoded `DefaultHasher` safe.

3. **Cold-start bundled:** Code-reviewer flagged first request post-deploy pays timing leak. 4-line Program.cs fix eliminates it; deferred = recurring vulnerability under rolling deploy.

4. **PHC parseability test included:** Pure NSubstitute tests miss malformed decoy hash that breaks at runtime. New `PasswordVerifyTimingDecoyTests` includes real-hasher case to catch it.

## Verification

- AuthServer.Tests: 22 → 27 PASS (5 new)
- Architecture: 26/26 PASS
- Startup.SmokeTests: 1/1 PASS
- Build: 0E, 0W

**Code-review:** APPROVE_WITH_CONCERNS 8.7/10 (0 critical / 0 P1 / 1 P2 bundled cold-start / 3 P3 backlog)

## Residual Issues (Backlog)

**BE-LOGIN-TIMING-RESIDUE-FU1 (P3, ~30m):** ROPC `FindByEmail ?? FindByName` dual lookup — user-not-found pays 2 DB hits vs 1 on user-found (~5-20ms gap). Sub-dominant vs PBKDF2. Mitigated: `AllowPasswordFlow`-gated, dev-only in current config.

**BE-LOGIN-TIMING-RESIDUE-FU2 (P3, ~15m):** `CanSignInAsync` internal DB hit + cookie-write residue. Sub-millisecond variance, low ROI.

## Lessons

- **Security clustering:** PR #92 (message) → PR #93 (timing) shipped same day. Momentum kept code-review context warm; avoided multi-day handoff drift.
- **Non-obvious findings from implementation:** Cold-start window invisible in code alone — illustrates dedicated review pass value even on small changes (~40 LOC).
- **DI seam YAGNI exception:** Chose seam only because user asked for tests. Without test mandate = inline helper + no DI. Spawned seam justified by request.

## Branch & Push

- **Branch:** `fix/be-login-timing-side-channel`
- **Commit:** `4a856868`
- **PR:** #93
- **Status:** Merged to main 2026-05-08

---

**Dependency:** Follows PR #92 (BE-LOGIN-USER-ENUMERATION). See `journal-260508-2030-be-login-user-enumeration.md`.

**Status:** DONE
