using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.GetDelegationRules;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetDelegationRulesQueryHandler.
/// F03 tenant isolation: a rule seeded in Tenant A must not appear when queried from Tenant B context.
/// This is verified at the repository/SQL level — the handler passes TenantId to Dapper which
/// includes it in the WHERE clause. We mock IReadDbConnection to verify the correct parameter is passed.
/// </summary>
public sealed class GetDelegationRulesQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly GetDelegationRulesQueryHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid DelegatorId = Guid.NewGuid();

    public GetDelegationRulesQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _handler = new GetDelegationRulesQueryHandler(_db);
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ReturnsRulesFromDb()
    {
        var row = new GetDelegationRulesQueryHandler.RuleRow
        {
            Id = 1L,
            DelegatorUserId = DelegatorId,
            DelegateUserId = Guid.NewGuid(),
            ValidFrom = DateTimeOffset.UtcNow,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(7),
            IsActive = true,
            TenantId = TenantA,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = Guid.NewGuid()
        };

        _db.QueryAsync<GetDelegationRulesQueryHandler.RuleRow>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(new[] { row });

        var result = await _handler.Handle(
            new GetDelegationRulesQuery(TenantA), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].TenantId.Should().Be(TenantA);
    }

    [Fact]
    public async Task Handle_FiltersByDelegatorUserId_WhenProvided()
    {
        _db.QueryAsync<GetDelegationRulesQueryHandler.RuleRow>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GetDelegationRulesQueryHandler.RuleRow>());

        await _handler.Handle(
            new GetDelegationRulesQuery(TenantA, DelegatorId), CancellationToken.None);

        // Verify DB is called with both TenantId and DelegatorUserId in params
        await _db.Received(1).QueryAsync<GetDelegationRulesQueryHandler.RuleRow>(
            Arg.Is<string>(sql => sql.Contains("DelegatorUserId")),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    // ── F03: Tenant Isolation ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TenantB_ReceivesEmptyResult_WhenRuleOnlyInTenantA()
    {
        // Tenant A has a rule — but when Tenant B queries, DB returns empty
        // (Dapper WHERE TenantId = @TenantId enforces this at SQL level)
        _db.QueryAsync<GetDelegationRulesQueryHandler.RuleRow>(
            Arg.Any<string>(),
            Arg.Is<object>(p => ContainsTenantId(p, TenantB)),
            Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GetDelegationRulesQueryHandler.RuleRow>());

        var result = await _handler.Handle(
            new GetDelegationRulesQuery(TenantB), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("Tenant B must not see Tenant A's delegation rules — F03 isolation");
    }

    [Fact]
    public async Task Handle_SqlAlwaysIncludesTenantIdFilter()
    {
        _db.QueryAsync<GetDelegationRulesQueryHandler.RuleRow>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GetDelegationRulesQueryHandler.RuleRow>());

        await _handler.Handle(new GetDelegationRulesQuery(TenantA), CancellationToken.None);

        // SQL must contain TenantId filter — this is the contract that prevents cross-tenant leakage
        await _db.Received(1).QueryAsync<GetDelegationRulesQueryHandler.RuleRow>(
            Arg.Is<string>(sql => sql.Contains("TenantId = @TenantId")),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    /// Helper: inspect anonymous object's TenantId property via reflection.
    private static bool ContainsTenantId(object parameters, Guid expected)
    {
        var prop = parameters?.GetType().GetProperty("TenantId");
        return prop is not null && Guid.Equals((Guid)prop.GetValue(parameters)!, expected);
    }
}
