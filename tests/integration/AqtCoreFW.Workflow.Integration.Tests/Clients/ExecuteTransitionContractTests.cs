using AqtCoreFW.Api;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Integration.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Contract = AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Workflow.Integration.Tests.Clients;

/// <summary>
/// COVR-1 (post-PR#71 restoration) — smoke test for IWorkflowModuleClient.ExecuteTransitionAsync
/// against a generic 2-state inline workflow definition (no Cases dependency).
///
/// Verifies the delegation chain end-to-end:
///   Client → InProcessWorkflowModuleClient → ExecuteTransitionCommandHandler
///   → ApplyTransition (mutates state + appends history) → UpdateWithStateGuardAsync.
///
/// Permission resolution is stubbed via IIdentityModuleClient (scoped override) — that is the
/// surface called by ExecuteTransitionCommandHandler:60-67. Identity-side permission DB seeding
/// is out of scope.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ExecuteTransitionContractTests : IClassFixture<WebAppFixture>
{
    private const string SubmitPermission = "WORKFLOW.TEST.SUBMIT";
    private readonly WebAppFixture _fixture;

    public ExecuteTransitionContractTests(WebAppFixture fixture)
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

    [Fact]
    public async Task ExecuteTransitionAsync_SubmitFromDraft_AdvancesStateAndAppendsHistory()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var app = WithPermissionStub(SubmitPermission);
        _ = app.CreateClient();

        // S3 Slice 4: factory no longer accepts requiredPermissionCode (entity field removed).
        // RPC gate replaced by resolver in Slice 5; permission stub remains wired for Slice-5 readiness.
        var seeded = await InlineWorkflowDefinitionFactory.CreateAsync(
            app.Services, tenantId, actor);

        // Slice-5: authz-of-T gate is fail-CLOSED; seed a ByPermission assignment on the Submit
        // transition so the test actor (who holds SubmitPermission via the stub) is authorized.
        // Submitted is isFinal=true so WF_NO_HANDLER pre-check does NOT fire — no extra seeding needed.
        await InlineWorkflowDefinitionFactory.SeedAuthorizingAssignmentAsync(
            app.Services, seeded.SubmitTransitionId, SubmitPermission);

        // instanceId = PublicId (Guid T2 AK) — cross-module contract uses Guid, not internal long PK.
        Guid instanceId;
        await using (var setupScope = app.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var gen = setupScope.ServiceProvider.GetRequiredService<IGuidGenerator>();
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
            instanceId = instance.PublicId;             // T2 Guid AK — used in contract request
        }

        await using var actScope = app.Services.CreateAsyncScope();
        var client = actScope.ServiceProvider.GetRequiredService<IWorkflowModuleClient>();

        var outcome = await client.ExecuteTransitionAsync(new Contract.ExecuteTransitionRequest(
            InstanceId: instanceId,
            ActionName: "Submit",
            ActorId: actor,
            TenantId: tenantId,
            Comment: "Initial submission"));

        outcome.IsSuccess.Should().BeTrue(
            $"transition should succeed (error: {outcome.ErrorCode} — {outcome.ErrorMessage})");
        outcome.Result.Should().NotBeNull();
        // WorkflowState is T1 (no Guid AK) — InProcessWorkflowModuleClient returns Guid.Empty for NewStateId.
        // Verified Phase-4b B4c: no consumer reads NewStateId from ExecuteTransitionResult.
        // Candidate for contract cleanup Phase-4c.
        outcome.Result!.NewStateId.Should().Be(Guid.Empty);
        outcome.Result.NewStateName.Should().Be("Submitted");
        outcome.Result.IsFinal.Should().BeTrue("Submitted is the final state in the inline definition");

        await using var verifyScope = app.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        // Filter by PublicId (Guid AK) — instanceId is T2 public identifier, not internal long PK.
        var reloaded = await verifyDb.Set<WorkflowInstance>()
            .IgnoreQueryFilters()
            .Include(i => i.History)
            .FirstAsync(i => i.PublicId == instanceId);

        // CurrentStateId is long (T1 FK) — compare against SubmittedStateId long PK.
        reloaded.CurrentStateId.Should().Be(seeded.SubmittedStateId);
        reloaded.History.Should().HaveCount(1,
            "ApplyTransition must append exactly one WorkflowInstanceHistory row");
        reloaded.History[0].ActionName.Should().Be("Submit");
        reloaded.History[0].TransitionedBy.Should().Be(actor);
    }

    /// <summary>Minimal IIdentityModuleClient stub — returns a fixed permission set for any actor.</summary>
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
