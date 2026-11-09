# IpFilterRule Flip Data Audit & Remediation Runbook

**Date:** 2026-06-12 ~10:30
**Commit:** `1fed129c` — 1 ahead of origin/main, NOT pushed
**Flow:** backlog row `IPFILTERRULE-PROD-FLIP-DATA-AUDIT` (T4 follow-up filed 2026-06-11) → `/cook` (operationalize audit + runbook) → `/code-review` (verify audit logic)

## Session goal
Translate the fact that IpFilterRule insert-flip bug (`ffbe2fac` 2026-06-11) corrupted any rule inserted with `IsEnabled=false` or `IsBlock=false` before fix, into: (1) a detection query per environment, (2) a remediation runbook for downstream adopters, (3) a close-note on the backlog row.

## What was done

### Artifact 1: Detection + remediation SQL script
**File:** `scripts/260612-ipfilterrule-insert-flip-remediation.sql`

**Core insight:** A flipped row is **value-indistinguishable from a legit rule** in the DB. E.g. an inserted "allow CIDR X" (intended `IsBlock=false`) is now stored as `IsBlock=1` — indistinguishable from a legit "block CIDR X" with `IsBlock=1`. **Only discriminator = CreatedAt** (rows pre-fix are suspect).

**Scout-first caught inverted audit query in original backlog note:** The originally-filed audit query was `WHERE IsBlock=0 OR IsEnabled=0`, which returns the **un-corrupted** rows (explicit false that survived the bug) → false "all clean" claim. Real procedure:
1. Identify **all pre-fix rows**: `CreatedAt < @FixDeployUtc` (date fix was deployed)
2. Manual reconciliation against intended config (audit log, admin tickets, rule-intent history)
3. Remediation per row (idempotent template provided; parameterized by @FixDeployUtc)

**Key security concern:** `IsEnabled=false` flipped to `true` means an inert rule enters the live rule set. disabled→enabled flip is **materially active** (middleware loads via `ListAllEnabledAsync` filtering IsEnabled). Conversely, `IsBlock` flag flip (allow→block) inverts middleware behavior.

**Script structure:** Read-only detection + commented idempotent per-row remediation template (UPDATE per row + comment with intent-verification instructions). NOT executed in template (safe for audit before commit).

### Artifact 2: Runbook documentation
**File:** `docs/ops/ipfilterrule-flip-data-audit-runbook.md`

**Sections:**
- §1 Scope: what rows are suspect (pre-fix by CreatedAt), why value-indistinguishable, why manual reconciliation required
- §2 Detection: how to run script per environment (SQL Server 2016+), interpretation of result set
- §3 Manual Triage: workflow (admin checks tickets/intent logs, correlates to IpFilterRule.IpAddress+rule-name, decides fix/no-fix)
- §4 Remediation: execution of UPDATE statements, cache flush (key = `system:ip-rules`, TTL = 5min per `IpFilterMiddleware:26`)
- §5 Verification: post-fix assertions (`ListAllEnabledAsync` enumerates correct final set)
- §6 AqtCoreFW note: no live prod data (template repo); runbook is a pattern for downstream adopters

**Key lessons embedded in §1:**
- Lesson 2: **UPDATE-path heals, UPDATE with same-value doesn't (cache-miss risk).** A row edited to re-submit the flipped value doesn't trigger EF change-tracking → UpdatedAt NOT stamped → row stays wrong. Sorting by UpdatedAt or filtering `UpdatedAt IS NULL` both produce false-negatives. **Wide net is safer** (reconcile all pre-fix rows; reconciliation cost << security control false-negative cost).
- Lesson 3: **Cache bypass on direct DB UPDATE.** Runbook must explicitly flush `system:ip-rules` key (or wait 5min TTL). Verified key+TTL exact in `IpFilterMiddleware:26–28` + source citation in runbook.

### Artifact 3: Backlog row closure
**File:** `backlogs/backlog.md`

Row `IPFILTERRULE-PROD-FLIP-DATA-AUDIT` (T4, filed 2026-06-11) closed with:
- Link to script + runbook
- Note that **AqtCoreFW has no live prod** (dev DB `[identity].IpFilterRules` = 0 rows, re-confirmed empirical sqlcmd)
- Pattern applies downstream (e.g. if Aequitas deployed pre-fix)

**Self-caught medium issue during code-review:** Original backlog row inlined a narrower suspect predicate (`AND UpdatedAt IS NULL AND (IsEnabled=1 OR IsBlock=1)`) which reintroduces the exact false-negative the runbook argues against. Corrected in-row to point at script's wide predicate (all pre-fix rows).

### Artifact 4: Changelog entry
**File:** `docs/project-changelog.md` (2026-06-12 entry added)

Recorded: T4 follow-up operationalized into runbook + script for downstream audit.

## What worked / verified

- Code-review (code-reviewer subagent): 5/5 audit-logic claims CONFIRMED with source citations (IpFilterMiddleware cache key+TTL, EF change-tracking INSERT-only sentinel behavior, CreatedAt discriminator invariant).
- 1 Medium finding: backlog row predicate too narrow → fixed.
- 1 polish: lesson 4 (disabled→enabled flip is materially active) clarified in runbook §1.
- Empirical: sqlcmd run on dev DB `[identity].IpFilterRules` = 0 rows, script EXITCODE=0 (no errors).
- Scope verified via INVARIANT 260522-A: row was 1 day old but empirical probe (direct query, not trusting row claim) run before opening; grid empirical confirms "all clean" for template repo.

## Test status
No integration tests (operationalization task, not code-change task). SQL script syntax verified via sqlcmd run; runbook peer-reviewed. Build remains 0W/0E.

## Lessons

1. **Flipped rows are value-indistinguishable → empirical detection fails.** Only invariant is temporal (CreatedAt). Cannot SQL-auto-detect; must manual-reconcile against intent (audit log / tickets). Wide-net suspect-set is correct (every pre-fix row) even if 90% turn out benign.
2. **UPDATE-same-value ≠ UPDATE-change-value in EF.** EF change-tracking only emits UPDATE if value differs → UpdatedAt NOT stamped → cache-miss false-negative. Runbook must reconcile all pre-fix rows, not filter by UpdatedAt null-ness.
3. **Sentinel-only fixes INSERT-path, not UPDATE-path.** A flipped row in live system is not self-healing (UpdatedAt won't stamp unless value-changes); manual remediation required. `ffbe2fac` prevents future flips only.
4. **Cache invalidation is explicit.** Direct DB UPDATE bypasses EF cache-invalidation hooks → runbook must flush `system:ip-rules` key by name (or wait 5min TTL). Verified in `IpFilterMiddleware:26–28`.
5. **disabled→enabled flip is materially active.** A stale disabled rule flipped on enters live rule set via `ListAllEnabledAsync` (filters IsEnabled) → security posture changes. Not cosmetic data corruption; actual behavior shift.

## Unresolved / next steps

- **Sibling T4 follow-up** `EF-TENANTSIGCONFIG-ISENABLED-DEFAULT` (YAGNI-deferred) — optional migration to drop DB default on IsEnabled column.
- Commit `1fed129c` remains unpushed (awaiting user approval or next session).

## Notes on AqtCoreFW scope

AqtCoreFW is a template repository with no live production environment. Dev database empirically confirmed zero IpFilterRules. Runbook is a pattern document for downstream adopters (e.g. Aequitas platform if deployed pre-fix). No live data remediation needed here.
