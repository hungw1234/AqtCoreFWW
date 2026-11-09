using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Infrastructure.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.TenantIsolation.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// Verifies that the RuleSetRepository inline tenant filter and write guards
/// enforce cross-tenant isolation against a real SQL Server instance.
///
/// Design notes:
///   - Each test inserts its own data and operates on an independent DbContext scope.
///   - "Seed" contexts use IsSystemAdmin=true (NullTenantContext behaviour) to bypass the
///     filter when inserting data for multiple tenants in a single test.
///   - Repository write guards enforce strict tenant matching — no SystemAdmin bypass.
/// </summary>
[Collection(RulesTenantIsolationCollection.CollectionName)]
public sealed class RuleSetTenantIsolationTests
{
    private readonly RulesTenantIsolationFixture _fixture;

    private static readonly Guid Actor = Guid.NewGuid();

    // T2 entity: IGuidGenerator required by RuleSet.Create to assign PublicId.
    private static IGuidGenerator MockGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    public RuleSetTenantIsolationTests(RulesTenantIsolationFixture fixture)
    {
        _fixture = fixture;
    }

    // -------------------------------------------------------------------------
    // I1: CreateRuleSet_AsTenantA_SucceedsAndIsVisibleToTenantA
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateRuleSet_AsTenantA_SucceedsAndIsVisibleToTenantA()
    {
        var tenantA = Guid.NewGuid();
        Guid ruleSetId;

        // Seed via admin context (IsSystemAdmin=true) to create data
        await using (var adminCtx = _fixture.CreateContext(new TestTenantContext(null, isSystemAdmin: true)))
        {
            var ruleSet = MakeRuleSet(tenantA, "I1-TenantA-RuleSet");
            ruleSetId = ruleSet.PublicId; // T2: external addressing via PublicId (Guid AK)
            adminCtx.RuleSets.Add(ruleSet);
            await adminCtx.SaveChangesAsync();
        }

        // Query as TenantA — should see the RuleSet
        await using var ctxA = _fixture.CreateContext(new TestTenantContext(tenantA));
        var repository = new RuleSetRepository(ctxA, new TestTenantContext(tenantA), NullLogger<RuleSetRepository>.Instance);

        var result = await repository.GetByPublicIdAsync(ruleSetId);

        result.Should().NotBeNull("TenantA should see its own RuleSet");
        result!.TenantId.Should().Be(tenantA);
        result.Name.Should().Be("I1-TenantA-RuleSet");
    }

    // -------------------------------------------------------------------------
    // I2: GetRuleSet_AsTenantB_AgainstTenantACreatedRuleSet_ReturnsNull
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRuleSet_AsTenantB_AgainstTenantACreatedRuleSet_ReturnsNull()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid ruleSetId;

        // Seed RuleSet with TenantId = TenantA (admin context)
        await using (var adminCtx = _fixture.CreateContext(new TestTenantContext(null, isSystemAdmin: true)))
        {
            var ruleSet = MakeRuleSet(tenantA, "I2-TenantA-RuleSet");
            ruleSetId = ruleSet.PublicId; // T2: external addressing via PublicId (Guid AK)
            adminCtx.RuleSets.Add(ruleSet);
            await adminCtx.SaveChangesAsync();
        }

        // Query as TenantB — should NOT see TenantA's RuleSet (cross-tenant filtered)
        await using var ctxB = _fixture.CreateContext(new TestTenantContext(tenantB));
        var repository = new RuleSetRepository(ctxB, new TestTenantContext(tenantB), NullLogger<RuleSetRepository>.Instance);

        var result = await repository.GetByPublicIdAsync(ruleSetId);

