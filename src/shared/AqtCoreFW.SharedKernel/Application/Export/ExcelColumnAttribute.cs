namespace AqtCoreFW.SharedKernel.Application.Export;

/// <summary>
/// Mark a DTO property with a Vietnamese (or any locale) column header for Excel export.
/// If not present, the property name is used as-is.
/// Usage: [ExcelColumn("Mã vụ việc")] public string CaseNumber { get; init; }
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ExcelColumnAttribute(string header) : Attribute
{
    public string Header { get; } = header;

    /// <summary>Column order (0-based). Unset = reflection order.</summary>
    public int Order { get; set; } = -1;

    /// <summary>Exclude this property from the export entirely.</summary>
    public bool Ignore { get; set; } = false;
}
