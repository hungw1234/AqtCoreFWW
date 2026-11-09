using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Application.Commands.ManageAbacRule;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands.ManageAbacRule;

/// <summary>
/// Tests for UpdateAbacRuleCommandHandler.
/// Verifies: cross-tenant rejection, same-tenant success.
/// </summary>
public sealed class UpdateAbacRuleCommandHandlerTests
{
    private readonly IAttributeRuleRepository _repository = Substitute.For<IAttributeRuleRepository>();
    private readonly IAbacCacheInvalidator _cacheInvalidator = Substitute.For<IAbacCacheInvalidator>();
    private readonly ILogger<UpdateAbacRuleCommandHandler> _logger = Substitute.For<ILogger<UpdateAbacRuleCommandHandler>>();
    private readonly UpdateAbacRuleCommandHandler _sut;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private const long RuleId = 42L;

    public UpdateAbacRuleCommandHandlerTests()
    {
        _sut = new UpdateAbacRuleCommandHandler(_repository, _cacheInvalidator, _logger);
    }

    [Fact]
    public async Task UpdateAbacRule_DifferentTenantId_ReturnsFail()
    {
        // Arrange â€” rule belongs to TenantA, command attempts update as TenantB
        var rule = AttributeRule.Create("Case", "Read", "department", "HR", AttributeEffect.Allow, TenantA);
        typeof(AttributeRule).GetProperty(nameof(AttributeRule.Id))!.SetValue(rule, RuleId);

        _repository.GetByIdAsync(RuleId, Arg.Any<CancellationToken>())
            .Returns(rule);

        var cmd = new UpdateAbacRuleCommand(
            RuleId,
            "Case",
            "Delete",
            "department",
            "HR",
            "Allow",
            TenantB); // Different tenant

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Cross-tenant"));

        // Repository.UpdateAsync should NOT have been called
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<AttributeRule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAbacRule_SameTenantId_Succeeds()
    {
        // Arrange â€” rule belongs to TenantA, command updates as TenantA
        var rule = AttributeRule.Create("Case", "Read", "department", "HR", AttributeEffect.Allow, TenantA);
        typeof(AttributeRule).GetProperty(nameof(AttributeRule.Id))!.SetValue(rule, RuleId);

        _repository.GetByIdAsync(RuleId, Arg.Any<CancellationToken>())
            .Returns(rule);

        var cmd = new UpdateAbacRuleCommand(
            RuleId,
            "Case",
            "Delete",
            "department",
            "Finance",
            "Deny",
            TenantA); // Same tenant

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Rule fields should be updated
        rule.Action.Should().Be("Delete");
        rule.AttributeValue.Should().Be("Finance");
        rule.Effect.Should().Be(AttributeEffect.Deny);

        // Repository.UpdateAsync should have been called
        await _repository.Received(1).UpdateAsync(rule, Arg.Any<CancellationToken>());
    }
}
