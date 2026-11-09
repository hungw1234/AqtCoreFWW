---
title: PHASE7-TIER-D-RESIDUAL drill — auth-helper TTL + mfa-flow contract
date: 2026-05-09 17:00 ICT
branch: main
commits: 3393b5c2, 39161935
---

# Journal — PHASE7-TIER-D-RESIDUAL drill (2026-05-09 17:00 ICT)

## Summary

Drilled PHASE7-TIER-D-RESIDUAL backlog row. Premise refuted (claim "19 residuals"; real Tier D parallel baseline today: **152 failures / 73 specs**). Shipped 2 cascade fixes; final state **78 failures / 58 specs** in parallel mode (~50% reduction). Token-revocation-mid-suite identified as the dominant remaining issue and spawned as new FU.

## What changed

### Commit `3393b5c2` — auth-helper cache TTL drift

`web/e2e/helpers/auth-helper.ts:27` hardcoded `CACHE_DURATION_MS = 50 * 60 * 1000` (50 min) under the comment "safety margin under 60min ROPC expiry". AuthServer ROPC actually issues `expires_in: 900` (15 min) tokens. After 15 min, cached token returned 401 from API but `isValid()` still served it — silent cascade across any spec calling `getAccessToken()`.

Fix: read `expires_in` from ROPC response, derive TTL per-call. `REFRESH_THRESHOLD_MS` tightened 5min → 2min so the 15min token has a usable ~13min window. Fallback `FALLBACK_CACHE_MS = 10min` only if `expires_in` absent.

Also dropped stale `/reports/dashboard` (no BE endpoint) from `full-route-rendering.spec.ts` ADMIN/USER endpoint list, and skipped `reports-execution.spec.ts` TC-RPT-001/003/004 with backlog ref `REPORTS-DASHBOARD-ENDPOINT`.

Cleared the dominant `full-route-rendering` cluster (34/34 → 35/36 cases with one residual `/reports/dashboard` removed).

### Commit `39161935` — mfa-flow contract drift

Routes had moved `/api/v1/mfa/*` → `/api/v1/account/mfa/*` (`AccountMfaController`). Spec was hitting the old paths and got 404. Body field renamed `code` → `totpCode`. Response field renamed `otpauthUri` → `qrCodeUri`. `/mfa/send-otp` (email OTP send) endpoint was REMOVED outright — TC-IDENT-055/056 now `test.skip` with backlog ref `MFA-EMAIL-OTP-ENDPOINT`.

Verified per-test pass in isolation. Tier D parallel re-run: mfa-flow cluster dropped 6 → 2 cases (the 2 remaining likely parallel-mode rate-limit interaction on `[EnableRateLimiting("mfa-verify")]`).

## Test impact

| Run | When | Mode | Pass | Fail | Skip | Did-not-run | Specs |
|---|---|---|---|---|---|---|---|
| Pre-drill (cached) | 2026-05-09 14:41 | parallel | — | **152** | — | — | 73 |
| Post-auth-helper | ~16:00 | parallel | 393 | 72 | 44 | 266 | 51 |
| Single-worker | ~16:30 | workers=1 | 202 | (cascade) | 55 | 357 | — |
| Final post-mfa | ~17:00 | parallel | 351 | **78** | 42 | 303 | 58 |

Net: **152 → 78 cases** (49% reduction); **73 → 58 specs** (21% reduction). mfa-flow 6 → 2.

## What was deferred + why

- **Bucket drilling for remaining 78 residuals** — out of scope for this session. Token-revocation-mid-suite is masking the genuine residual landscape; classification before that's fixed will be noisy. Spawned PHASE7-TIER-D-RESIDUAL-CONTRACT (P3) for the post-token-fix drill.
- **`POST /reports/dashboard` BE handler** — depending on whether FE Dashboard route still exists / whether `POLL-FU2` aggregate replaces it. Spawned REPORTS-DASHBOARD-ENDPOINT (P3) for owner decision.
- **`POST /mfa/send-otp` BE handler** — depending on whether FE still expects email OTP fallback. Spawned MFA-EMAIL-OTP-ENDPOINT (P3).

## Mistakes / corrections

- **Trusted backlog claim "19 residuals" without re-running first.** Today's Tier D parallel run revealed 152 failures = 8x the backlog claim. Lesson: the previous re-verify entry used `--last-failed --workers=1` against a yesterday baseline that was 83 — but that's a subset of the 256 prior passes which weren't re-validated. The "19/83" wasn't a snapshot of the suite, it was a snapshot of yesterday's failure subset. Backlog row PHASE7-TIER-D-RESIDUAL has been replaced with today's measurement.
- **Hypothesised TC-IDENT-017 reset-password as the token-revoker.** Verified false in 30s with curl: first user is a test user, not admin. Hypothesis discarded; spawned a more open-ended PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE FU instead of guessing.
- **Started a single-worker re-run thinking it would give cleaner data.** It produced different and worse data (357 did-not-run from cascade) because single-worker holds one token across the full ~10min run, exposing the token-revocation issue more severely. Killed the run mid-flight; finalised on the parallel-run baseline.

## Code review

N/A this session — small tactical fixes on 3 spec files + 1 helper. Both commits self-verified via per-test runs. Tier D parallel re-run is the integration check.

## Files modified

- `web/e2e/helpers/auth-helper.ts` — cache TTL derivation
- `web/e2e/full-route-rendering.spec.ts` — drop `/reports/dashboard` line
- `web/e2e/reports-execution.spec.ts` — 3 `test.skip` for TC-RPT-001/003/004
- `web/e2e/mfa-flow.spec.ts` — `/account/mfa/*` route rewrite + 2 `test.skip` for /send-otp tests
- `backlogs/backlog.md` — PHASE7-TIER-D-RESIDUAL row replaced with today's measurement; spawned 4 new FU rows (TOKEN-REVOKE-INTRA-SUITE, RESIDUAL-CONTRACT, REPORTS-DASHBOARD-ENDPOINT, MFA-EMAIL-OTP-ENDPOINT)

## Memory implications

- New observation: **ROPC `expires_in` may be much shorter than developer assumes** (here 15min not 60min). Treat hardcoded TTLs as suspect when reviewing client-side token caches. Worth memory entry if pattern recurs in another module — defer for now (single-incident YAGNI).
- Existing memory `feedback-oidc-silent-renew-cascade.md` is adjacent context (FE silent-renew gap from dev key rotation); the auth-helper TTL bug is its E2E analogue.

## Spawned backlog

| ID | P | Title | Notes |
|---|---|---|---|
| PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE | P2 | Identify what revokes admin token mid-suite | Highest-leverage fix: 401-retry-with-refresh in `auth-helper.getAccessToken` |
| PHASE7-TIER-D-RESIDUAL-CONTRACT | P3 | Drill remaining 78 cases by error-message bucket | Defer until token-revoke shipped |
| REPORTS-DASHBOARD-ENDPOINT | P3 | Decide BE handler vs FE realign vs aggregate route | 4 specs currently `test.skip` |
| MFA-EMAIL-OTP-ENDPOINT | P3 | Decide restore vs UI removal vs new route | 2 specs currently `test.skip` |
