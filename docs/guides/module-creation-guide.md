# How to Create a New Module

This guide walks through creating a new module following the Workflow module as the golden path example. All steps assume the module is named `Permits`.

---

## Quick Start: Use Module Template

**V1.5+:** Use the `dotnet new` template for fastest scaffolding:

```bash
# Install template (one-time)
dotnet new install ./templates/aqt-module/

# Create new module
dotnet new aqt-module --param:name Permits --output src/modules/permits

# Result: Four-layer project structure, sample entity, and DI registration skeleton
```

Template generates all files below. Proceed to **Step 2 (Create Domain Entity)** to customize.

---

## Manual Setup: Create Project Structure

For customized setup or if template doesn't fit your use case:

```
src/modules/permits/
├── AqtCoreFW.Permits.Domain/
│   ├── AqtCoreFW.Permits.Domain.csproj
│   ├── Entities/
│   ├── Events/
│   ├── Exceptions/
│   └── Repositories/
├── AqtCoreFW.Permits.Application/
│   ├── AqtCoreFW.Permits.Application.csproj
│   ├── Commands/
│   ├── Queries/
│   ├── DTOs/
│   └── ModuleRegistration.cs
├── AqtCoreFW.Permits.Infrastructure/
│   ├── AqtCoreFW.Permits.Infrastructure.csproj
│   ├── Persistence/
│   │   ├── PermitsDbContext.cs
│   │   └── Repositories/
│   └── PermitsInfrastructureRegistration.cs
└── AqtCoreFW.Permits.Presentation/
    ├── AqtCoreFW.Permits.Presentation.csproj
    └── Controllers/
```

### Domain .csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../shared/AqtCoreFW.SharedKernel/AqtCoreFW.SharedKernel.csproj" />
    <PackageReference Include="FluentResults" />
  </ItemGroup>
</Project>
```

### Application .csproj
```xml
<ItemGroup>
  <ProjectReference Include=".../AqtCoreFW.Permits.Domain.csproj" />
  <PackageReference Include="MediatR" />
  <PackageReference Include="FluentValidation" />
  <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
</ItemGroup>
```

---

## 2. Create Domain Entity

```csharp
// src/modules/permits/AqtCoreFW.Permits.Domain/Entities/Permit.cs
namespace AqtCoreFW.Permits.Domain.Entities;

public sealed class Permit : AuditableEntity, IAggregateRoot, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string PermitNumber { get; private set; } = string.Empty;
    public string ApplicantName { get; private set; } = string.Empty;
    public PermitStatus Status { get; private set; } = PermitStatus.Pending;

    private Permit() { }

    public static Permit Create(
        Guid tenantId,
        string applicantName,
        Guid createdBy)
    {
        var permit = new Permit
        {
            TenantId = tenantId,
            ApplicantName = applicantName,
            Status = PermitStatus.Pending,
        };
        permit.SetAuditCreate(createdBy);
        return permit;
    }
}
```

---

## 3. Define IRepository in Domain

```csharp
// src/modules/permits/AqtCoreFW.Permits.Domain/Repositories/IPermitRepository.cs
namespace AqtCoreFW.Permits.Domain.Repositories;

public interface IPermitRepository : IRepository<Permit, long>
{
    Task<Permit?> GetByPermitNumberAsync(
        string permitNumber,
        Guid tenantId,
        CancellationToken ct = default);
}
```

---

## 4. Create CQRS Command + Handler

```csharp
// Commands/CreatePermit/CreatePermitCommand.cs
public sealed record CreatePermitCommand(
    Guid TenantId,
    string ApplicantName) : ICommand<PermitDto>;

// Commands/CreatePermit/CreatePermitCommandHandler.cs
public sealed class CreatePermitCommandHandler(
    IPermitRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<CreatePermitCommand, Result<PermitDto>>
{
    public async Task<Result<PermitDto>> Handle(
        CreatePermitCommand request,
        CancellationToken cancellationToken)
    {
        var permit = Permit.Create(
            request.TenantId,
            request.ApplicantName,
            currentUser.UserId);

        await repository.AddAsync(permit, cancellationToken);
        return Result.Ok(MapToDto(permit));
    }

    private static PermitDto MapToDto(Permit p) =>
        new(p.Id, p.TenantId, p.PermitNumber, p.ApplicantName, p.Status.ToString());
}

// Commands/CreatePermit/CreatePermitCommandValidator.cs
public sealed class CreatePermitCommandValidator : AbstractValidator<CreatePermitCommand>
{
    public CreatePermitCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ApplicantName)
            .NotEmpty().WithMessage("Tên người nộp đơn không được để trống.")
            .MaximumLength(200);
    }
}
```

**Read-side convention:** Query handlers use `IReadDbConnection` with Dapper rather than the write repository. Bind every input as a SQL parameter and explicitly filter by `TenantId` and `IsDeleted = 0`, because Dapper does not use EF Core global filters. See the [MasterData dictionary query](../../src/modules/masterdata/AqtCoreFW.MasterData.Application/Queries/GetDictionaries/GetDictionariesQueryHandler.cs) for the established pattern.

---

## 5. Register Module with DI

### Application layer
```csharp
// AqtCoreFW.Permits.Application/ModuleRegistration.cs
public static class ModuleRegistration
{
    public static IServiceCollection AddPermitsModule(this IServiceCollection services)
    {
        // MediatR picks up handlers + validators from this assembly
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ModuleRegistration).Assembly));

        services.AddValidatorsFromAssembly(
            typeof(ModuleRegistration).Assembly,
            includeInternalTypes: true);

        return services;
    }
}
```

### Infrastructure layer
```csharp
// AqtCoreFW.Permits.Infrastructure/PermitsInfrastructureRegistration.cs
public static class PermitsInfrastructureRegistration
{
    public static IServiceCollection AddPermitsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PermitsDbContext>(opts =>
            opts.UseSqlServer(configuration.GetConnectionString("Default")));

