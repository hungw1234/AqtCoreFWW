# Journal — 2026-04-16 14:30 — Phase 00 Chunk 1: Workflow Masterdata Taxonomy Foundation

## Session Goal

Implement Chunk 1 (Foundation) of Phase-00 Workflow Masterdata Taxonomy — establish domain entities, constants, DbContext wiring, and migrations. Pure plumbing: no UI, no handlers, no integration logic. Unblocks Cases→Workflow Engine Delegation refactor.

## What Shipped

### Domain Entities + Constants (5 commits, 34 new tests, 90/90 pass)

**Artifacts:**
- 2 new entities: `WorkflowStateTaxonomy`, `WorkflowActionTaxonomy` (AuditableEntity<Guid>, RowVersion via `IsRowVersion()`, ClassificationLevel=Public in Create factory)
- 1 enum: `WorkflowStateCategory` (Initial/Intermediate/Terminal/Rejected with int values 1-4)
- 1 constants file: `WorkflowTaxonomyCodes` (9 state codes + 9 action codes, hard-coded strings matching R1 specs)
- DbContext wiring: 2 DbSets + inline `modelBuilder.Entity<T>()` config in `MasterDataDbContext` (following MasterData module convention, not Workflow's `IEntityTypeConfiguration<T>`)
- EF migration `20260416072444_AddWorkflowTaxonomy` (regenerated after nvarchar 20→30 fix on Category column)

**Code Review Quality:** 9.0/10 APPROVE_WITH_MINOR. 5 Medium findings (M1-M5) addressed in commit `57630468`. M4/M5 deferred to correct chunk boundary (handler/integration concerns out of scope). L1-L5 deferred as optional/FE.

**Test Coverage:** 21 entity unit tests + 8 constants tests + 5 follow-up guard tests = 34 new (56 pre-existing = 90/90 pass). Release build 0W/0E (2m 13s).

## The Brutal Truth

This session felt faster than estimated (3h actual vs 8h planned) because scope was pure foundation — no domain logic surprises, no integration surprises. BUT the speedup hides a critical dependency risk: Task 1.3 (DbContext wiring) and Task 1.4 (migration) are logically tightly coupled. Separate commits create a bisect point where model has pending migration, making intermediate commits unbuildable. Acceptable on feature branch, but sloppy for PR review (reviewer must view all 3 commits as atomic, not sequential verification). Should have merged 1.3 + 1.4 into single "DbContext + Migration" commit.

Also: discovered that our advisor saved us from shipping broken code. Without the functional-index guidance, we'd have committed `HasIndex().HasFilter($"LOWER(Code) = LOWER('{value}')")` which SQL Server rejects outright (expression indexes aren't supported inline on SQL Server). Would have failed at migration apply. Advisor intervention: 5 minutes, prevented 45-minute rollback cycle later.

## Technical Details

### The Advisor Save (Functional Index Bug Prevention)

**What we almost did:** PostgreSQL-style `CREATE UNIQUE INDEX ... ON tbl (LOWER(Code))` functional index, assuming it was portable to SQL Server via EF HasFilter/HasIndex chaining.

**What went wrong:** SQL Server does NOT support expression indexes. EF's `HasFilter()` produces WHERE clauses (filter conditions), not INDEX expressions. Committed code would have compiled, but `dotnet ef database update` would fail: `Msg 156, Level 15 — SQL text requires either:...`.

**Lesson:** "Portable" cross-DB approaches often mean "correct nowhere, wrong everywhere". Always consult advisor before committing to patterns that bridge DB dialects. MasterData already uses `Province.Code` with plain `HasIndex(x => x.Code).IsUnique()` relying on SQL Server default `CI_AS` collation — same pattern, simpler, proven.

### Strict TDD Per-Task Cadence

Each of 4 foundation tasks (1.1, 1.2, 1.3, 1.4) followed red→green→commit:
- **1.1 (entities):** Test RED (build fail on missing type), GREEN (21/21 pass), commit `7fa07d3f`
- **1.2 (constants):** Test RED (undefined constant), GREEN (8/8 pass), commit `b57322fe`
- **1.3 (DbContext):** Config RED (missing DbSet), GREEN (no new tests but model compiles), commit `4b47f00d`
- **1.4 (migration):** Schema RED (no table), GREEN (migration applies, no schema errors), commit `5f2f4ad6`

This produced readable git history and easy bisection. Downside: tasks 1.3 + 1.4 belong logically together but are separate commits. Future refactor: merge into "DbContext + Migration" to preserve atomicity for code review.

### Module Convention Drift

**MasterData convention:** inline `modelBuilder.Entity<T>(e => { ... })` in `MasterDataDbContext` (established by Province/District/CaseType)

**Workflow convention:** separate `IEntityTypeConfiguration<T>` classes (cleaner, more modular)

**Decision:** Followed MasterData precedent. Lesson: module patterns can diverge within same codebase — scout the target module first, not the "most elegant" pattern elsewhere. Consistency within module > global elegance.

