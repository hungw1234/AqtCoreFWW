using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Domain.Tests.Entities;

/// <summary>
/// Child-entity factory range guards. Spot-checks one boundary per child to lock
/// the contract without exhaustively duplicating the same shape for every arg.
/// </summary>
public sealed class BusinessProcessChildFormTests
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
    public void StateForm_Create_NonPositiveWorkflowStateId_Throws()
    {
        var act = () => BusinessProcessStateForm.Create(
            Gen(), TenantId,
            businessProcessDefinitionId: 1L,
            workflowStateId: 0L, formTemplateId: 5L, formTemplateVersion: 1,
            createdBy: ActorId);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*workflowStateId*");
    }

    [Fact]
    public void TransitionForm_Create_VersionLessThanOne_Throws()
    {
        var act = () => BusinessProcessTransitionForm.Create(
            Gen(), TenantId,
            businessProcessDefinitionId: 1L,
            workflowTransitionId: 20L, formTemplateId: 5L, formTemplateVersion: 0,
            createdBy: ActorId);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*formTemplateVersion*");
    }
}
