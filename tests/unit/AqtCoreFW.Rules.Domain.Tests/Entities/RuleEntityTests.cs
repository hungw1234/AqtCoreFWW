using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Rules.Domain.Tests.Entities;

public sealed class RuleEntityTests
{
    // T1 FK: RuleSet.Id is long (internal PK). Use a constant to represent a parent FK.
    private const long RuleSetId = 1L;
    private static readonly Guid UserId = Guid.NewGuid();

    // T2 entity: Rule.Create requires IGuidGenerator for PublicId.
    private static IGuidGenerator MakeGen() =>
        Substitute.For<IGuidGenerator>().WithNewIdMonotonic(Guid.NewGuid());

    private static Rule CreateRule(string name = "AgeCheck") =>
        Rule.Create(MakeGen(), RuleSetId, name, "input.Age >= 18", UserId, priority: 1);

    // --- Factory ---

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var rule = CreateRule("AgeCheck");
        rule.RuleSetId.Should().Be(RuleSetId);
        rule.Name.Should().Be("AgeCheck");
        rule.Expression.Should().Be("input.Age >= 18");
        rule.Priority.Should().Be(1);
        rule.Enabled.Should().BeTrue();
        rule.FailureAction.Should().Be(FailureAction.Skip);
    }

    [Fact]
    public void Create_AssignsPublicId()
    {
        // T2 entity: PublicId (Guid AK) is set by IGuidGenerator at factory time.
        var rule = CreateRule();
        rule.PublicId.Should().NotBeEmpty();
    }

    // --- Update ---

    [Fact]
    public void Update_Name_ChangesName()
    {
        var rule = CreateRule();
        rule.Update("NewName", null, null, null, null, UserId);
        rule.Name.Should().Be("NewName");
    }

    [Fact]
    public void Update_Expression_ChangesExpression()
    {
        var rule = CreateRule();
        rule.Update(null, "input.Score > 50", null, null, null, UserId);
        rule.Expression.Should().Be("input.Score > 50");
    }

    [Fact]
    public void Update_Priority_ChangesPriority()
    {
        var rule = CreateRule();
        rule.Update(null, null, 10, null, null, UserId);
        rule.Priority.Should().Be(10);
    }

    [Fact]
    public void Update_Enabled_False_DisablesRule()
    {
        var rule = CreateRule();
        rule.Update(null, null, null, false, null, UserId);
        rule.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Update_FailureAction_ChangesAction()
    {
        var rule = CreateRule();
        rule.Update(null, null, null, null, FailureAction.Throw, UserId);
        rule.FailureAction.Should().Be(FailureAction.Throw);
    }

    [Fact]
    public void Update_NullParams_LeavesExistingValuesUnchanged()
    {
        var rule = CreateRule("Original");
        rule.Update(null, null, null, null, null, UserId);
        rule.Name.Should().Be("Original");
        rule.Expression.Should().Be("input.Age >= 18");
    }
}
