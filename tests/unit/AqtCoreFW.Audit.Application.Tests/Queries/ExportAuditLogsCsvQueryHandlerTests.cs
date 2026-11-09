using AqtCoreFW.Audit.Application.Queries.ExportAuditLogs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using Dapper;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Application.Tests.Queries;

/// <summary>
/// Unit tests for ExportAuditLogsCsvQueryHandler.
/// Focus: tenant isolation parity with GetAuditLogsQueryHandler â€” non-SystemAdmin requests
/// MUST always filter by the ambient TenantId (overriding any caller-supplied TenantId), and
/// MUST throw â€” before any DB call â€” when no tenant context is available. SystemAdmin reads
/// route through the audit_admin_bypass connection so NULL-tenant system rows are visible.
/// Assertions inspect the generated SQL and the bound DynamicParameters via ReceivedCalls().
/// (ExportRow is internal â€” see AqtCoreFW.Audit.Application.csproj InternalsVisibleTo â€” so that
/// NSubstitute can auto-stub the Task&lt;IEnumerable&lt;ExportRow&gt;&gt; return of QueryAsync.)
/// </summary>
public sealed class ExportAuditLogsCsvQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ExportAuditLogsCsvQueryHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public ExportAuditLogsCsvQueryHandlerTests()
    {
        // Default: non-SystemAdmin with a valid ambient tenant
        _tenantContext.IsSystemAdmin.Returns(false);
        _tenantContext.TenantId.Returns(TenantId);
        _sut = new ExportAuditLogsCsvQueryHandler(_db, _tenantContext);
    }

    [Fact]
    public async Task Handle_NonAdmin_MissingTenantContext_ThrowsBeforeAnyDbCall()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var act = () => _sut.Handle(BuildQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _db.ReceivedCalls().Should().BeEmpty();  // fail closed â€” no IO before the tenant gate
    }

    [Fact]
    public async Task Handle_NonAdmin_WithoutQueryTenantId_StillEnforcesAmbientTenantClause()
    {
        // Regression: query carries no TenantId; handler must inject the ambient one.
        var result = await _sut.Handle(BuildQuery(tenantId: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        DataSql().Should().Contain("TenantId = @TenantId");
        DataParams().Get<Guid?>("TenantId").Should().Be(TenantId);
        // Non-admin must NOT use the bypass connection.
        BypassCallCount().Should().Be(0);
    }

    [Fact]
    public async Task Handle_NonAdmin_CallerSuppliedTenantId_IsOverriddenByAmbient()
    {
        // The actual attack scenario: a non-admin crafts the query with someone else's TenantId.
        var attackerTarget = Guid.NewGuid();

        var result = await _sut.Handle(BuildQuery(tenantId: attackerTarget), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        DataParams().Get<Guid?>("TenantId").Should().Be(TenantId, "ambient tenant must override the caller value");
        DataParams().Get<Guid?>("TenantId").Should().NotBe(attackerTarget);
        BypassCallCount().Should().Be(0);
    }

    [Fact]
    public async Task Handle_NonAdmin_ReturnsCsvWithUtf8BomAndHeader()
    {
        var result = await _sut.Handle(BuildQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith(System.Text.Encoding.UTF8.GetPreamble());
        System.Text.Encoding.UTF8.GetString(result.Value)
            .Should().Contain("Timestamp,Action,ResourceType,ResourceId,UserId,IpAddress,Details");
    }

    [Fact]
    public async Task Handle_SystemAdmin_UsesAuditAdminBypassConnection()
    {
        _tenantContext.IsSystemAdmin.Returns(true);

        var result = await _sut.Handle(BuildQuery(tenantId: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        BypassCallCount().Should().Be(1);
    }

    [Fact]
    public async Task Handle_SystemAdmin_NoTenantFilter_OmitsTenantClause()
    {
        _tenantContext.IsSystemAdmin.Returns(true);

        await _sut.Handle(BuildQuery(tenantId: null), CancellationToken.None);

        BypassSql().Should().NotContain("TenantId = @TenantId");
    }

    [Fact]
    public async Task Handle_SystemAdmin_WithTenantFilter_AppliesTenantClause()
    {
        _tenantContext.IsSystemAdmin.Returns(true);

        await _sut.Handle(BuildQuery(tenantId: TenantId), CancellationToken.None);

        BypassSql().Should().Contain("TenantId = @TenantId");
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A9)")]
    public async Task Handle_CellStartingWithFormulaTrigger_IsPrefixedWithSingleQuote(string payload)
    {
        StubRows(new ExportAuditLogsCsvQueryHandler.ExportRow(
            DateTimeOffset.UtcNow, payload, "Type", payload, null, payload, payload));

        var result = await _sut.Handle(BuildQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var csv = System.Text.Encoding.UTF8.GetString(result.Value);
        var dataLine = csv.Split('\n')[1];
        // every occurrence of the payload must be neutralised with a leading apostrophe
        dataLine.Should().NotContain($",{payload}").And.NotContain($"\"{payload}");
        dataLine.Should().Contain($"'{payload}");
    }

    [Fact]
    public async Task Handle_FormulaTriggerCellAlsoNeedingRfc4180Quoting_PrefixesThenWraps()
    {
        // The prefix-then-wrap ordering: the apostrophe must land *inside* the double-quotes.
        const string payload = "=1,2\"3";
        StubRows(new ExportAuditLogsCsvQueryHandler.ExportRow(
            DateTimeOffset.UtcNow, payload, "Type", null, null, null, null));

        var result = await _sut.Handle(BuildQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // ' inside the quotes, embedded " doubled per RFC 4180
        System.Text.Encoding.UTF8.GetString(result.Value).Should().Contain("\"'=1,2\"\"3\"");
    }

    [Fact]
    public async Task Handle_LeadingTabCell_IsPrefixedWithSingleQuote()
    {
        StubRows(new ExportAuditLogsCsvQueryHandler.ExportRow(
            DateTimeOffset.UtcNow, "\tcmd", "Type", null, null, null, null));

        var result = await _sut.Handle(BuildQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        System.Text.Encoding.UTF8.GetString(result.Value).Should().Contain("'\tcmd");
    }

    [Fact]
    public async Task Handle_BenignCellNotStartingWithTrigger_IsNotPrefixed()
    {
        StubRows(new ExportAuditLogsCsvQueryHandler.ExportRow(
            DateTimeOffset.UtcNow, "Login", "User", "abc123", null, "127.0.0.1", "corr-1"));

        var result = await _sut.Handle(BuildQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var csv = System.Text.Encoding.UTF8.GetString(result.Value);
        csv.Should().Contain(",Login,").And.NotContain("'Login");
    }

    // --- Helpers ---

    private void StubRows(params ExportAuditLogsCsvQueryHandler.ExportRow[] rows) =>
        _db.QueryAsync<ExportAuditLogsCsvQueryHandler.ExportRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(rows.AsEnumerable());

    private string DataSql() => SqlForMethod("QueryAsync");
    private string BypassSql() => SqlForMethod("QueryWithAuditAdminBypassAsync");
    private DynamicParameters DataParams() => ParamsForMethod("QueryAsync");
    private int BypassCallCount() =>
        _db.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "QueryWithAuditAdminBypassAsync");

    private string SqlForMethod(string methodName) =>
        (string)ArgsForMethod(methodName)[0]!;

    private DynamicParameters ParamsForMethod(string methodName) =>
        (DynamicParameters)ArgsForMethod(methodName)[1]!;

    private object?[] ArgsForMethod(string methodName) =>
        _db.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == methodName)
            .Select(c => c.GetArguments())
            .Single();

    private static ExportAuditLogsCsvQuery BuildQuery(
        Guid? tenantId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        Guid? userId = null,
        string? action = null,
        string? moduleName = null,
        string? resourceType = null,
        string? resourceId = null) =>
        new(tenantId, from, to, userId, action, moduleName, resourceType, resourceId);
}


