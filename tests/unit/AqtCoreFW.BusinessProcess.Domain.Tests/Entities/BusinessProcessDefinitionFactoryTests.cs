using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Domain.Tests.Entities;

/// <summary>
/// Factory invariants for <see cref="BusinessProcessDefinition.Create"/>.
/// Covers required-field guards, range guards, and the GOV_BP_010 XOR pairing rule
/// for IntakeFormTemplateId / IntakeFormTemplateVersion.
/// </summary>
public sealed class BusinessProcessDefinitionFactoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static IGuidGenerator Gen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    [Fact]
    public void Create_MinimalValidArgs_BuildsInactiveBpWithCreatedEvent()
    {
        var bp = BusinessProcessDefinition.Create(
            Gen(), TenantId, code: "bp_onboarding", name: "Onboarding",
            workflowDefinitionId: 42L, createdBy: ActorId);

        bp.TenantId.Should().Be(TenantId);
        bp.Code.Should().Be("bp_onboarding");
        bp.Name.Should().Be("Onboarding");
        bp.WorkflowDefinitionId.Should().Be(42L);
        bp.IsActive.Should().BeFalse();
        bp.IntakeFormTemplateId.Should().BeNull();
        bp.IntakeFormTemplateVersion.Should().BeNull();
        bp.DomainEvents.Should().ContainSingle(e => e is BusinessProcessCreatedEvent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCode_Throws(string blank)
    {
        var act = () => BusinessProcessDefinition.Create(
            Gen(), TenantId, blank, "Onboarding", 42L, ActorId);

        act.Should().Throw<ArgumentException>().WithMessage("*code*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string blank)
    {
        var act = () => BusinessProcessDefinition.Create(
            Gen(), TenantId, "bp_onboarding", blank, 42L, ActorId);

        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Create_NonPositiveWorkflowDefinitionId_Throws(long badId)
    {
        var act = () => BusinessProcessDefinition.Create(
            Gen(), TenantId, "bp_onboarding", "Onboarding", badId, ActorId);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*workflowDefinitionId*");
    }

    [Fact]
    public void Create_IntakeTemplateIdWithoutVersion_ThrowsGovBp010()
    {
        var act = () => BusinessProcessDefinition.Create(
            Gen(), TenantId, "bp", "Name", 42L, ActorId,
            intakeFormTemplateId: 99L, intakeFormTemplateVersion: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GOV_BP_010*");
    }

    [Fact]
    public void Create_IntakeVersionWithoutTemplateId_ThrowsGovBp010()
    {
        var act = () => BusinessProcessDefinition.Create(
            Gen(), TenantId, "bp", "Name", 42L, ActorId,
            intakeFormTemplateId: null, intakeFormTemplateVersion: 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GOV_BP_010*");
    }

    [Fact]
    public void Create_IntakePairBothSet_Succeeds()
    {
        var bp = BusinessProcessDefinition.Create(
            Gen(), TenantId, "bp", "Name", 42L, ActorId,
            intakeFormTemplateId: 99L, intakeFormTemplateVersion: 3);

        bp.IntakeFormTemplateId.Should().Be(99L);
        bp.IntakeFormTemplateVersion.Should().Be(3);
    }
}
