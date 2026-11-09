# Code Standards

## File & Namespace Naming

| Artifact | Convention | Example |
|---|---|---|
| C# files | PascalCase | `CreateWorkflowInstanceCommandHandler.cs` |
| Namespaces | Match folder structure | `AqtCoreFW.Workflow.Application.Commands.CreateInstance` |
| Interfaces | `I` prefix | `IWorkflowInstanceRepository`, `ICurrentUser` |
| Enums | PascalCase singular | `WorkflowInstanceStatus`, `ClassificationLevel` |
| Test files | `{Subject}Tests.cs` | `WorkflowStateMachineTests.cs` |

Namespace root per module: `AqtCoreFW.{ModuleName}.{Layer}`.

---

## CQRS Patterns

### Command naming
```
CreateWorkflowInstanceCommand  → CreateWorkflowInstanceCommandHandler  → CreateWorkflowInstanceCommandValidator
SubmitWorkflowInstanceCommand  → SubmitWorkflowInstanceCommandHandler
ApproveWorkflowInstanceCommand → ApproveWorkflowInstanceCommandHandler
```

### Query naming
```
ListWorkflowInstancesQuery     → ListWorkflowInstancesQueryHandler
GetWorkflowInstanceByIdQuery   → GetWorkflowInstanceByIdQueryHandler
```

### Command record pattern
```csharp
// Commands are immutable records — no setters
public sealed record CreateCaseCommand(
    Guid TenantId,
    string Title,
    string Description,
    CaseType Type,
    CasePriority Priority) : ICommand<CaseDto>;

// ICommand<T> is shorthand for IRequest<Result<T>>
// IQuery<T> is shorthand for IRequest<Result<T>> (read-side)
```

### Command Audit Category Decoration (MANDATORY for QĐ 742)
Every concrete `IBaseCommand` (or `ICommand<T>` inheriting it) **MUST** be decorated with `[AuditCategory(AuditCategory.X)]` from `AqtCoreFW.SharedKernel.Compliance`. This enables audit category mapping via reflection in `AuditBehavior`. Enforced by CI guard `AuditCategoryAttributeCoverageTests`.

```csharp
[AuditCategory(AuditCategory.CaseManagement)]
public sealed record CreateCaseCommand(...) : ICommand<CaseDto>;

[AuditCategory(AuditCategory.SecurityPolicy)]
public sealed record ApplyMfaPolicyCommand(...) : ICommand;
```

### Handler pattern
```csharp
public sealed class CreateCaseCommandHandler(
    ICaseRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<CreateCaseCommand, Result<CaseDto>>
{
    public async Task<Result<CaseDto>> Handle(
        CreateCaseCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Domain logic via aggregate factory/methods
        // 2. Persist via repository
        // 3. Return Result.Ok(dto) or Result.Fail(error)
    }
}
```

### Validator pattern
```csharp
public sealed class CreateCaseCommandValidator : AbstractValidator<CreateCaseCommand>
{
    public CreateCaseCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tiêu đề không được để trống.")
            .MaximumLength(500);

        RuleFor(x => x.TenantId)
            .NotEmpty();
    }
}
```

All validation messages must have a Vietnamese version (`WithMessage`).

---

## Thin Controllers (MANDATORY — enforced by NetArchTest)

Controllers are **HTTP adapters only**. They MUST NOT contain query/command logic.

### Rule
A controller may only depend on:
- `MediatR.ISender` (mediator) — to dispatch commands/queries
- `ApiControllerBase` helpers — `ResolveTenantId()`, `ResolveUserId()`, `ToApiResponse(result)`
- ASP.NET Core attributes — `[Authorize]`, `[EnableRateLimiting]`, `[ProducesResponseType]`, etc.
- Auth/identity services for `[AllowAnonymous]` endpoints (e.g., `IFeatureFlagService` sync L0 read)

A controller MUST NOT inject:
- ❌ `Dapper` / `IReadDbConnection` (`AqtCoreFW.SharedKernel.Application.Data`)
- ❌ `DbContext` / `Microsoft.EntityFrameworkCore`
- ❌ Cross-module clients (e.g., `IIdentityModuleClient`) — enrichment belongs in handlers
- ❌ Repositories or domain services

### Why
1. **Pipeline integrity** — direct DB access bypasses MediatR behaviors (`ValidationBehavior`, `TenantAwareBehavior`, `LoggingBehavior`)
2. **Layer purity** — cross-module enrichment from Presentation forces wrong project references
3. **Testability** — handler logic testable via fast unit tests; controller logic only via slow integration tests

### Pattern
```csharp
// ✅ Thin controller
[Route("api/v1/users/search")]
[Authorize]
public sealed class UserSearchController(ISender mediator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct)
    {
        var result = await mediator.Send(
            new SearchUsersQuery(q, 20, ResolveTenantId(), ResolveUserId()), ct);
        return ToApiResponse(result);
    }
}
```

### Enforcement
`tests/architecture/AqtCoreFW.Architecture.Tests/ThinControllerTests.cs` — 3 NetArchTest rules iterate **every module's Presentation assembly** and fail the build if any controller references Dapper, `IReadDbConnection`, or `Microsoft.EntityFrameworkCore`. Adding a new controller that violates this WILL break CI.

