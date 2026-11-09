using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Queries.GetAuditStatistics;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetAuditStatisticsQueryHandler.
/// Handler issues three Dapper queries: counts tuple, byAction list, byModule list.
/// Tests use a non-admin tenant context (IsSystemAdmin=false) so the standard (non-bypass) path runs.
/// </summary>
public sealed class GetAuditStatisticsQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ITenantContext _tenantCtx = Substitute.For<ITenantContext>();
    private readonly GetAuditStatisticsQueryHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public GetAuditStatisticsQueryHandlerTests()
    {
        // Tests cover the non-admin path (tenant-scoped reads, no audit_admin_bypass)
        _tenantCtx.IsSystemAdmin.Returns(false);
        _tenantCtx.TenantId.Returns(TenantId);
        _sut = new GetAuditStatisticsQueryHandler(_db, _tenantCtx);
    }

    // --- Happy path: returns populated stats DTO ---

    [Fact]
    public async Task Handle_WithTenantId_ReturnsSuccessDto()
    {
        SetupDb(todayTotal: 5, weekTotal: 30, monthTotal: 120,
            byAction: [new("Create", 80), new("Update", 40)],
            byModule: [new("Cases", 100), new("Audit", 20)]);

        var result = await _sut.Handle(new GetAuditStatisticsQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WithTenantId_CountsAreCorrect()
    {
        SetupDb(todayTotal: 5, weekTotal: 30, monthTotal: 120);

        var result = await _sut.Handle(new GetAuditStatisticsQuery(TenantId), CancellationToken.None);

        result.Value.TodayTotal.Should().Be(5);
        result.Value.WeekTotal.Should().Be(30);
        result.Value.MonthTotal.Should().Be(120);
    }

    [Fact]
    public async Task Handle_WithTenantId_ByActionListPopulated()
    {
        SetupDb(byAction: [new("Login", 10), new("Logout", 5)]);

        var result = await _sut.Handle(new GetAuditStatisticsQuery(TenantId), CancellationToken.None);

        result.Value.ByAction.Should().HaveCount(2);
        result.Value.ByAction.Should().Contain(a => a.Action == "Login" && a.Count == 10);
    }

    [Fact]
    public async Task Handle_WithTenantId_ByModuleListPopulated()
    {
        SetupDb(byModule: [new("Cases", 50), new("Notifications", 20)]);

        var result = await _sut.Handle(new GetAuditStatisticsQuery(TenantId), CancellationToken.None);

        result.Value.ByModule.Should().HaveCount(2);
        result.Value.ByModule.Should().Contain(m => m.Module == "Cases" && m.Count == 50);
    }

    // --- No tenant filter (global stats) ---

    [Fact]
    public async Task Handle_NullTenantId_ReturnsSuccess()
    {
        SetupDb();

        var result = await _sut.Handle(new GetAuditStatisticsQuery(TenantId: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- Empty lists (no activity) ---

    [Fact]
    public async Task Handle_NoActivity_ReturnsZeroCounts()
    {
        SetupDb(todayTotal: 0, weekTotal: 0, monthTotal: 0, byAction: [], byModule: []);

        var result = await _sut.Handle(new GetAuditStatisticsQuery(TenantId), CancellationToken.None);

        result.Value.TodayTotal.Should().Be(0);
        result.Value.ByAction.Should().BeEmpty();
        result.Value.ByModule.Should().BeEmpty();
    }

    // --- DB called three times (counts + byAction + byModule) ---

    [Fact]
    public async Task Handle_Always_QueriesDbThreeTimes()
    {
        SetupDb();

        await _sut.Handle(new GetAuditStatisticsQuery(TenantId), CancellationToken.None);

        // Handler issues exactly 3 Dapper queries: counts tuple, byAction, byModule
        await _db.Received(1).QueryFirstOrDefaultAsync<(int TodayTotal, int WeekTotal, int MonthTotal)>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await _db.Received(1).QueryAsync<ActionSummary>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await _db.Received(1).QueryAsync<ModuleSummary>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private void SetupDb(
        int todayTotal = 0,
        int weekTotal = 0,
        int monthTotal = 0,
        IEnumerable<ActionSummary>? byAction = null,
        IEnumerable<ModuleSummary>? byModule = null)
    {
        _db.QueryFirstOrDefaultAsync<(int TodayTotal, int WeekTotal, int MonthTotal)>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((todayTotal, weekTotal, monthTotal));

        _db.QueryAsync<ActionSummary>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(byAction ?? []);

        _db.QueryAsync<ModuleSummary>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(byModule ?? []);
    }
}
