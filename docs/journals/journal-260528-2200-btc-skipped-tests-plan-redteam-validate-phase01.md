# Journal — BTC skipped-tests: investigate → plan → red-team → validate → phase-01 ship

**Date:** 2026-05-28 ~22:00 ICT
**Commit:** `394e2efb` fix(api): skip Hangfire recurring-job registration in Testing env (local main, unpushed)
**Plan:** `plans/260528-1905-btc-orgunit-skipped-tests-reactivation/` (gitignored, local)

## Session goal
`/cook BTC [Fact(Skip)] ×15 un-skip` — reactivate 15 skipped OrgUnit tests. Backlog framed it as "Hangfire race, ~1-2d, quick un-skip."

## What worked
- **Empirical probe before trusting the row (INVARIANT 260522-A/260527-F).** Counted 15 (accurate), then RAN tests instead of trusting the skip-message cause:
  - Probe #1 (Testcontainers): 3/3 deterministic fail → `RegisterRecurringJobs()` SqlException at `Program.cs:401` (NOT the flaky "race" the comment claimed).
  - Probe #2 (local-SQL): 1 pass (tautology) / 2 fail for DIFFERENT reasons → revealed 3 independent layers.
- **Red-team (3 reviewers, 12 findings, 0 rejected)** caught design-breaking assumptions BEFORE any real code: L3 is MFA not role (`[RequiresMfa]` + `includeMfaClaim:false`); `masterdata.Wards` cross-schema breaks "self-contained ctx"; `OrgDbContext` needs 5 DI interceptors → "drop WebAppFixture" impractical; `NoCycle_InParentIdGraph` reachability assertion dead-on-arrival.
- **Validate** added a Phase-0 spike GATE to de-risk the load-bearing assumption (env-guard → Testcontainers boot) before investing 5-7h.
- **Phase-01 shipped + verified:** env-guard `Program.cs:400-448` (build 0W/0E); spike PASS (host boots under Testcontainers, no Hangfire exception); `Workflow.Integration` 74/74 regression clean.

## What didn't / lessons
- **Backlog row was misleading at 3 levels** — "Hangfire race" understated a deterministic connection failure + 2 unrelated bugs (seeder config-disabled, smoke MFA). Lesson: a skip-message's stated *cause* decays like any stale claim — probe it, don't just count the artifacts.
- **First design (drop WebAppFixture + manual ctx) was wrong** — red-team F6/F7 showed `OrgDbContext` needs 5 interceptors (incl. `AuditableEntityInterceptor` for `CreatedBy` NOT NULL); reversed to KEEP WebAppFixture + rely on env-guard. The original "proven pattern" sibling (`BtcOrgUnitSeederL4TreasuryTests`) is InMemory + hand-built units — a false equivalence to "real seeder + real SQL."
- **User's MaxLevel=3 choice over-cut** — verification showed `masterdata.Wards` is queried ONLY in L4 Treasury; MaxLevel=4 + SkipGiaoDichPhong=true avoids masterdata while keeping L4 Tax(350)+Statistics(≥476) green. Surfaced the refinement rather than silently applying.
- **planner subagent died mid-run (API socket error)** after writing plan.md + phase-01; finished phase-02/03 directly (had full context).
- **Docker Desktop dropped mid-session** — spike #1 blocked at `MsSqlBuilder.Build()` (Docker unreachable); paused for user to restart, then spike passed. Long sessions can outlive Docker uptime.

## Decisions made
1. KEEP WebAppFixture for all 15 (reversed "drop it") — full DI graph free; rely on env-guard for boot.
2. Seed = `Options(MaxLevel=4, SkipGiaoDichPhong=true)` — avoids masterdata, keeps L4 Tax/Stats coverage.
3. env-guard wraps `:400-448` only (`:398` JobStorage + `:450+` PII upgraders stay unconditional).
4. Smoke fix = `includeMfaClaim:true` (MFA gate, role moot for GET).
5. Phase-0 spike GATE before phase-02; rename-9-tests decision deferred to phase-03.

## Test status
- env-guard: API host build 0W/0E.
- Spike (Testcontainers): host boots; smoke assertion 403 (expected phase-02 MFA fix).
- Regression: `Workflow.Integration` 74 passed / 0 failed / 3 pre-existing skips (local-SQL).
- 15 target tests: still skipped (phase-02 owns the un-skip).

## Next session
- **Phase-02 (~5-7h):** reactivate 15 tests per `phase-02-reactivate-15-skipped-tests.md` — seed Options, MFA claim, repair dead `NoCycle`, tautology exact-counts, reframe `#7`. env-guard already in repo.
- **Phase-03:** dual-mode verify (Testcontainers + local-SQL) + decide rename of 9 `…IntegrationTests`.
- Push `394e2efb` (origin + bitbucket) when ready — currently local-only.
- Backlog row `BTC-SKIPPED-TESTS-REACTIVATE` sharpened with 3-layer root cause + plan pointer.
