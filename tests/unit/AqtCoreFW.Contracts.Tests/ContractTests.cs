using AqtCoreFW.Contracts.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Contracts.Tests;

public sealed class ContractTests
{
    [Fact]
    public void Create_SetsTenantPropertiesAndDraftStatus()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var startDate = new DateTime(2026, 1, 1);
        var endDate = new DateTime(2026, 12, 31);

        var contract = Contract.Create(
            tenantId, "HD-001", "Service agreement", "Acme", startDate, endDate, null, createdBy);

        contract.TenantId.Should().Be(tenantId);
        contract.ContractNumber.Should().Be("HD-001");
        contract.Title.Should().Be("Service agreement");
        contract.CounterpartyName.Should().Be("Acme");
        contract.StartDate.Should().Be(startDate);
        contract.EndDate.Should().Be(endDate);
        contract.Status.Should().Be(ContractStatus.Draft);
        contract.CreatedBy.Should().Be(createdBy);
    }

    [Fact]
    public void Update_ChangesContractFieldsAndAuditActor()
    {
        var contract = Contract.Create(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            "Initial",
            Guid.NewGuid());
        var updatedBy = Guid.NewGuid();

        contract.Update(
            "HD-002",
            "Updated agreement",
            "Contoso",
            new DateTime(2026, 2, 1),
            new DateTime(2027, 1, 31),
            ContractStatus.Active,
            "Updated",
            updatedBy);

        contract.ContractNumber.Should().Be("HD-002");
        contract.Title.Should().Be("Updated agreement");
        contract.CounterpartyName.Should().Be("Contoso");
        contract.StartDate.Should().Be(new DateTime(2026, 2, 1));
        contract.EndDate.Should().Be(new DateTime(2027, 1, 31));
        contract.Status.Should().Be(ContractStatus.Active);
        contract.Description.Should().Be("Updated");
        contract.ModifiedBy.Should().Be(updatedBy);
    }
}
