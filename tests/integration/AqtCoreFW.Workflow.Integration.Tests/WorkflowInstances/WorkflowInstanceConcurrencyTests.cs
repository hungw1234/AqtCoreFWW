using AqtCoreFW.Api;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Integration.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Contract = AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Workflow.Integration.Tests.WorkflowInstances;

/// <summary>
/// Concurrency guard integration test for WorkflowInstance.CurrentStateId on a generic
/// 2-state inline workflow definition (restored post-PR#71).
///
/// Scenario A (sequential): Second transition on same instance fails with InvalidTransition
///   because the first has already moved CurrentStateId off Draft.
///
/// The simultaneous-race counterpart (EF IsConcurrencyToken on CurrentStateId → StateConflict)
/// is covered deterministically by WorkflowInstanceConcurrencyTokenTests (Infrastructure.Tests),
/// which reuses WinnerFirstInterceptor to force winner-before-loser ordering. The former
/// Task.WhenAll variant lived here but was probabilistic (app-layer guard frequently won all
/// attempts) and has been removed.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowInstanceConcurrencyTests : IClassFixture<WebAppFixture>
{
    private const string SubmitPermission = "WORKFLOW.TEST.SUBMIT";
    private readonly WebAppFixture _fixture;

    public WorkflowInstanceConcurrencyTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> WithPermissionStub(params string[] permissions) =>
        _fixture.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentityModuleClient>();
                services.AddScoped<IIdentityModuleClient>(_ => new StubIdentityClient(permissions));
            });
        });

    // Returns PublicId (Guid T2 AK) — cross-module contract uses Guid, not internal long PK.
    private static async Task<Guid> CreateInstanceAtDraftAsync(
        WebApplicationFactory<Program> factory,
        InlineWorkflowDefinitionFactory.SeededDefinition seeded,
        Guid actor,
        Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var instance = WorkflowInstance.Create(
            gen: gen,
            definitionId: seeded.DefinitionId,      // long (T1 WorkflowDefinition.Id)
            initialStateId: seeded.DraftStateId,    // long (T1 WorkflowState.Id)
            entityType: "IntegrationTest",
            entityId: Guid.NewGuid(),
            tenantId: tenantId,
            createdBy: actor);
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance.PublicId;   // T2 Guid AK — used in ExecuteTransitionRequest.InstanceId
    }

    /// <summary>Each parallel task gets its own DI scope to avoid EF tracker cross-talk.</summary>
    private static async Task<Contract.ExecuteTransitionOutcome> ExecuteWithOwnScope(
        WebApplicationFactory<Program> factory,
        Guid instanceId,
        Guid actorId,
        Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IWorkflowModuleClient>();
        return await client.ExecuteTransitionAsync(new Contract.ExecuteTransitionRequest(
            InstanceId: instanceId,
            ActionName: "Submit",
            ActorId: actorId,
            TenantId: tenantId,
            Comment: "Concurrency test transition"));
    }

    // ── Scenario A — Sequential race ────────────────────────────────────────────

    [Fact]
    public async Task SequentialRace_SecondTransition_FailsWithInvalidTransition()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var app = WithPermissionStub(SubmitPermission);
        _ = app.CreateClient();

        // S3 Slice 4: factory no longer accepts requiredPermissionCode (entity field removed).
        // RPC gate replaced by resolver in Slice 5; permission stub still wired for Slice-5 readiness.
        var seeded = await InlineWorkflowDefinitionFactory.CreateAsync(
            app.Services, tenantId, actor);

        // Slice-5: authz-of-T gate is fail-CLOSED; seed a ByPermission assignment on the Submit
        // transition so the test actor (who holds SubmitPermission via the stub) is authorized.
        // Submitted is isFinal=true so WF_NO_HANDLER pre-check does NOT fire.
        await InlineWorkflowDefinitionFactory.SeedAuthorizingAssignmentAsync(
            app.Services, seeded.SubmitTransitionId, SubmitPermission);

        var instanceId = await CreateInstanceAtDraftAsync(app, seeded, actor, tenantId);

        var outcome1 = await ExecuteWithOwnScope(app, instanceId, actor, tenantId);
        outcome1.IsSuccess.Should().BeTrue(
            $"first transition should succeed (error: {outcome1.ErrorCode})");
        outcome1.Result!.NewStateName.Should().Be("Submitted");

        var outcome2 = await ExecuteWithOwnScope(app, instanceId, actor, tenantId);

        outcome2.IsSuccess.Should().BeFalse("second transition should fail (state already moved)");
        outcome2.ErrorCode.Should().Be(WorkflowErrorCodes.InvalidTransition,
            "sequential race: app-layer guard catches invalid from-state before DB round-trip");

        await using var verifyScope = app.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        // Filter by PublicId (Guid AK) — instanceId is T2 public identifier.
        var reloaded = await verifyDb.Set<WorkflowInstance>()
            .IgnoreQueryFilters()
            .Include(i => i.History)
            .FirstAsync(i => i.PublicId == instanceId);

        // CurrentStateId is long (T1 FK) — compare against SubmittedStateId long PK.
        reloaded.CurrentStateId.Should().Be(seeded.SubmittedStateId);
        reloaded.History.Should().HaveCount(1, "only the first transition should have committed");
    }

    /// <summary>Minimal IIdentityModuleClient stub — returns fixed permission set.</summary>
    private sealed class StubIdentityClient(IReadOnlyCollection<string> permissions) : IIdentityModuleClient
    {
        private readonly IReadOnlySet<string> _set = permissions.ToHashSet();

        public Task<UserInfoDto?> FindByEmailAsync(string email, Guid? tenantId = null, CancellationToken ct = default)
            => Task.FromResult<UserInfoDto?>(null);

        public Task<IReadOnlyDictionary<Guid, UserInfoDto>> GetUserInfoByIdsAsync(
            IEnumerable<Guid> userIds, Guid? tenantId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, UserInfoDto>>(new Dictionary<Guid, UserInfoDto>());

        public Task<IReadOnlyDictionary<Guid, string?>> GetUserPhoneNumbersByIdsAsync(
            IEnumerable<Guid> userIds, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string?>>(new Dictionary<Guid, string?>());

        public Task<IReadOnlyList<Guid>> GetUserIdsInTenantAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlySet<string>> GetEffectivePermissionCodesAsync(
            Guid userId, CancellationToken ct = default)
            => Task.FromResult(_set);

        public Task<UserActiveStatusDto> GetUserActiveStatusAsync(
            Guid userId, CancellationToken ct = default)
            => Task.FromResult(new UserActiveStatusDto { IsActive = true, IsLockedOut = false });

        public Task<IReadOnlySet<string>> GetUserRoleCodesAsync(
            Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

        public Task<IReadOnlySet<string>> GetUserGroupCodesAsync(
            Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

        public Task<IReadOnlyList<Guid>> ResolveActiveUsersInGroupsAsync(
            IReadOnlyList<string> groupCodes, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlyList<Guid>> ResolveActiveUsersInRolesAsync(
            IReadOnlyList<string> roleNames, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlySet<string>> ResolveActiveUserGroupsByCodesAsync(
            IReadOnlyCollection<string> codes, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }
}
