using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.QueryHandlers;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.QueryHandlers;

/// <summary>
/// Unit tests for ListWorkflowInstancesQueryHandler.
///
/// RT-01 proof test: asserts ToQueryString() contains "[Id] &lt;" (not CompareTo)
/// when cursor is applied — proving the keyset predicate is SQL-translatable.
///
/// Uses UseSqlServer("Server=.;Database=fake") for SQL generation only.
/// No actual DB connection is opened — ToQueryString() is a design-time operation.
///
/// Phase-4b B6:
///   WorkflowInstance is T2: PublicId (Guid AK) is the external Id.
///   WorkflowInstance.Id is long (bigint PK) — used for keyset cursor (InternalId in DTO).
///   WorkflowInstanceListItemDto: (Guid Id, long InternalId, long DefinitionId, ..., long CurrentStateId, ...).
///   WorkflowInstance.Create requires IGuidGenerator + long defId + long stateId + Guid createdBy.
///   Cursor keyset: i.Id (long) &lt; cursorInternalId (long) — not Guid comparison.
/// </summary>
public sealed class ListWorkflowInstancesQueryHandlerTests : IDisposable
{
    private readonly WorkflowDbContext _ctx;
    private readonly ListWorkflowInstancesQueryHandler _handler;
    private static readonly Guid TenantId = Guid.NewGuid();

    public ListWorkflowInstancesQueryHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);

        // UseSqlServer with fake connection string — enables SQL generation without connecting.
        // Required for ToQueryString() which needs a relational provider.
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer("Server=.;Database=fake_db_for_query_gen;Trusted_Connection=true;")
            .Options;

        _ctx = new WorkflowDbContext(options, tenantCtx);
        _handler = new ListWorkflowInstancesQueryHandler(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    // ── RT-01: Keyset predicate SQL-translation proof ─────────────────────────

    [Fact]
    public void Query_WithCursor_SqlContainsIdLessThan_NotCompareTo()
    {
        // Arrange: build a valid cursor token using long InternalId (WorkflowInstance.Id bigint PK)
        var cursorTime = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        // Cursor keyset uses long InternalId (not Guid PublicId) for stable pagination
        long cursorInternalId = 42L;

        // Act: materialise the IQueryable to get SQL (without executing against DB)
        // Phase-4b B6: i.Id is long (bigint PK); keyset predicate must be long comparison.
        var q = _ctx.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.TenantId == TenantId)
            .Where(i =>
                i.StartedAt < cursorTime ||
                (i.StartedAt == cursorTime && i.Id < cursorInternalId))
            .OrderByDescending(i => i.StartedAt)
            .ThenByDescending(i => i.Id)
            .Take(51)
            .Select(i => new WorkflowInstanceListItemDto(
                i.PublicId,      // Guid — T2 external AK
                i.Id,            // long — InternalId bigint PK for cursor keyset
                i.DefinitionId,  // long
                i.EntityType,
                i.EntityId,
                i.CurrentStateId, // long
                i.StartedAt,
                i.DueAt,
                i.CompletedAt));

        var sql = q.ToQueryString();

        // [RT-01] Proof: EF translates `i.Id < cursorInternalId` to `[Id] <` (not CompareTo)
        sql.Should().Contain("[Id] <",
            because: "EF must translate `i.Id < lastId` to a SQL-native `[Id] <` comparison, not Guid.CompareTo");

        // Also verify ORDER BY is correct for keyset stability
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("[StartedAt] DESC");
    }

    // ── RT-11: PageSize clamping ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_PageSizeTooLarge_ClampedTo100()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"ClampTest_{Guid.NewGuid()}")
            .Options;
        using var ctx = new WorkflowDbContext(opts, tenantCtx);
        var handler = new ListWorkflowInstancesQueryHandler(ctx);

        var query = new ListWorkflowInstancesQuery(
            TenantId,
            Cursor: null,
            PageSize: 10_000,
            Filter: new InstanceFilter());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.Meta.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_PageSizeZero_ClampedTo1()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"ClampTest_{Guid.NewGuid()}")
            .Options;
        using var ctx = new WorkflowDbContext(opts, tenantCtx);
        var handler = new ListWorkflowInstancesQueryHandler(ctx);

        var query = new ListWorkflowInstancesQuery(
            TenantId,
            Cursor: null,
            PageSize: 0,
            Filter: new InstanceFilter());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Meta.PageSize.Should().Be(1);
    }

    // ── RT-03: Malformed cursor returns BadRequestError ───────────────────────

    [Fact]
    public async Task Handle_MalformedCursor_ReturnsBadRequestError()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"BadCursorTest_{Guid.NewGuid()}")
            .Options;
        using var ctx = new WorkflowDbContext(opts, tenantCtx);
        var handler = new ListWorkflowInstancesQueryHandler(ctx);

        var query = new ListWorkflowInstancesQuery(
            TenantId,
            Cursor: "!!!not-valid-base64!!!",
            PageSize: 50,
            Filter: new InstanceFilter());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is AqtCoreFW.SharedKernel.Errors.BadRequestError);
    }

    // ── RT-07: TotalCount is null in cursor mode ───────────────────────────────

    [Fact]
    public async Task Handle_FirstPage_TotalCountIsNull()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"TotalCountTest_{Guid.NewGuid()}")
            .Options;
        using var ctx = new WorkflowDbContext(opts, tenantCtx);
        var handler = new ListWorkflowInstancesQueryHandler(ctx);

        var result = await handler.Handle(
            new ListWorkflowInstancesQuery(TenantId, null, 50, new InstanceFilter()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().BeNull(
            because: "cursor-mode pagination does not perform COUNT(*) — TotalCount is always null [RT-07]");
    }

    // ── Pagination: peek strategy ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_MoreRowsThanPageSize_HasNextPageTrue_AndNextCursorSet()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"PeekTest_{Guid.NewGuid()}")
            .Options;
        using var ctx = new WorkflowDbContext(opts, tenantCtx);

        // Seed 6 instances for pageSize=5 (peek fetches 6, detects hasNext=true)
        SeedInstances(ctx, count: 6, tenantId: TenantId);
        await ctx.SaveChangesAsync();

        var handler = new ListWorkflowInstancesQueryHandler(ctx);
        var result = await handler.Handle(
            new ListWorkflowInstancesQuery(TenantId, null, 5, new InstanceFilter()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(5);
        result.Value.Meta.HasNextPage.Should().BeTrue();
        result.Value.Meta.NextCursor.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_ExactlyPageSizeRows_HasNextPageFalse()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"ExactTest_{Guid.NewGuid()}")
            .Options;
        using var ctx = new WorkflowDbContext(opts, tenantCtx);

        SeedInstances(ctx, count: 3, tenantId: TenantId);
        await ctx.SaveChangesAsync();

        var handler = new ListWorkflowInstancesQueryHandler(ctx);
        var result = await handler.Handle(
            new ListWorkflowInstancesQuery(TenantId, null, 3, new InstanceFilter()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.Meta.HasNextPage.Should().BeFalse();
        result.Value.Meta.NextCursor.Should().BeNull();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds WorkflowInstance rows using T2-compliant Create signature.
    /// Phase-4b B6: requires IGuidGenerator + long defId + long stateId + Guid createdBy.
    /// defId=0/stateId=0 acceptable for in-memory unit tests (no FK validation).
    /// </summary>
    private static void SeedInstances(WorkflowDbContext ctx, int count, Guid tenantId)
    {
        var gen = TestGuidGenerator.Create();
        var actorId = Guid.NewGuid();

        for (var i = 0; i < count; i++)
        {
            var instance = AqtCoreFW.Workflow.Domain.Entities.WorkflowInstance.Create(
                gen,
                definitionId: 0L,   // placeholder — InMemory has no FK constraint
                initialStateId: 0L, // placeholder
                entityType: "TestEntity",
                entityId: Guid.NewGuid(),
                tenantId: tenantId,
                createdBy: actorId);
            instance.SetAuditCreate(actorId);
            ctx.WorkflowInstances.Add(instance);
        }
    }
}
