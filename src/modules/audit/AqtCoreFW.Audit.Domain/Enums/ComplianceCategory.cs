namespace AqtCoreFW.Audit.Domain.Enums;

/// <summary>Category of a compliance policy — governs which domain it applies to.</summary>
public enum ComplianceCategory
{
    DataRetention = 0,
    AIUsage = 1,
    AccessControl = 2,
    Privacy = 3
}
