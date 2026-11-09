# Phase-01 Tech Debt Cleanup — M1 + M2 Follow-ups

**Date**: 2026-04-17 08:59
**Severity**: Low (quality-of-life, zero functional impact)
**Component**: Workflow.Infrastructure (seeder + health checks)
**Status**: Completed

## Session Outcome

Phase-01 follow-up items M1 + M2 from code-reviewer report closed. PR #26 remains open (feat/phase-01-seed-caseprocess-definition). Commit 1edcee8a: 1 bundled commit `fix(workflow): Phase-01 tech debt — M1 seeder comment + M2 startup health check` (5 files, 171 insertions / 3 deletions). Both items approved at 9.5/10 by code-reviewer (2 minor nits fixed pre-commit, 1 deferred cosmetic test assertion fragility per YAGNI). Sync work: backlog ticked, project-state bumped v2.103→v2.104, dependency-map NOT_APPLICABLE verdict (health check is infra observability, not domain contract).

## What Happened

**M1 — Seeder comment fix (2 lines)**:
File: `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Seeding/CaseProcessDefinitionSeeder.cs:175`. Original comment falsely claimed `WorkflowDefinition.Activate()` validates initial-state count. Root cause: comment writer confused Activate()'s scope — it only sets `IsActive=true` + audit timestamp (lines 163-167 in WorkflowDefinition.cs). Invariant enforcement (exactly one IsInitial=true) is data-driven by the literal `stateData` collection in seeder, not by Activate(). Fix: rewrote comment to explain that invariant is enforced by seeder's row layout, not by the API call. Prevents future seeder writers from being misled.

**M2 — Startup health check (~60 LOC)**:
New file: `HealthChecks/CaseProcessDefinitionHealthCheck.cs` (IHealthCheck impl). Queries `WorkflowDefinitions` for `DefinitionKey="CaseProcess" && TenantId=Empty && Version=1 && IsLatest`. Registered in `WorkflowInfrastructureModule.AddHealthChecks().AddCheck<>()` with:
- Tag: "ready" (joins `/health/ready` k8s probe)
- FailureStatus: Degraded (not Unhealthy — intentional design choice)

**Design decision**: Degraded vs Unhealthy — per-module health checks should fail soft to avoid removing pod from k8s service when only one module is impacted. Hard infra failures (sqlserver, redis) already covered by separate checks returning Unhealthy. This keeps service responsive even if CaseProcess seeder misfired.

**Pattern**: Uses IServiceScopeFactory because DbContext is scoped; health-check resolution may run outside HTTP request scope. Mirrors existing integration-test pattern for scope safety.

**Future-proofing**: Added v2-drift protection comment — when Phase-02 extends CaseProcess to v2, this probe must be updated to check v2 baseline or accept both versions.

## Technical Details

**Health check query rationale**:
- DefinitionKey="CaseProcess" — only this definition
- TenantId=Empty — global seeded instance (not tenant-specific)
- Version=1 — baseline version must exist
- IsLatest — ensures seeded row hasn't been orphaned by migration

**IHealthCheck interface**:
```csharp
public async Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context, 
    CancellationToken cancellationToken = default)
```
Returns `HealthCheckResult.Healthy()` if definition found, `Degraded("...")` if missing/query throws (catch as Degraded, not rethrow).

**Registration**:
```csharp
services
    .AddHealthChecks()
    .AddCheck<CaseProcessDefinitionHealthCheck>(
        name: "CaseProcessDefinition", 
        tags: ["ready"], 
        failureStatus: HealthStatus.Degraded);
```

**Tests** (3 unit tests, InMemory DbContext):
1. Healthy-when-present — definition row exists
2. Degraded-when-missing — no matching rows
3. Degraded-when-query-throws — DbContext throws, result is Degraded not Exception

All 48/48 Workflow.Infrastructure.Tests pass.

## What We Tried

**M1**: Direct comment rewrite — 2-line fix, clean.

