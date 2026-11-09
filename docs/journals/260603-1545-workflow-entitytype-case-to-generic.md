# Workflow EntityType: "Case" Default Orphaned by PR #71 Module Deletion — Buried in 3 Sites + 1 Migration

**Date**: 2026-06-03 15:45
**Severity**: High
**Component**: Workflow Definition (EntityType field + default), GenericEntityStatusEnumContributor, Workflow Graph Editor validation
**Status**: Resolved

## What Happened

User reported `GOV_SYS_002` "unknown entity type 'Case'" when editing workflow graph via PUT `/workflow/definitions/{id}/graph`. PR #71 removed the Cases module entirely, but the `"Case"` EntityType default remained frozen in:

1. `WorkflowDefinition` field initializer and ctor
2. `CreateWorkflowDefinitionCommand` parameter default
3. `WorkflowDefinitionConfiguration.HasDefaultValue` (EF)
4. Migrations (immutable history)

Additionally, `GenericEntityStatusEnumContributor` — the only remaining contributor after Cases deletion — was **off the canonical workflow-status taxonomy** (`WorkflowTaxonomyCodes.WorkflowStates`), using `InReview`/`Cancelled` while the canonical list defined `UnderReview`/`Closed`. Graph validator (`WorkflowGraphValidator`) only runs on **edit** (PUT), not activate (POST), so activation-path tests (R1-R4) never caught this.

The user's `gov-case-processing` workflow (PublicId 2000…0001) auto-seeded with broken "Case" default via `E2ETestDataSeeder.cs:329` no-arg ctor call.

## The Brutal Truth

This is **lazy cleanup after module deletion.** The mindset was "remove the module code, tests pass" without sweeping the artifact defaults. The broken state lived in prod-facing migrations and EF config for ~2 weeks undetected because:

- Activation path doesn't validate EntityType (only graph-edit does).
- User's first attempt hit a different bug (missing assignment rules, 6 transitions).
- Only when adding assignments via graph editor did GOV_SYS_002 surface.

The real frustration: two developers (code-reviewer + tester) independently caught a **blast-radius miss** (InReview→UnderReview rename in only 3 of 5 test files), forcing a second commit. That's not a detection failure — that's a coordination gap. I renamed 3 files under `tests/integration/` but left 2 active tests in the separate `src/tests/AqtCoreFW.Tests.Integration/` project unmodified. Convergent catch = strong signal that the scope was **incompletely reasoned from the start**.

## Technical Details

**Root Cause (Two Layers)**

Layer 1: Default value persistence
```csharp
// WorkflowDefinition.cs
public static WorkflowDefinition Create(
    TenantId tenantId,
    string name,
    string entityType = "Case",  // ← ORPHAN: Cases module deleted
    ...
)

// WorkflowDefinitionConfiguration.cs
builder.Property(x => x.EntityType)
    .HasDefaultValue("Case");  // ← Frozen in EF config
```

Layer 2: Taxonomy misalignment
```csharp
// GenericEntityStatusEnumContributor.cs (only remaining after Cases deletion)
public IEnumerable<TaxonomyItem> GetItems()
{
    yield return new TaxonomyItem { Code = "InReview", Name = "In Review" };
    yield return new TaxonomyItem { Code = "Cancelled", Name = "Cancelled" };
    // Missing: "UnderReview", "Closed" from canonical WorkflowTaxonomyCodes.WorkflowStates
}

// WorkflowTaxonomyCodes.WorkflowStates (canonical, 10 codes)
public static readonly IReadOnlyList<string> WorkflowStates = new[]
{
    "Draft", "Active", "UnderReview", "Closed", "Suspended", 
    "Cancelled", "Archived", "InDraft", "InProgress", "OnHold"
};
```

**Symptom Path**

User PUT `/workflow/definitions/{id}/graph` with assignment payload → `WorkflowGraphValidator.Validate` → `entityTypeService.ValidateEntityType(definition.EntityType)` → 0 contributors claim "Case" → `GOV_SYS_002` thrown.

Activation path (POST `/workflow/definitions/{id}/activate`) does NOT call `WorkflowGraphValidator` → tests R1-R4 never saw the error.

**Applied Fix (Non-Destructive)**

1. **Default value change (3 sites)**
   - `WorkflowDefinition.Create(…, entityType = "Generic", …)`
   - `CreateWorkflowDefinitionCommand.EntityType = "Generic"`
   - `WorkflowDefinitionConfiguration.HasDefaultValue("Generic")`

2. **New migration** (20260603151754_ChangeWorkflowDefinitionEntityTypeDefaultToGeneric)
   ```sql
   ALTER TABLE [workflow].[WorkflowDefinitions]
       ADD CONSTRAINT DF_EntityType_Generic
       DEFAULT 'Generic' FOR [EntityType];
   ```
   Non-destructive (field stays NOT NULL, no row backfill, reversible). Existing "Case" rows unmodified; new rows/reseeds pick up "Generic".

3. **GenericEntityStatusEnumContributor alignment**
   Static literal list of 10 codes matching canonical `WorkflowTaxonomyCodes.WorkflowStates` (no dependencies on deleted Cases module, preserves module isolation):
   ```csharp
   public IEnumerable<TaxonomyItem> GetItems()
   {
       var codes = new[] { "Draft", "Active", "UnderReview", "Closed", "Suspended", 
                          "Cancelled", "Archived", "InDraft", "InProgress", "OnHold" };
       // …
   }
   ```

