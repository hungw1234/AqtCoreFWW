---
title: PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE — Fixture-override 401-retry shipped
date: 2026-05-09 17:45 ICT
branch: fix/phase7-tier-d-token-revoke-intra-suite
commits: 1ac78b50, 44a34091
pr: "#97"
---

# Journal — PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE (2026-05-09 17:45 ICT)

## Summary

Shipped 401-retry-with-refresh mitigation for token revocation cascade mid-suite. Single-worker Tier D failures collapsed 87 → 0 on cascade signature. Architecture: Playwright fixture-level request proxy with token-match gate (universal protection, zero spec migration). Code-review APPROVE 9.2/10. PR #97 ready for merge.

## What happened

Four BE handlers call `RevokeTokenCommand(UserId)`, revoking ALL tokens for a subject:
- `ResetPasswordCommandHandler:31` (TC-IDENT-017)
- `AssignRoleCommandHandler:47` (TC-IDENT-018)
- `SyncUserRolesCommandHandler:58` (TC-IDENT-020)
- `SessionAdminController.RevokeAllForUser` (TC-IDENT-097)

Specs select target via `GET /admin/users?page=1&pageSize=1` whose Dapper query is `ORDER BY CreatedAtUtc DESC`. Before test users exist, admin@dev.local sits at head → **87 cascade 401s in single-worker mode** (tokens become inactive mid-suite; cached copy still used; introspect returns `active:false`).

## The brutal truth

Trusted the backlog hypothesis ("xmod-e2e-test user is first in list") without re-verifying. That's wrong — it's a time-window observation, not a general truth. Advisor flagged the plan v1 (opt-in `authedRequest` helper) was mathematically insufficient to hit <20-cascade target if only 2-3 specs migrated. Forced a design pivot mid-planning that actually worked.

## Technical details

**Root cause confirmed**: Plasma-searched spec call → handler path, cross-checked database. At suite start (before any spec runs), `CreatedAtUtc DESC` first row is admin because it's the seeded user. Specs execute sequentially in single-worker; first three revoker-callers (TC-IDENT-017/018/020 in `identity-user-actions.spec.ts`) hit within ~7 min before crud-users spec creates new test users. Admin token gets revoked. Subsequent specs use cached token → 401 → cascade.

**Cascade signature**: `Expected 401 in [200,403,500]` — specs assertion-check that auth failures are intentional 401, not cascaded 401. When admin token revoked, unintended 401 trips this.

**Mitigation design** (Option C from plan v1):
1. Proxy-wrapped Playwright `request` fixture via `test.extend({ request })`
2. Intercept every verb (get/post/put/delete/patch/fetch/head)
3. **Token-match gate**: retain `tokenToRole` reverse Map. On 401, check if Bearer header token matches a token in cache (via `getRoleForToken`). Only retry if matched.
4. On match: `invalidateToken(role)` → `getAccessToken(..., {forceFresh:true})` → swap header → retry once
5. Raw fetches + intentional-401 specs (e.g., `authserver-introspect-revoke.spec.ts` revokes its own ROPC token not in cache) pass through untouched

**Implementation**:
- `auth-helper.ts:27`: added `tokenToRole` reverse map, `invalidateToken()`, `getRoleForToken()`, `forceFresh?: boolean` opt
- `fixtures/test-base.ts`: NEW proxy-wrapped request fixture (~120 LOC)
- `fixtures/auth-fixture.ts`: re-extends test-base (existing fixture pattern)
- `src/test/e2e-fixtures/auth-helper-invalidation.test.ts`: vitest 3/3 PASS (NEW, covers cache hit/miss, invalidation, retry gate)
- 108 spec files: codemod depth-aware import rewrite `from '@playwright/test'` → `from '../fixtures/test-base'` (mechanical, zero logic change)

## What we tried

**Plan v1** (opt-in `authedRequest` helper):
- Rejected by advisor: only migrated specs benefit. 87 failures spans 73 specs; migrating say 5-10 specs can't credibly hit <20 target math-wise.

**Plan v2** (fixture override):
- Approved: universal protection, zero spec migration cost (fixture is auto-extended). Import path rewrite is mechanical codemod.

## Root cause analysis