### Windows Path Quirk (dotnet ef)

`dotnet tool restore` succeeded but subsequent `dotnet ef` calls reported missing tool despite install. `PATH` propagation quirk across bash subshells on Windows. Workaround: invoked direct binary `C:/Users/thien/.dotnet/tools/dotnet-ef.exe`. Unblocked migration apply.

## Code Review Findings (Minor + Deferred)

| ID | Severity | Item | Status | Chunk |
|----|----------|------|--------|-------|
| M1 | Medium | Deprecate null-guard in WorkflowStateCategory | Fixed (commit 57630468) | C1 |
| M2 | Medium | Add Create/Update null-guards to both entities | Fixed (commit 57630468) | C1 |
| M3 | Medium | Category nvarchar(20) buffer (collision risk) | Fixed 20→30 + regen migration (commit 57630468) | C1 |
| M4 | Medium | Handler integration contract verification | **Deferred to C4** | C4 |
| M5 | Medium | Query result DTO shape alignment | **Deferred to C4** | C4 |
| L1-L5 | Low | 5 optional refactorings (naming, comment clarification) | **Deferred as FE** | Future |

## Lessons Learned

1. **Functional indexes are NOT portable.** "Portable" solutions often fail silently in planning, fail loudly in execution. Consult before committing to cross-DB patterns.

2. **Advisor value > reviewer value on unfamiliar patterns.** Code reviewer catches style/consistency. Advisor catches fundamental incompatibilities. Use both, in order.

3. **Tight logical coupling across commits hurts review.** Task 1.3 + 1.4 should be one "DbContext + Migration" commit. Don't let TDD red→green→commit ritual override logical atomicity.

4. **Module conventions vary; follow local precedent.** MasterData uses inline config. Workflow uses type configs. Both work. Consistency within module > cross-module purity.

5. **Pure foundation phases expose integration risk late.** All 34 tests pass because we test entities in isolation. Real pressure (RowVersion behavior, collation uniqueness, seeder R2 strictness) comes in C2-C3 integration. Expect surprises when seeder and handler code runs against real SQL Server.

6. **Pre-flight estimates on foundation work are usually optimistic.** Estimated 8h, delivered 3h because scope was pure plumbing. C2 (seeder + 4 queries + 8 commands) will likely exceed 14h estimate. Budget time for advisor consultations early.

## Decisions Captured for C2-C5

- **C2 seeder audit identity:** Use `Guid.Empty` or system-user sentinel for `CreatedBy` in masterdata inserts?
- **C2 seeder collation:** Verify masterdata schema inherits `CI_AS` collation from MasterDataDbContext.
- **C3 integration tests:** MUST use real SQL Server container (InMemory won't simulate RowVersion behavior + `CI_AS` uniqueness).
- **C3 state code collision:** "Draft" vs "DRAFT" – conflicting state codes in spec? Verify before implementing 409 error.
- **C4 FE/BE alignment:** `WorkflowStateCategory.Rejected` enum value (int 4) vs state code string `"Rejected"` – risk of type confusion in JSON. Document in API spec.
- **C5 domain rule:** Can `CaseProcess` reference taxonomy row with `IsActive=false`? Define soft-delete semantics early.

## Test Status

| Suite | Pass | Delta |
|-------|------|-------|
| AqtCoreFW.MasterData.Domain.Tests (entities) | 21/21 | +21 new |
| AqtCoreFW.MasterData.Application.Tests (constants) | 8/8 | +8 new |
| Guard tests (integration precursors) | 5/5 | +5 new |
| **Total** | **90/90** | +34 |
| Debug build | 0 warn/0 err | unchanged |
| Release build (warnings-as-errors) | 0 warn/0 err | 2m 13s |

## Next Session

**Chunk 2 Application Layer** — Estimated 14h, likely to exceed.
- Seeder (R2 strict INSERT-IF-NOT-EXISTS per Code, not "AnyAsync return" pattern)
- PLATFORM.* permission tier (new in codebase, coordinate with ABAC module)
- 4 queries + 8 commands with FluentValidation
- All backed by integration tests against real SQL Server

Bring advisor for seeder audit identity decision + Permission tier integration point.

## Related Artifacts

- **Branch:** `feat/phase-00-workflow-masterdata-taxonomy` (5 commits, not yet pushed)
- **Plan:** `plans/260415-1737-cases-workflow-refactor/phase-00-workflow-masterdata-taxonomy.md`
- **Code review:** `plans/reports/code-reviewer-260416-1425-chunk-01.md`
- **Commits:**
  - `7fa07d3f` — Domain entities (TDD 1.1)
  - `b57322fe` — Constants (TDD 1.2)
  - `4b47f00d` — DbContext wiring (1.3)
  - `5f2f4ad6` — EF migration (1.4)
  - `57630468` — Code review fixes (M1/M2/M3)
