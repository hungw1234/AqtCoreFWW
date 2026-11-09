using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.GetEscalationRules;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

using RuleRow = GetEscalationRulesQueryHandler.RuleRow;

/// <summary>
/// Unit tests for GetEscalationRulesQueryHandler.
/// Validates: returns rules for workflow, optional active filter, ordered by escalation time.
/// Phase-4b B4c: RuleRow is a sealed class (not positional record).
/// Phase-07 PR-D2: RuleRow.WorkflowDefinitionId changed long→Guid (T2 PublicId AK).
/// </summary>
public sealed class GetEscalationRulesQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly GetEscalationRulesQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    // WorkflowDefinitionPublicId is Guid (T2 PublicId AK) — used in both query filter and RuleRow
    private static readonly Guid WorkflowDefinitionPublicId = Guid.NewGuid();

    public GetEscalationRulesQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _handler = new GetEscalationRulesQueryHandler(_db);
    }

    // Helper to build a RuleRow with object initializer (sealed class, no positional ctor)
    private static RuleRow MakeRow(
        long id,
        int escalationAfterMinutes,
        EscalationTarget target,
        bool isActive,
        long? stateId = null) =>
        new()
        {
            Id = id,
            WorkflowDefinitionId = WorkflowDefinitionPublicId,
            StateId = stateId,
            EscalationAfterMinutes = escalationAfterMinutes,
            EscalateTo = (int)target,
            EscalateToValue = $"value-{target}",
            NotifyVia = "Email",
            IsActive = isActive,
            TenantId = TenantId
        };

    // --- Success path ---

    [Fact]
    public async Task Handle_WithRules_ReturnsRuleList()
    {
        var rows = new List<RuleRow>
        {
            MakeRow(1L, 60,  EscalationTarget.Manager, true),
            MakeRow(2L, 120, EscalationTarget.Role,    true)
        };

        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>(rows));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoRules_ReturnsEmpty()
    {
        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>([]));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithoutActiveFilter_ReturnsAll()
    {
        var rows = new List<RuleRow>
        {
            MakeRow(1L, 60,  EscalationTarget.Manager, true),
            MakeRow(2L, 120, EscalationTarget.Manager, false)
        };

        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>(rows));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId);
        var result = await _handler.Handle(query, CancellationToken.None);

        // When IsActive is null, both active and inactive should be returned
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(r => r.IsActive);
        result.Value.Should().Contain(r => !r.IsActive);
    }

    [Fact]
    public async Task Handle_WithActiveFilterTrue_ReturnsActiveOnly()
    {
        var rows = new List<RuleRow>
        {
            MakeRow(1L, 60, EscalationTarget.Manager, true)
        };

        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>(rows));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId, IsActive: true);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.Value.Should().AllSatisfy(r => r.IsActive.Should().BeTrue());
    }

    [Fact]
    public async Task Handle_WithActiveFilterFalse_ReturnsInactiveOnly()
    {
        var rows = new List<RuleRow>
        {
            MakeRow(1L, 60, EscalationTarget.Manager, false)
        };

        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>(rows));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId, IsActive: false);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.Value.Should().AllSatisfy(r => r.IsActive.Should().BeFalse());
    }

    [Fact]
    public async Task Handle_OrderedByEscalationTime()
    {
        var rows = new List<RuleRow>
        {
            MakeRow(1L,  30, EscalationTarget.Manager, true),
            MakeRow(2L,  60, EscalationTarget.Manager, true),
            MakeRow(3L, 120, EscalationTarget.Manager, true)
        };

        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>(rows));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.Value.Select(r => r.EscalationAfterMinutes)
            .Should().ContainInOrder(30, 60, 120);
    }

    [Fact]
    public async Task Handle_WithOptionalStateScope()
    {
        const long stateId = 99L;
        var rows = new List<RuleRow>
        {
            MakeRow(1L, 60, EscalationTarget.Manager, true, stateId: stateId)
        };

        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>(rows));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.Value.Should().ContainSingle(r => r.StateId == stateId);
    }

    [Fact]
    public async Task Handle_PassesCorrectParameters()
    {
        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>([]));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId, IsActive: true);
        await _handler.Handle(query, CancellationToken.None);

        await _db.Received(1).QueryAsync<RuleRow>(
            Arg.Is<string>(sql => sql.Contains("EscalationRules") && sql.Contains("WorkflowDefinitionId")),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CancellationTokenPropagated()
    {
        var cts = new CancellationTokenSource();

        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RuleRow>>([]));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId);
        await _handler.Handle(query, cts.Token);

        await _db.Received(1).QueryAsync<RuleRow>(
            Arg.Any<string>(),
            Arg.Any<object?>(),
            cts.Token);
    }

    [Fact]
    public async Task Handle_DatabaseException_Propagates()
    {
        _db.QueryAsync<RuleRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<RuleRow>>>(_ =>
                Task.FromException<IEnumerable<RuleRow>>(
                    new InvalidOperationException("DB connection lost")));

        var query = new GetEscalationRulesQuery(WorkflowDefinitionPublicId, TenantId);
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB connection lost*");
    }
}