**M2**: Two health-check registration patterns tested:
- Pattern 1: Direct DbContext injection — rejected (lifetime mismatch, health-check resolution outside HTTP request scope)
- Pattern 2: IServiceScopeFactory ✓ — used, safe, matches integration-test precedent

Considered Unhealthy vs Degraded — consulted module owner, settled on Degraded per soft-fail policy.

## Root Cause Analysis

**M1**: Comment was aspirational (what writer *thought* Activate() did) vs descriptive (what it actually does). Code reviewer caught it on careful read of Activate() source. Lesson: comments must reference actual implementation, not assumed behavior.

**M2**: Phase-01 deployed seeder but no observability on whether seeder actually ran. No health signal in k8s probes, no way to detect if CaseProcess definition was missing after restart. Gap identified in code review as "nice to have" follow-up.

## Lessons Learned

1. **Comment lies are technical debt** — previous comment on Activate() would have misled future devs writing new seeders. Code-reviewer catch here was worth its weight. Always validate comments against actual source before writing.

2. **Degraded vs Unhealthy policy matters** — per-module probes should fail soft to avoid cascade failures. Reserve Unhealthy for cross-module hard dependencies (DB, cache, queue).

3. **IHealthCheck + DbContext = IServiceScopeFactory** — don't inject DbContext directly into health checks. Lifetime mismatch will cause issues outside request scope. Safe pattern is always scope-factory.

4. **docs-manager's NOT_APPLICABLE verdict was correct** — dependency-map documents domain contracts and module relationships, not infra observability. Inventing a health-check section would reduce doc quality per YAGNI. Health checks are deployment/ops concern, not architecture concern.

5. **plans/ gitignored by design** — working notes (plan updates, follow-up resolution notes) stay local; only code + backlog + journal enter commits. Git-manager verified correctly. Keeps commit history clean.

## Next Steps

1. **M1 + M2 merged to PR #26** — this commit will land when PR #26 merges to main
2. **v2-drift comment** — when Phase-02 extends CaseProcess to v2, revisit health-check query and update accordingly
3. **Health-check observability** — consider Prometheus metrics export in future (outside Phase-01 scope)
4. **Seeder testing pattern doc** — document IAsyncLifetime + manual seeder invoke pattern in developer guide for future seeder implementations

## Sync Status

| Item | Status |
|------|--------|
| backlogs/backlog.md | M1 + M2 ticked ✓ |
| memory/project-state.md | v2.103 → v2.104 ✓ |
| docs/dependency-map.md | NOT_APPLICABLE (health check is infra, not domain) ✓ |
| docs/project-changelog.md | Updated by docs-manager ✓ |

## Metrics

| Item | Value |
|------|-------|
| Commit | 1edcee8a |
| Branch | feat/phase-01-seed-caseprocess-definition |
| Files modified | 5 |
| LOC added | 171 |
| LOC removed | 3 |
| Tests added | 3 (unit) |
| Test suite | 48/48 pass (Workflow.Infrastructure.Tests) |
| Code review | 9.5/10 APPROVED_WITH_MINOR |
| Execution time | ~35 min (M1 + M2 fix + code review + sync) |

## Emotional Reality

Phase-01 follow-ups are hygiene work — not glamorous, but necessary. The comment lie on Activate() is exactly the kind of silent failure that hurts months later when someone tries to understand the seeder logic. Glad code-reviewer caught it. Health check addition is straightforward infra plumbing, but the design decision on Degraded vs Unhealthy matters for cluster resilience — choosing soft-fail here prevents cascading pod evictions when seeder misfires (hard to diagnose, easy to panic about). M2 pattern (IServiceScopeFactory) is now reusable for any future health checks that need scoped services. Nice to ship it clean.

**Confidence level**: HIGH. Both M1 and M2 are production-ready. Health check is observability insurance; comment fix is maintainability insurance. No rework needed.
