using System.Text.Json;
using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Application.Queries.WorkflowTaxonomy;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;

namespace AqtCoreFW.MasterData.Tests.Queries;

/// <summary>
/// TC-MD-Q005: Workflow taxonomy queries — cache behavior + bypass conditions (plan D5).
/// Redis-only cache: default-params path cached with 15min TTL; filtered/search paths DB direct.
/// </summary>
public sealed class WorkflowTaxonomyQueriesTests
{
    // --- State: cache hit ---

    [Fact]
    public async Task States_DefaultParams_CacheHit_ReturnsCachedResult_DbNotQueried()
    {
        var db = Substitute.For<IReadDbConnection>();
        var cache = Substitute.For<IDistributedCache>();

        var cached = new List<WorkflowStateTaxonomyDto>
        {
            new(Guid.NewGuid(), "Draft", "Nháp", "Draft", null, null, null, "Initial", 10, true, null, []),
        };
        cache.GetAsync(GetWorkflowStateTaxonomiesQueryHandler.CacheKey, Arg.Any<CancellationToken>())
            .Returns(JsonSerializer.SerializeToUtf8Bytes(cached));

        var handler = new GetWorkflowStateTaxonomiesQueryHandler(db, cache);
        var result = await handler.Handle(new GetWorkflowStateTaxonomiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Code.Should().Be("Draft");
        await db.DidNotReceive().QueryAsync<WorkflowStateTaxonomyRow>(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task States_DefaultParams_CacheMiss_QueriesDb_PopulatesCache()
    {
        var db = Substitute.For<IReadDbConnection>();
        var cache = Substitute.For<IDistributedCache>();

        cache.GetAsync(GetWorkflowStateTaxonomiesQueryHandler.CacheKey, Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        db.QueryAsync<WorkflowStateTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(new[]
            {
                new WorkflowStateTaxonomyRow
                {
                    Id = Guid.NewGuid(), Code = "Draft", NameVi = "Nháp", NameEn = "Draft",
                    Category = "Initial", SortOrder = 10, IsActive = true, RowVersion = [1],
                    TotalCount = 1,
                },
            });

        var handler = new GetWorkflowStateTaxonomiesQueryHandler(db, cache);
        var result = await handler.Handle(new GetWorkflowStateTaxonomiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        await cache.Received(1).SetAsync(
            GetWorkflowStateTaxonomiesQueryHandler.CacheKey,
            Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task States_WithSearch_BypassesCache_NoCacheReadOrWrite()
    {
        var db = Substitute.For<IReadDbConnection>();
        var cache = Substitute.For<IDistributedCache>();

        db.QueryAsync<WorkflowStateTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns([]);

        var handler = new GetWorkflowStateTaxonomiesQueryHandler(db, cache);
        await handler.Handle(new GetWorkflowStateTaxonomiesQuery(Search: "Dra"), CancellationToken.None);

        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task States_IncludeInactive_BypassesCache()
    {
        var db = Substitute.For<IReadDbConnection>();
        var cache = Substitute.For<IDistributedCache>();

        db.QueryAsync<WorkflowStateTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns([]);

        var handler = new GetWorkflowStateTaxonomiesQueryHandler(db, cache);
        await handler.Handle(new GetWorkflowStateTaxonomiesQuery(IncludeInactive: true), CancellationToken.None);

        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task States_NonDefaultPageSize_BypassesCache()
    {
        var db = Substitute.For<IReadDbConnection>();
        var cache = Substitute.For<IDistributedCache>();

        db.QueryAsync<WorkflowStateTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns([]);

        var handler = new GetWorkflowStateTaxonomiesQueryHandler(db, cache);
        await handler.Handle(new GetWorkflowStateTaxonomiesQuery(PageSize: 50), CancellationToken.None);

        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- State: detail by id ---

    [Fact]
    public async Task StateById_NotFound_ReturnsFail()
    {
        var db = Substitute.For<IReadDbConnection>();
        db.QueryAsync<WorkflowStateTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns([]);

        var handler = new GetWorkflowStateTaxonomyByIdQueryHandler(db);
        var result = await handler.Handle(new GetWorkflowStateTaxonomyByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task StateById_Found_ReturnsDto_WithRowVersion()
    {
        var db = Substitute.For<IReadDbConnection>();
        var id = Guid.NewGuid();
        db.QueryAsync<WorkflowStateTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(new[]
            {
                new WorkflowStateTaxonomyRow
                {
                    Id = id, Code = "Draft", NameVi = "Nháp", NameEn = "Draft",
                    Category = "Initial", SortOrder = 10, IsActive = true,
                    RowVersion = [0x01, 0x02, 0x03],
                },
            });

        var handler = new GetWorkflowStateTaxonomyByIdQueryHandler(db);
        var result = await handler.Handle(new GetWorkflowStateTaxonomyByIdQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.RowVersion.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03 });
    }

    // --- Action: smoke ---

    [Fact]
    public async Task Actions_DefaultParams_UsesCorrectCacheKey()
    {
        var db = Substitute.For<IReadDbConnection>();
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(GetWorkflowActionTaxonomiesQueryHandler.CacheKey, Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);
        db.QueryAsync<WorkflowActionTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns([]);

        var handler = new GetWorkflowActionTaxonomiesQueryHandler(db, cache);
        await handler.Handle(new GetWorkflowActionTaxonomiesQuery(), CancellationToken.None);

        await cache.Received(1).GetAsync(GetWorkflowActionTaxonomiesQueryHandler.CacheKey, Arg.Any<CancellationToken>());
        GetWorkflowActionTaxonomiesQueryHandler.CacheKey.Should().Be("mst:workflow-action-taxonomy:all");
    }

    [Fact]
    public async Task Actions_ById_Found_ReturnsDto()
    {
        var db = Substitute.For<IReadDbConnection>();
        var id = Guid.NewGuid();
        db.QueryAsync<WorkflowActionTaxonomyRow>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(new[]
            {
                new WorkflowActionTaxonomyRow
                {
                    Id = id, Code = "Submit", NameVi = "Nộp", NameEn = "Submit",
                    SortOrder = 10, IsActive = true, RowVersion = [0xAA],
                },
            });

        var handler = new GetWorkflowActionTaxonomyByIdQueryHandler(db);
        var result = await handler.Handle(new GetWorkflowActionTaxonomyByIdQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.Code.Should().Be("Submit");
    }

    // --- Cache key contract ---

    [Fact]
    public void CacheKeys_AreGlobalNotTenantScoped()
    {
        // Plan D5: taxonomy is global MVP, cache key must not contain tenant ID
        GetWorkflowStateTaxonomiesQueryHandler.CacheKey.Should().NotContain("tenant");
        GetWorkflowActionTaxonomiesQueryHandler.CacheKey.Should().NotContain("tenant");
        GetWorkflowStateTaxonomiesQueryHandler.CacheKey.Should().Be("mst:workflow-state-taxonomy:all");
    }
}