        services.AddScoped<IPermitRepository, PermitRepository>();
        return services;
    }
}
```

**Naming Convention (Enforced by Architecture Guard):**
- **Application layer:** Class named `ModuleRegistration`, method `Add{ModuleName}Module()`
- **Infrastructure layer:** Class named `{ModuleName}InfrastructureRegistration`, method `Add{ModuleName}Infrastructure()`
- **File names:** Match class names (`ModuleRegistration.cs`, `PermitsInfrastructureRegistration.cs`)
- All classes must be `public static` and discoverable by the `ModuleMediatRRegistrationTests` guard (class name contains "Registration" or ends with "Module")
- Modules with no separate Application layer (e.g., MasterData, Organization, SystemParams) register everything from the single Infrastructure entry point

---

## 6. Create Controller

```csharp
// AqtCoreFW.Permits.Presentation/Controllers/PermitsController.cs
[Route("api/v1/permits")]
[Authorize]
public sealed class PermitsController(ISender mediator) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePermitCommand command,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(command, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        long id,
        [FromQuery] Guid tenantId,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(new GetPermitByIdQuery(id, tenantId), ct));
}
```

---

## 7. Register in Host Program.cs

```csharp
// src/host/AqtCoreFW.Api/AqtCoreFW.Api.csproj

<!-- Add ProjectReference to Presentation assembly -->
<ItemGroup>
  <ProjectReference Include=".../AqtCoreFW.Permits.Presentation.csproj" />
</ItemGroup>

// src/host/AqtCoreFW.Api/Program.cs

// Register module services (controllers auto-discover via ProjectReference)
builder.Services
    .AddPermitsModule()
    .AddPermitsInfrastructure(builder.Configuration);
```

**Why this works:** ASP.NET Core automatically discovers controllers from project-referenced assemblies via `DependencyContext`. No explicit `AddApplicationPart()` calls needed. The framework scans the dependency graph at startup and registers all controller types from discovered Presentation projects. A presence guard in the test suite (`Api_EveryModulePresentation_ContributesAtLeastOneController`) ensures each module's controllers are properly routed.

**Note:** If single-file / trim / AOT publishing is enabled later, `DependencyContext` discovery may change. In that case, re-introduce explicit `AddApplicationPart()` calls — the presence guard will catch this first as a test failure.

---

## 8. Add EF Core Migration

```bash
# From repo root — targets Infrastructure project, uses Api as startup
dotnet ef migrations add InitialPermits \
  --project src/modules/permits/AqtCoreFW.Permits.Infrastructure \
  --startup-project src/host/AqtCoreFW.Api \
  --context PermitsDbContext
```

---

## 9. Add Tests

Create test projects following the pattern:
```
tests/unit/AqtCoreFW.Permits.Domain.Tests/
tests/unit/AqtCoreFW.Permits.Application.Tests/
tests/integration/AqtCoreFW.Permits.Integration.Tests/
```

Add the new assemblies to `AssemblyFixtures.cs` in the architecture tests project and write an isolation rule for the new module.

---

## 10. Use Feature Flags (V1.5+)

Gate operational behavior with feature flags without code deployment:

```csharp
// Inject IFeatureFlagService (from SharedKernel.Contracts)
public sealed class CreatePermitCommandHandler(
    IPermitRepository repository,
    IFeatureFlagService featureFlags,
    ICurrentUser currentUser)
    : IRequestHandler<CreatePermitCommand, Result<PermitDto>>
{
    public async Task<Result<PermitDto>> Handle(
        CreatePermitCommand request,
        CancellationToken cancellationToken)
    {
        var permit = Permit.Create(
            request.TenantId,
            request.ApplicantName,
            currentUser.UserId);

        // Example: conditionally enable validation via flag
        if (await featureFlags.IsEnabledAsync(
            "permits.strict-validation.enabled",
            cancellationToken))
        {
            permit.ValidateStrict();
        }

        await repository.AddAsync(permit, cancellationToken);
        return Result.Ok(MapToDto(permit));
    }
}
```

**Flag management:**
- Defined in `Infrastructure/FeatureFlags/SeedFlags.cs`
- Toggled in admin UI (when available) or direct DB updates
- Cached in Redis (5 min TTL) — instant effect across instances
- Convention: `{module}.{feature}.{property}` (e.g., `permits.validation.strict-mode`)

---

## Checklist

- [ ] Domain entity extends non-generic `AuditableEntity` (database-assigned `long` ID), implements `IAggregateRoot`, `ITenantScoped`
- [ ] Factory method (`Create(...)`) — no `new` from outside Domain
- [ ] `IPermitRepository` defined in Domain, implemented in Infrastructure
- [ ] Command/Query records are immutable (`sealed record`)
- [ ] Handler returns `Result<T>` — never throws for business errors
- [ ] FluentValidation with Vietnamese error messages
- [ ] Controller inherits `ApiControllerBase`, uses `ToApiResponse()`
- [ ] Module registered in host `Program.cs`
- [ ] Architecture tests updated with new assembly references
- [ ] Unit tests cover: entity creation, state transitions, handler happy path + failure
- [ ] Use feature flags for operational toggles (not code-level conditionals)
