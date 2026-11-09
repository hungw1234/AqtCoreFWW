using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Domain.Tests.Entities;

/// <summary>
/// Unit tests for RemoveStateForm / RemoveTransitionForm / ClearStateForms / ClearTransitionForms
/// added in BP MVP P7-FU1 Phase B.
/// TC-BP-DOM-010..013.
/// </summary>
public sealed class BusinessProcessDefinitionRemoveAndClearTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static IGuidGenerator Gen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static BusinessProcessDefinition NewBp() =>
        BusinessProcessDefinition.Create(Gen(), TenantId, "bp", "Name", 42L, ActorId);

    private static BusinessProcessStateForm NewStateForm(long stateId = 10L) =>
        BusinessProcessStateForm.Create(
            Gen(), TenantId,
            businessProcessDefinitionId: 1L,
            workflowStateId: stateId,
            formTemplateId: 5L,
            formTemplateVersion: 1,
            createdBy: ActorId);

    private static BusinessProcessTransitionForm NewTransitionForm(long transitionId = 20L) =>
        BusinessProcessTransitionForm.Create(
            Gen(), TenantId,
            businessProcessDefinitionId: 1L,
            workflowTransitionId: transitionId,
            formTemplateId: 5L,
            formTemplateVersion: 1,
            createdBy: ActorId);

    [Fact]
    public void RemoveStateForm_ExistingItem_RemovesFromCollection()
    {
        // TC-BP-DOM-010
        var bp = NewBp();
        var sf = NewStateForm();
        bp.AddStateForm(sf);
        bp.StateForms.Should().HaveCount(1);

        bp.RemoveStateForm(sf);

        bp.StateForms.Should().BeEmpty();
    }

    [Fact]
    public void RemoveTransitionForm_ExistingItem_RemovesFromCollection()
    {
        // TC-BP-DOM-011
        var bp = NewBp();
        var tf = NewTransitionForm();
        bp.AddTransitionForm(tf);
        bp.TransitionForms.Should().HaveCount(1);

        bp.RemoveTransitionForm(tf);

        bp.TransitionForms.Should().BeEmpty();
    }

    [Fact]
    public void ClearStateForms_MultipleItems_ClearsAll()
    {
        // TC-BP-DOM-012: ClearStateForms removes all entries atomically
        var bp = NewBp();
        bp.AddStateForm(NewStateForm(10L));
        bp.AddStateForm(NewStateForm(11L));
        bp.StateForms.Should().HaveCount(2);

        bp.ClearStateForms();

        bp.StateForms.Should().BeEmpty();
    }

    [Fact]
    public void ClearTransitionForms_MultipleItems_ClearsAll()
    {
        // TC-BP-DOM-013: ClearTransitionForms removes all entries atomically
        var bp = NewBp();
        bp.AddTransitionForm(NewTransitionForm(20L));
        bp.AddTransitionForm(NewTransitionForm(21L));
        bp.TransitionForms.Should().HaveCount(2);

        bp.ClearTransitionForms();

        bp.TransitionForms.Should().BeEmpty();
    }

    [Fact]
    public void ClearStateForms_EmptyCollection_IsIdempotent()
    {
        // Idempotent re-clear: clearing an already-empty list does not throw
        var bp = NewBp();

        var act = () => bp.ClearStateForms();

        act.Should().NotThrow();
        bp.StateForms.Should().BeEmpty();
    }

    [Fact]
    public void ClearTransitionForms_EmptyCollection_IsIdempotent()
    {
        var bp = NewBp();

        var act = () => bp.ClearTransitionForms();

        act.Should().NotThrow();
        bp.TransitionForms.Should().BeEmpty();
    }
}
