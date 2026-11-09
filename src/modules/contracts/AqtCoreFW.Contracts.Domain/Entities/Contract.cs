using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Contracts.Domain.Entities;

public sealed class Contract : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string ContractNumber { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string CounterpartyName { get; private set; } = string.Empty;
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public ContractStatus Status { get; private set; }
    public string? Description { get; private set; }

    private Contract() { }

    public static Contract Create(
        Guid tenantId,
        string contractNumber,
        string title,
        string counterpartyName,
        DateTime startDate,
        DateTime endDate,
        string? description,
        Guid createdBy)
    {
        var contract = new Contract
        {
            TenantId = tenantId,
            ContractNumber = contractNumber,
            Title = title,
            CounterpartyName = counterpartyName,
            StartDate = startDate,
            EndDate = endDate,
            Status = ContractStatus.Draft,
            Description = description
        };
        contract.SetAuditCreate(createdBy);
        return contract;
    }

    public void Update(
        string contractNumber,
        string title,
        string counterpartyName,
        DateTime startDate,
        DateTime endDate,
        ContractStatus status,
        string? description,
        Guid updatedBy)
    {
        ContractNumber = contractNumber;
        Title = title;
        CounterpartyName = counterpartyName;
        StartDate = startDate;
        EndDate = endDate;
        Status = status;
        Description = description;
        SetAuditUpdate(updatedBy);
    }
}
