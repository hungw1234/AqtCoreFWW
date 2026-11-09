using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Reporting.Domain.Entities;

/// <summary>
/// Parameterized report template stored in DB.
/// Admin creates these; Hangfire jobs execute them via Dapper.
/// SqlTemplate MUST include @TenantId — enforced at creation time.
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// TenantId: string? (DEVIATION — pre-existing design: null = system-wide, non-null = tenant slug;
///   NOT a Guid FK — kept as string? to preserve multi-tenant scoping semantics. Phase-4b review.)
/// </summary>
public sealed class ReportDefinition : AuditableEntity, IPublicIdentified
{
    public Guid PublicId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NameVi { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    /// <summary>Parameterized SQL — e.g. SELECT ... WHERE TenantId = @TenantId AND Date >= @FromDate.</summary>
    public string SqlTemplate { get; private set; } = string.Empty;

    /// <summary>JSON schema: [{"name":"FromDate","type":"date","required":true}, ...]</summary>
    public string ParametersSchema { get; private set; } = "[]";

    public OutputFormat OutputFormat { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Tenant scoping slug: null = system-wide template; non-null = tenant-specific override.
    /// DEVIATION: kept as string? (not Guid) — design intent is slug/name not a FK to Tenant entity.
    /// </summary>
    public string? TenantId { get; private set; }

    private ReportDefinition() { }

    public static ReportDefinition Create(
        IGuidGenerator gen,
        string name,
        string nameVi,
        string description,
        string sqlTemplate,
        string parametersSchema,
        OutputFormat format,
        string? tenantId = null)
    {
        return new ReportDefinition
        {
            PublicId = gen.NewIdMonotonic(),
            Name = name,
            NameVi = nameVi,
            Description = description,
            SqlTemplate = sqlTemplate,
            ParametersSchema = parametersSchema,
            OutputFormat = format,
            IsActive = true,
            TenantId = tenantId
        };
    }

    public void UpdateDefinition(
        string name,
        string nameVi,
        string description,
        string sqlTemplate,
        string parametersSchema,
        OutputFormat format)
    {
        Name = name;
        NameVi = nameVi;
        Description = description;
        SqlTemplate = sqlTemplate;
        ParametersSchema = parametersSchema;
        OutputFormat = format;
        MarkUpdated();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
