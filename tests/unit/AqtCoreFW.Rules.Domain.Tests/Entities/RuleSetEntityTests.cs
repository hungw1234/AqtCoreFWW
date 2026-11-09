using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.Rules.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Rules.Domain.Tests.Entities;

/// <summary>
/// Unit tests for RuleSet aggregate root.
/// Pure in-process — no DB, no HTTP.
/// </summary>
public sealed class RuleSetEntityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // T2 entity: factory requires IGuidGenerator to set PublicId.
    private static IGuidGenerator MakeGen() =>
        Substitute.For<IGuidGenerator>().WithNewIdMonotonic(Guid.NewGuid());

    private static RuleSet CreateDraft(string name = "Test RuleSet") =>
        RuleSet.Create(MakeGen(), name, TenantId, UserId, "desc", "category", "Invoice");

    // --- Factory ---

    [Fact]
    public void Create_ValidParams_ReturnsDraftStatus()
    {
        var rs = CreateDraft();
        rs.Status.Should().Be(RuleSetStatus.Draft);
    }

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var rs = CreateDraft("My Rules");
        rs.Name.Should().Be("My Rules");
        rs.TenantId.Should().Be(TenantId);
        rs.Description.Should().Be("desc");
        rs.Category.Should().Be("category");
        rs.TargetEntityType.Should().Be("Invoice");
        rs.Version.Should().Be(1);
    }

    [Fact]
    public void Create_ValidParams_EmptyDomainEvents()
    {
        var rs = CreateDraft();
        rs.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Create_AssignsPublicId()
    {
        // T2 entity: PublicId (Guid AK) is set by IGuidGenerator at factory time.
        var rs = CreateDraft();
        rs.PublicId.Should().NotBeEmpty();
    }

    // --- Activate ---

    [Fact]
    public void Activate_FromDraft_SetsStatusActive()
    {
        var rs = CreateDraft();
        rs.Activate(MakeGen());
        rs.Status.Should().Be(RuleSetStatus.Active);
    }

    [Fact]
    public void Activate_FromDraft_RaisesRuleSetActivatedEvent()
    {
        var rs = CreateDraft();
        rs.Activate(MakeGen());
        rs.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<RuleSetActivatedEvent>();
    }

    [Fact]
    public void Activate_EventHasCorrectIds()
    {
        var rs = CreateDraft();
        rs.Activate(MakeGen());
        var evt = rs.DomainEvents.OfType<RuleSetActivatedEvent>().Single();
        // Phase-4a/B5b: event field renamed RuleSetId → RuleSetPublicId (Guid AK).
        evt.RuleSetPublicId.Should().Be(rs.PublicId);
        evt.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Activate_FromDeprecated_ThrowsInvalidOperation()
    {
        var rs = CreateDraft();
        rs.Activate(MakeGen());
        rs.Deprecate();
        var act = () => rs.Activate(MakeGen());
        act.Should().Throw<InvalidOperationException>();
    }

    // --- Deprecate ---

    [Fact]
    public void Deprecate_FromActive_SetsStatusDeprecated()
    {
        var rs = CreateDraft();
        rs.Activate(MakeGen());
        rs.Deprecate();
        rs.Status.Should().Be(RuleSetStatus.Deprecated);
    }

    [Fact]
    public void Deprecate_FromDraft_ThrowsInvalidOperation()
    {
        var rs = CreateDraft();
        var act = () => rs.Deprecate();
        act.Should().Throw<InvalidOperationException>();
    }

    // --- IncrementVersion ---

    [Fact]
    public void IncrementVersion_IncrementsBy1()
    {
        var rs = CreateDraft();
        rs.IncrementVersion();
        rs.Version.Should().Be(2);
    }

    [Fact]
    public void IncrementVersion_MultipleTimes_AccumulatesCorrectly()
    {
        var rs = CreateDraft();
        rs.IncrementVersion();
        rs.IncrementVersion();
        rs.Version.Should().Be(3);
    }

    // --- ClearDomainEvents ---

    [Fact]
    public void ClearDomainEvents_AfterActivate_RemovesAllEvents()
    {
        var rs = CreateDraft();
        rs.Activate(MakeGen());
        rs.ClearDomainEvents();
        rs.DomainEvents.Should().BeEmpty();
    }
}

/// <summary>Extension helper to make NSubstitute IGuidGenerator fluent setup one-liner.</summary>
internal static class GuidGeneratorSubstituteExtensions
{
    internal static IGuidGenerator WithNewIdMonotonic(this IGuidGenerator gen, Guid id)
    {
        gen.NewIdMonotonic().Returns(id);
        return gen;
    }
}
