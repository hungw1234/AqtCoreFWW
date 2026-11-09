using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Queries.GetAuditLogs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Audit.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetAuditLogsQueryHandler.
/// Handler uses IReadDbConnection (Dapper read-side) — two SQL calls: COUNT + data rows.
/// WHERE clause construction is exercised via optional filter combos.
/// Enforces tenant isolation via ITenantContext.
/// </summary>
public sealed class GetAuditLogsQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IFeatureFlagService _featureFlags = Substitute.For<IFeatureFlagService>();
    private readonly ILogger<GetAuditLogsQueryHandler> _logger = Substitute.For<ILogger<GetAuditLogsQueryHandler>>();
    private readonly GetAuditLogsQueryHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public GetAuditLogsQueryHandlerTests()
    {
        // Default: non-SystemAdmin, enforces TenantId isolation
        _tenantContext.IsSystemAdmin.Returns(false);
        _tenantContext.TenantId.Returns(TenantId);
        _sut = new GetAuditLogsQueryHandler(_db, _tenantContext, _featureFlags, _logger);
    }

    // --- Happy path: filtered paginated results ---

    [Fact]
    public async Task Handle_WithResults_ReturnsSuccessPagedResult()
    {
        var rows = BuildDtos(5);
        SetupDb(totalCount: 5, rows);

        var query = BuildQuery(tenantId: TenantId);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(5);
        result.Value.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task Handle_MultiPageResult_MetaIsCorrect()
    {
        SetupDb(totalCount: 42, BuildDtos(10));

        var query = BuildQuery(page: 2, pageSize: 10);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.Value.Meta.Page.Should().Be(2);
        result.Value.Meta.PageSize.Should().Be(10);
        result.Value.Meta.TotalPages.Should().Be(5);  // ceil(42/10)
        result.Value.Meta.HasNextPage.Should().BeTrue();
    }

    // --- Empty result when no match ---

    [Fact]
    public async Task Handle_NoMatch_ReturnsEmptyPagedResult()
    {
        SetupDb(totalCount: 0, []);

        var query = BuildQuery(action: "NonExistentAction");
        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NoMatch_HasNextPageIsFalse()
    {
        SetupDb(totalCount: 0, []);

        var result = await _sut.Handle(BuildQuery(), CancellationToken.None);

        result.Value.Meta.HasNextPage.Should().BeFalse();
    }

    // --- Last page boundary ---

    [Fact]
    public async Task Handle_ExactlyOnePage_HasNextPageIsFalse()
    {
        SetupDb(totalCount: 20, BuildDtos(20));

        var query = BuildQuery(page: 1, pageSize: 20);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.Value.Meta.HasNextPage.Should().BeFalse();
    }

    // --- DB called twice (COUNT + rows) ---

    [Fact]
    public async Task Handle_Always_QueriesDbTwice()
    {
        SetupDb(totalCount: 3, BuildDtos(3));

        await _sut.Handle(BuildQuery(), CancellationToken.None);

        await _db.Received(1).QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await _db.Received(1).QueryAsync<AuditLogDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    // --- Optional filter combinations ---

    [Fact]
    public async Task Handle_WithAllFilters_StillReturnsSuccess()
    {
        SetupDb(totalCount: 1, BuildDtos(1));

        var query = BuildQuery(
            tenantId: TenantId,
            userId: UserId,
            from: DateTimeOffset.UtcNow.AddDays(-7),
            to: DateTimeOffset.UtcNow,
            action: "Login",
            moduleName: "Cases",
            resourceType: "Case",
            resourceId: "case-001");

        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoFilters_StillReturnsSuccess()
    {
        SetupDb(totalCount: 100, BuildDtos(20));

        // All optional filters omitted — WHERE clause should be empty
        var query = BuildQuery();
        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(100);
    }

    // --- SearchTerm tests ---

    [Fact]
    public async Task Handle_WithSearchTerm_UsesLikeFallbackByDefault()
    {
        SetupDb(totalCount: 2, BuildDtos(2));
        _featureFlags.IsEnabled(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var query = BuildQuery(searchTerm: "Login");
        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithoutSearchTerm_DoesNotFilterBySearch()
    {
        SetupDb(totalCount: 10, BuildDtos(10));

        var query = BuildQuery(searchTerm: null);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(10);
    }

    // --- Pagination clamp (ARCH-FU2.2) ---

    [Fact]
    public async Task Handle_PageSizeAboveAdminMax_ClampsToAdminMax()
    {
        SetupDb(totalCount: 500, BuildDtos(100));

        var query = BuildQuery(page: 1, pageSize: 9999);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.Value.Meta.PageSize.Should().Be(PaginationDefaults.AdminMax,
            "pageSize above AdminMax must clamp via PaginationDefaults.Normalize");
    }

    [Fact]
    public async Task Handle_PageBelowOne_ClampsToOne()
    {
        SetupDb(totalCount: 0, []);

        var query = BuildQuery(page: 0, pageSize: 20);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.Value.Meta.Page.Should().Be(1, "page<1 must clamp to 1 via PaginationDefaults.Normalize");
    }

    // --- Helpers ---

    private void SetupDb(int totalCount, IEnumerable<AuditLogDto> rows)
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(totalCount);
        _db.QueryAsync<AuditLogDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(rows);
    }

    private static GetAuditLogsQuery BuildQuery(
        Guid? tenantId = null,
        Guid? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? action = null,
        string? moduleName = null,
        string? resourceType = null,
        string? resourceId = null,
        string? searchTerm = null,
        int page = 1,
        int pageSize = 20) =>
        new(tenantId, from, to, userId, action, moduleName, resourceType, resourceId, searchTerm, page, pageSize);

    private static List<AuditLogDto> BuildDtos(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new AuditLogDto(
                Guid.NewGuid(), TenantId, UserId,
                UserName: "tester", Action: "Create", ModuleName: "Cases",
                ResourceType: "Case", ResourceId: null, IpAddress: "127.0.0.1",
                OccurredAt: DateTimeOffset.UtcNow, CorrelationId: null))
            .ToList();
}
