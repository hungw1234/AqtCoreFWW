using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Application.Commands.ManageAbacRule;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands.ManageAbacRule;

/// <summary>
/// Tests for DeleteAbacRuleCommandHandler.
/// Verifies: silent no-op idempotency, precise cache invalidation, audit log for cross-tenant/missing attempts.
/// </summary>
public sealed class DeleteAbacRuleCommandHandlerTests
{
    private readonly IAttributeRuleRepository _repository = Substitute.For<IAttributeRuleRepository>();
    private readonly IAbacCacheInvalidator _cacheInvalidator = Substitute.For<IAbacCacheInvalidator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ILogger<DeleteAbacRuleCommandHandler> _logger = Substitute.For<ILogger<DeleteAbacRuleCommandHandler>>();
    private readonly DeleteAbacRuleCommandHandler _sut;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid CallerTenant = Guid.NewGuid();
    private const long RuleId = 42L;

    public DeleteAbacRuleCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CallerTenant);
        _sut = new DeleteAbacRuleCommandHandler(_repository, _cacheInvalidator, _tenantContext, _logger);
    }

    [Fact]
    public async Task Delete_ExistingRule_ReturnsSuccess()
    {
        var rule = AttributeRule.Create("Case", "Delete", "department", "HR", AttributeEffect.Allow, TenantA);
        typeof(AttributeRule).GetProperty(nameof(AttributeRule.Id))!.SetValue(rule, RuleId);
        _repository.GetByIdAsync(RuleId, Arg.Any<CancellationToken>()).Returns(rule);

        var result = await _sut.Handle(new DeleteAbacRuleCommand(RuleId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ExistingRule_InvokesPreciseCacheInvalidation()
    {
        var rule = AttributeRule.Create("Case", "Delete", "department", "HR", AttributeEffect.Allow, TenantA);
        typeof(AttributeRule).GetProperty(nameof(AttributeRule.Id))!.SetValue(rule, RuleId);
        _repository.GetByIdAsync(RuleId, Arg.Any<CancellationToken>()).Returns(rule);

        await _sut.Handle(new DeleteAbacRuleCommand(RuleId), CancellationToken.None);

        await _cacheInvalidator.Received(1).InvalidateAsync(TenantA, "HR", Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteAsync(RuleId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RuleNotFound_ReturnsSuccessIdempotent()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((AttributeRule?)null);

        var result = await _sut.Handle(new DeleteAbacRuleCommand(RuleId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(); // Silent no-op preserves idempotency + hides tenant leak
    }

    [Fact]
    public async Task Delete_RuleNotFound_DoesNotInvokeInvalidatorOrDelete()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((AttributeRule?)null);

        await _sut.Handle(new DeleteAbacRuleCommand(RuleId), CancellationToken.None);

        await _cacheInvalidator.DidNotReceive().InvalidateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RuleNotFound_LogsWarningForAudit()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((AttributeRule?)null);

        await _sut.Handle(new DeleteAbacRuleCommand(RuleId), CancellationToken.None);

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>()!);
    }
}
