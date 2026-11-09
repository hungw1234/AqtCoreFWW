# Journal — 2026-06-02 · DemoWorkflowSeeder (P4)

**Commits:** `ce57b0f2` feat + `3a224b40` docs (v2.137p137), pushed origin + bitbucket, ahead=0.

## What
NEW `DemoWorkflowSeeder` (Workflow.Infrastructure/Seeding) — seeds one generic 6-state case-processing definition per `TenantSeedProfiles` entry (BTC + BNV) in **Draft**, each of the 6 transitions carrying ONE `WorkflowTransitionAssignment` + `ByRole` clause keyed by per-tenant role **CODE** (`profile.RolePrefix + suffix`). Auto-hierarchy matrix: Submit→[SPECIALIST,OFFICER,STAFF] · Review→[DIVISION_HEAD] · Approve/Reject/Close→[DEPT_DIRECTOR]. No instances/tasks. Gated `Workflow:DemoWorkflowSeed` (OFF). Idempotent per `(TenantId, DefinitionKey)`.

Consumes the precursor (p136 ByRole-by-code) — this is the first production ByRole producer, now clean by code.

## Key design decisions (scout-verified, not assumed)
- **3-phase save** — `WorkflowTransitionAssignment` is a SEPARATE entity keyed by the DB-assigned `WorkflowTransitionId`, not owned by the transition: (1) def+states→state Ids, (2) transitions→transition Ids, (3) assignments. (Verified against the entity + the existing graph-publish test helpers.)
- **Seeder leaves Draft, never activates** — `def.Activate()` (domain) only flips a flag; R1-R4 validation runs only in `ToggleDefinitionActiveCommandHandler`. User decision = Draft → operator activates in UI (re-runs R1-R4). T4 drives that command path to prove the seeded graph is activatable.
- **EntityType="Case" is safe** — `CreateWorkflowInstanceCommandHandler` does NOT enum-check state names against EntityType (the registry is SaveGraph/design-time only). Mirrors E2E seeder.
- **No tenant-existence guard (diverges from OrgTreeSeeder)** — `WorkflowDefinition.TenantId` is an unenforced Guid stamp (no FK), so a missing tenant yields a harmless never-routed def, not an orphaned-FK row. Dropping the guard avoids a cross-schema read → no Architecture whitelist + no test-arrange tenant seeding.
- **`SystemSeederTenantContext` promoted to SharedKernel** — was `internal` in Organization; promoted (public) to `SharedKernel.Application.Seeding` (next to `TenantSeedProfiles`) so module seeders share one filter-bypass context. Organization seeders already import that namespace → zero call-site churn. Chose this over reusing the value-identical `BackgroundJobTenantContext`/`NullTenantContext` to respect the team's seeder-semantic separation.

## Verification
Build slnx 0E · `DemoWorkflowSeederTests` 4/4 real-SQL · full Workflow.Integration suite 78 pass / 3 pre-existing skips / 0 fail · code-review APPROVE-WITH-NITS (0 Crit/High/Med).

## Lessons (META)
1. **Probe entity contracts before designing the save** — assuming the assignment was owned by the transition would have produced a 2-phase save that silently dropped assignments. Reading `WorkflowTransitionAssignment` (separate entity, FK by transition Id) forced the correct 3-phase order.
2. **Activation is the real validation gate, not seeding** — `def.Activate()` is just a flag; the value of the seeded graph is that it PASSES R1-R4 on operator activate. The acceptance test must exercise the command/endpoint path, not the domain method.
3. **The order-dependence I dismissed bit back** — I noted the T4-activates-shared-def risk during planning, judged it order-independent, and shipped a test that failed under batch ordering (T4 activated BTC's def; idempotent seeder won't reset; T1 then saw IsActive=true). The tester caught it. Fix: give the mutation test (T4) its own throwaway tenant. Lesson: when a test mutates shared seeded state, isolate it — don't reason that "the assertions are independent."
4. **Don't trust subagent "no X needed"** (reinforced) — applied to the activate path: verified empirically that CreateInstance doesn't enum-check EntityType rather than trusting the post-Cases-removal comment's implication.

## Carry-forward
- **P5 Form Templates = NEXT** (plan `260530-0803-reseed-all-modules-demo-data/phase-05`). Needs only P2 (tenant context); independent of P4.
- Manual smoke owed: reset-all → activate a demo def in UI → create instance → confirm inbox routes to the seeded role holders (read-side ByRole-by-code already proven p136, so low risk).
- Nit (non-blocking): no other test asserts persisted Vietnamese beyond T1's def.Name + initial-state DisplayNameVi (columns are NVARCHAR-safe).
