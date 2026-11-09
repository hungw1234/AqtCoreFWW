# Journal — Soft-Delete Filter on Unique Indexes: Scout Contradiction (2026-06-11 ~18:00)

**Commit:** `4d142285` (NOT pushed — 1 ahead origin)

## Session goal

Backlog row `AUDIT-IDENTITY-CROSSSCHEMA-DEBT` spawned a latent discovery: ExternalIdentity soft-delete + unique constraints appeared to allow re-linking the same provider/external-id pair after an unlink without requiring a migration. Scout this + fix if real.

## What happened — the correction

**My initial hypothesis:** `IdentityDbContext` has NO global soft-delete query filter on ExternalIdentity → GetById read-leak possible + relink collision unfixed.

**Reality (corrected by planner agent):** `ExternalIdentityConfiguration : EntityTypeConfigurationBase<ExternalIdentity>` inherits `ConfigureSoftDelete`, which applies `HasQueryFilter(x => !x.IsDeleted)` globally to every repo call (GetById, dup-check, `GetByProviderAndExternalIdAsync`). Soft-deleted rows are already filtered at the DB level **for all reads**.

**Consequence:** My described bugs (GetById read-leak, relink collision) were phantom. I'd grepped `IdentityDbContext.cs` (saw only DbSet) and glob-failed on `*Configuration*.cs` config files. Planner surfaced the actual config file; I verified it empirically (read `EntityTypeConfigurationBase` + the actual `ExternalIdentityConfiguration` decorator) before revising the scope.

## The one real bug fixed

Unique indexes `(UserId,Provider)` + `(Provider,ExternalId)` were **NOT filtered on IsDeleted**. A soft-deleted (unlinked) row occupied the unique slot indefinitely → re-linking the same provider/external-id after an unlink threw **duplicate-key 500** instead of succeeding.

**Fix (Option B — partial unique indexes):**
- Add `.HasFilter("[IsDeleted] = 0")` to both indexes in `ExternalIdentityConfiguration.cs`.
- Migration `20260611170000_FilterExternalIdentityUniqueIndexesOnSoftDelete`: drop/recreate indexes as partial (copied the `AddIpFilterRuleUniqueIndex` precedent).
- Add `.Designer.cs` (architecture consistency test requires it; see lesson 3).
- 3 new integration tests: (a) re-link succeeds post-unlink, (b) second active same-provider blocked, (c) DB-level partial-index proof (1 active + N soft-deleted coexist; 2 active rejected).
- NO repo/handler/`JitProvisioningService` change — global filter already handles reads; adding `!e.IsDeleted` would be dead code (YAGNI).

## Tooling friction — lesson 1

**`dotnet ef` unresolvable in this environment.** `dotnet tool list` shows it installed; `dotnet ef` + `dotnet tool run dotnet-ef` both report "run dotnet tool restore." Tried ~6 approaches (dropping `2>&1` per PS 5.1 stderr, full path, restore-again, etc.). Cost: ~30 min. **Decision:** hand-authored the migration + Designer (precedent: `20260512_RestorePhase06RlsPolicies` is SQL-only, no model change, legitimately has no Designer; I conflated that with model-change migrations).

## Code review arc — lesson 2

**C1 — Missing `.Designer.cs`:** Architecture test `MigrationConsistencyTests.AllModules_ModelSnapshot_Should_Match_LatestMigrationDesigner` RED. My integration-test run covered handlers but NOT the architecture suite (same gap the reviewer flagged). **Fix:** Copied snapshot to Designer file at filesystem level, transformed header (`BuildModel` → `BuildTargetModel`, moved attrs, renamed class). Re-ran: architecture 2/2 GREEN.

## Lesson 3 — precedent misapplication

Raw-SQL-only migrations (no model change) legitimately skip Designer. Model-changing migrations must have Designer or snapshot-consistency guard fails. I'd half-justified skipping the Designer from the SQL-only precedent — wrong analogy. **Next time:** "does this migration ALTER the EF model?" is the question, not "is it precedented to skip Designer?"

## Verification

Build 0W/0E `/warnaserror` · re-link integration 3/3 · existing ExternalIdentities integration 19/19 (no regression) · architecture MigrationConsistency 2/2. Code review REQUEST_CHANGES (C1) → fixed → clean APPROVE.

## Decision transparency

User's original scope included "GetById read-leak" + "relink collision" as real bugs justifying migration. Planner's correction (global filter already exists) made those phantom. I traced the correction empirically before revising the scope and transparently documented the before/after with the user rather than silently re-scoping. Per review-audit-self-decision §1: new empirical data justifies revising a prior conclusion; blind trust of even a good agent beats direct verification.

## Backlog updates

- Closed `AUDIT-IDENTITY-CROSSSCHEMA-DEBT` (soft-delete filter + unique index partial filter was the latent item).
- Existing backlog rows (`LOGINAUDIT-TENANT-IDS-PERREQ-MATERIALIZE`, `CI-DISABLED-SINCE-APRIL`) unblocked.

## Next session

- Feature dev focus: T5-01 KPI Alerting (user deferred choice), polish suite (WF/Forms/BP).
- Push timing: user controls.
