using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateDelegationRule;
using AqtCoreFW.Workflow.Application.Commands.DeleteDelegationRule;
using AqtCoreFW.Workflow.Application.Commands.UpdateDelegationRule;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// TDD tests for CreateDelegationRuleCommandHandler, UpdateDelegationRuleCommandHandler,
/// and DeleteDelegationRuleCommandHandler.
/// Written BEFORE handlers — these initially fail to compile until handlers exist.
/// </summary>
public sealed class DelegationRuleCrudCommandHandlerTests
{
    private readonly IDelegationRuleRepository _repository;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DelegatorId = Guid.NewGuid();
    private static readonly Guid DelegateId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public DelegationRuleCrudCommandHandlerTests()
    {
        _repository = Substitute.For<IDelegationRuleRepository>();
    }

    // ── CREATE ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_NoOverlap_ReturnsDelegationRuleDto()
    {
        _repository.HasOverlappingRuleAsync(
            DelegatorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            TenantId, null, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new CreateDelegationRuleCommandHandler(_repository);
        var command = new CreateDelegationRuleCommand(
            DelegatorId, DelegateId, Now, Now.AddDays(7), TenantId, CreatedBy);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DelegatorUserId.Should().Be(DelegatorId);
        result.Value.DelegateUserId.Should().Be(DelegateId);
        result.Value.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task Create_NoOverlap_PersistsViaRepository()
    {
        _repository.HasOverlappingRuleAsync(
            DelegatorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            TenantId, null, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new CreateDelegationRuleCommandHandler(_repository);
        var command = new CreateDelegationRuleCommand(
            DelegatorId, DelegateId, Now, Now.AddDays(7), TenantId, CreatedBy);

        await handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<DelegationRule>(r =>
                r.DelegatorUserId == DelegatorId &&
                r.DelegateUserId == DelegateId &&
                r.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_OverlappingRule_ReturnsConflictError()
    {
        _repository.HasOverlappingRuleAsync(
            DelegatorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            TenantId, null, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new CreateDelegationRuleCommandHandler(_repository);
        var command = new CreateDelegationRuleCommand(
            DelegatorId, DelegateId, Now, Now.AddDays(7), TenantId, CreatedBy);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ConflictError);
    }

    [Fact]
    public async Task Create_OverlappingRule_DoesNotPersist()
    {
        _repository.HasOverlappingRuleAsync(
            DelegatorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            TenantId, null, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new CreateDelegationRuleCommandHandler(_repository);
        var command = new CreateDelegationRuleCommand(
            DelegatorId, DelegateId, Now, Now.AddDays(7), TenantId, CreatedBy);

        await handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().AddAsync(Arg.Any<DelegationRule>(), Arg.Any<CancellationToken>());
    }

    // ── UPDATE ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingRule_NoOverlap_ReturnsUpdatedDto()
    {
        const long ruleId = 1L;
        var newDelegateId = Guid.NewGuid();
        var rule = DelegationRule.Create(DelegatorId, DelegateId, Now.AddDays(-1), Now.AddDays(7), TenantId, CreatedBy);

        _repository.GetByIdAndTenantAsync(ruleId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(rule));
        _repository.HasOverlappingRuleAsync(
            DelegatorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            TenantId, ruleId, Arg.Any<CancellationToken>())
            .Returns(false);

        var handler = new UpdateDelegationRuleCommandHandler(_repository);
        var command = new UpdateDelegationRuleCommand(
            ruleId, newDelegateId, Now, Now.AddDays(14), CreatedBy, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DelegateUserId.Should().Be(newDelegateId);
    }

    [Fact]
    public async Task Update_RuleNotFound_ReturnsNotFoundError()
    {
        const long ruleId = 1L;
        _repository.GetByIdAndTenantAsync(ruleId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("not found")));

        var handler = new UpdateDelegationRuleCommandHandler(_repository);
        var command = new UpdateDelegationRuleCommand(
            ruleId, DelegateId, Now, Now.AddDays(7), CreatedBy, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Update_OverlappingRule_ReturnsConflictError()
    {
        const long ruleId = 1L;
        var rule = DelegationRule.Create(DelegatorId, DelegateId, Now.AddDays(-1), Now.AddDays(7), TenantId, CreatedBy);

        _repository.GetByIdAndTenantAsync(ruleId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(rule));
        _repository.HasOverlappingRuleAsync(
            DelegatorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            TenantId, ruleId, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new UpdateDelegationRuleCommandHandler(_repository);
        var command = new UpdateDelegationRuleCommand(
            ruleId, DelegateId, Now, Now.AddDays(14), CreatedBy, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ConflictError);
    }

    [Fact]
    public async Task Update_CrossTenantRule_ReturnsNotFoundError()
    {
        // GetByIdAndTenantAsync with wrong TenantId returns not found — tenant isolation
        const long ruleId = 1L;
        var otherTenantId = Guid.NewGuid();
        _repository.GetByIdAndTenantAsync(ruleId, otherTenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("not found")));

        var handler = new UpdateDelegationRuleCommandHandler(_repository);
        var command = new UpdateDelegationRuleCommand(
            ruleId, DelegateId, Now, Now.AddDays(7), CreatedBy, otherTenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<DelegationRule>(), Arg.Any<CancellationToken>());
    }

    // ── DELETE (soft) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingRule_DeactivatesAndUpdates()
    {
        const long ruleId = 1L;
        var rule = DelegationRule.Create(DelegatorId, DelegateId, Now, Now.AddDays(7), TenantId, CreatedBy);

        _repository.GetByIdAndTenantAsync(ruleId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(rule));

        var handler = new DeleteDelegationRuleCommandHandler(_repository);
        var command = new DeleteDelegationRuleCommand(ruleId, CreatedBy, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).UpdateAsync(
            Arg.Is<DelegationRule>(r => !r.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RuleNotFound_ReturnsNotFoundError()
    {
        const long ruleId = 1L;
        _repository.GetByIdAndTenantAsync(ruleId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("not found")));

        var handler = new DeleteDelegationRuleCommandHandler(_repository);
        var command = new DeleteDelegationRuleCommand(ruleId, CreatedBy, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Delete_CrossTenantRule_ReturnsNotFoundError()
    {
        // GetByIdAndTenantAsync with wrong TenantId returns not found — tenant isolation
        const long ruleId = 1L;
        var otherTenantId = Guid.NewGuid();
        _repository.GetByIdAndTenantAsync(ruleId, otherTenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("not found")));

        var handler = new DeleteDelegationRuleCommandHandler(_repository);
        var command = new DeleteDelegationRuleCommand(ruleId, CreatedBy, otherTenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<DelegationRule>(), Arg.Any<CancellationToken>());
    }
}