### Migration recipe (when violation found)
1. Move query/command record to `{Module}.Application/Queries|Commands/{Verb}{Entity}/{Verb}{Entity}Query.cs` (record implements `IQuery<T>` / `ICommand<T>` / `ICommand`)
2. Place handler in `{Module}.Infrastructure/QueryHandlers|CommandHandlers/{Verb}{Entity}QueryHandler.cs` (Infrastructure may reference DbContext — that's the layer that knows EF)
3. Return `Result<T>` from FluentResults; use typed errors `ConflictError` / `NotFoundError` / `ValidationError` from `AqtCoreFW.SharedKernel.Errors`
4. Controller becomes `(ISender mediator) : ApiControllerBase` and calls `mediator.Send(query, ct)` → `ToApiResponse(result)`
5. For cross-module enrichment, handler injects `I{Module}ModuleClient` (Layer 2 pattern — see "Cross-Module Data Access" below)

### Authorization Policy Gates (v2.137p92 — 4-A-A PERMANENT INVARIANT)

**MANDATORY:** Class-level `[Authorize]` attribute **without an explicit `Policy =` parameter is NOT a security gate.** It only verifies the presence of a valid authentication token (user is logged in), not permissions.

**Pattern violation (IDOR risk):**
```csharp
// ❌ WRONG — any authenticated user passes
[Route("api/v1/workflow/instances")]
[Authorize]  // <-- naked [Authorize], no Policy parameter
public sealed class WorkflowInstancesController(ISender mediator) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateWorkflowInstanceRequest req, CancellationToken ct)
    {
        // Any authenticated user can create instances
        var result = await mediator.Send(...);
        return ToApiResponse(result);
    }
}
```

**Correct pattern:**
```csharp
// ✅ RIGHT — explicit policy gate
[Route("api/v1/workflow/instances")]
[Authorize(Policy = PermCodes.Workflow.Instance.Manage)]  // <-- explicit policy
public sealed class WorkflowInstancesController(ISender mediator) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateWorkflowInstanceRequest req, CancellationToken ct)
    {
        // Only users with WORKFLOW.INSTANCE.MANAGE permission can create instances
        var result = await mediator.Send(...);
        return ToApiResponse(result);
    }
}
```

**Enforcement:** When adding or modifying controller actions, **grep the file for naked `[Authorize]`** (without `Policy =` parameter) and add explicit policy gates. Routine authorization validation caught a live IDOR class in Phase 4-A (WorkflowInstancesController + sibling controllers) missed in 5+ days of prior reviews. See v2.137p92 changelog for closure details (8 routes hardened across 3 controllers, +1 PermCode).

**Workflow.Instance.Manage admin-only by design (FE-FU close-note, v2.137p92):** `POST /api/v1/workflow/instances` is gated on `WORKFLOW.INSTANCE.MANAGE` (PermCode #129). This PermCode is intentionally **NOT** granted to `GovOfficer` or any BTC business role via seeder — instance creation is an admin-tier operation. SystemAdmin/Admin auto-grant via `RolePermissionSeeder`. If a future business case requires non-admin instance creation, grant explicitly via admin UI; do NOT amend seeders without product-owner sign-off (YAGNI, FE-FU 260522 wontfix per scout `WorkflowInstancesController.cs:41-42`).

### Architecture Test Suppressions (Phase-07 PR-D1, refined PR-D2)

When an architecture test violation is intentional (e.g., T3 entity holding a T2 FK by design), suppress it with an inline comment directive:

```csharp
// Directive on same line as violation (trailing comment) OR preceding line (pure comment)
// arch:suppress <RuleCode> <Entity>: <reason ≥3 chars>

// Example:
long FileRecordId, // arch:suppress R_T2FK FileRecord: T3 cross-aggregate FK by design (Phase-4b B3b)

// Or preceding line:
// arch:suppress R_T2FK FileRecord: T3 cross-aggregate FK by design
long FileRecordId,
```

**Grammar:** `<RuleCode>` = rule identifier (e.g., `R_T2FK`), `<Entity>` = entity name, `<reason>` ≥3 non-whitespace characters.

**Precision (PR-D2 M2):** `SourceCodeScanner.SuppressDirectiveRegex` tightened with `(?<!/)//` negative lookbehind — rejects `///` doc-comment lines and `////` decorative lines, accepts only pure `//` comment-line or trailing `//` comment. This prevents false positives from documentation comments.

**Enforcement:** `tests/architecture/AqtCoreFW.Architecture.Tests/Helpers/SourceCodeScannerSuppressTests.cs` (unit tests verify directive parsing, cases 7/8/9 cover doc-comment rejection, spacing, trailing-comment edge cases). Only use when there is genuine architectural reason; over-suppression defeats guard value.

### External Long-FK Fields — ACCEPT-LONG Documentation (Phase-07 PR-C, 22 surfaces)

**Context:** Some T1 entities carry long FK columns that leak to external-facing DTOs/commands due to architectural constraints (e.g., WorkflowTransition, WorkflowState, FormField are T1 but deeply embedded in FE DTOs). Rather than retier to T2 (which would impact database width + column naming), we document the field as an accepted long via inline comment.

**When to use:** When a T1 entity's long FK field **intentionally appears** in public API surface (DTO, command, query response) due to cross-module embedding, document with:

```csharp
// arch:accept-long T1 <Entity>: <reason ≥3 chars>

// Example:
long WorkflowTransitionId, // arch:accept-long T1 WorkflowTransition: FE Workflow Designer embeds Transition Ids in batch-execute request
```

**Grammar:** Directive on same line (trailing comment) OR preceding line (pure comment). Syntax identical to `arch:suppress` but semantics: "this long field is intentionally external-facing."

**Known surfaces (Phase-07 PR-C, 22 total):**
- **WorkflowTransition (7):** `WorkflowTransitionDto.Id`, `ExecuteTransitionCommand.TransitionId`, `BatchTransitionItem.TransitionId`, `BatchTransitionRequestItem.TransitionId`, `ExecuteTransitionRequest.TransitionId`, `BatchTransitionResult.TransitionId`, `WorkflowTransitionWebhookPayload.FromStateId/ToStateId`
- **WorkflowState (6):** `WorkflowStateDto.Id`, `StateDistributionDto.StateId`, `StateProcessingTimeDto.StateId`, `BottleneckDto.StateId`, `UnmappedStateDto.StateId`, `WorkflowStateDto.AutoTransitionOnTimeoutId`
- **FormField (9):** `FormTemplatesController` 4 routes, `ReorderFormFieldCommand.FieldId`, `UpdateFormFieldCommand.FieldId`, `DeactivateFormFieldCommand.FieldId`, `FieldOrderItem.FieldId`, `GetFormFieldOptionsQuery.FieldId`

**Distinction from suppress:**
- `arch:suppress` = rule violation allowed (exception to guard, rare, needs justification) — **parsed and enforced** by `SourceCodeScanner` (consumed by rules such as `R_T2FK`)
- `arch:accept-long` = field is intentionally external, documented for FE contract clarity (informational, common for T1 embedded in T2 aggregates) — **not parsed** by `SourceCodeScanner`; purely documentary. Do **not** rewrite these to `arch:suppress <code> …`: no T1-scanning rule exists, so doing so would introduce a dangling rule code that falsely implies enforcement. (Resolves backlog `PHASE7-FE-CLASSIFY-FU1` — by design, no scanner change.)

---

## Domain Entity Conventions

### Base classes
```
Entity<TId>                    → Id property, equality by Id
AuditableEntity<TId>           → + CreatedBy, ModifiedBy, ClassificationLevel
AggregateRoot : AuditableEntity → + DomainEvents collection
```

### Aggregate factory method pattern (no public constructors)
```csharp
public sealed class Case : AuditableEntity<Guid>, IAggregateRoot
{
    private Case() { }  // EF Core only

    public static Case Create(/* params */) { ... }   // factory
}
```

### State transitions — always via WorkflowEngine
```csharp
public void Submit(Guid actorId)
{
    var result = CaseWorkflow.Engine.Execute(Status, CaseAction.Submit);
    if (result.IsFailed)
        throw new InvalidCaseTransitionException(Status, CaseAction.Submit);
    Status = result.Value;
    SetAuditUpdate(actorId);
    AddDomainEvent(new CaseSubmittedEvent(Id, TenantId, actorId));
}
```

### DataClassification Attribute Pattern
Tag PII-containing properties with `[DataClassification]` to enforce governance:

```csharp
public sealed class ApplicationUser : AuditableEntity<Guid>
{
    [DataClassification(DataClassificationLevel.Confidential)]
    public string Email { get; set; }

    [DataClassification(DataClassificationLevel.Restricted)]
    public string PhoneNumber { get; set; }

    [DataClassification(DataClassificationLevel.Internal)]
    public string FullName { get; set; }
}
```

**Levels:** Public (default) → Internal (employees only) → Confidential (staff + subject) → Restricted (audit-only)
- Governs RLS row-level encryption, audit trail detail level, data retention policies
- Audit module logs access to Restricted properties
- RTBF erasure respects classification (Restricted fields purged first)

**Phase-04 Enhancement:** Architecture guard `EncryptedFieldArchitectureGuardTests` (D11.5) validates that all properties tagged `[DataClassification(Restricted)]` MUST have the `EncryptedStringConverter` applied OR explicitly opt out via `[DoNotEncryptAtRestAttribute]` with documented reason.

### DoNotEncryptAtRest Attribute Pattern

Use `[DoNotEncryptAtRest]` to explicitly opt out of encryption for security-justified fields (typically cryptographic hashes or tokens that verify-only):

```csharp
public sealed class ApplicationUser : AuditableEntity<Guid>
{
    [DataClassification(DataClassificationLevel.Restricted)]
    public string RecoveryCodesHash { get; set; }  // BCrypt verify-only, plaintext comparison fails on encryption
    
    [DoNotEncryptAtRest("Hash verification requires plaintext compare; use BCrypt constant-time verify")]
    public string PasswordHash { get; set; }  // ASP.NET Identity convention; plaintext unsafe but BCrypt handles equality
}
```

**Valid Use Cases:**
- BCrypt/Scrypt hashes (verify requires plaintext)
- JWT tokens (value is proof-of-auth, not PII)
- Symmetric cipher nonces (stored alongside ciphertext)
- Computed checksums (verification requires plaintext)

**Invalid Excuses:**
- "Performance" — Encryption perf acceptable for PII (Phase-06 benchmarks: <1ms decrypt)
- "Legacy systems" — Use `PlaintextToEncryptedUpgrader` for backfill
- "Third-party library" — Wrap or patch; don't skip encryption

**Enforcement:** Architecture test `EncryptedFieldArchitectureGuardTests` scans all `[DataClassification(Restricted)]` properties; fails build if missing converter AND missing `[DoNotEncryptAtRest]` attribute.

---

## Repository Pattern

### Generic base (SharedKernel)
```csharp
public interface IRepository<T, TId> where T : class
{
    Task<Result<T>> GetByIdAsync(TId id, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(TId id, CancellationToken ct = default);  // soft-delete
}
```

### Module-specific extension
```csharp
public interface ICaseRepository : IRepository<Case, Guid>
{
    Task<Case?> GetWithCommentsAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<int> GetNextYearSequenceAsync(Guid tenantId, int year, CancellationToken ct = default);
}
```

**Read-side:** bypass repository, use `IReadDbConnection` (Dapper) directly in query handlers.

### Cross-Module Atomic Writes — Unit-of-Work Pattern

**MANDATORY for cross-module command handlers that write to 2+ module DbContexts:**

Multi-module transactions must NOT use `System.Transactions.TransactionScope` — it promotes to Distributed Transaction Coordinator (DTC) on .NET 10, which fails on Linux/Docker (no MSDTC). Use `ICrossModuleUnitOfWork` instead:

**Pattern:**
```csharp
[AuditCategory(AuditCategory.BusinessProcess)]
public sealed class ExecuteBusinessProcessTransitionCommand(...) : ICommand<Result>;

public sealed class ExecuteBusinessProcessTransitionCommandHandler(
    IBusinessProcessRepository bpRepo,
    IWorkflowModuleClient workflowClient,
    ICrossModuleUnitOfWork? uow)  // Scoped; null if no cross-module writes needed
    : IRequestHandler<ExecuteBusinessProcessTransitionCommand, Result>
{
    public async Task<Result> Handle(ExecuteBusinessProcessTransitionCommand request, CancellationToken ct)
    {
        if (uow is not null)
        {
            await uow.BeginAsync(request.TenantId, ct);  // Single shared SqlConnection + SqlTransaction
            try
            {
                var bp = await bpRepo.GetByIdAsync(request.Id, ct);
                var wfResult = await workflowClient.ExecuteTransitionAsync(..., ct);  // Uses shared connection
                
                if (wfResult.IsFailed)
                    return wfResult;
                    
                bp.RecordTransition(...);
                await bpRepo.UpdateAsync(bp, ct);  // Enlists on shared transaction
                
                await uow.CommitAsync(ct);  // Single commit across both modules
                return Result.Ok();
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync(ct);
                return Result.Fail(/* ... */);
            }
        }
        else
        {
            // Single-module path (legacy, no UoW needed)
            var bp = await bpRepo.GetByIdAsync(request.Id, ct);
            // ...
        }
    }
}
```

**Key invariants:**
- `ICrossModuleUnitOfWork` is **scoped** (one per request)
- Inject as **nullable** (`ICrossModuleUnitOfWork?`) — handlers must support both cross-module (UoW provided) and single-module (UoW null) paths
- `BeginAsync(tenantId, ct)` sets `sp_set_session_context N'TenantId'` once (RLS enforcement)
- All participating DbContexts enlist via `SetDbConnection() + UseTransaction()` (done by repository/client auto-enlistment)
- All Dapper reads via `DapperReadDbConnection` use shared connection when UoW active
- `MARS=true` on connection string is **load-bearing** — supports concurrent reads + writes on shared connection
- Always wrap in try/catch → `uow.RollbackAsync()` on failure (fail-safe)

**Don't use:** `System.Transactions.TransactionScope` for cross-module writes.

**Don't use:** Separate `SqlConnection` instances in ambient transaction context (promotes to DTC).

### Dapper DTO Type Convention (Phase-04a RT, PR #75)

When materializing rows via Dapper, **use `sealed class` (not positional record)**. Positional records with nullable parameters (`Guid?`, `string?`) lack the parameterless constructor Dapper requires — nullable fields silently fail during materialization. Use `init`-only properties instead:

```csharp
// ✓ CORRECT: class with init-only props
private sealed class UserRow
{
    public Guid Id { get; init; }
    public Guid? PrimaryOrgUnitId { get; init; }  
    public string? PositionName { get; init; }
}

// ✗ WRONG: positional record fails on null fields
private sealed record UserRow(Guid Id, Guid? PrimaryOrgUnitId, string? PositionName);
```

Reference: `BtcIdentitySeeder.OrgUnitRow` (Phase-04a precedent), `BtcAssignmentSeeder.UserRow` (PR #75 RT-3 fix).

### Dapper DTO Type Alignment (Phase-07 B4, commit `b6df1840` 2026-05-09)

When projecting rows via Dapper, **DTO property types MUST match SQL column definitions** — mismatches cause silent NULL failures or runtime conversion errors.

**Rule 1: DateTimeOffset ↔ datetime2 Type Mismatch**
```csharp
// ✗ WRONG: Entity has DateTime, EF column datetime2, DTO has DateTimeOffset
public sealed class PartnerDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }  // FAILS: SQL datetime2 ≠ DateTimeOffset
}

// ✓ CORRECT: Match DTO type to Entity + SQL column type
public sealed class PartnerDto
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }  // Matches Entity.CreatedAt DateTime + column datetime2
}
```

**Rule 2: Enum ↔ String Column with HasConversion**
When an enum column is stored as string via `HasConversion<string>()`, pass enum value as `.ToString()`, not `(int)enum`:

```csharp
// ✗ WRONG: Dapper interprets @status as int, but column is NVARCHAR(50)
var status = RtbfRequestStatus.Rejected;
connection.Query(
    "SELECT * FROM audit.RtbfRequests WHERE @StatusFilter = CASE ... CAST(Status AS INT) ...", 
    new { StatusFilter = (int)status }  // FAILS: "Conversion failed converting 'Rejected' to int"
);

// ✓ CORRECT: Pass enum as string to match HasConversion<string>()
var status = RtbfRequestStatus.Rejected;
connection.Query(
    "SELECT Status FROM audit.RtbfRequests WHERE Status = @status", 
    new { status = status.ToString() }  // "Rejected" matches NVARCHAR column
);
```

**Rule 3: NULL Type Inference for Optional Fields**
When materializing optional/conditional fields as NULL, use `CAST(NULL AS <type>)` to force Dapper type inference:

```csharp
// ✗ WRONG: Dapper infers NULL AS INT (SQL Server default) instead of datetime2
connection.Query<MessageLogDto>(
    "SELECT Id, MessageId, Status, NULL AS UpdatedAt FROM integration.MessageLogs"
);
// Result: UpdatedAt property gets int NULL, expects DateTime → type mismatch

// ✓ CORRECT: Force datetime2 inference
connection.Query<MessageLogDto>(
    "SELECT Id, MessageId, Status, CAST(NULL AS datetime2) AS UpdatedAt FROM integration.MessageLogs"
);
// Result: UpdatedAt NULL correctly typed as DateTime?
```

**Reference:** Commit `b6df1840` (2026-05-09) fixed 3 occurrences in Integration/Audit modules. Also see memory feedback `feedback-dapper-no-positional-records.md` (4th codebase occurrence).

### T2 Tier — Repository Naming Convention (Phase-4b, PR-C precedent 2026-05-07)

T2 entities (`Entity<long>` + `IPublicIdentified` Guid AK) MUST use the following accessor shapes on their domain repository interfaces. This pins the convention — any drift in spelling (e.g. `GetByIdInternalAsync` vs `GetByInternalIdAsync`) MUST follow the canonical form below, which is enforced by 28+ callsites across `identity` / `integration` / `reporting` / `collaboration` / `notifications` / `ai` / `rules` modules.

**Canonical accessor set:**

```csharp
public interface IFooRepository
{
    /// External addressing — controllers, command/query handlers reaching from HTTP boundary.
    /// MUST be the only `GetById*Async`-shaped method exposed on the controller surface.
    Task<Foo?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    /// Internal addressing — Application-layer pre-resolve when a long FK is already in hand
    /// (e.g. another aggregate's FK column). MUST NOT be invoked from controllers.
    Task<Foo?> GetByInternalIdAsync(long internalId, CancellationToken ct = default);

    /// Lightweight projection — PR-D2 pattern. Resolves PublicId from a long FK without
    /// loading the full aggregate. Use when the only thing needed is the external Guid
    /// for an outbound DTO / domain event.
    Task<Guid?> GetPublicIdByInternalIdAsync(long internalId, /* tenantId */ Guid tenantId, CancellationToken ct = default);
}
```

**External-accessor variants (both patterns acceptable, enforced by `R_T2_PUBLICID_REQUIRED`):**

| Pattern | Shape | When |
|---------|-------|------|
| A (preferred) | Explicit `Task<T?> GetByPublicIdAsync(Guid publicId, ...)` | Module needs additional read-side methods alongside the external lookup; aligns naming with `GetByInternalIdAsync` sibling. |
| B (acceptable) | Inherit `IRepository<T, Guid>` or `IRepository<T, GuidWrapperId>` (e.g. `FileId`) | Repo's external accessor is exactly the inherited `GetByIdAsync(TId)` and TId IS Guid-backed; no extra read methods needed. Examples: `IFileRepository`, `IFormSubmissionRepository`, `IWorkflowInstanceRepository`, `IWorkflowDefinitionRepository`. |

Both patterns satisfy the architectural invariant ("T2 repos MUST expose an external Guid lookup"). The positive-coverage rule fires only when **neither** pattern is present.

**Banned shapes (architecture-test enforced):**

| Banned | Rule | Reason |
|--------|------|--------|
| Single-overload `GetByIdAsync(long)` on T2 repo | `R_T2_REPO_SIG` | Ambiguous w.r.t. external/internal addressing; bypasses PublicId AK contract. |
| Controller calling `*InternalAsync` method | `R_INTERNAL_CTRL` | Leaks long internal Id past HTTP external boundary. |
| Returning T2 entity by long Id from a DTO mapper | `R_T2FK` (SourceLevelRulesTests) | Same boundary leak vector. |

**Exemptions:**
- ASP.NET Identity Guid-PK satellites (`ApplicationUser` + 7 EF stock identity tables) — listed in `IdentifierTierShapeRulesTests.AspNetIdentityGuidPkExceptions`.
- T1 entities (`Entity<long>`, no `IPublicIdentified`) — repos may use any shape; long Id is the canonical key.
- Aggregate-owned child entities accessed only via root — need not declare standalone repo.

**Reference tests:**
- `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/TierRepoArchitectureRulesTests.cs` — `R_INTERNAL_CTRL` + `R_T2_REPO_SIG`.
- `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/SourceLevelRulesTests.cs` — `R_T2FK` (DTO leak ban).

**Suppress directive (per-line escape hatch):**
```csharp
// arch:suppress R_INTERNAL_CTRL <MethodName>: <reason ≥3 chars>
```

### IgnoreQueryFilters Re-Query Pattern (4-C-A PERMANENT INVARIANT, v2.137p88)

**MANDATORY:** Production repository re-queries by primary key from catch blocks (defensive PK-by-Id reads to discriminate exception classes) MUST use `.IgnoreQueryFilters().AsNoTracking()` as defense-in-depth against EF model-cache contamination.

**Background:** `Expression.Constant(tenantContext)` in `ApplyGlobalFilters` captures the FIRST `ITenantContext` instance process-wide into the cached query filter. Subsequent `DbContext` instances using the same `DbContextOptions<T>` inherit a broken filter referencing stale mock state. Symptom: `WorkflowTasks.FirstOrDefaultAsync(t => t.Id == id)` returns null while raw SQL + `IgnoreQueryFilters()` find the row with matching `TenantId`.

**Pattern violation:**
```csharp
// ❌ WRONG — global filter may reference stale tenant context
catch (DbUpdateConcurrencyException)
{
    var current = await Ctx.WorkflowTasks
        .FirstOrDefaultAsync(t => t.Id == task.Id, ct);
    // ... discriminate Done/Cancelled vs Active
}
```

**Correct pattern:**
```csharp
// ✅ RIGHT — bypass cached filter; tenant predicate intentionally omitted because the row
// was tracked under the caller's tenant (PK alone is sufficient + robust against drift).
catch (DbUpdateConcurrencyException)
{
    var current = await Ctx.WorkflowTasks
        .IgnoreQueryFilters()
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == task.Id, ct);
}
```

**When to apply:**
- Catch-block PK re-queries that discriminate exception classes (e.g., RowVersion conflict vs already-finalized).
- Integration test fixtures sharing `DbContextOptions<T>` across test classes — apply via `IModelCacheKeyFactory` discriminator OR consistent mock behavior to avoid cache pollution.

**When NOT to apply:** Normal forward-path reads where the global filter is part of the contract (e.g., list endpoints scoped to tenant). Only catch-block defensive PK reads need this escape.

**Origin:** Phase 4-C M1 closure (`WorkflowTaskRepository.cs:140-143`; rationale in adjacent comment `:130-139`). EF model-cache contamination diagnosed via raw SQL → `IgnoreQueryFilters` → plain EF probe bisection (~10min after probe framework setup). Documented as INVARIANT 4 per orchestrator memory.

---

## Host Smoke Tests — Program Namespace Pattern (v2.137, 2026-05-09)

When a codebase has multiple host programs (API, AuthServer, future Gateway), each running as a separate application entry point, the root `Program.cs` must be wrapped in a **named namespace** to avoid test fixture type collision.

**Pattern:**
```csharp
// src/host/AqtCoreFW.Api/Program.cs
namespace AqtCoreFW.Api;

public partial class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // ... configuration
        var app = builder.Build();
        await app.RunAsync();
    }
}

// src/host/AqtCoreFW.AuthServer/Program.cs
namespace AqtCoreFW.AuthServer;

public partial class Program
{
    static async Task Main(string[] args)
    {
        // ...
    }
}
```

**Rationale:** Test fixtures use `WebApplicationFactory<Program>` generics. Without explicit namespaces, both hosts' top-level `Program` classes collide in test assembly namespace resolution. Named namespace (e.g. `AqtCoreFW.Api.Program`, `AqtCoreFW.AuthServer.Program`) makes type FQN explicit, allowing both to be tested in the same fixture file without ambiguity.

**Test Fixture Usage (post-FU4 2026-05-09 — ApplicationPartManager-based):**
```csharp
// tests/integration/AqtCoreFW.Startup.SmokeTests/HostDiArchGuard.cs
public static class HostDiArchGuard
{
    /// Enumerate every controller via ApplicationPartManager + ControllerFeatureProvider —
    /// covers host-local AND every module Presentation assembly auto-discovered via DependencyContext.
    public static IReadOnlyList<Type> EnumerateAllControllers(IServiceProvider sp)
    {
        var partManager = sp.GetRequiredService<ApplicationPartManager>();
        var feature = new ControllerFeature();
        partManager.PopulateFeature(feature);
        return feature.Controllers.Cast<Type>().ToList();
    }

    /// Assert each controller resolves via ActivatorUtilities; returns failure list.
    public static IReadOnlyList<string> TryResolveControllers(
        IServiceProvider sp, IReadOnlyList<Type> controllerTypes) { /* ... */ }
}

// Test usage in SmokeTests project:
[Fact, Trait("Category","Smoke")]
public void Api_AllControllers_AreConstructableViaDi()
{
    _fixture.CreateClient();           // triggers host build
    using var scope = _fixture.Services.CreateScope();
    var controllers = HostDiArchGuard.EnumerateAllControllers(scope.ServiceProvider);
    var failures   = HostDiArchGuard.TryResolveControllers(scope.ServiceProvider, controllers);
    failures.Should().BeEmpty();
}
```

**FU4 rationale:** Pre-FU4 helper enumerated only the host assembly via `Assembly.GetTypes()`; module controllers were validated only transitively when a host-local controller resolved a cross-module dep. Post-FU4 walks the canonical ASP.NET `ApplicationPartManager + ControllerFeatureProvider` — same set MVC routes against — so module controllers (auto-discovered via project references) are directly DI-validated.

**When to Use:** Any new host program (Gateway, Batch Service, etc.) should follow this pattern. Extract `HostDiArchGuard` to SharedFixtures when 3rd consumer arrives (YAGNI: currently 2 consumers, helper lives in SmokeTests project).

---

## Enum Contracts & JSON Serialization

All shared enums between frontend and backend must:
1. Use **PascalCase** field names in C# (e.g., `PendingApproval`)
2. Serialize to **SCREAMING_SNAKE_CASE** in JSON responses (e.g., `"PENDING_APPROVAL"`)
3. Be aligned across frontend/backend with same values

**Aligned Enums:**
- `CaseStatus` — DRAFT, SUBMITTED, PENDING_APPROVAL, APPROVED, CLOSED, REJECTED
- `FormFieldType` — TEXT, TEXTAREA, DROPDOWN, CHECKBOX, DATE, FILE, SIGNATURE, NUMBER
- `FormTemplateStatus` — DRAFT, PUBLISHED, ARCHIVED, RETIRED
- `ChatRole` — SYSTEM, USER, ASSISTANT

**JsonStringEnumConverter Configuration:**
Add to API `Program.cs` dependency injection:
```csharp
services
    .AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(new SnakeCaseNamingPolicy(), allowIntegerValues: false));
    });
```

This ensures all enum properties serialize to snake_case in OpenAPI/responses. Frontend orval SDK auto-generates matching TypeScript enums.

---

## Error Handling

### Error code format: `GOV_{MODULE}_{NNN}`
```
GOV_CASE_001   Case not found
GOV_CASE_002   Invalid state transition
GOV_IDN_001    User not found
GOV_IDN_002    Invalid credentials
GOV_FIL_001    File too large
GOV_FIL_002    Virus detected
GOV_LIMIT_001  Rate limit exceeded (authenticated)
GOV_LIMIT_002  Rate limit exceeded (anonymous)
```

### Rate Limiting

ASP.NET Core built-in rate limiter via `AddGovRateLimiter()` in `InfrastructureRegistration`.

**Named Policies — use `[EnableRateLimiting("policy-name")]` on controllers/actions:**

| Policy | Prod Limit | Dev Limit | Use Case |
|--------|-----------|-----------|----------|
| `read-ops` | 60/min per IP | 600/min | Search, list, lookup endpoints |
| `write-ops` | 20/min per IP | 200/min | POST/PUT/DELETE mutations |
| `public-form-submit` | 5/min per IP | 50/min | Public form submission (spam prevention) |
| `mfa-verify` | 5/min per client | 50/min | TOTP brute-force prevention |
| `anonymous` | 60/min per IP | 600/min | Unauthenticated public endpoints |
| `authenticated` | 600/min per userId | 600/min | High-throughput authenticated APIs |
| `gateway` | 100/min per IP | 100/min | YARP reverse-proxy route config |

**Global limiter** (always active, no attribute needed):
- Authenticated: 100/min (dev: 1000/min) per userId
- Anonymous: 20/min (dev: 200/min) per IP

**When to use which:**
- `read-ops` → GET endpoints that accept user search input (user search, autocomplete)
- `write-ops` → Any state-changing endpoint (create, update, delete)
- `public-form-submit` → Public-facing form submission (no auth required)
- `mfa-verify` → Security-sensitive verification endpoints
- No attribute → Global limiter applies by default

**Response:** 429 Too Many Requests + `Retry-After` header + `GOV_SYS_005` error code.

**Implementation:**
```csharp
// Controller-level
[EnableRateLimiting("read-ops")]
public class UserSearchController : ApiControllerBase { }

// Action-level
[HttpPost]
[EnableRateLimiting("write-ops")]
public async Task<IActionResult> Create(...) { }
```

**Config:** `src/shared/AqtCoreFW.Infrastructure/RateLimiting/RateLimiterConfiguration.cs`

**Frontend Handling:**
- Check `Retry-After` header on 429 response
- Display user message: "Too many requests. Please try again in X seconds."
- Implement exponential backoff for retry logic

### FluentResults + typed errors
```csharp
// Define typed errors in SharedKernel.Errors
public sealed class NotFoundError(string message) : Error(message);
public sealed class ForbiddenError(string message) : Error(message);
public sealed class ConflictError(string message) : Error(message);
public sealed class ValidationError(string message) : Error(message);
```

### Return from handler
```csharp
if (@case is null)
    return Result.Fail(new NotFoundError("GOV_CASE_001: Case not found."));

return Result.Ok(MapToDto(@case));
```

### ApiControllerBase maps to HTTP
```
Result.Ok(value)       → 200 OK      with ApiResponse<T>
NotFoundError          → 404
ForbiddenError         → 403
ConflictError          → 409
ValidationError        → 422
(unhandled)            → 500
```

### Pagination Clamping (ARCH-FU2)

**All query handlers that read `PageSize` from requests MUST defend against unbounded pagination.**

**Enforced by:** `PaginationClampScannerTests` — new handlers reading unclamped `request.PageSize` fail the build.

**Three approved patterns** (pick one):

1. **Use the centralized helper** (recommended):
```csharp
using AqtCoreFW.SharedKernel.Application.Pagination;

var (page, pageSize) = PaginationDefaults.Normalize(
    query.Page, 
    query.PageSize, 
    PaginationDefaults.AdminMax  // or PickerMax
);

// Now use page, pageSize safely in SQL offset/limit
```

2. **Inline Math.Clamp:**
```csharp
var clampedPageSize = Math.Clamp(query.PageSize, 1, 100);
var page = Math.Max(1, query.Page);
```

3. **FluentValidation rule on the query:**
```csharp
RuleFor(x => x.PageSize).InclusiveBetween(1, PaginationDefaults.AdminMax);
RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
```

**Tier Reference:**
- `AdminMax = 100` — Admin lists, audit logs, CRUD pages, search results
- `PickerMax = 1000` — FE picker/dropdown endpoints that bulk-load selectable items
- `DefaultPageSize = 20` — Default when caller omits PageSize

**Why:** Unclamped `PageSize` enables DoS attacks (e.g., `pageSize=999999`) that reach the database FETCH NEXT/LIMIT unchecked, causing memory pressure or long-running queries.

**Anti-pattern:** `Math.Min` caps the upper bound only; negative or zero `PageSize` silently flows to SQL OFFSET/FETCH (producing empty pages rather than failing gracefully). Always use `PaginationDefaults.Normalize()` (enforced by ARCH-FU2.1 `PaginationClampScanner`, validated across 5+ audit handlers in ARCH-FU2.2).

### Dual-Metadata Error Envelope (4-B-A PERMANENT INVARIANT, v2.137p87)

**MANDATORY:** When a typed Error subclass (`ConflictError`, `ValidationError`, `NotFoundError`, etc.) is constructed via a module-specific factory, the factory **MUST** set BOTH metadata keys: the generic `"ErrorCode"` (which `ApiResponse.Fail` reads into the response body) AND a provenance-specific `"<Module>ErrorCode"`. Base ctors do NOT auto-populate `"ErrorCode"`; if a factory only sets `"WorkflowErrorCode"`, the API silently falls back to `GOV_SYS_000` (System.Generic).

**Pattern violation:**
```csharp
// ❌ WRONG — only module-specific key set; ApiResponse maps to GOV_SYS_000
public static ConflictError AlreadyClaimed(long taskId) =>
    new ConflictError($"Task {taskId} already claimed")
        .WithMetadata("WorkflowErrorCode", "GOV_WFL_016");
```

**Correct pattern (factory at `WorkflowErrors.cs`):**
```csharp
// ✅ RIGHT — BOTH keys; body shows GOV_WFL_016, provenance preserved
public static ConflictError AlreadyClaimed(long taskId) =>
    new ConflictError($"Task {taskId} already claimed")
        .WithMetadata("ErrorCode", "GOV_WFL_016")
        .WithMetadata("WorkflowErrorCode", "GOV_WFL_016");
```

**Enforcement:** When adding a new error code, **anti-tautology test the code in body**:
```csharp
// Asserts NEW code appears in body, not generic fallback
result.Should().Be(409);
response.Errors[0].Code.Should().Be("GOV_WFL_016");  // not "GOV_SYS_000"
```

Future factories MUST grep `ApiResponse.Fail` usage + integration assertion of `Errors[0].Code`. See `WorkflowErrors.cs` (10 factories) + `BusinessProcessErrors.cs` for canonical examples. Origin: Phase 4-B/4-C code-reviewer caught `WorkflowErrors.NoHandlerResolved` missing `"ErrorCode"` key (silently mapped to `GOV_SYS_000` in 5b LOCAL tests until 4-B-A invariant fix).

---

## API Response Envelope

All endpoints return `ApiResponse<T>`:

```json
{
  "success": true,
  "data": { ... },
  "meta": { "page": 1, "pageSize": 20, "total": 150 },
  "errors": []
}
```

Error response:
```json
{
  "success": false,
  "data": null,
  "errors": [
    { "code": "GOV_CASE_001", "message": "Case not found.", "detail_vi": "Hồ sơ không tồn tại." }
  ]
}
```

### Structured Violation Metadata (`PublicMetadata`) Wire-Shape Contract (v2.137p117–p120)

Complements §Dual-Metadata Error Envelope (4-B-A) above: that section governs **error-code** propagation (`ErrorCode` + `<Module>ErrorCode` keys); this one governs the **structured violation payload** projected on graph-validation save/activate failures. **Call-sites:** Workflow `SaveDefinitionGraphCommand` (v2.137p117), `ExecuteTransitionCommand` (v2.137p117), BP `ActivateBusinessProcessCommand` (v2.137p120).

**Default-deny opt-in.** Domain errors may carry arbitrary internal metadata, but `ApiResponse.Fail()` lifts **only** the single reserved key `PublicMetadata` onto the wire (into `ApiError.Metadata`) — every other key stays server-side. A new public payload is therefore a deliberate `WithMetadata("PublicMetadata", …)` call, never an accidental leak.

**The cast is load-bearing.** The value MUST be cast inline to `IReadOnlyDictionary<string, object?>`:

```csharp
// WorkflowErrors.cs — PublicMetadata is the sole key ApiResponse.Fail() projects to the wire
error.WithMetadata("PublicMetadata", (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
{
    ["violations"] = violations,   // each: { rule, stateName, transitionActionName, detail, metadata }
});
```

`WithMetadata` has an invariant `TValue`; a wrong/missing cast compiles fine but serializes to **null on the wire** (silent failure). Enforced solution-wide by arch rule `SourceLevelRulesTests.Rule_PublicMetadata_WithMetadata_Value_MustCast_To_IReadOnlyDictionary` (scans every `src/**/*.cs` `WithMetadata("PublicMetadata", …)` call-site — no allowlist).

**Casing is split — do NOT assume one global policy.** Outer envelope props are camelCase (anon-type properties under the default `JsonNamingPolicy.CamelCase`), but the **inner dictionary keys stay PascalCase** because no `DictionaryKeyPolicy` is configured:

```jsonc
{
  "errors": [{
    "code": "GOV_WFL_xxx",
    "metadata": {                                 // camelCase (anon prop)
      "violations": [{
        "rule": "...", "stateName": "...",        // camelCase (anon props)
        "metadata": { "UnknownGroupCodes": [] }   // PascalCase (dict keys — no DictionaryKeyPolicy)
      }]
    }
  }]
}
```

A FE consumer that lower-cases the inner keys silently renders nothing. Integration tests `WorkflowByGroupProducerAndSaveValidationTests` (I4/I5) + the FE parser both assert this exact shape — change it intentionally or not at all.

**Swashbuckle emits the dict untyped → hand-write the FE type.** C# `IReadOnlyDictionary<string, object?>?` surfaces in OpenAPI as `{ type: [null, object] }` with no property schema, so orval generates an untyped `metadata`. The narrowed FE shape is hand-written at `web/src/core/api/api-error.ts` (`GraphViolation` interface + `extractViolations` defensive parser) and is required **regardless** of an orval regen — regen will never recover the property types.

---

## Security Patterns

### Tenant & User Identity Resolution (CRITICAL)
All controllers MUST resolve tenant and user ID from JWT claims only — never accept from query parameters, request body, or other untrusted sources.

**ApiControllerBase provides two protected methods:**

1. `ResolveTenantId()` — Extracts tenant from JWT. SystemAdmin can override via `X-Tenant-Id` header (cross-tenant debugging).
2. `ResolveUserId()` — Extracts user ID from JWT.

**Correct pattern:**
```csharp
[HttpPost("{id}/approve")]
public async Task<IActionResult> ApproveCase(Guid id)
{
    var tenantId = ResolveTenantId();  // From JWT
    var userId = ResolveUserId();      // From JWT

    var command = new ApproveCaseCommand(id, tenantId, userId);
    var result = await Mediator.Send(command);
    return ToApiResponse(result);
}
```

**NEVER do this:**
```csharp
// ✗ WRONG: tenantId from query param
[HttpPost("{id}/approve")]
public async Task<IActionResult> ApproveCase(Guid id, [FromQuery] Guid tenantId)

// ✗ WRONG: createdBy from request body
public record ApproveCaseRequest(Guid Id, Guid CreatedBy, string Reason);

// ✗ WRONG: reviewedBy from form field
[HttpPost("batch-review")]
public async Task<IActionResult> BatchReview([FromBody] BatchReviewRequest req) // req.ReviewedBy ← WRONG
```

**Removing security fields from DTOs:** If a DTO contains tenantId, userId, createdBy, updatedBy, reviewedBy, or similar identity fields, remove them and derive from JWT instead.

### Tenant-Privilege Convention — Two Tiers (CRITICAL)

Read-side handlers (especially Dapper queries that bypass EF global filters, on tables
with **no `TenantId` column and no RLS policy**) gate visibility with a "privilege" flag.
There are **two distinct operations** with **different role-sets**; do not conflate them.

| Tier | Operation | Canonical role-set | Flag source | Example |
|------|-----------|--------------------|-------------|---------|
| **1. Cross-tenant escape** | Bypass tenant isolation entirely — see rows of **every** tenant | **SystemAdmin only** | `ITenantContext.IsSystemAdmin` | `ListExternalIdentitiesQueryHandler`, `GetLoginAuditQueryHandler` |
| **2. Within-tenant ownership relaxation** | See **all rows in the caller's own tenant** instead of only rows they personally own — still tenant-bound | **Admin or SystemAdmin** | `User.IsInRole(Roles.SystemAdmin) \|\| User.IsInRole(Roles.Admin)` | `ListDelegationsQueryHandler` |

**Why the role-sets differ (this is intentional, not an inconsistency):**

- A tenant **Admin** must NOT see another tenant's federation links or login attempts —
  cross-tenant federation/login data is the most isolation-sensitive surface, so the escape
  hatch is SystemAdmin-only. A non-privileged caller (including a tenant Admin) is already
  bound to their **whole** tenant via the `EXISTS([identity].AspNetUsers u WHERE u.Id = …
  AND u.TenantId = @CallerTenantId)` predicate — they lose nothing by being non-privileged.
- For **per-row-owned** data (delegations are owned by individual delegator/delegate users),
  the relaxation answers a different question — "show me everyone's rows in my tenant, not
  just mine" — which a tenant Admin legitimately needs, and it stays tenant-bound regardless.

**Rule:** when adding a privilege gate, first decide *which tier* the operation is.
If it can leak rows across tenants → Tier 1, SystemAdmin-only. If it only widens visibility
within the caller's tenant → Tier 2, Admin-or-SystemAdmin. Never give Admin a Tier-1
cross-tenant escape without an explicit managed-operator business decision.

### Permission-Based Controller Authorization (REQUIRED for new controllers)

All controllers MUST use **permission-based authorization** via `[Authorize(Policy = PermCodes.X.Y.Z)]` constants — **never** use `[Authorize(Roles = "...")]` or custom role checks.

**PermCodes class hierarchy:**
```csharp
// Location: src/modules/identity/AqtCoreFW.Identity.Application/Authorization/PermCodes.cs
// Format: MODULE.RESOURCE.ACTION (e.g., "IDENTITY.USER.VIEW")
public static class PermCodes
{
    public static class Identity
    {
        public static class User
        {
            public const string View = "IDENTITY.USER.VIEW";
            public const string Create = "IDENTITY.USER.CREATE";
            public const string Update = "IDENTITY.USER.UPDATE";
            public const string Delete = "IDENTITY.USER.DELETE";
        }
        // ... 22 more resource classes
    }
    // ... Files, Audit, Workflow, Forms, Notif, Org, Master, AI, Collab, Integ, Report, Rules, Search, Sign, SysParam, System
}
```

**Controller pattern:**
```csharp
[Route("api/v1/admin/users")]
[Authorize(Policy = PermCodes.Identity.User.View)]  // Base policy for GET access
public sealed class UsersAdminController(ISender mediator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(...) { }  // Requires IDENTITY.USER.VIEW

    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.User.Create)]  // Override for CREATE
    public async Task<IActionResult> Create(...) { }

    [HttpPut("{id}")]
    [Authorize(Policy = PermCodes.Identity.User.Update)]  // Override for UPDATE
    public async Task<IActionResult> Update(Guid id, ...) { }

    [HttpDelete("{id}")]
    [Authorize(Policy = PermCodes.Identity.User.Delete)]  // Override for DELETE
    public async Task<IActionResult> Delete(Guid id) { }
}
```

**Key rules:**
1. **Use constants only** — Never hardcode "IDENTITY.USER.VIEW" strings; use `PermCodes.Identity.User.View`
2. **Controller-level default** — Place `[Authorize(Policy = ...)]` at class level for the most common action
3. **Action-level overrides** — Override with more restrictive or different policies on specific methods
4. **No role mixing** — Never combine permission policies with role checks; authorization is **purely permission-based**
5. **ABAC policies remain separate** — Attribute-based policies (DepartmentRestricted, ClassifiedAccess) work alongside permission checks

**Authorization handler:**
- Evaluates `PermissionRequirement` against user's effective permissions (direct roles + delegated roles + group memberships)
- Falls back to ABAC handlers after permission check passes
- Fails with 403 Forbidden if permission denied

**Adding new permissions:**
1. Define constant in `PermCodes.cs` nested class (e.g., `PermCodes.Workflow.Def.Manage = "WORKFLOW.DEF.MANAGE"`)
2. Add to `PermCodes.All` static array
3. Create seed entry in RolePermissions (assigns permission to roles)
4. Update controllers with `[Authorize(Policy = PermCodes.Workflow.Def.Manage)]`

### Middleware Pipeline Ordering (QĐ 742 Compliance §1.3.a / §1.4.b / §1.4.d)

**v2.133p20 Compliance Gates Pattern:** Middleware handling authentication security gates MUST execute in strict order to enforce compliance requirements before authorization. Default ASP.NET ordering (Auth→Authz) is insufficient; inserted compliance gates run BETWEEN.

**Canonical ordering (AuthServer + Api hosts):**
```csharp
// Program.cs middleware registration order (position index)
// 431  Authentication (OpenIddict)
// 436  PasswordExpiry (pre-compliance check) — v2.133p18 hotfix
// 440  FirstLoginPasswordChange (compliance gate §1.3.a) — v2.133p20 NEW
// 442  Authorization (RBAC + ABAC)
```

**Rationale:**
1. **Auth (431)** establishes user identity via token/cookie
2. **PasswordExpiry (436)** gates expired credentials (pre-existing, soft requirement)
3. **FirstLogin (440)** gates `MustChangePassword` flag BEFORE authorization (compliance gate — user cannot use system until password reset after admin bulk import)
4. **Authz (442)** enforces permission-based access control on endpoint (hard requirement)

**Gate implementation pattern:**
Each compliance gate middleware:
- Checks authenticated user identity ONLY (skips path whitelist for public routes)
- Returns early `401 Unauthorized` with specific reason header (e.g., `X-Must-Change-Password: true`)
- FE intercepts response header → routes to specialized page (e.g., `change-password-page.tsx`)
- Gate is self-clearing on business action (e.g., ChangePassword command)
- Does NOT block OpenID connect endpoints (`/connect/..`, `/openapi/..`) to prevent auth feedback loops

**Code sample (FirstLoginPasswordChangeMiddleware):**
```csharp
public class FirstLoginPasswordChangeMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> ExemptPaths = new()
    {
        "/api/v1/me/password",  // Change password endpoint
        "/openapi",             // API docs
        "/connect/logout",      // Logout
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var sub = user.GetClaim(Claims.Subject);
            if (Guid.TryParse(sub, out _))
            {
                var request = context.Request;
                var isExempt = ExemptPaths.Any(p => request.Path.StartsWithSegments(p));
                
                if (!isExempt && user.HasClaim(c => c.Type == "must_change_password" && c.Value == "true"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.Add("X-Must-Change-Password", "true");
                    return;  // Do NOT call next()
                }
            }
        }

        await next(context);
    }
}
```

**Compliance Gate Headers Registry (v2.133p22+):**

| Header | Trigger | FE Behavior | Self-Clear | Handler Type | Notes |
|--------|---------|-------------|------------|--------------|-------|
| `X-Must-Change-Password: true` | `[FirstLoginPasswordChangeMiddleware]` detects `MustChangePassword` claim on authenticated user | Redirect `/change-password?reason=must-change` | ChangePasswordCommand | Middleware (pre-authorization) | Admin bulk import, password resets; blocks all endpoints except exempt paths |
| `X-Password-Expired: true` | `[PasswordExpiryMiddleware]` detects expired credential date | Redirect `/profile?passwordExpired=true` | CredentialPolicy TTL expiry + manual reset | Middleware (pre-authorization) | Legacy soft gate (v2.133p18 hotfix position 436) |
| `X-Mfa-Required: true` | `[MfaForbidResultHandler]` (IAuthorizationMiddlewareResultHandler) detects forbidden response from `[RequiresMfa]` policy on authenticated user | Redirect `/account/mfa?reason=mfa-required` if not already there | TOTP enrolment complete + `mfa_at` claim issued | Authorization middleware result handler | **QĐ 742 §1.6.b NEW (v2.133p22 follow-up):** MFA policy gates; user has no MFA session yet. See [ADR-007](./adr/007-mfa-totp-dual-storage.md). Handler is NOT middleware — it hooks the authorization result pipeline (different from password gates which are true middleware). |

**MFA-Required Detail (X-Mfa-Required handler pattern):**

Unlike password gates (`FirstLoginPasswordChange`, `PasswordExpiry`) which intercept requests as middleware, the MFA required signal is generated by an `IAuthorizationMiddlewareResultHandler` — a hook that runs *after* authorization policy evaluation fails. This is architecturally necessary because:
- MFA check IS an authorization decision (`[RequiresMfa]` policy), not a pre-authorization gate
- The handler only fires when the policy fails + user is authenticated (prevents 401-as-403 confusion)
- Delegates the actual response to framework's `AuthorizationMiddlewareResultHandler` after header annotation

**Implementation (MfaForbidResultHandler.cs):**
```csharp
public sealed class MfaForbidResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string MfaRequiredHeader = "X-Mfa-Required";
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, 
        AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden &&
            context.User?.Identity?.IsAuthenticated == true &&
            policy.Requirements.OfType<MfaRequirement>().Any())
        {
            context.Response.Headers.Append(MfaRequiredHeader, "true");
        }
        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
```

**CORS exposure (critical):** The shared `GovApiPolicy` CORS configuration MUST expose all three headers so browsers allow SPA (http://localhost:5173) to read them from cross-origin responses (http://localhost:5001). Without exposure, headers are silently stripped by CORS preflight. Header list updated in `AqtCoreFW.Infrastructure.InfrastructureRegistration`:
```csharp
GovApiPolicy.WithExposedHeaders(
    "X-Mfa-Required", 
    "X-Must-Change-Password", 
    "X-Password-Expired", 
    "X-Correlation-Id")
```

**FE integration (api-client.ts):**
All three headers:
- Axios interceptor detects headers on 403/401 response in priority order: `X-Password-Expired` → `X-Must-Change-Password` → `X-Mfa-Required`
- Routes user to appropriate page (profile, change-password, or mfa enrolment)
- After completing the gate action, clears state and continues original request
- Includes loop guard on MFA redirect: skips redirect if already on `/account/mfa` page (prevents infinite redirect)

```typescript
// From api-client.ts (lines 170–180)
if (status === 403 && error.response?.headers?.['x-password-expired'] === 'true') {
  window.location.href = '/profile?passwordExpired=true';
} else if (status === 403 && error.response?.headers?.['x-must-change-password'] === 'true') {
  window.location.href = '/change-password?reason=must-change';
} else if (status === 403 && error.response?.headers?.['x-mfa-required'] === 'true') {
  if (!window.location.pathname.startsWith('/account/mfa')) {
    window.location.href = '/account/mfa?reason=mfa-required';
  }
}
```

**Testing pattern:**
- Unit: Mock authenticated user + test gate activation logic
- Integration: Full request→middleware→response flow via `WebAppFixture`
- Test gates in isolation BEFORE testing authorization (separate concerns)
- For MFA gate: verify handler condition logic (Forbidden flag set, MfaRequirement present in policy, user authenticated)

---

### SQL Injection Prevention (Forms Module)
All user input used in dynamic SQL queries (especially in filter expressions) MUST be validated via regex patterns:

**Example from `ListFormSubmissionsQueryHandler`:**
```csharp
private static readonly Regex FieldKeyPattern = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

public async Task<Result<PagedList<FormSubmissionDto>>> Handle(
    ListFormSubmissionsQuery request,
    CancellationToken cancellationToken)
{
    // Validate field key pattern before using in SQL
    if (!FieldKeyPattern.IsMatch(request.Filter.FieldKey))
        return Result.Fail(new ValidationError("Invalid field key format"));

    var submissions = await _readDbConnection.QueryAsync<FormSubmissionDto>(
        @"SELECT * FROM FormSubmissions
          WHERE FieldKey = @fieldKey AND TenantId = @tenantId",
        new { fieldKey = request.Filter.FieldKey, tenantId = request.TenantId });

    return Result.Ok(new PagedList<FormSubmissionDto>(...));
}
```

### ExtController Tenant Isolation
ExtControllers (analytics, diff, bulk operations, PDF export) MUST enforce tenant isolation:
- Call `ResolveTenantId()` at method entry
- Never accept tenantId from query params
- Validate ownership before access

---

## Cross-Module Data Access (CRITICAL)

**NEVER** cross-schema JOIN between modules. Use `IModuleClient` interface instead.

### Why
- Cross-schema JOINs break when modules have separate databases (microservice extraction)
- No audit trail — impossible to trace which module accessed which data
- Tight coupling — schema migration in one module breaks queries in another

### Pattern
```csharp
// ✗ WRONG: Direct cross-schema JOIN
LEFT JOIN [identity].AspNetUsers u ON u.Id = r.DataSubjectId

// ✓ CORRECT: Use IModuleClient interface
var userMap = await identityClient.GetUsersByIdsAsync(userIds, ct);
// enrich DTOs in-memory
```

### IModuleClient Contracts
Defined in `SharedKernel.Contracts/Clients/`. Each module exposes an interface:
- `IIdentityModuleClient` — user/role lookups
- `IWorkflowModuleClient` — workflow dispatch
- `INotificationModuleClient` — send notifications
- etc.

**Exception - startup seeders:** seeders that run once while the host builds data (`Identity.Infrastructure/Seeding/TenantBackfillSeeder`, `TenantUserSeeder`; `Organization.Infrastructure/Seeding/TenantAssignmentSeeder`, `SeedOrphanPurger`, `OrgTreeSeeder`, `OrgTreeSeeder.Treasury`) may read another module's schema. They are not on a request path and module clients are not the right seam during host bootstrap. New seeders that need this name the schema they read in a comment; business code never uses this exception.

**Monolith:** `InProcess*ModuleClient` (direct DB query, zero network overhead)
**Microservice:** Swap to `Http*ModuleClient` or `Grpc*ModuleClient` — zero consumer code change.

### Migration Path
1. Phase 1 (now): `IModuleClient` batch lookup — in-process Dapper query
2. Phase 2 (microservice): Swap implementation via DI — HTTP/gRPC call
3. Phase 3 (scale): Local Cache + Event Sync for high-frequency reference data

### Error Code Relocation Pattern (Cross-Module Contracts)
**Phase-03 Pattern:** When a domain's error codes become cross-module public contracts, relocate from `ModuleX.Domain.Errors` → `SharedKernel.Contracts.Clients.ModuleXErrors`. Example: `WorkflowErrorCodes` moved to `SharedKernel.Contracts.Clients` so Forms module can map errors returned from `IWorkflowModuleClient` calls without importing Workflow.Domain (tight coupling). Prevents circular references + establishes error codes as public API contracts.

### Cross-Module Error Code Factory Pattern
**CRITICAL FOR CROSS-MODULE USAGE:** When adding a new error code to the public contracts enum (e.g., `WorkflowErrorCodes` in `SharedKernel.Contracts.Clients`), MUST also add a corresponding factory method in the module's Domain.Errors class. The factory MUST call `.WithMetadata("WorkflowErrorCode", code)` to attach the error code for cross-module extraction.

**Why this matters:** Cross-module clients (e.g., `IWorkflowModuleClient` callers in Forms module) use `InProcessWorkflowModuleClient.ExtractWorkflowErrorCode(result)` to map returned `FluentResults.Result` → `string errorCode`. Without metadata attachment, the error code is invisible to callers — they see the generic message only.

**Implementation Pattern:**

1. **Define code in SharedKernel contract:**
   ```csharp
   // src/shared/AqtCoreFW.SharedKernel.Contracts/Clients/WorkflowErrorCodes.cs
   public static class WorkflowErrorCodes
   {
       public const string StateConflict = "GOV_WFL_013";
   }
   ```

2. **Add factory in module's Domain.Errors:**
   ```csharp
   // src/modules/workflow/AqtCoreFW.Workflow.Domain/Errors/WorkflowErrors.cs
   public static IError StateConflict(Guid instanceId)
       => new ConflictError($"Workflow instance {instanceId}: state conflict (simultaneous update detected).")
           .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.StateConflict);
   ```

3. **Use factory in handlers (not inline constructor):**
   ```csharp
   // ✓ CORRECT
   catch (DbUpdateConcurrencyException)
   {
       return Result.Fail(WorkflowErrors.StateConflict(instance.Id));
   }

   // ✗ WRONG — metadata lost
   catch (DbUpdateConcurrencyException)
   {
       return Result.Fail(new ConflictError(WorkflowErrorCodes.StateConflict));
   }
   ```

4. **Cross-module client extracts code:**
   ```csharp
   // src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Clients/InProcessWorkflowModuleClient.cs
   public static string? ExtractWorkflowErrorCode(Result result)
   {
       var error = result.Errors.FirstOrDefault();
       return error?.HasMetadataKey("WorkflowErrorCode") == true
           ? error.Metadata["WorkflowErrorCode"]?.ToString()
           : null;
   }
   ```

**Test Validation:** When D2 StateConflict was added (2026-04-17), absence of the factory caused cross-module error mapping to fail — callers received generic message, not error code. Fix: add factory with metadata → error code now flows correctly to callers of IWorkflowModuleClient.

### Cross-Module Client Caching Strategy (Live vs Cached)

Not all cross-module methods are created equal. Cache staleness tolerance depends on security sensitivity:

| Method | Staleness Tolerance | Implementation |
|--------|-------------------|-----------------|
| `GetEffectivePermissionCodesAsync` (Identity) | **10-min cache OK** — permissions checked at request entry; admin-perm changes tolerate brief delay; tenant-safe cache key = `{tenantId}:{userId}` | Redis `SETEX code:10min` |
| `GetUserRoleCodesAsync` (Identity) — **v2.137p110 NEW, renamed v2.137p136** | **LIVE only (no cache)** — authz security-critical; role membership changes must reflect immediately in task-pool predicate; used in Workflow claim/inbox eligibility gate; matches by role Code (v2.137p136 refactor from Name for consistency w/ Permission/Group/OrgUnit) | Dapper direct query (in-process), no persistence |
| `GetUserActiveStatusAsync` (Identity) | **LIVE only (no cache)** — checks `IsActive` + `IsLockedOut` flags for assignment eligibility; lockout enforcement must not lag; used at override gate (v2.137p82) | Dapper direct query, no persistence |

**Rule:** When adding a new cross-module method, assess the security impact. If the method informs an authorization decision (eligibility, permission check, assignment, claim gate), prefer LIVE + document the exception. Cache only reference data with acceptable staleness windows (lookups, classifications, static mappings). Use `IMemoryCache` for in-process L1 + Redis for multi-pod L2 when cache-safe.

### Tenant Scoping in Cross-Module Client Methods (v2.137p112 SECURITY FIX)

**CRITICAL:** Cross-module client methods that access tenant-scoped data **MUST accept an explicit `Guid tenantId` parameter**. Do NOT rely on ambient `IReadDbConnection.SESSION_CONTEXT` RLS predicates unless the target table has an explicit `CREATE SECURITY POLICY` enforcing the tenant filter.

**Why:** In v2.137p112, `IIdentityModuleClient.ResolveActiveUserGroupsByCodesAsync(codes, tenantId, ct)` and `ResolveActiveUsersInGroupsAsync(codes, tenantId, ct)` initially lacked the `tenantId` parameter. The underlying `[identity].UserGroups` table had NO RLS policy, so SESSION_CONTEXT was set but ignored — queries returned rows from **all tenants** (cross-tenant data leak). Code-review caught and fixed via:

1. Add `Guid tenantId` parameter to method signature
2. Thread through to SQL: `AND (TenantId = @TenantId OR TenantId IS NULL)` predicate (supports global groups with NULL TenantId)
3. Update all call sites to pass `request.TenantId` or similar
4. Add integration test proving isolation: `I-CT1` seed same group code in two tenants, verify only caller's tenant returned

**Pattern (correct):**
```csharp
// ✓ CORRECT — explicit tenantId param + SQL predicate
public async Task<IReadOnlySet<string>> ResolveActiveUserGroupsByCodesAsync(
    IReadOnlyCollection<string> codes, 
    Guid tenantId,  // <-- explicit parameter
    CancellationToken ct)
{
    const string sql = @"
        SELECT DISTINCT Code FROM [identity].[UserGroups]
        WHERE Code IN @Codes 
          AND IsActive = 1
          AND (TenantId = @TenantId OR TenantId IS NULL)";  // <-- tenant predicate
    return await _connection.QueryAsync<string>(sql, new { Codes = codes, TenantId = tenantId }, ct);
}
```

**When you can skip this rule:** Only if the table has a `CREATE SECURITY POLICY` enforcing the tenant predicate at the database layer. Verify the migration file `RlsMigrationHelper` usage. If not present, add the explicit parameter.

### Cross-Module Client Interface Surface Audit (260526-A INVARIANT — Naming Variant Detection)

**CRITICAL:** When extending an existing cross-module client interface with new methods, **grep the codebase using MULTIPLE naming variants** before auditing. Project folder naming (e.g., `Organization` module) may differ from contract namespace naming (e.g., `OrgUnit` contracts), causing audit misses.

**Origin:** RT2-15 Phase 04 audit initially searched `grep IOrganizationModuleClient` but found zero results, suggesting new interface was needed (false negative). Reality: existing `IOrgUnitModuleClient` at `src/shared/AqtCoreFW.OrgUnit.Contracts/` was not discovered. Issue: project folder = `Organization` but contract namespace = `OrgUnit` (conceptual vs module-name inconsistency). DRY violation prevented (avoid duplicate interface); correct decision made post-audit but with efficiency cost.

**Pattern (correct audit):**
```bash
# WRONG: Single variant misses inconsistencies
grep -r "IOrganizationModuleClient" src/

# CORRECT: Multiple variants (plural/singular + module-name vs concept-name)
grep -r "IOrganizationModuleClient\|IOrgUnitModuleClient" src/
grep -r "IIdentityModuleClient\|IIdentityModuleClient" src/  # usually same, but verify
grep -r "IWorkflowModuleClient\|IWorkflowModuleClient" src/  # ditto
```

**Recommendation:** When adding cross-module methods, include all known variants in your grep command. Update this list if new module naming inconsistencies are discovered.

---

## Encryption & Key Versioning (QĐ 742 Phase-06)

### EncryptedString Pattern with Key Rotation

Sensitive fields (PII, credentials, secrets) are encrypted at rest via `EncryptedStringConverter` with support for key rotation without decryption:

**Schema Format:** `v{N}:ciphertext` where N is the key version. Example: `v1:AQAAAAEAAAAwAA...` (v1 key, Base64 ciphertext).

**Backward Compatibility:** LegacyCipherUpgrader auto-detects unversioned ciphertexts (v0, stored before key versioning) and handles decryption with fallback keys.

### IKeyResolver Abstraction

All encrypted fields depend on `IKeyResolver` to fetch encryption keys dynamically (supports multi-version keys from Vault):

```csharp
public interface IKeyResolver
{
    /// <summary>
    /// Resolves the encryption key for a specific version.
    /// Supports key rotation: same field may have v1, v2, v3 versions (different rows).
    /// </summary>
    Task<byte[]> GetKeyAsync(int version, CancellationToken ct);
}
```

**Implementation:** `VaultKeyResolver` in dev (HashiCorp Vault container `hashicorp/vault:latest`), production uses external Vault instance.

### Resolving cryptographic key material — MANDATORY rule

Never read HMAC / pepper / encryption keys from `IConfiguration` directly in module code. Always use the resolver pattern:

| Resolver | Purpose | Vault path |
|----------|---------|------------|
| `IKeyResolver` / `VaultKeyResolver` | Field-level AES encryption keys (versioned) | `aqt/encryption/v{N}` |
| `IPepperResolver` / `VaultPepperResolver` | Per-tenant email-hash peppers | `aqt/encryption/email-pepper-{TenantId}` |
| `IAuditHmacKeyResolver` / `VaultAuditHmacKeyResolver` | Audit-log HMAC chain key (Api host only) | `aqt/audit/hmac-v{N}` |

**Client secrets with a dev default:** never write `configuration["...Secret"] ?? "<literal>"`. A literal fallback ships the secret in the repository and silently becomes the production value when the key is not set. Resolve through a helper that allows the dev value only in Development/Testing/Benchmark and fails startup otherwise, and add the key to `ProductionSecretValidator` (pattern: `AqtCoreFW.Infrastructure.Configuration.AuthResourceClient` for `Auth:ResourceClientSecret`). Third-party API keys read from configuration without a literal fallback (for example `AI:AzureOpenAI:ApiKey`) and the documented `Audit:HmacKey` legacy fallback inside `VaultAuditHmacKeyResolver` are not covered by this rule.

**Architecture guard:** `tests/architecture/AqtCoreFW.Architecture.Tests/SecretConfigBypassGuardTests.cs` enforces this rule. It substring-scans every `*.cs` file under `src/**` for `IConfiguration["...Hmac/Pepper/EncryptionKey/FieldKey/SecretKey..."]` reads (allowlist: `ProductionSecretValidator.cs` boot-time presence check + the resolver implementations themselves). Any new bypass fails the architecture suite. A second test (`AuthServerHost_DoesNotResolve_IAuditHmacKeyResolver`) pins the boundary that Api is the only host that runs HMAC compute — AuthServer is Hangfire-client-only and must NOT register `IAuditHmacKeyResolver`. Shipped v2.137p13 (2026-05-13); plan `plans/260513-1805-audit-hmac-vault-seeder-sweep/`.

### EncryptedStringConverter Refactoring (v2.137)

Changed from static key to dynamic resolver:

```csharp
// OLD (v2.136): Static byte[] key
var converter = new EncryptedStringConverter(staticKeyBytes);

// NEW (v2.137): Dynamic key resolver + current version
var converter = new EncryptedStringConverter(
    keyResolver: async (version, ct) => await vaultKeyResolver.GetKeyAsync(version, ct),
    currentVersion: 1  // Encryption writes use v1 key; reads auto-detect version from ciphertext
);
```

### Entity Configuration Pattern (ApplicationUser, FileRecord)

Entity configs now take `IKeyResolver` via DI:

```csharp
// Before (v2.136)
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    private readonly byte[] _encryptionKey;
    public ApplicationUserConfiguration(IConfiguration config)
    {
        _encryptionKey = Convert.FromBase64String(config["Encryption:Key"]);
    }
    
    protected override void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.PhoneNumber)
            .HasConversion(new EncryptedStringConverter(_encryptionKey));
    }
}

// After (v2.137)
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    private readonly IKeyResolver _keyResolver;
    private readonly int _currentVersion;
    
    public ApplicationUserConfiguration(IKeyResolver keyResolver, IConfiguration config)
    {
        _keyResolver = keyResolver;
        _currentVersion = int.Parse(config["Encryption:CurrentVersion"] ?? "1");
    }
    
    protected override void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.PhoneNumber)
            .HasConversion(new EncryptedStringConverter(_keyResolver, _currentVersion));
    }
}
```

### AuthServer & Api Host Parity (v2.137)

Both hosts now wire Vault integration identically:

```csharp
// Program.cs (both AuthServer + Api)
builder.Services.AddVaultConfiguration(builder.Configuration);  // Registers IKeyResolver + VaultHealthCheck
builder.Services.AddSingleton<ProductionSecretValidator>();     // Validates env var secrets in production
```

### Key Rotation Runbook

Documented in `docs/ops/vault-key-rotation-runbook.md`:
1. **Create new key in Vault** with version N+1
2. **Deploy with `Encryption:CurrentVersion=N+1`** (reads v1..N, writes v(N+1))
3. **Monitor migration** via `SecretMaskingDestructuringPolicy` logs (counts v1..N vs v(N+1) rows)
4. **Run background upgrade job** (future Phase-07 tooling) to rewrite old versions
5. **Retire old key** after verification period

### Testing Pattern

Architecture tests in `Qd742Phase06ComplianceTests.cs` verify:
- IKeyResolver is wired on startup
- `currentVersion` config matches deployment plan
- Entity configurations accept IKeyResolver via DI
- Migrations include version-prefix schema guard

```csharp
[Fact]
public void ApplicationUserConfig_RequiresIKeyResolver()
{
    var services = new ServiceCollection();
    services.AddScoped<IKeyResolver>(_ => throw new InvalidOperationException("Missing IKeyResolver"));
    
    var config = services.BuildServiceProvider().GetRequiredService<ApplicationUserConfiguration>();
    Assert.NotNull(config);  // Should not throw on instantiation; resolver called only on entity conversion
}
```

---

## Cache Strategy Decision Framework

Production deployment: Kubernetes HPA, **2-10 pods** (`infra/helm/aqtcorefw/values.yaml`). All cache decisions must account for multi-pod consistency.

### 4 Tiers Available (all pre-configured, use in parallel)

| Tier | Interface | Backed By | Latency | Cross-Pod Consistent | When To Use |
|------|-----------|-----------|---------|---------------------|-------------|
| **T1: Memory-Only** | `IMemoryCache` | In-process per-pod | ~0.01ms | ❌ Stale ≤ TTL | Best-effort UI hints (presence, typing indicators) where stale is acceptable UX |
| **T2: Redis-Only** | `IDistributedCache` | Redis shared | ~2ms | ✅ Instant | Security/auth data (perms, claims, sessions) — correctness > speed |
| **T3: Two-Tier** | `ICacheService` | L1 Memory + L2 Redis | ~0.01ms (L1 hit) | ⚠️ L1 stale ≤ 10min | Business data with medium-high read frequency — definitions, configs, org trees |
| **T4: Two-Tier + Pub/Sub** | `ICacheService` + Redis `Publish/Subscribe` | L1 + L2 + broadcast | ~0.01ms (L1 hit) | ✅ ~1ms propagation | Data where change must reflect cross-pod near-instantly — feature flags |

### Decision Flowchart

```
Is stale data a security risk? (auth, permissions, roles)
  YES → T2 (Redis-Only: IDistributedCache)

Is data best-effort / cosmetic? (presence, typing, UI badge)
  YES → T1 (Memory-Only: IMemoryCache)

Does data change require <1s cross-pod reflection?
  YES → T4 (Two-Tier + Pub/Sub) — precedent: SystemParamService feature flags
  NO  ↓

Is read frequency >100 req/sec per pod?
  YES → T3 (Two-Tier: ICacheService) — L1 absorbs hot-path reads
  NO  → T2 (Redis-Only: IDistributedCache) — simpler, always consistent
```

### Key Naming Convention

All cache keys MUST follow these patterns:

| Scope | Pattern | Example |
|-------|---------|---------|
| Tenant-scoped | `"{module}:{entity}:{tenantId}:{id}"` | `"wf:def:3fa85f64:def123"` |
| Global/system | `"sys:{module}:{entity}:{id}"` | `"sys:mst:wf-taxonomy:all"` |
| User-scoped | `"{module}:{entity}:{userId}"` | `"presence:7b8c9d01"` |

Helper: `TenantCacheKeyFactory.Build(tenantContext, key)` → `"t:{tenantId}:{key}"`

**Security rule:** Tenant-scoped data keys MUST include `tenantId` — prevents cross-tenant cache leaks.

### TTL Guidelines

| Data Profile | Recommended TTL | Examples |
|-------------|-----------------|---------|
| Immutable / content-addressed | 1-7 days | AI embeddings (`emb:{hash}`) |
| Rarely changed (admin config) | 15-30 min | Menu tree, org tree, workflow definitions |
| Auth/security | 5-10 min | Permissions, claims, ABAC rules |
| Real-time-ish | 1-5 min | KPI dashboards, analytics, system params |
| Replay protection | 24h | Idempotency keys |

### Invalidation Rules

1. **Always call `RemoveAsync(key)` in the command handler** that mutates the data — never rely on TTL alone for correctness
2. **T3 (Two-Tier) limitation:** `RemoveAsync` only evicts L1 on the local pod + L2 Redis. Other pods' L1 remains stale until L1 TTL expires. Acceptable when data changes are infrequent admin operations.
3. **If instant cross-pod L1 eviction required:** add Redis `Publish` after `RemoveAsync` (T4 pattern). Precedent: `SystemParamService` lines 110-122 (`ff-invalidate` channel).
4. **Event-driven invalidation:** use for cross-module cache dependencies. Prefer MassTransit integration events over direct Redis pub/sub for cross-module communication; use Redis pub/sub for intra-module cross-pod L1 eviction (precedent: `abac-invalidate`, `rules:cache-invalidate`).

### Existing Usage Map (Reference)

| Module | Tier | Key Pattern | TTL | Invalidation |
|--------|------|-------------|-----|-------------|
| SystemParams | T4 | `sysparam:{tenant}:{key}` | 5min | Explicit + Redis pub/sub |
| OrgUnit | T3 | `org:tree:{tenantId}` | 15min | Explicit RemoveAsync |
| Menu | T2 | `menu-tree:{tenantId}` | 30min | TTL-only |
| Permissions | T2 | `perm:{userId}` | 10min | TTL-only |
| Claims | T2 | `delegation:{userId}` | 5min | TTL-only |
| ABAC Rules | T3 ✓ | `t:{tenant}:abac:*` / `sys:abac:*` | 10min | Two-Tier (Redis L2 + pub/sub L1 invalidation) — T4-05 Phase 01 shipped v2.120 |
| Rules Engine | T1 ⚠️ | `rules:{ruleSetId}` | 10min | Explicit local-only (T4-05 Phase 03: add Redis pub/sub overlay) |
| Presence | T1 ✓ | `presence:{userId}` | 5min | TTL (best-effort by design) |
| AI Embeddings | T2 | `emb:{dim}:{hash}` | 7 days | TTL (immutable) |
| Idempotency | T3 | `idempotency:{tenant}:{hash}` | 24h | TTL |
| KPI Dashboard | T3 | `wf:analytics:*` | 5min | TTL |

⚠️ = tech debt — should be T3 per decision flowchart. Tracked in **T4-05** backlog.

---

## Multi-tenancy

Every request-scoped service receives `ITenantContext` (injected via `TenantAwareBehavior`). EF Core global query filters enforce `org_id` isolation — never query cross-tenant without explicit admin scope.

```csharp
// In DbContext OnModelCreating:
builder.HasQueryFilter(c => c.TenantId == _tenantContext.TenantId);
```

### SQL Server Row-Level Security (RLS) — v2.137p4

36 `SECURITY POLICY` objects enforce tenant isolation at the DB layer (independent of EF global filters). Three predicate families, all GO-free via `RlsMigrationHelper`:

| Family | Helper | Tables | Predicate note |
|--------|--------|--------|----------------|
| Standard | `GenerateRlsPolicyStatements()` | 33 tables (Guid TenantId, NOT NULL) | `IS_MEMBER('db_owner')=1 OR @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)` |
| Asymmetric audit | `GenerateAuditAdminBypassRlsPolicyStatements()` | `audit.AuditLogEntries`, `audit.SecurityEventLogs` | FILTER adds `OR (@TenantId IS NULL AND SESSION_CONTEXT(N'audit_admin_bypass')='true')`. BLOCK-INSERT adds `OR @TenantId IS NULL` (allow server-side writers). |
| Symmetric sysparams | `GenerateSystemParamRlsPolicyStatements()` | `config.SystemParameters` | `IS_MEMBER('db_owner')=1 OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS nvarchar(100)) OR @TenantId IS NULL` (global rows visible to all). |

**`audit_admin_bypass` flag rules (MANDATORY):**
- Set: `EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=N'true', @read_only=0`
- **NEVER use `@read_only=1`** — the flag MUST be clearable by `sp_reset_connection` (ADO.NET pool reset). `@read_only=1` would permanently lock the flag for the lifetime of the physical connection, creating a security leak.
- Always clear in `finally`: `EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=NULL, @read_only=0`
- Call sites: `AuditLogPurgeJob.ExecuteAsync()`, `DapperReadDbConnection.OpenWithAuditAdminBypassAsync()`
- Pool-leak proof: `AuditRlsEnforcementTests.PoolLeak_BypassFlag_DoesNotSurvive_PoolRecycle` tests `SESSION_CONTEXT(N'audit_admin_bypass') IS NULL` after pool recycle.

**Adding RLS to a new migration:**
```csharp
// Standard (Guid TenantId, NOT NULL):
foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements("myschema", "MyTable"))
    migrationBuilder.Sql(sql);

// Nullable uniqueidentifier (audit tables):
foreach (var sql in RlsMigrationHelper.GenerateAuditAdminBypassRlsPolicyStatements("audit", "MyAuditTable"))
    migrationBuilder.Sql(sql);
```

Each `migrationBuilder.Sql()` call = one T-SQL batch. `CREATE OR ALTER FUNCTION` MUST be the only statement in its batch — enforced by the helper's iterator design.

---

## Concurrency

Use `RowVersion` on all aggregates → EF Core detects conflicts → return `ConflictError` with `ETag` header. Client must retry with `If-Match`.

---

## Testing Conventions

| Test type | Project suffix | Tools |
|---|---|---|
| Unit | `*.Tests` | xUnit, NSubstitute, FluentAssertions |
| Integration | `*.Integration.Tests` | Testcontainers, WebApplicationFactory |
| Architecture | `AqtCoreFW.Architecture.Tests` | NetArchTest.Rules |

### Global Test Setup
- **Directory.Build.props** (`tests/Directory.Build.props`): Global `using` statements across all test projects
  - Includes: `Xunit`, `FluentAssertions`, `NSubstitute`
  - Eliminates repetitive imports, ensures consistency

### Unit Test Pattern
- **No** database, **no** HTTP. Mock all external deps with NSubstitute.
- Test method naming: `{Method}_{Scenario}_{ExpectedOutcome}` e.g. `Submit_FromDraft_TransitionsToSubmitted`
- Use `FluentAssertions` for readable assertions: `result.Should().BeSuccess().And.Value.Should().Be(...)`

### Integration Test Pattern
- Real SQL Server via **Testcontainers** (isolated container per test run)
- Use `WebAppFixture` for `WebApplicationFactory<Program>` setup with:
  - `SqlServerFixture` — manages container lifecycle, applies migrations
  - `TestAuthHandler` — replaces JWT validation, allows auth as any principal
- Database is fresh per test class (not per method) — fast teardown via container disposal
- **E2E Playwright test hygiene (PHASE7-TIER-D-CASCADE):** Avoid admin-token-dependent wrong-password tests or lockout-triggering scenarios; use dedicated victim user (e.g., lockout-victim@dev.local) to prevent cascading pollution across parallel test runs. Test-state collision = admin locked → downstream 401 failures unrelated to assertion target.
- **E2E auth-helper / fixture choice (PHASE7-TIER-D-TOKEN-REVOKE):** Specs MUST import `test`/`expect` from `web/e2e/fixtures/test-base.ts`, not `@playwright/test` directly. The `request` fixture there is Proxy-wrapped to auto-retry once on 401 when the Bearer token came from `getAccessToken` (recovers from BE handlers that revoke admin reference tokens mid-suite — `ResetPasswordCommandHandler`, `AssignRoleCommandHandler`, `SyncUserRolesCommandHandler`, `SessionAdminController.RevokeAllForUser`). When a spec drives admin-mutating endpoints whose handlers may revoke caller tokens, prefer the wrapped `request` fixture over the worker-scoped `apiContext`/`testData`/`moduleData` fixtures in `auth-fixture.ts` (those use `extraHTTPHeaders` on a separate context and bypass the wrapper).
- Example:
  ```csharp
  public sealed class CreateCaseIntegrationTests : IAsyncLifetime
  {
      private WebAppFixture _fixture;

      public async Task InitializeAsync() => _fixture = new WebAppFixture();
      public async Task DisposeAsync() => await _fixture.DisposeAsync();

      [Fact]
      public async Task CreateCase_WithValidCommand_PersistsAndReturnsDto()
      {
          // Arrange
          var client = _fixture.CreateClient();
          client.DefaultRequestHeaders.Authorization =
              new("Bearer", _fixture.GetAuthToken(userId: Guid.NewGuid()));

          // Act
          var response = await client.PostAsync("/api/v1/forms/templates", ...);

          // Assert
          response.StatusCode.Should().Be(HttpStatusCode.Created);
      }
  }
  ```

### Architecture Test Pattern
- **NetArchTest.Rules** enforces Clean Architecture boundaries:
  - Domain layer has **no** references to Application, Infrastructure, Presentation
  - Application layer has **no** references to Infrastructure concrete classes (only interfaces)
  - Infrastructure does **not** reference Presentation controllers directly
  - Presentation references **only** Application (via MediatR)
- Run via `AqtCoreFW.Architecture.Tests` assembly
- Example rules:
  ```csharp
  [Fact]
  public void DomainLayer_ShouldNotReferenceApplicationLayer()
  {
      Types.InNamespace("AqtCoreFW.Identity.Domain")
          .Should()
          .NotHaveDependencyOn("AqtCoreFW.Identity.Application")
          .Because("Domain is core business logic, independent of application services");
  }
  ```

### Cross-Domain Test Selection (full-assembly gate)

A change can ripple across **multiple test domains** at once — e.g. a workflow handler-resolution change touches the read-side repository (`WorkflowTaskRepository`), the graph validator (`WorkflowGraphPublishValidationTests`), AND handler integration in the same slice. A diff-mapped or `--filter`ed test subset selects only one of these domains and reports green while the others stay red.

**Rule:** when a PR touches **≥2 distinct test-domain directories** for a module (repository + validator + handler-integration, etc.), run the **full** `*.Integration` + `*.Application` assemblies for that module (no `--filter`) before claiming test-green. The diff-aware subset is for single-domain changes only. (This is the project mirror of the tester subagent's cross-domain-spread escalation rule.)

---

## k6 Performance Testing

### Directory Structure
```
tests/performance/
├── lib/
│   ├── auth.js           # Shared auth helpers (API Key scheme)
│   └── helpers.js        # Shared utility functions
├── load-test.js          # General steady-state load test
├── spike-test.js         # Sudden burst test
├── soak-test.js          # Long-running endurance test
└── {module}-load-test.js # Module-specific tests (audit, identity, files, notifications)
```

### API Key Auth Pattern

All k6 tests use X-Api-Key header scheme (not JWT). Import shared auth helpers:

```javascript
import { getJsonApiKeyHeaders, getApiKeyHeaders } from './lib/auth.js';

export default function() {
  const headers = getJsonApiKeyHeaders();  // includes Content-Type

  const response = http.post(
    `${__ENV.BASE_URL}/api/v1/forms/templates`,
    JSON.stringify({ /* body */ }),
    { headers }
  );
}
```

Environment variables:
- `API_KEY` — test key plaintext (from CI secret K6_TEST_APIKEY)
- `BASE_URL` — API endpoint (default: http://localhost:5000)

### Test Type Naming & Thresholds

| Test | File | VUs | Duration | Target p95 | Max Error% | Status |
|------|------|-----|----------|-----------|-----------|--------|
| Load | `load-test.js` | 50 | 5 min | <500ms | 0.1% | ✓ Passing |
| Spike | `spike-test.js` | 100→500 | 2 min | <800ms | 1% | ✓ Passing |
| Soak | `soak-test.js` | 10 | 70 min | <600ms | 0% | ✓ Passing |
| Module | `{module}-load-test.js` | 20 | 3 min | <500ms | 0.1% | ✓ Passing (Audit, Identity, Files, Notifications) |
| E2E | Playwright | - | - | - | 0% | ✓ 13 tests (9 pass + 4 skip-Vite) |

**npm audit:** 0 vulnerabilities

All tests must pass before merge. Deviations require performance review.

---

## Security Testing Standards

### Test-Driven Security

All security findings must have corresponding test coverage:
- Unit tests for authentication/authorization handlers (NSubstitute mocks)
- Integration tests for token lifecycle (creation, refresh, revocation)
- Architecture tests for layer isolation (prevent privilege escalation via coupling)

### Security Test Categories

| Category | Examples | Framework |
|----------|----------|-----------|
| Authentication | Token generation, JWT validation, MFA TOTP | xUnit + NSubstitute |
| Authorization | RBAC role checks, ABAC attribute evaluation, delegation chaining | xUnit + mocks |
| PII Protection | RTBF anonymization, encryption key rotation, data breach notification | xUnit |
| Audit Logging | Immutability (HMAC chains), append-only enforcement, tamper detection | Integration tests |
| Rate Limiting | Brute force prevention (MFA lockout), API throttling | k6 load tests |
| SSRF Protection | URL whitelist validation, DNS rebinding prevention | xUnit |

### Security Audit Integration

- **Phases 1-4 Remediation:** 31/35 findings fixed (Groups A-D)
- **Test Evidence:** Each remediation has corresponding test in `tests/` directory
- **CI/CD Gate:** SAST/DAST (SonarQube, OWASP ZAP, Trivy) must pass before merge
- **Pentest Timeline:** Q2 2026 (Groups E-F findings pending)

### Examples

**Token Revocation (CRITICAL):**
```csharp
[Fact]
public async Task ChangePassword_RevokesExistingTokens()
{
    // Arrange
    var token = _fixture.GetAuthToken(userId: Guid.NewGuid());

    // Act
    await _handler.Handle(new ChangePasswordCommand(userId, oldPwd, newPwd), CancellationToken.None);

    // Assert
    var introspect = await _client.GetAsync($"/introspect?token={token}");
    introspect.StatusCode.Should().Be(HttpStatusCode.Unauthorized); // revoked
}
```

**MFA Rate Limiting (CRITICAL):**
```csharp
[Fact]
public async Task SetupMfa_RateLimits_After5FailedAttempts()
{
    for (int i = 0; i < 5; i++)
        await _handler.Handle(new ValidateMfaCommand(userId, invalidOtp), ct);

    var result = await _handler.Handle(new ValidateMfaCommand(userId, correctOtp), ct);
    result.IsFailed.Should().BeTrue(); // locked out
}
```

**SSRF Protection (HIGH):**
```csharp
[Fact]
public void ExternalRef_RejectsUrlOutsideWhitelist()
{
    var validator = new ExternalRefUrlValidator(_whitelist);
    var action = () => validator.Validate("https://malicious.internal");
    action.Should().Throw<InvalidOperationException>();
}
```

---

## Git Commit Conventions

```
feat(cases): add bulk close endpoint
fix(identity): prevent concurrent MFA setup race condition
refactor(shared): extract WorkflowEngine to SharedKernel
test(cases): add cross-tenant isolation integration tests
chore(deps): update EF Core to 10.0.1
docs(adr): add ADR-006 for event sourcing decision
```

No AI references, no "Claude", no "as per your request" in commit messages.

---

## Asynchronous Report Execution Pattern

### Report Submission (HTTP 202 Accepted)
Report execution is asynchronous via Hangfire. Submission returns 202 with executionId:

```json
{
  "success": true,
  "data": { "executionId": "550e8400-e29b-41d4-a716-446655440000" },
  "errors": []
}
```

### Report Polling (Status Check)
Client polls `GET /api/v1/reports/executions/{id}` to check execution status:

```json
{
  "success": true,
  "data": {
    "executionId": "550e8400-e29b-41d4-a716-446655440000",
    "status": "COMPLETED",
    "createdAt": "2026-03-18T10:00:00Z",
    "completedAt": "2026-03-18T10:01:30Z",
    "rowsGenerated": 5000,
    "format": "EXCEL"
  },
  "errors": []
}
```

### Report Download (File Retrieval)
Once status is COMPLETED, client downloads via `GET /api/v1/reports/executions/{id}/download` → binary file response.

**Security:** ResultFilePath validated against execution record before retrieval (path traversal prevention).

---

## SQL Template Validation (Reporting Module)

All report SQL templates validated via `SqlValidationHelper`:

**Rules:**
- Must start with `SELECT` (INSERT/UPDATE/DELETE blocked)
- Must include `@TenantId` parameter (multi-tenancy enforcement)
- Blocklist: EXEC, xp_*, sp_*, OPENQUERY, OPENDATASOURCE (expanded for security)
- Parameterized Dapper execution (no string interpolation)

**Example:**
```sql
-- VALID
SELECT Id, Title, CreatedAt FROM workflow.WorkflowInstances WHERE TenantId = @TenantId

-- INVALID (will reject)
SELECT * FROM Users; DROP TABLE workflow.WorkflowInstances;
INSERT INTO workflow.WorkflowInstances ...
SELECT ... FROM workflow.WorkflowInstances -- missing @TenantId filter
```

---

## Frontend Standards

**Moved:** [`docs/frontend-code-standards.md`](./frontend-code-standards.md)

React/TypeScript conventions for `web/`: local typecheck practice, error-handling patterns, polling
guidelines, React performance & memoization, dark mode & UI features, batch operations, E2E test
fixture patterns (`test-base.ts` auth-helper 401-retry), and the announcement banner.

Split out on 2026-07-28: this file was 2361 lines and the frontend sections were its one clean
boundary. Section titles are unchanged, so `§ "<title>"` references still resolve — only the file
differs.

---

## Migration Folder Convention

EF Core migrations + the auto-generated `*ModelSnapshot.cs` MUST live in a single `Migrations/` directory inside each module's `*.Infrastructure` assembly. The path is **NOT uniform** across the codebase — there is one historical exception which has bitten us in production.

### Two layouts in current tree

| Layout | Modules | Path pattern |
|--------|---------|--------------|
| **Standard** (14 modules) | `ai`, `audit`, `collaboration`, `files`, `forms`, `identity`, `masterdata`, `notifications`, `organization`, `reporting`, `rules`, `systemparams`, `workflow`, plus `messaging` infra | `src/modules/{name}/AqtCoreFW.{Name}.Infrastructure/Persistence/Migrations/` |
| **Anomaly** (1 module) | `integration` | `src/modules/integration/AqtCoreFW.Integration.Infrastructure/Migrations/` (no `Persistence/` subfolder) |

The Integration anomaly is legacy from earlier scaffolding and has not been normalized to avoid renaming the migration assembly + risking `__EFMigrationsHistory` lookup churn. The architecture suite (`MigrationConsistencyTests`) makes the deviation safe by enforcing snapshot↔migration parity regardless of folder location.

### Why this matters — v2.133p12 incident (recovered as v2.133p13)

When v2.133p12 added `ClaimedAtUtc datetimeoffset NULL` to `OutboxMessages` across 14 modules, the staging glob used to git-add the new migration files matched only `**/Persistence/Migrations/*.cs`. Integration's migration `.cs` + `.Designer.cs` were generated correctly on disk but **never staged** — the model snapshot (which lives in the same Migrations folder and was modified) made it into the commit, but the migration `Up()`/`Down()` script did not.

Symptom on first deploy: `OutboxDispatcher` would crash with `Invalid column name 'ClaimedAtUtc'` for the Integration schema only — snapshot declared the column, EF history table had no migration entry to trigger the `ALTER TABLE`. Recovery: v2.133p13 (commit `92be7215`) added the missing files. See `docs/project-changelog.md` v2.133p13 entry.

### Rules for new modules / new migrations

1. **New module:** use `Persistence/Migrations/` to follow the dominant convention. Set `MigrationsAssembly` and `MigrationsHistoryTable(schema:)` on `DbContext.OnConfiguring` consistently with peers (e.g., AI, Audit, Workflow).
2. **Existing module (including Integration):** keep the established path. Don't relocate folders mid-flight — `__EFMigrationsHistory` references migration type names, not file paths, but tooling (deploy scripts, doc generators, audit globs) often hard-codes one of the two patterns.
3. **Staging migrations:** never write a glob that assumes a single layout. Either stage explicitly per module, or use a recursive Git pathspec matching both forms — for example `git add 'src/modules/**/Migrations/'` (Git's `**` matches across directory levels) instead of `git add 'src/modules/*/*/Persistence/Migrations/'` (would skip Integration). When using shell globs (bash, zsh) outside Git's pathspec parser, enable `globstar` first or rely on Git's pathspec syntax directly.
4. **CI guard:** `tests/architecture/AqtCoreFW.Architecture.Tests/MigrationConsistencyTests.cs` runs in the architecture gate (Section 7a, `primary-workflow.md`). Its `AllModules_ModelSnapshot_Should_Match_LatestMigrationDesigner` will catch the v2.133p12 class of drift — snapshot bumped without a matching migration script — regardless of folder layout. Do not whitelist around it.