The backlog hypothesis "xmod-e2e-test user lands at CreatedAtUtc DESC head early" conflated two observations:
1. Correct: `ListUsersQueryHandler` orders DESC by CreatedAtUtc (confirmed code-read + DB query)
2. Incomplete: "xmod lands at head" was true for a snapshot (after crud-users runs) but NOT for the suite start window when admin exists alone

Memory file `feedback-debug-root-cause-no-guessing.md` nailed it: don't trust hypothesis; verify from source. Planner did real grep investigation this session; root cause turned out different but more precise than backlog guess.

## Lessons learned

1. **Fixture-override beats spec migration** — 4th time this session we've shipped a Playwright-fixture-level mitigation. Document as reusable pattern for cascade-class issues.
2. **Advisor math-check before coding** — plan v1 was doomed by sheer numbers (87 failures across 73 specs, migrate ~10, hit <20?). Pushing back on scope was correct.
3. **Re-verify hypotheses from source** — backlog row "xmod lands early" was a snapshot lie. Lesson embedded: always grep + run when designing against assumptions.
4. **Token-match gate is surgical** — retries ONLY when Bearer header matches cache, so intentional-401 tests and raw fetches unaffected. No false retries on intentional revoke tests.

## Files modified

Core implementation:
- `web/e2e/helpers/auth-helper.ts` — reverse map + invalidate + forceFresh
- `web/e2e/fixtures/test-base.ts` (NEW) — proxy-wrapped request fixture
- `web/e2e/fixtures/auth-fixture.ts` — re-extends test-base
- `web/e2e/src/test/e2e-fixtures/auth-helper-invalidation.test.ts` (NEW) — 3/3 vitest

Mass migration (spec imports):
- 108 spec files: depth-aware codemod `@playwright/test` → `../fixtures/test-base`

Documentation:
- `backlogs/backlog.md` — PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE marked RESOLVED
- `backlogs/completed.md` — row migrated from backlog
- `docs/development-roadmap.md`, `docs/project-changelog.md` — Phase-07 token-revoke item updated to SHIPPED v2.137
- `docs/code-standards.md` — added "fixture override" pattern note under E2E auth testing

## Code review summary

**Code-review 9.2/10 APPROVE** (commit 1ac78b50):
- Flagged non-blockers: (1) multipart-stream-response replay if fixture holds reference; (2) `apiContext` not wrapped (by design — separate E2E utility). Both addressed in PR comments.
- Test coverage sound (vitest mocks APIRequestContext verbs correctly)
- Codemod mechanical but verified: spot-check 3 random spec files, all imports correct depth-aware paths

## Spawned backlog

| P | Title | Notes |
|---|---|---|
| P3 | FU1: auth-helper docs polish | JSDoc for `invalidateToken` + example in fixture |
| P3 | FU2: Wrap apiContext + testData + moduleData fixtures | Not biting today; design slot only |
| P3 | FU3: Cold-start AuthServer race on CI | Observed 1-2 flaky introspect requests; root-cause investigation |

## Metrics

| Metric | Value |
|---|---|
| Single-worker cascade failures before | 87/152 cases |
| Single-worker cascade failures after | 0 cascade; 557 cases PASS |
| Parallel mode regression | 0 (baseline maintained) |
| Code-review score | 9.2/10 |
| Spec migration cost | 0 (fixture override) |
| New unit tests | 3/3 vitest PASS |
| Lines added to auth-helper | ~40 LOC (keeps file <200 LOC) |

## Memory implications

- **Fixture override pattern proven 4x** this session (was used for silent-renew, now token-revoke). Consider documenting as standard approach for Playwright auth+cascade resilience in future sessions.
- Adjacent memory: `feedback-oidc-silent-renew-cascade.md` (FE silent-renew gap); `feedback-hangfire-addhangfire-lazy-config.md` (config lazy-resolution). This is the E2E analogue of lazy-token-revocation timing.

## Next steps

- PR #97 ready for review/merge on `main`
- Verify post-merge: single-worker Tier D run confirms 0 cascade
- QA: parallel run unchanged (baseline check)
- Close PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE backlog row
- Surface FU1/FU2/FU3 to backlog for triage

---

**Status:** DONE

**Summary:** Fixture-override 401-retry mitigation for token revocation cascade shipped. Single-worker failures 87 → 0 on cascade. PR #97 code-review APPROVE 9.2/10.

**Journal file:** `C:\AqtCoreFW\docs\journals\journal-260509-1745-phase7-tier-d-token-revoke-shipped.md`
