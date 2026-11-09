using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.Integration.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Integration.Domain.Tests.Entities;

/// <summary>
/// Unit tests for Partner aggregate root.
/// Verifies factory, update, state transitions, and domain event emission.
/// Pure in-process tests — no DB, no HTTP.
/// Phase-02c: IGuidGenerator injected for Rule1-compliant event MessageId.
/// </summary>
public sealed class PartnerEntityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    private static IGuidGenerator MakeGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdRandom().Returns(Guid.NewGuid());
        return gen;
    }

    private static Partner CreatePartner() =>
        Partner.Create(MakeGen(), TenantId, "Agency A", "AGY-A", CreatedBy,
            "a@gov.vn", "+84123456789", "https://api.agency-a.gov.vn", "Bearer");

    // --- Factory ---

    [Fact]
    public void Create_ValidParams_ReturnsActivePartner()
    {
        var partner = CreatePartner();
        partner.Status.Should().Be(PartnerStatus.Active);
    }

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var partner = CreatePartner();
        partner.TenantId.Should().Be(TenantId);
        partner.Name.Should().Be("Agency A");
        partner.Code.Should().Be("AGY-A");
        partner.ContactEmail.Should().Be("a@gov.vn");
        partner.ContactPhone.Should().Be("+84123456789");
        partner.Endpoint.Should().Be("https://api.agency-a.gov.vn");
        partner.AuthScheme.Should().Be("Bearer");
    }

    [Fact]
    public void Create_ValidParams_RaisesPartnerCreatedEvent()
    {
        var partner = CreatePartner();
        partner.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PartnerCreatedEvent>();
    }

    [Fact]
    public void Create_ValidParams_SetsAuditFields()
    {
        var partner = CreatePartner();
        partner.CreatedBy.Should().Be(CreatedBy);
    }

    // --- Update ---

    [Fact]
    public void Update_ChangesAllMutableFields()
    {
        var partner = CreatePartner();
        var modifiedBy = Guid.NewGuid();
        partner.Update("Agency B", "AGY-B", "b@gov.vn", "+84987654321",
            "https://api.agency-b.gov.vn", "ApiKey", modifiedBy);

        partner.Name.Should().Be("Agency B");
        partner.Code.Should().Be("AGY-B");
        partner.ContactEmail.Should().Be("b@gov.vn");
        partner.ContactPhone.Should().Be("+84987654321");
        partner.Endpoint.Should().Be("https://api.agency-b.gov.vn");
        partner.AuthScheme.Should().Be("ApiKey");
    }

    [Fact]
    public void Update_SetsModifiedBy()
    {
        var partner = CreatePartner();
        var modifiedBy = Guid.NewGuid();
        partner.Update("Agency B", "AGY-B", null, null, null, null, modifiedBy);
        partner.ModifiedBy.Should().Be(modifiedBy);
    }

    // --- Suspend ---

    [Fact]
    public void Suspend_WhenActive_SetsStatusSuspended()
    {
        var partner = CreatePartner();
        partner.Suspend();
        partner.Status.Should().Be(PartnerStatus.Suspended);
    }

    [Fact]
    public void Suspend_WhenSuspended_ThrowsInvalidOperation()
    {
        var partner = CreatePartner();
        partner.Suspend();
        var act = () => partner.Suspend();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Suspend_WhenDeactivated_ThrowsInvalidOperation()
    {
        var partner = CreatePartner();
        partner.Deactivate(MakeGen());
        var act = () => partner.Suspend();
        act.Should().Throw<InvalidOperationException>();
    }

    // --- Activate ---

    [Fact]
    public void Activate_WhenSuspended_SetsStatusActive()
    {
        var partner = CreatePartner();
        partner.Suspend();
        partner.Activate();
        partner.Status.Should().Be(PartnerStatus.Active);
    }

    [Fact]
    public void Activate_WhenActive_ThrowsInvalidOperation()
    {
        var partner = CreatePartner();
        var act = () => partner.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Activate_WhenDeactivated_ThrowsInvalidOperation()
    {
        var partner = CreatePartner();
        partner.Deactivate(MakeGen());
        var act = () => partner.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    // --- Deactivate ---

    [Fact]
    public void Deactivate_WhenActive_SetsStatusDeactivated()
    {
        var partner = CreatePartner();
        partner.Deactivate(MakeGen());
        partner.Status.Should().Be(PartnerStatus.Deactivated);
    }

    [Fact]
    public void Deactivate_WhenSuspended_SetsStatusDeactivated()
    {
        var partner = CreatePartner();
        partner.Suspend();
        partner.Deactivate(MakeGen());
        partner.Status.Should().Be(PartnerStatus.Deactivated);
    }

    [Fact]
    public void Deactivate_WhenAlreadyDeactivated_ThrowsInvalidOperation()
    {
        var partner = CreatePartner();
        partner.Deactivate(MakeGen());
        var act = () => partner.Deactivate(MakeGen());
        act.Should().Throw<InvalidOperationException>();
    }

    // --- ClearDomainEvents ---

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var partner = CreatePartner();
        partner.ClearDomainEvents();
        partner.DomainEvents.Should().BeEmpty();
    }
}