        result.Should().BeNull("TenantB must not see RuleSet belonging to TenantA");
    }

    // -------------------------------------------------------------------------
    // I3: UpdateRuleSet_AsTenantB_AgainstTenantAEntity_ThrowsInvalidOperationException
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRuleSet_AsTenantB_AgainstTenantAEntity_ThrowsInvalidOperationException()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        RuleSet ruleSet;

        // Seed RuleSet as admin with TenantId = TenantA
        await using (var adminCtx = _fixture.CreateContext(new TestTenantContext(null, isSystemAdmin: true)))
        {
            ruleSet = MakeRuleSet(tenantA, "I3-TenantA-RuleSet");
            adminCtx.RuleSets.Add(ruleSet);
            await adminCtx.SaveChangesAsync();
        }

        // Load via admin context so we have a valid entity reference
        await using (var adminCtx2 = _fixture.CreateContext(new TestTenantContext(null, isSystemAdmin: true)))
        {
            var adminRepo = new RuleSetRepository(adminCtx2, new TestTenantContext(null, isSystemAdmin: true), NullLogger<RuleSetRepository>.Instance);
            ruleSet = (await adminRepo.GetByPublicIdAsync(ruleSet.PublicId))!; // T2: external addressing via PublicId
        }

        // Create TenantB context and repository — attempt cross-tenant update
        await using var ctxB = _fixture.CreateContext(new TestTenantContext(tenantB));
        var tenantBRepo = new RuleSetRepository(ctxB, new TestTenantContext(tenantB), NullLogger<RuleSetRepository>.Instance);

        // Act & Assert: UpdateAsync MUST throw InvalidOperationException
        var act = async () => await tenantBRepo.UpdateAsync(ruleSet);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant RuleSet update rejected*",
                "repo guard enforces strict tenant match on writes");
    }

    // -------------------------------------------------------------------------
    // I4: SystemAdmin_CanReadAllTenants_ButWriteStillRequiresMatchingTenantId
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SystemAdmin_CanReadAllTenants_ButWriteStillRequiresMatchingTenantId()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid ruleSetAId, ruleSetBId;

        // Seed 2 RuleSets (TenantA + TenantB) via admin
        await using (var adminCtx = _fixture.CreateContext(new TestTenantContext(null, isSystemAdmin: true)))
        {
            var ruleSetA = MakeRuleSet(tenantA, "I4-TenantA-RuleSet");
            var ruleSetB = MakeRuleSet(tenantB, "I4-TenantB-RuleSet");
            ruleSetAId = ruleSetA.PublicId; // T2: external addressing via PublicId (Guid AK)
            ruleSetBId = ruleSetB.PublicId; // T2: external addressing via PublicId (Guid AK)
            adminCtx.RuleSets.AddRange(ruleSetA, ruleSetB);
            await adminCtx.SaveChangesAsync();
        }

        // SystemAdmin context (IsSystemAdmin=true, TenantId=null) should read both
        await using var adminQueryCtx = _fixture.CreateContext(new TestTenantContext(null, isSystemAdmin: true));
        var adminRepo = new RuleSetRepository(adminQueryCtx, new TestTenantContext(null, isSystemAdmin: true), NullLogger<RuleSetRepository>.Instance);

        var resultA = await adminRepo.GetByPublicIdAsync(ruleSetAId);
        var resultB = await adminRepo.GetByPublicIdAsync(ruleSetBId);

        resultA.Should().NotBeNull("SystemAdmin should read TenantA's RuleSet");
        resultB.Should().NotBeNull("SystemAdmin should read TenantB's RuleSet");

        // Now attempt admin write with a RuleSet whose TenantId doesn't match admin's context
        // Admin context has TenantId=null — any non-null TenantId mismatches
        var foreignTenantId = Guid.NewGuid();
        var newRuleSet = MakeRuleSet(foreignTenantId, "I4-Foreign-RuleSet");

        // Act & Assert: AddAsync MUST throw — strict write guard, no admin bypass
        var act = async () => await adminRepo.AddAsync(newRuleSet);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant RuleSet create rejected*",
                "admin read-bypass does NOT extend to writes — prevents mis-tagging");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static RuleSet MakeRuleSet(Guid tenantId, string name)
        => RuleSet.Create(
            gen: MockGen(),
            name: name,
            tenantId: tenantId,
            createdBy: Actor,
            description: $"Integration test RuleSet for {name}");
}