4. **Seeder updates**
   - `DemoWorkflowSeeder`: `WorkflowDefinition.Create(..., entityType: "Generic", ...)`
   - `DemoRulesSeeder`: Same
   - `E2ETestDataSeeder`: No-arg ctor now uses "Generic" default (auto-benefit)

5. **Test state rename** (InReview → UnderReview)
   - Changed in 5 integration test files (3 under `tests/integration/`, 2 under `src/tests/AqtCoreFW.Tests.Integration/`)
   - **Blast-radius miss on first pass:** I renamed only 3, missed 2 in separate project. Code-reviewer (CRIT-1) + tester both flagged. Fixed in follow-up commit (tester responsibility).

6. **Dead-code cleanup**
   - Removed Cases-module doc references from `IStreamingExportService`, `IEntityStatusEnumContributor`, test fixtures
   - Left untouched: "GOV Case Processing" workflow name (domain word "hồ sơ", not module remnant); Notifications csproj comment ("Case" = historical context); CaseType masterdata, RuleTestCase, circuit-breaker "Closed" state (unrelated English)

## What We Tried

1. **Grep EntityType usage** — Found 4 sites with hardcoded "Case": ctor default, command param, EF config, migration history.
2. **Trace seeder callers** — Identified E2ETestDataSeeder.cs:329 no-arg ctor; confirmed it auto-picks broken default.
3. **Validate contributor registry** — Confirmed GenericEntityStatusEnumContributor did NOT claim all 10 canonical codes; alignment gap.
4. **Test-rename scope** — First pass caught 3 files; second pass (after reviewer alert) caught 2 more in separate project.
5. **Reseed + reset-all** — DB post-reset shows `EntityType: Generic ×3, 0 Case`; demo workflows seeded with 6 transition-assignments each; full stack restored (Auth 5002, API 5001, FE 5173).

## Root Cause Analysis

**Why the bug persisted 2 weeks:**

- Module deletion did NOT sweep derived artifacts (field defaults, EF config, migrations). Root assumption: "if I remove the module project, the codebase is clean." False. Defaults are loose couplings that survive project deletion.
- Graph validation runs only on **edit**, not **activate**. Activation tests (R1-R4) passed because they skip the graph-editor path. The bug stayed latent until a user tried to edit.
- Missing **module-removal checklist**: (a) Remove project/DI; (b) Sweep defaults; (c) Sweep enums/registries; (d) Verify all entry points (activate + edit + seed + export).
- Blast-radius miss on test renames: I scoped the change to the obvious file locations without tracing the second project's separate test root. Assumption: "all integration tests in one place." Coordinators caught this faster than I could reason it.

## Lessons Learned

1. **Module deletion is NOT finished when the project is removed.** Default values in fields, EF config, and migrations are artifact remnants that survive. Checklist: (a) code; (b) config; (c) migrations; (d) enum/registry contributors; (e) all code-paths that instantiate the entity.

2. **Validator placement matters.** If `WorkflowGraphValidator` also ran on activate (not just edit), the bug would have surfaced immediately in the reseed. One-path validators hide bugs. Consider: (a) run all validators on all paths, or (b) document why a path is exempt, or (c) add a separate smoke test that exercises the rare path.

3. **Distinguish deleted-module remnants from unrelated English words.** "Case" in `RuleTestCase`, `Closed` state, "GOV Case Processing" domain language, `CaseType` masterdata = NOT module artifacts. Premature renaming risks breaks Playwright specs or business semantics. Scope deletions narrowly: only sweep the **actual** Cases-module traces (entities, contributors, defaults), leave domain language alone.

4. **Coordinate refactoring scope across isolated test projects.** The codebase has two test roots: `tests/integration/` and `src/tests/AqtCoreFW.Tests.Integration/`. Renaming in one without the other creates subtle failures. Grep-before-commit is cheap.

5. **Convergent catches = scope was incomplete.** When two independent reviewers flag the same category of issue (missed test files), it signals the original reasoning was too narrow. Don't assume first-instinct scope is sufficient; ask "where else could this apply?"

## Next Steps

- **PR #72 merged** (commit b4bff447, pushed origin+bitbucket, ahead=0). Build 0E/0W; 1198 unit + 100/103 integration pass (3 skipped).
- **User's gov-case-processing workflow:** Now seeds with "Generic" (fixed). Still has **0 transition-assignments** (original goal not auto-complete). Manual step remains: re-send original PUT (assignment payload) with fresh Bearer token post-reset. Alternative: use demo BP `demo-business-process` (BTC/BNV tenant), which has 6 pre-seeded assignments.
- **FE stack:** reset-all does NOT restart Vite (`npm run dev`). Advisor caught that; I restarted manually (future: add check or script).
- **No related follow-ups.** All artifact defaults swept; contributor taxonomy aligned; tests fixed. Residuals: (a) Edges not rendering in BP visualization (separate `/ck:debug` needed, out of scope); (b) Assignment-rule first-failure on activate (user's original issue, pre-existed this fix).

**Status**: RESOLVED

## Unresolved / Follow-up

- User's original goal: `gov-case-processing` transitions still have 0 assignments. Requires manual re-PUT of original assignment payload with fresh token post-reset, **then** activate. Not auto-completed by default-change; that only unblocks validation.
- Edges not rendering in workflow visualization (WF#13, 2 transitions → 0 edges). Symptom pre-dates this fix; root cause unclear (numeric ID mismatch, missing react-flow handles, or data issue). Recommend separate debug session.
- Reset-all script doesn't auto-restart FE dev server (Vite). Manual restart required; consider automation.
