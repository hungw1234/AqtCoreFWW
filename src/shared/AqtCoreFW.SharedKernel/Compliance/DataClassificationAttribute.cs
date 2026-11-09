namespace AqtCoreFW.SharedKernel.Compliance;

/// <summary>
/// Marks a property with its data sensitivity classification level.
/// Used by DataClassificationScanner to generate inventory reports and
/// enforce masking/encryption policies per QĐ742 / PDPL requirements.
///
/// Apply to entity or DTO properties containing PII or sensitive data:
///   [DataClassification(DataClassificationLevel.Restricted)]
///   public string NationalId { get; set; }
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DataClassificationAttribute(DataClassificationLevel level) : Attribute
{
    /// <summary>Sensitivity level of the annotated property.</summary>
    public DataClassificationLevel Level { get; } = level;
}

/// <summary>
/// Data sensitivity classification levels aligned with PDPL / QĐ742 cấp 4.
///
/// Public      — freely shareable, no controls needed
/// Internal    — internal use only, standard access controls
/// Confidential — limited distribution, role-based access, audit logging required
/// Restricted  — PII / secrets, encryption at rest + in transit mandatory,
///               access logged, masking in UI/API responses required
/// </summary>
public enum DataClassificationLevel
{
    Public = 0,
    Internal = 1,
    Confidential = 2,
    Restricted = 3
}
