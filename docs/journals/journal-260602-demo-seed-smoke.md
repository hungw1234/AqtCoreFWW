# Journal — WF-DEMO-SEED-SMOKE live reset-all smoke (2026-06-02)

## Session goal
Close `WF-DEMO-SEED-SMOKE` — the last open follow-up on the reseed niche-defs slice. Confirm all 6 demo seeders (P4 Workflow, P5 Forms, P6 BP, P7c Integration, P7a Rules, P7b Reporting) converge at a real host boot and write correct data for both tenants (BTC + BNV).

## What happened
- **Audit-at-slice-start first.** User initially chose the non-destructive path ("verify existing data"). Queried the live DB → **all 6 demo seeders returned 0 rows.** Root cause (empirical): the last `reset-all` was 2026-06-01 (P2.3c), which **predates** all 6 seeders shipped today (2026-06-02). The DB was in a pre-seeder state — nothing to verify. Surfaced this honestly rather than reporting a false pass; the non-destructive path simply could not close the row.
- **Static half was already covered** without a reset: DI/hosted-service wiring proven by `SeederMarkerContractTests` 2/2 per module; reset-all marker wiring confirmed (script has all 6 `$MK_*` in `$pending`); per-seeder seed-correctness proven by integration tests. Only the live boot-path remained.
- **User then authorized the nuke.** Ran `scripts/reset-all.ps1 -Force -SkipBuild` (skip-build safe: full slnx built 0E/0W earlier this session after the txn-wrap edits, tree clean since).

## What worked
- **All 10 markers converged** at real host boot: 4 core + 6 demo `[Demo*Seeder] All tenants complete`. The expected 2-restart convergence fired for the `TenantAssignmentSeeder` cross-module boot-order race (Organization module boots before Identity user seed finishes → "incomplete" → API restarted once → resolved). Script exited converged. Verify: 3 tenants / 11,506 users / 1,166 orgunits.
- **DB data-verify PASS, both tenants:** Workflow 1 Draft def/tenant (12 states + 12 transitions = 6×2) · Forms 1 Active template/tenant (14 fields = 7×2, 2 versions) · BP 1 Draft/tenant · Integration 1 Active partner/tenant + 2 contracts · Rules 1 Draft RuleSet/tenant (4 rules + 2 decision tables) · Reporting 1 Active def/tenant (string-slug TenantId) + 2 widgets + 2 dashboards + **1 GLOBAL** query-catalog. The global-once catalog (not per-tenant) and the string-slug ReportDefinition.TenantId both confirmed live — the load-bearing P7b design quirks hold at runtime, not just in tests.

## Decisions made
- **Non-destructive first, nuke only on explicit authorization.** reset-all DROPs the dev DB (11.5k users). Treated as a high-reversibility action — surfaced the cost + the fact that the static half was already proven, then let the user decide. Did not silently nuke.
- **Depth = marker + DB data verify (user-scoped).** The optional e2e instance-routing (workflow activate→instance→task-routing; BP activate→run) was not driven live — already covered by integration tests (`DemoWorkflowSeederTests` T4 activate-via-endpoint 204/R1-R4; `DemoBusinessProcessSeederTests` R1-R4). Confirmation, not correctness.

## Test / verification status
Live smoke PASS (10/10 markers + 6/6 seeders correct-shape both tenants). No code change → no build/test/review gate. Backlog row closed `09c4fae9`.

## Lessons
- **Audit-at-slice-start defends against a false-positive close.** The row read "verify the seeders" — but the DB predated them. A 5-min query caught that the non-destructive scope could not actually close the row, before any wasted nuke-less "verification".
- **A verification task can still have a real destructive dependency.** This row looked like "just run a smoke" but its only honest completion path required nuking the dev DB — worth a confirmation gate, not an assumption.
- **Marker convergence + data-shape verify is a strong, cheap live proof.** The integration tests prove correctness in isolation; the live smoke proves the boot-path orchestration (registration order, cross-module race, marker wiring) that no single test exercises.

## Next session
- Reseed arc fully closed (P2–P7 + txn-wrap parity + live smoke). Open: `P3b` FE geo migration (GATED, 5 Qs + treasury blocker), `SEED-ORPHANPURGER-TEST-DATA-STALE` (test-infra debt), `BP-FORM-OPTIONS-SNAPSHOT` (EnumRef empty at BP-run).
- Auth :5002 + API :5001 left running with freshly-seeded demo data — usable for a demo, or stop via `scripts/1_run-local-dev.ps1 stop all`.
