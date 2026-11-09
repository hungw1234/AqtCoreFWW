using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Application.Commands.CreateEscalationRule;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// Regression coverage for the three "create rule" handlers that resolve a WorkflowDefinition by its
/// PublicId (Guid AK) to its internal long FK before persisting:
///   CreateAssignmentRuleCommandHandler, CreateNotificationConfigCommandHandler, CreateEscalationRuleCommandHandler.
///
/// All three call IWorkflowDefinitionRepository.GetByIdAsync(Guid). That method was silently broken until
/// WorkflowDefinitionRepository overrode it to resolve by PublicId — the generic GenericRepository
/// implementation issued EF FindAsync, which targets the long PRIMARY KEY, not the Guid PublicId AK, so a
/// Guid lookup fails on the key-type mismatch → every create failed, persisting no row. These run against
/// REAL EF + Testcontainers SQL Server through the real repository override, so they exercise the exact
/// PublicId→long resolution (unit mocks of the repo are structurally blind to this — they stub
/// GetByIdAsync to succeed).
///
/// Non-vacuity: each fact asserts the persisted rule's long definition FK equals the seeded definition's
/// internal Id. Pre-fix the Guid lookup fails → no row at all → the assertion fails.
///
/// HTTP path (assignment, notification) also covers controller wiring; escalation has no create endpoint,
/// so it is driven handler-direct with real DI-resolved dependencies (sidesteps the mediator pipeline,
/// which is not what this regression guards).
///
/// Auth: WebAppFixture's TestSystemAdminPermissionBypassHandler grants effective SystemAdmin, so the
/// Workflow.Assign.Manage / Workflow.Def.Manage policies on the create endpoints are satisfied.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowCreateRuleHandlersDefinitionResolutionTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public WorkflowCreateRuleHandlersDefinitionResolutionTests(WebAppFixture app)
    {
        _app = app;
    }

    private sealed record SeededDefinition(Guid PublicId, long InternalId);

    private async Task<SeededDefinition> SeedEmptyDefinitionAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(
            gen: gen,
            name: $"create-rule-handlers-coverage {Guid.NewGuid():N}",
            description: "Definition for create-rule handler PublicId-resolution integration tests",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"crh-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();
        return new SeededDefinition(def.PublicId, def.Id);
    }

    // ── Assignment rule (HTTP POST /api/v1/workflow/assignments) ───────────────────

    [Fact]
    public async Task CreateAssignmentRule_ResolvesDefinitionByPublicId_PersistsWithInternalLongFk()
    {
        var def = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var resp = await client.PostAsJsonAsync("/api/v1/workflow/assignments", new
        {
            workflowDefinitionId = def.PublicId, // Guid PublicId AK — handler must resolve to internal long
            entityType = "Generic",
            entitySubType = (string?)null,
            priority = 10,
            description = "create-rule-handlers-coverage assignment rule",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the PublicId resolves to the seeded definition (pre-fix the Guid lookup failed → no row persisted)");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var rule = await db.Set<WorkflowAssignmentRule>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.WorkflowDefinitionId == def.InternalId);

        rule.Should().NotBeNull("the rule must persist against the resolved internal long FK, not be dropped on a failed lookup");
        rule!.TenantId.Should().Be(TenantId);
    }

    // ── Notification config (HTTP POST /definitions/{id}/notification-configs) ──────

    [Fact]
    public async Task CreateNotificationConfig_ResolvesDefinitionByPublicId_PersistsWithInternalLongFk()
    {
        var def = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // DefinitionId comes from the route {id}; the body carries only the config fields.
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/workflow/definitions/{def.PublicId}/notification-configs", new
            {
                actionName = "Submit",
                channel = "Email",
                recipientType = "user",
                recipientValue = ActorId.ToString(),
                subject = "create-rule-handlers-coverage notification",
                bodyTemplate = "Body for {action}",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the route PublicId resolves to the seeded definition (pre-fix the Guid lookup failed → no row persisted)");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var config = await db.Set<WorkflowNotificationConfig>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.DefinitionId == def.InternalId);

        config.Should().NotBeNull("the config must persist against the resolved internal long FK");
        config!.ActionName.Should().Be("Submit");
    }

    // ── Escalation rule (handler-direct — no create endpoint exists) ────────────────

    [Fact]
    public async Task CreateEscalationRule_ResolvesDefinitionByPublicId_PersistsWithInternalLongFk()
    {
        var def = await SeedEmptyDefinitionAsync();

        await using var scope = _app.Services.CreateAsyncScope();
        // Real DI-resolved dependencies — IWorkflowDefinitionRepository is the concrete
        // WorkflowDefinitionRepository whose GetByIdAsync(Guid) override is under test.
        var definitionRepository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<EscalationRule, long>>();
        var handler = new CreateEscalationRuleCommandHandler(repository, definitionRepository);

        var result = await handler.Handle(
            new CreateEscalationRuleCommand(
                WorkflowDefinitionId: def.PublicId, // Guid PublicId AK — must resolve to internal long
                StateId: null,
                EscalationAfterMinutes: 60,
                EscalateTo: EscalationTarget.Manager,
                EscalateToValue: "manager@example.com",
                NotifyVia: "Email",
                TenantId: TenantId,
                CreatedBy: ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "the PublicId resolves to the seeded definition (pre-fix the generic Find-by-PK lookup failed on the Guid-vs-long key mismatch → no row persisted)");

        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var rule = await db.Set<EscalationRule>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.WorkflowDefinitionId == def.InternalId);

        rule.Should().NotBeNull("the rule must persist against the resolved internal long FK");
        rule!.EscalationAfterMinutes.Should().Be(60);
    }
}
