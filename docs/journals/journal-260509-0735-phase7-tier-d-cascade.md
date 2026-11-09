# Journal — PHASE7-TIER-D-CASCADE Source Fix (2026-05-09 07:35 ICT)

## What changed

Branch `fix/phase7-tier-d-cascade` (uncommitted, 2 commits):
1. `9a3607dc` — TC-AUTH-022 + 2 change-password specs swapped to non-admin users
2. `1d093bd0` — PHASE4B journal housekeeping (carry-over from prior session)

## Root cause

83 cascading 401 failures in last Tier D Playwright run (2026-05-08 closure) traced to single source: `web/e2e/authserver-login-page.spec.ts:39-49` (TC-AUTH-022) submits `admin@dev.local` + `WrongPassword999!` through the UI login form. ASP.NET Core Identity `SignInManager.PasswordSignInAsync` calls `UserManager.AccessFailedAsync` on each wrong-password attempt against an existing user. `TenantAwareUserManager` reads per-tenant `CredentialPolicy.MaxFailedAttempts` (default 5) and locks admin for `LockoutMinutes` (default 5min). Cumulative pollution across re-runs trips the threshold → 80+ admin-token-dependent specs receive 401 cascading.

Backlog row's third claim — `account-change-password.spec.ts:77` change-password wrong-current-pass also pollutes admin — is **unverified** per Identity source review: `UserManager.ChangePasswordAsync` directly invokes `VerifyPasswordAsync` without `AccessFailedAsync` side-effect. Defensive viewer-swap retained as paranoid hardening + alignment with TC-IDENT-034 pattern.

`accessibility/keyboard-navigation.spec.ts:45` (third claim in backlog) uses non-existent `test@example.com` — post-BE-LOGIN-USER-ENUMERATION fix (2026-05-08), user-not-found path skips `AccessFailedAsync`. Not a pollution source. No edit.

## What was deferred + why

Phase-03 Playwright re-verify (the actual confirmation of 83→0 closure) deferred to next session. Dev stack down (API:5001 / AUTH:5002 / FE:5173 unreachable when probed) and `scripts/1_run-local-dev.ps1` is an interactive monitor loop unsuitable for `--auto` background invocation. Re-verify procedure documented in plan: warm stack, `npx playwright test --last-failed --workers=1`, optional one-time SQL recovery if accumulated count > threshold.

Backlog rows reflect deferred state:
- `PHASE7-TIER-D-CASCADE` — PARTIAL — SOURCE FIX SHIPPED, RE-VERIFY DEFERRED
- `PHASE7-TIER-D-RESIDUAL` — OPEN, awaiting Phase-03 to clear

## Spawned backlog (P3)

**`PHASE7-TIER-D-LOCKOUT-VICTIM-PARALLEL` (P3, ~2h)** — 3 specs now share `lockout-victim@dev.local` (TC-AUTH-022/024 + TC-AUTH-055). With `fullyParallel: true` + 3 workers, lockout state interleaves unpredictably across workers. Risk pre-existed (qd742 + token-grants already shared); +1 contribution from new TC-AUTH-022 is statistically negligible. Future hardening: serialize lockout-touching specs OR per-spec dedicated victim Guids (`...0002`, `...0003`).

## Code review

APPROVE_WITH_MINOR 8.8/10. 0 P0/P1. Three P3 observations — L1 cross-describe afterAll scope (non-issue: +1 well below threshold), L2 parallel-worker contention (pre-existing), L3 no afterAll in change-password (Identity contract verified safe).

## Mistakes / corrections

- **Initial scope assumption**: Started preparing to run Playwright + classify all 83 failures with traces. Correctly aborted after `curl http://localhost:500{1,2}` returned `000` — recognized dev-stack-down + interactive run script make Playwright re-verify infeasible in `--auto` mode without user intervention. Pivoted to source-fix + classification + plan-doc deliverable. Honest scope match.
- **Identity source verification**: Initially defaulted to backlog claim that change-password wrong-pass pollutes admin. Source-read corrected: `UserManager.ChangePasswordAsync` is AccessFailedAsync-free. Defensive swap retained anyway (zero cost, future-proofing).

## Test impact

None for this session — no Playwright run, no .NET tests touched. Tier D 83-failure delta unverified until Phase-03.

## Files modified (commit `9a3607dc`)

- `web/e2e/authserver-login-page.spec.ts` (+4 / -1)
- `web/e2e/account-change-password.spec.ts` (+8 / -2)
- `backlogs/backlog.md` (+3 / -2)
- `docs/code-standards.md` (+1 / -0)
- `docs/project-changelog.md` (+24 / -0)

## Memory implications

No new memory entries warranted. PHASE7-TIER-D-CASCADE outcome already documented in backlog.md + this journal. Existing memory `feedback-windows-case-blind-git-index.md` and `feedback-architecture-tests-in-finalize-gate.md` patterns unchanged.
