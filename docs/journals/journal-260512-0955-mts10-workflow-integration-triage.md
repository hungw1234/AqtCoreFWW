# Journal — MTS-10: Workflow.Integration Triage (2026-05-12 09:55 ICT)

## What

Triaged the 8 pre-existing failures in `AqtCoreFW.Workflow.Integration.Tests` (confirmed identical at parent commit `c841ebf6` by an earlier bisect — long-standing, never investigated). **Result: 14/24 → 22/24 PASS** (0 fail, 2 skip). The 2 skips are intentional perf-gate tests (`P95Under{200,500}ms`) — Testcontainers single-node can't reliably meet the thresholds; reason still valid. Architecture suite 94/94 unchanged. 3 files changed, test-only — no `src/` change.

## Three root causes (all test-side)

1. **`Migrations/UniqueActiveInstanceIndexPreflightCleanupTests.cs`** — Phase-04b changed `WorkflowInstances.Id` from `uniqueidentifier` to `bigint IDENTITY`. The raw-SQL `SeedActiveInstanceAsync` helper still passed `Guid.NewGuid()` for `[Id]`/`[DefinitionId]`/`[CurrentStateId]` (implicit Guid→bigint = SQL Server "Operand type clash"), and omitted the `[PublicId]` AK column (NOT NULL) entirely; `GetSingleActiveIdAsync` read a bigint column as `Guid?` (always null). Fix: drop `[Id]` from the INSERT (IDENTITY), add `[PublicId]` (fresh Guid), `1L` sentinel for the FK bigints, capture the assigned PK via `OUTPUT INSERTED.[Id]`, change return type to `long`; switched to ADO.NET `cmd.Parameters` with explicit `DbType` to avoid EF string-interpolation type inference.
2. **`Helpers/InlineWorkflowDefinitionFactory.cs`** — `WorkflowState.Create(definitionId: definition.Id)` was called before any `SaveChangesAsync`, so `DefinitionId` was `0` (IDENTITY not yet populated) → states MERGEd referencing a non-existent definition → `FK_WorkflowStates_WorkflowDefinitions_DefinitionId` violation. Fix: 3-phase save — persist definition (get real Id) → persist states (get real state Ids) → persist transition. **Closes backlog `PHASE7-WF-INLINE-FACTORY-FK-DRIFT`.**
3. **`WorkflowEndpointsTests.cs`** — `WorkflowDefinitionsController` carries `[Authorize(Policy="WORKFLOW.DEF.VIEW")]`; the test host's `PermissionAuthorizationHandler` does a real DB permission lookup, and a random test `UserId` has no permission rows → 403 (empty body → "input does not contain any JSON tokens" on deserialize). The `TestSystemAdminPermissionBypassHandler` only fires for role `"SystemAdmin"`; the two tests used `"GovOfficer"` / no role. Fix: `CreateAuthenticatedClient(UserId, TenantA, "SystemAdmin")` — the established pattern across `SaveDefinitionGraphIntegrationTests`, Identity.Integration, SmokeTests.

Code-review: 9.4/10 APPROVE, 0 critical/high, 3 Low (informational): non-atomic 3-phase save in a throwaway-DB helper; connection-open-without-explicit-close matches pre-existing local style; explicit `DbType` bindings now brittle to schema column-type changes. Reports: `plans/reports/debugger-260512-0928-mts10-workflow-integration-triage.md`, `plans/reports/code-reviewer-260512-0928-mts10-workflow-integration-fixes.md`.

## Follow-up

- MTS-10-FU1 (P3, ~30m): add a negative-path `WorkflowEndpointsTests` — regular officer without `WORKFLOW.DEF.VIEW` → 403 — to restore the authz coverage the SystemAdmin client now bypasses.

## MTS sweep status (2026-05-12)

MTS-1 (regression pass), MTS-9 (arch-suite compile fix), MTS-10 (Workflow.Integration triage) all DONE. Remaining MTS rows are P3 nits (MTS-2 nvarchar tightening, MTS-4/5 Dapper Row-record cleanup, MTS-6 react-flow setState anti-pattern, MTS-7/8 Visual Designer E2E/Clone tests, MTS-9-FU1, MTS-10-FU1).
