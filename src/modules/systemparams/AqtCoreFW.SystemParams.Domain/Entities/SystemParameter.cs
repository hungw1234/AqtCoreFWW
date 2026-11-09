using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SystemParams.Domain.Entities;

public enum SystemParamDataType { String, Int, Bool, Json }

/// <summary>
/// Runtime configuration key-value pair. Key stored lowercase-normalized.
/// IsEditable=false blocks API updates (system-managed params).
/// TenantId=null means system-wide default; tenant override checked first on reads.
/// T1: long PK (DB-assigned, via non-generic AuditableEntity). No PublicId — config lookup uses Key.
/// </summary>
public sealed class SystemParameter : AuditableEntity
{
    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;
    public SystemParamDataType DataType { get; private set; }
    public string Description { get; private set; } = default!;
    public bool IsEditable { get; private set; }
    public string? TenantId { get; private set; }

    private SystemParameter() { }

    public static SystemParameter Create(
        string key, string value, SystemParamDataType dataType,
        string description, bool isEditable, string? tenantId = null) =>
        new()
        {
            Key = key.ToLowerInvariant(),
            Value = value,
            DataType = dataType,
            Description = description,
            IsEditable = isEditable,
            TenantId = tenantId
        };

    /// <summary>Throws if IsEditable=false (GOV_CFG_001).</summary>
    public void UpdateValue(string newValue)
    {
        if (!IsEditable)
            throw new InvalidOperationException($"GOV_CFG_001: Parameter '{Key}' is read-only and cannot be updated.");
        Value = newValue;
        MarkUpdated();
    }
}
