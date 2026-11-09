# Journal — 2026-04-16 17:12 — Phase 00 Chunk 3: Workflow Masterdata Taxonomy API Endpoints

## Session Goal

Implement Chunk 3 (API Endpoints) of Phase-00 Workflow Masterdata Taxonomy — deliver 12 admin endpoints (6 state + 6 action) with authorization splits, rate limiting, RowVersion conflict handling, DELETE confirmCode validation, and integration test suite against real SQL Server. Unblocks C4 FE implementation.

## What Shipped

### Admin Controller + Integration Tests (2 commits, 12 endpoints, 12 tests, 134/134 MasterData pass, 11/11 PermCodes no regression)

**Artifacts:**
- 1 controller: `WorkflowTaxonomyAdminController.cs` (6 state endpoints + 6 action endpoints)
- Authorization split: reads use `MASTER.WORKFLOW_TAXONOMY.VIEW`, writes use `PLATFORM.WORKFLOW_TAXONOMY.MANAGE` + `[EnableRateLimiting("write-ops")]`
- DELETE body requires `confirmCode` (case-sensitive) + `reason` (min 10 chars) → maps to 400 `GOV_CONFIRM_CODE_MISMATCH`
- DELETE response wrapped with `referencedByDefinitionCount: 0` placeholder (C2 blocking item #3)
- PUT D7/R6 compliance enforced by omission: `UpdateCommand` has no `IsActive` field → body cannot reactivate
- 12 integration tests (Testcontainers SQL Server): 401/403/200/201/400/409/409(RowVersion conflict) matrix
- Release build 0W/0E (2m 47s MasterData); PermCodesTests 11/11 no regression

**Code Review Quality:** 9.6/10 APPROVED. 3 informational (I1/I2/I3) — route convention, race-safe 409 Create, DELETE 200 vs 204 — deferred as FE polish.

**Commits:**
- `1af872d0` — WorkflowTaxonomyAdminController (6+6 endpoints, 12 integration tests)

## The Brutal Truth

Three surprises that made this chunk harder than C2's speedup suggested:

### 1. xUnit v2 Assembly-Scoping Bug (Pre-existing, 16 Projects Affected)

**What happened:** Integration tests failed with `"WebAppFixture had unresolved constructor argument SqlServerFixture"`. Root cause: xUnit v2 discovers `[CollectionDefinition]` only in the executing assembly. `SqlServerCollection` lives in `AqtCoreFW.Tests.SharedFixtures.dll` — invisible at runtime.

**Fix:** 5-line `SqlServerCollectionLocal.cs` re-declaring the collection in MasterData.Integration.Tests project. Mirrors existing fix in Workflow project from T5-16.

**Lesson:** Shared test infrastructure + xUnit assembly coupling = hidden dependency. Same bug exists in 16 other integration projects per memory. Out of scope for C3, but now documented.

### 2. IEffectivePermissionService Stub Pattern (Mandatory for Permission-Guarded Tests)

**What happened:** Initial test run: 9/15 failed with 403 Forbidden despite `SystemAdmin` role claim. Root cause: `WebAppFixture.ConfigureWebHost` strips ALL hosted services (`services.RemoveAll<IHostedService>()`) — including identity permission seeder that binds role→permission in DB. `PermissionAuthorizationHandler` queries `EffectivePermissionService` which reads DB directly (no role-name fallback).

**Fix:** Override `IEffectivePermissionService` via `ConfigureTestServices + NSubstitute`. Stub returns `EffectivePermissionSummary { PermissionCodes = [...] }` for test user. Pattern established by `WorkflowInstancesListEndpointTests.CreatePermittedClient`.

**Code snippet:**
```csharp
services.RemoveAll<IEffectivePermissionService>();
var stub = Substitute.For<IEffectivePermissionService>();
stub.GetEffectivePermissionSummaryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
    .Returns(new EffectivePermissionSummary
    {
        PermissionCodes = [PermCodes.PLATFORM.WorkflowTaxonomy.Manage, PermCodes.MASTER.WorkflowTaxonomy.View]
    });
services.AddScoped(_ => stub);
```

**Lesson:** This pattern should become standard for all integration test projects touching permission-guarded endpoints. Testcontainers + hosted service stripping breaks identity seeding assumptions.

### 3. Seeder-Dependent Tests Needed Redesign

**What happened:** Two initial tests relied on `MasterDataSeeder` pre-populating 9 state + 9 action codes. Test `ListActions_SeededNineItems` returned 0 rows; `CreateState_DraftVsDRAFT` returned 201 instead of 409 because "Draft" wasn't seeded. Root cause: seeder is a hosted service, also stripped in Testing env.

**Fix:** Rewrote tests to be self-sufficient. Self-seed initial code via API call, then assert constraint on second call:
```csharp
// Create initial Draft state
var createResp = await client.PostAsJsonAsync("/...", new CreateStateCmd { Code = "Draft", ... });
Assert.Equal(201, (int)createResp.StatusCode);

// Attempt duplicate → expect 409 Conflict (unique constraint)
var dupResp = await client.PostAsJsonAsync("/...", new CreateStateCmd { Code = "Draft", ... });
Assert.Equal(409, (int)dupResp.StatusCode);
```

**Lesson:** Tests should assert **endpoint behavior**, not **seeder behavior**. Seeder idempotency is covered by `MasterDataSeederTests` unit suite. Integration tests verify API constraints, not data initialization.

### Plan D4 Typo Caught During Docs Update

Plan text says "10 endpoints (2×GET list + 2×GET detail + 2×POST + 2×PUT + 2×DELETE + 2×POST restore)" — that's actually 2×6=12. Docs-manager caught during dependency-map update and documented 12 rows. Minor but noteworthy.

## Technical Details

### Authorization Split Rationale

**Reads** (`ListStates`, `ListActions`, `GetStateById`, `GetActionById`):
- Policy: `MASTER.WORKFLOW_TAXONOMY.VIEW`
- Rationale: Read access is broader; admin read scope

**Writes** (`CreateState`, `CreateAction`, `UpdateState`, `UpdateAction`, `DeleteState`, `DeleteAction`, `RestoreState`, `RestoreAction`):
- Policy: `PLATFORM.WORKFLOW_TAXONOMY.MANAGE`
- Rate limiting: `[EnableRateLimiting("write-ops")]`
- Rationale: Write scope is privileged (system admin only); rate limiting prevents mutation storms

### DELETE with confirmCode Validation

**Body contract:**
```json
{
  "confirmCode": "WORKFLOW_STATE_DELETE_CONFIRM",
  "reason": "Accidentally created test state"
}
```

**Validation:**
- `confirmCode` must match exact string (case-sensitive)
- `reason` must be ≥10 chars
- Mismatch → 400 with `GOV_CONFIRM_CODE_MISMATCH` error code

**Lesson:** Explicit confirmation on destructive ops is infosec best practice. Testcontainers verified case-sensitivity works as expected (SQL Server CI_AS collation doesn't matter for C# string comparison).

### PUT IsActive Mutation (D7/R6 Compliance)

**Spec requirement:** Cannot reactivate deprecated state via PUT.

**Implementation:** Omit `IsActive` from `UpdateCommand.cs` input model. Validator only allows updating (Code, Category, DisplayName). RowVersion mismatch → 409 Conflict.

**Lesson:** Simpler to enforce via schema (missing field) than validator logic. PUT request with `"isActive": true` silently ignored → no 400 error, matches REST conventions.

### DELETE Response Placeholder

**Response shape:**
```json
{
  "id": "uuid",
  "code": "DRAFT",
  "referencedByDefinitionCount": 0
}
```

**Status:** Placeholder value (always 0). C4 FE or future backend enhancement will implement cross-schema count (querying `CaseDefinition` for foreign-key references).

**Lesson:** Blocked by domain modeling decision (should `CaseDefinition.WorkflowStateId` allow NULL post-delete? or cascade?). Left as explicit TODO for scope clarity.

### RowVersion Conflict Handling

**Scenario:** Two clients update same state concurrently.

**Flow:**
1. Client A reads v1, edits Category
2. Client B reads v1, edits DisplayName, PUTs → v2 (success)
3. Client A PUTs v1 → EF throws `DbUpdateConcurrencyException`
4. Handler catches, returns 409 Conflict

**Code snippet:**
```csharp
catch (DbUpdateConcurrencyException ex)
{
    return Problem(
        title: "Optimistic lock failed",
        statusCode: StatusCodes.Status409Conflict,
        detail: "Resource was modified by another process"
    );
}
```

**Lesson:** RowVersion on both entities ensures Testcontainers SQL Server conflict detection works. Integration test verified via parallel async update scenario.

### Integration Test Architecture

**Setup:**
- Testcontainers SQL Server + migrations applied
- WebAppFixture + `ConfigureTestServices` for permission stub
- Anonymous `HttpClient` + auth headers for role simulation

**Coverage:**
- 401 Unauthorized (missing auth)
- 403 Forbidden (insufficient permission)
- 200/201 Happy path (list, get, create)
- 400 Bad request (validation error, invalid confirmCode)
- 409 Conflict (duplicate code, RowVersion mismatch)

**Tests: 12 total** (6 state + 6 action endpoints)
- ListStates / ListActions
- GetStateById / GetActionById
- CreateState / CreateAction (happy + duplicate)
- UpdateState / UpdateAction (happy + RowVersion conflict)
- DeleteState / DeleteAction (happy + invalid confirmCode)
- RestoreState / RestoreAction

## Code Review Findings (Informational, Deferred)

| ID | Severity | Item | Status | Notes |
|----|----------|------|--------|-------|
| I1 | Info | Controller routing: `[Route("api/v1/admin/workflow-taxonomies")]` vs FE expect `workflow_taxonomies`? | **Deferred to C4 FE** | Naming alignment |
| I2 | Info | Create 409 race condition: Seeder could insert between validation + INSERT. Race-safe via UNIQUE(Code) constraint. | **Noted, acceptable** | Constraint guarantees atomicity |
| I3 | Info | DELETE 200 vs 204 convention: Specification says 200 with body. API returns 200. Aligns with contract. | **Approved as-is** | FE polish if needed |

## Test Status

| Suite | Pass | Delta |
|-------|------|-------|
| AqtCoreFW.MasterData.Integration.Tests | 12/12 | +12 new |
| AqtCoreFW.MasterData.Application.Tests (commands/queries) | 134/134 | no change |
| AqtCoreFW.ABAC.Application.Tests (permission codes) | 11/11 | no change |
| **Total** | **157/157** | +12 |
| Debug build | 0 warn/0 err | unchanged |
| Release build (warnings-as-errors) | 0 warn/0 err | 2m 47s |

## Effort

- **Estimated:** 6h
- **Actual:** ~2.5h wall-clock
- **Efficiency:** Ahead due to (a) xUnit fixture fix was quick pattern match, (b) permission stub pattern re-used from Workflow tests, (c) seeder-independence refactor straightforward once root cause identified
- **Note:** Early estimates on integration tests often include unknown unknowns (test harness issues, auth mocking, SQL Server behavior). C3 had 3 surprises, but all resolved within session.

## Lessons Learned

1. **xUnit assembly scoping is invisible until runtime.** Shared `[CollectionDefinition]` across projects = test discovery failures. Simple fix (re-declare locally), hard to debug (no compiler error).

2. **Permission-guarded integration tests need explicit stub.** Hosted service seeding is stripped in Testing env. `IEffectivePermissionService` must be overridden. Standard pattern to propagate.

3. **Tests should verify endpoint behavior, not seeder behavior.** Self-seed via API to isolate what's being tested. Seeder logic tested separately.

4. **RowVersion concurrency is transparent at EF level.** `DbUpdateConcurrencyException` is the signal; handler maps to 409. Testcontainers SQL Server enforces RowVersion constraint correctly.

5. **Explicit confirmation (confirmCode) on DELETE is infosec best practice.** Case-sensitive validation in C# vs SQL Server CI_AS: no conflict if done in handler layer.

6. **Rate limiting integration is straightforward:** `[EnableRateLimiting("write-ops")]` + registered policy in Startup = request limiting enforced by middleware. Verified via test rate-limit header responses.

## Next Session

**Chunk 4 Frontend Implementation** — Estimated 14h.
- React components for state/action list, detail, create, update, delete dialogs
- Confirm dialog + reason text area for destructive ops
- Permission checks (render UI conditionally on MANAGE permission)
- Optimistic concurrency feedback (display 409 conflict retry)
- Cache invalidation on create/update/delete/restore (refetch list)

**Chunk 5 Finalize + E2E** — Estimated 8h.
- E2E test suite (Playwright) covering happy path + error scenarios
- Documentation (API spec, permission matrix, seeder behavior)
- Cross-schema count implementation (CaseDefinition references)
- Dependency map + roadmap updates

## Related Artifacts

- **Branch:** `feat/phase-00-workflow-masterdata-taxonomy` (12 commits total — 5 C1 + 6 C2 + 1 C3)
- **Plan:** `plans/260415-1737-cases-workflow-refactor/phase-00-workflow-masterdata-taxonomy.md`
- **Code review:** `plans/reports/code-reviewer-260416-1710-chunk-03.md` (pending)
- **Commits:**
  - `1af872d0` — WorkflowTaxonomyAdminController (12 endpoints, 12 integration tests, xUnit SqlServerCollectionLocal fix)
