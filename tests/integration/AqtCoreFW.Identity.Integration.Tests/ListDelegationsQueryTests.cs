using AqtCoreFW.Identity.Application.Queries.ListDelegations;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for ListDelegationsQuery via real DB.
///
/// Covers two concerns:
///   1. Status exposure + activeOnly semantics (activeOnly=true → Status=Active only).
///   2. Authorization scope: a non-privileged caller sees only delegations they are a party to;
///      an Admin/SystemAdmin sees a tenant-bound view (no cross-tenant leak). Soft-deleted excluded.
/// Existing status tests run as a privileged caller so they observe the full seeded set.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ListDelegationsQueryTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public ListDelegationsQueryTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task Handle_ListActiveOnly_ExcludesPendingApprovalStatus()
    {
        await SeedMixedDelegationsAsync();

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = PrivilegedQuery(activeOnly: true);

        var result = await mediator.Send(query);

        result.IsSuccess.Should().BeTrue("Query should succeed");
        var items = result.Value.Items;

        items.Should().Contain(i => i.Status == "Active", "At least one Active delegation should be returned");
        items.Should().NotContain(i => i.Status == "PendingApproval", "PendingApproval delegations excluded when activeOnly=true");
        items.Should().AllSatisfy(item => item.Status.Should().Be("Active"), "All returned items must have Active status");
    }

    [Fact]
    public async Task Handle_ListAllDelegations_ReturnsBothActiveAndPendingApproval()
    {
        await SeedMixedDelegationsAsync();

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = PrivilegedQuery(activeOnly: false);

        var result = await mediator.Send(query);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value.Items;

        items.Should().Contain(i => i.Status == "Active", "Active delegations must appear in full list");
        items.Should().Contain(i => i.Status == "PendingApproval", "PendingApproval delegations must appear in full list");
    }

    [Fact]
    public async Task Handle_DtoCarriesStatusAsStringRepresentation()
    {
        await SeedMixedDelegationsAsync();

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = PrivilegedQuery(activeOnly: false);

        var result = await mediator.Send(query);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value.Items;

        items.Should().AllSatisfy(item =>
        {
            item.Status.Should().NotBeNullOrEmpty("Status field must be populated");
            var validStatuses = new[] { "PendingApproval", "Active", "Expired", "Revoked" };
            validStatuses.Should().Contain(item.Status, "Status must be one of the defined enum values");
        });
    }

    // --- Authorization scope ---

    [Fact]
    public async Task Handle_NonPrivilegedActor_SeesOnlyDelegationsTheyArePartyTo()
    {
        // Seed a delegation the actor delegates, one delegated TO the actor, and one between strangers.
        var actor = Guid.NewGuid();
        var other1 = Guid.NewGuid();
        var other2 = Guid.NewGuid();
        var stranger1 = Guid.NewGuid();
        var stranger2 = Guid.NewGuid();
        await SeedUsersAsync((actor, null), (other1, null), (other2, null), (stranger1, null), (stranger2, null));
        var asDelegator = await SeedDelegationAsync(actor, other1);     // actor is delegator
        var asDelegate = await SeedDelegationAsync(other2, actor);      // actor is delegate
        var strangers = await SeedDelegationAsync(stranger1, stranger2); // actor not involved

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new ListDelegationsQuery(
            DelegatorId: null, DelegateId: null, ActiveOnly: false,
            ActorId: actor, IsPrivileged: false, Page: 1, PageSize: 100));

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value.Items.Select(i => i.Id).ToList();
        ids.Should().Contain(asDelegator).And.Contain(asDelegate);
        ids.Should().NotContain(strangers, "a non-privileged caller must not see delegations they are not a party to");
        result.Value.Items.Should().OnlyContain(
            i => i.DelegatorId == actor || i.DelegateId == actor,
            "every returned row must have the actor as a party");
    }

    [Fact]
    public async Task Handle_NonPrivilegedActor_ExcludesSoftDeletedDelegations()
    {
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedUsersAsync((actor, null), (other, null));
        var live = await SeedDelegationAsync(actor, other);
        var deleted = await SeedDelegationAsync(actor, other, softDeleted: true);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new ListDelegationsQuery(
            DelegatorId: null, DelegateId: null, ActiveOnly: false,
            ActorId: actor, IsPrivileged: false, Page: 1, PageSize: 100));

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value.Items.Select(i => i.Id).ToList();
        ids.Should().Contain(live);
        ids.Should().NotContain(deleted, "soft-deleted delegations must not be listed");
    }

    [Fact]
    public async Task Handle_PrivilegedActor_TenantBound_ExcludesOtherTenantDelegation()
    {
        // Two delegations whose parties live in different tenants. A privileged caller scoped to tenant A
        // must see tenant A's delegation and never tenant B's — proven by filtering on each delegator and
        // asserting the tenant-bind removes the out-of-tenant row even when explicitly requested.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var aDelegator = Guid.NewGuid();
        var aDelegate = Guid.NewGuid();
        var bDelegator = Guid.NewGuid();
        var bDelegate = Guid.NewGuid();
        await SeedUsersAsync((aDelegator, tenantA), (aDelegate, tenantA), (bDelegator, tenantB), (bDelegate, tenantB));
        var aDelegation = await SeedDelegationAsync(aDelegator, aDelegate);
        var bDelegation = await SeedDelegationAsync(bDelegator, bDelegate);

        using var scope = _app.Services.CreateScope();
        var handler = new ListDelegationsQueryHandler(
            scope.ServiceProvider.GetRequiredService<IReadDbConnection>(),
            scope.ServiceProvider.GetRequiredService<IIdentityModuleClient>(),
            new StubTenantContext(tenantA, isSystemAdmin: false));

        // Tenant A's own delegation is visible.
        var inTenant = await handler.Handle(new ListDelegationsQuery(
            DelegatorId: aDelegator, DelegateId: null, ActiveOnly: false,
            ActorId: Guid.NewGuid(), IsPrivileged: true, Page: 1, PageSize: 100), CancellationToken.None);
        inTenant.IsSuccess.Should().BeTrue();
        inTenant.Value.Items.Select(i => i.Id).Should().Contain(aDelegation);

        // Tenant B's delegation is filtered out even when its delegator is explicitly requested.
        var crossTenant = await handler.Handle(new ListDelegationsQuery(
            DelegatorId: bDelegator, DelegateId: null, ActiveOnly: false,
            ActorId: Guid.NewGuid(), IsPrivileged: true, Page: 1, PageSize: 100), CancellationToken.None);
        crossTenant.IsSuccess.Should().BeTrue();
        crossTenant.Value.Items.Select(i => i.Id).Should().NotContain(bDelegation,
            "a tenant-A admin must not see a tenant-B delegation");
    }

    // --- Helpers ---

    private static ListDelegationsQuery PrivilegedQuery(bool activeOnly) => new(
        DelegatorId: null, DelegateId: null, ActiveOnly: activeOnly,
        ActorId: Guid.NewGuid(), IsPrivileged: true, Page: 1, PageSize: 20);

    private async Task SeedMixedDelegationsAsync()
    {
        var delegatorId = Guid.NewGuid();
        var delegateId1 = Guid.NewGuid();
        var delegateId2 = Guid.NewGuid();
        await SeedUsersAsync((delegatorId, null), (delegateId1, null), (delegateId2, null));
        await SeedDelegationAsync(delegatorId, delegateId1);                       // Active
        await SeedDelegationAsync(delegatorId, delegateId2, requiresApproval: true); // PendingApproval
    }

    private async Task SeedUsersAsync(params (Guid id, Guid? tenantId)[] users)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.AddRange(users.Select(u => BuildUser(u.id, u.tenantId)));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedDelegationAsync(
        Guid delegatorId, Guid delegateId, bool requiresApproval = false, bool softDeleted = false)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var guidGen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var now = DateTime.UtcNow;

        var delegation = UserDelegation.Create(
            guidGen, delegatorId, delegateId,
            now.AddDays(-1), now.AddDays(30),
            reason: softDeleted ? "Soft-deleted" : "Seeded",
            delegatedRoleIds: null, scopeJson: null,
            requiresApproval: requiresApproval, actorId: Guid.NewGuid());
        if (softDeleted) delegation.Delete();

        db.UserDelegations.Add(delegation);
        await db.SaveChangesAsync();
        return delegation.PublicId;
    }

    // Minimal AspNetUsers row to satisfy the UserDelegations FK (mirrors the project's BuildUser pattern).
    private static ApplicationUser BuildUser(Guid userId, Guid? tenantId)
    {
        var email = $"deleg.test.{userId:N}@example.vn";
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "Delegation-test user",
            IsActive = true,
            TenantId = tenantId,
        };
    }

    private sealed class StubTenantContext(Guid? tenantId, bool isSystemAdmin) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsSystemAdmin => isSystemAdmin;
    }
}
