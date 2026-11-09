using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// Phase-03: Regression tests for GetLatestByKeyAsync tenant fallback fix (2026-04-21).
/// Bug repro: tenant-specific definition missing → must fallback to global (TenantId=Guid.Empty).
///
/// Phase-4b B6:
///   - WorkflowDefinition.Create requires IGuidGenerator as first param (T2 entity).
///   - createdBy Guid required (was implicit before T2 refactor).
/// </summary>
public sealed class WorkflowDefinitionRepositoryTenantFallbackTests : IDisposable
{
    private static readonly Guid GlobalTenantId = Guid.Empty;
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();
    private const string CaseProcessKey = "CaseProcess";

    private readonly WorkflowDbContext _ctx;
    private readonly WorkflowDefinitionRepository _sut;
    private readonly TestGuidGenerator _gen = TestGuidGenerator.Create();

    public WorkflowDefinitionRepositoryTenantFallbackTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(GlobalTenantId);

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"TenantFallbackTests_{Guid.NewGuid()}")
            .Options;

        _ctx = new WorkflowDbContext(options, tenantCtx);
        _sut = new WorkflowDefinitionRepository(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    private WorkflowDefinition CreateDefinition(string key, Guid tenantId)
    {
        // Phase-4b B6: Create requires IGuidGenerator as first param
        var def = WorkflowDefinition.Create(_gen, "Test", "Desc", tenantId, CreatedBy);
        // Use reflection to set DefinitionKey and IsLatest since they're set via factory
        var keyProp = typeof(WorkflowDefinition).GetProperty(nameof(WorkflowDefinition.DefinitionKey));
        keyProp!.SetValue(def, key);
        var latestProp = typeof(WorkflowDefinition).GetProperty(nameof(WorkflowDefinition.IsLatest));
        latestProp!.SetValue(def, true);
        return def;
    }

    [Fact]
    public async Task GetLatestByKeyAsync_TenantSpecificExists_ReturnsTenantVersion()
    {
        // Arrange: both global and tenant-specific exist — tenant override wins
        var globalDef = CreateDefinition(CaseProcessKey, GlobalTenantId);
        var tenantDef = CreateDefinition(CaseProcessKey, TenantA);

        _ctx.WorkflowDefinitions.AddRange(globalDef, tenantDef);
        await _ctx.SaveChangesAsync();

        // Act
        var result = await _sut.GetLatestByKeyAsync(CaseProcessKey, TenantA);

        // Assert: tenant-specific returned, not global
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(tenantDef.Id);
        result.Value.TenantId.Should().Be(TenantA);
    }

    [Fact]
    public async Task GetLatestByKeyAsync_TenantSpecificMissing_GlobalExists_FallsBackToGlobal()
    {
        // Arrange: only global exists — MAIN REGRESSION for 2026-04-21 fix
        var globalDef = CreateDefinition(CaseProcessKey, GlobalTenantId);

        _ctx.WorkflowDefinitions.Add(globalDef);
        await _ctx.SaveChangesAsync();

        // Act: query with tenant-specific ID (no tenant override seeded)
        var result = await _sut.GetLatestByKeyAsync(CaseProcessKey, TenantA);

        // Assert: falls back to global template
        result.IsSuccess.Should().BeTrue("Fallback to global template should succeed");
        result.Value.Id.Should().Be(globalDef.Id);
        result.Value.TenantId.Should().Be(GlobalTenantId);
    }

    [Fact]
    public async Task GetLatestByKeyAsync_BothMissing_ReturnsFailure()
    {
        // Arrange: no definitions seeded at all

        // Act
        var result = await _sut.GetLatestByKeyAsync(CaseProcessKey, TenantA);

        // Assert: 404 path intact
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task GetLatestByKeyAsync_CalledWithEmptyTenant_NoSecondQuery()
    {
        // Arrange: global definition exists, call with Guid.Empty directly
        // This tests the guard `if (definition is null && tenantId != Guid.Empty)` in the repo
        var globalDef = CreateDefinition(CaseProcessKey, GlobalTenantId);

        _ctx.WorkflowDefinitions.Add(globalDef);
        await _ctx.SaveChangesAsync();

        // Act: query with Guid.Empty tenant (should NOT issue a second query)
        var result = await _sut.GetLatestByKeyAsync(CaseProcessKey, GlobalTenantId);

        // Assert: returns global on first query
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(globalDef.Id);
        result.Value.TenantId.Should().Be(GlobalTenantId);
    }
}
