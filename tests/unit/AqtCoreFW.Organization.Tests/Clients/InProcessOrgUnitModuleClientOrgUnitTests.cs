using AqtCoreFW.Organization.Infrastructure.Clients;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Organization.Tests.Clients;

/// <summary>
/// Unit tests for InProcessOrgUnitModuleClient — RT2-15 Phase 04 methods.
///
/// OU-U1: ResolveActiveUsersInOrgUnitsAsync empty codes returns empty immediately (no SQL).
/// OU-U2: ResolveActiveUsersInOrgUnitsAsync passes @Now from TimeProvider (Q4 testability).
/// OU-U3: GetUserOrgUnitCodesAsync returns empty set when no rows returned.
/// OU-U4: GetUserOrgUnitCodesAsync passes @Now from TimeProvider (Q4 testability).
/// OU-U5: GetUserOrgUnitCodesAsync wraps result in Ordinal HashSet (canonical comparison).
///
/// Tests mock IReadDbConnection (Dapper abstraction) to avoid a real DB.
/// TimeProvider is substituted to control @Now injection (INVARIANT 260523-Q4).
/// INVARIANT 260525-E: tenantId mandatory on all calls.
/// </summary>
public sealed class InProcessOrgUnitModuleClientOrgUnitTests
{
    private static readonly Guid TenantId = new("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
    private static readonly Guid UserId = new("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
    private static readonly DateTimeOffset FakeNow = new(2026, 5, 26, 8, 0, 0, TimeSpan.Zero);

    private readonly IReadDbConnection _db;
    private readonly TimeProvider _timeProvider;
    private readonly InProcessOrgUnitModuleClient _sut;

    public InProcessOrgUnitModuleClientOrgUnitTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _timeProvider = Substitute.For<TimeProvider>();
        _timeProvider.GetUtcNow().Returns(FakeNow);
        _sut = new InProcessOrgUnitModuleClient(_db, _timeProvider);
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-U1: empty codes short-circuits before SQL
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveActiveUsersInOrgUnitsAsync_EmptyCodes_ReturnsEmptyWithoutSql()
    {
        var result = await _sut.ResolveActiveUsersInOrgUnitsAsync([], TenantId);

        result.Should().BeEmpty("no codes → no SQL needed, return empty immediately");

        // IReadDbConnection.QueryAsync must NOT be called.
        await _db.DidNotReceiveWithAnyArgs()
            .QueryAsync<Guid>(default!, default, default, default);
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-U2: TimeProvider.GetUtcNow() is called for @Now injection
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveActiveUsersInOrgUnitsAsync_CallsTimeProviderForNow()
    {
        var expected = new Guid[] { UserId };
        _db.QueryAsync<Guid>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(expected);

        await _sut.ResolveActiveUsersInOrgUnitsAsync(["OU_TEST"], TenantId);

        // TimeProvider.GetUtcNow() must be called once per method invocation.
        _ = _timeProvider.Received(1).GetUtcNow();
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-U3: GetUserOrgUnitCodesAsync empty DB result → empty set
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserOrgUnitCodesAsync_NoRows_ReturnsEmptySet()
    {
        _db.QueryAsync<string>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(Enumerable.Empty<string>());

        var result = await _sut.GetUserOrgUnitCodesAsync(UserId, TenantId);

        result.Should().BeEmpty("no DB rows → empty set");
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-U4: TimeProvider.GetUtcNow() is called for @Now injection
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserOrgUnitCodesAsync_CallsTimeProviderForNow()
    {
        _db.QueryAsync<string>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(Enumerable.Empty<string>());

        await _sut.GetUserOrgUnitCodesAsync(UserId, TenantId);

        // TimeProvider.GetUtcNow() must be called once per method invocation.
        _ = _timeProvider.Received(1).GetUtcNow();
    }

    // ─────────────────────────────────────────────────────────────────────
    // OU-U5: result wrapped in Ordinal HashSet (canonical comparison contract)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserOrgUnitCodesAsync_ReturnedCodesAreInHashSet_WithOrdinalComparison()
    {
        var dbRows = new[] { "OU_PHONG_TIEP_NHAN", "OU_BAN_GIAM_DOC" };
        _db.QueryAsync<string>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(dbRows);

        var result = await _sut.GetUserOrgUnitCodesAsync(UserId, TenantId);

        result.Should().BeOfType<HashSet<string>>("result must be a HashSet<string>");
        result.Should().Contain("OU_PHONG_TIEP_NHAN").And.Contain("OU_BAN_GIAM_DOC");
        // Ordinal comparison: uppercase required (SQL already projects UPPER).
        result.Should().NotContain("ou_phong_tiep_nhan",
            "ordinal comparison — lowercase is not equal to uppercase code");
    }
}
