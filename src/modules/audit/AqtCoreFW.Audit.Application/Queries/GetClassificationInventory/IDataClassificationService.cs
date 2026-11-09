using AqtCoreFW.Audit.Application.DTOs;

namespace AqtCoreFW.Audit.Application.Queries.GetClassificationInventory;

/// <summary>
/// Application-layer boundary: returns the classification inventory without coupling
/// Application to the Infrastructure assembly. Implemented by
/// <c>DataClassificationService</c> in AqtCoreFW.Audit.Infrastructure.
/// </summary>
public interface IDataClassificationService
{
    ClassificationInventoryDto GetInventory();
}
