using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Domain.Tests.Entities;

/// <summary>
/// Aggregate-root invariants: cross-tenant child rejection (GOV_BP_002), idempotent
/// Activate/Deactivate, and UpdateMetadata partial-update semantics (null/blank no-ops).
/// </summary>
public sealed class BusinessProcessDefinitionAggregateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static IGuidGenerator Gen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static BusinessProcessDefinition NewBp() =>
        BusinessProcessDefinition.Create(Gen(), TenantId, "bp", "Name", 42L, ActorId);

    [Fact]
    public void AddStateForm_DifferentTenant_ThrowsGovBp002()
    {
        var bp = NewBp();
        var alien = BusinessProcessStateForm.Create(
            Gen(), OtherTenantId,
            businessProcessDefinitionId: 1L,
            workflowStateId: 10L, formTemplateId: 5L, formTemplateVersion: 1,
            createdBy: ActorId);

        var act = () => bp.AddStateForm(alien);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_BP_002*");
        bp.StateForms.Should().BeEmpty();
    }

    [Fact]
    public void AddTransitionForm_DifferentTenant_ThrowsGovBp002()
    {
        var bp = NewBp();
        var alien = BusinessProcessTransitionForm.Create(
            Gen(), OtherTenantId,
            businessProcessDefinitionId: 1L,
            workflowTransitionId: 20L, formTemplateId: 5L, formTemplateVersion: 1,
            createdBy: ActorId);

        var act = () => bp.AddTransitionForm(alien);

        act.Should().Throw<InvalidOperationException>().WithMessage("*GOV_BP_002*");
        bp.TransitionForms.Should().BeEmpty();
    }

    [Fact]
    public void AddStateForm_MatchingTenant_Appends()
    {
        var bp = NewBp();
        var sf = BusinessProcessStateForm.Create(
            Gen(), TenantId, 1L, 10L, 5L, 1, ActorId);

        bp.AddStateForm(sf);

        bp.StateForms.Should().ContainSingle().Which.Should().BeSameAs(sf);
    }

    [Fact]
    public void AddTransitionForm_MatchingTenant_Appends()
    {
        var bp = NewBp();
        var tf = BusinessProcessTransitionForm.Create(
            Gen(), TenantId, 1L, 20L, 5L, 1, ActorId);

        bp.AddTransitionForm(tf);

        bp.TransitionForms.Should().ContainSingle().Which.Should().BeSameAs(tf);
    }

    [Fact]
    public void Activate_Then_Deactivate_Idempotent()
    {
        var bp = NewBp();
        bp.IsActive.Should().BeFalse();
        bp.ClearDomainEvents(); // drop Created event so we measure Activate-side fan-out cleanly

        bp.Activate(Gen(), ActorId);
        bp.IsActive.Should().BeTrue();
        bp.DomainEvents.Should().ContainSingle(e => e is BusinessProcessActivatedEvent,
            "Activate (false→true) must emit BusinessProcessActivatedEvent for outbox dispatch");

        bp.Activate(Gen(), ActorId); // second call no-op — no extra event
        bp.IsActive.Should().BeTrue();
        bp.DomainEvents.OfType<BusinessProcessActivatedEvent>().Should().ContainSingle(
            "idempotent Activate must NOT emit duplicate events");

        bp.Deactivate(Gen(), ActorId);
        bp.IsActive.Should().BeFalse();
        bp.DomainEvents.Should().Contain(e => e is BusinessProcessDeactivatedEvent,
            "Deactivate (true→false) must emit BusinessProcessDeactivatedEvent for outbox dispatch");

        bp.Deactivate(Gen(), ActorId); // second call no-op — no extra event
        bp.IsActive.Should().BeFalse();
        bp.DomainEvents.OfType<BusinessProcessDeactivatedEvent>().Should().ContainSingle(
            "idempotent Deactivate must NOT emit duplicate events");
    }

    [Fact]
    public void UpdateMetadata_BlankName_PreservesExisting()
    {
        var bp = NewBp();
        var originalName = bp.Name;

        bp.UpdateMetadata(name: "   ", description: "new desc", modifiedBy: ActorId);

        bp.Name.Should().Be(originalName);
        bp.Description.Should().Be("new desc");
    }

    [Fact]
    public void UpdateMetadata_NullDescription_PreservesExisting()
    {
        var bp = NewBp();
        bp.UpdateMetadata(name: null, description: "first", modifiedBy: ActorId);
        bp.Description.Should().Be("first");

        bp.UpdateMetadata(name: "New Name", description: null, modifiedBy: ActorId);

        bp.Name.Should().Be("New Name");
        bp.Description.Should().Be("first");
    }
}
