using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Reporting.Domain.Entities;

/// <summary>
/// Pre-approved Dapper query for AI NLQ (Natural Language Query) use.
/// AI maps natural-language question → catalog entry → execute with params.
/// NOT Text2SQL — all SQL is admin-approved and stored here.
/// SQL MUST include TenantId = @TenantId — enforced on seed and admin create.
///
/// T1 entity: AuditableEntity (long Id). No PublicId (T1 = internal config only).
/// </summary>
public sealed class QueryCatalogEntry : AuditableEntity
{
    /// <summary>Machine-readable code e.g. "SLA_BY_DEPARTMENT_PERIOD".</summary>
    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;
    public string DescriptionVi { get; private set; } = string.Empty;

    /// <summary>Dapper SQL — parameterized, MUST include @TenantId.</summary>
    public string DapperSql { get; private set; } = string.Empty;

    /// <summary>JSON array of required parameter names.</summary>
    public string RequiredParams { get; private set; } = "[]";

    /// <summary>JSON array of optional parameter names.</summary>
    public string OptionalParams { get; private set; } = "[]";

    /// <summary>RBAC role required to execute this query (null = any authenticated user).</summary>
    public string? RequiredRole { get; private set; }

    public bool IsActive { get; private set; }

    private QueryCatalogEntry() { }

    public static QueryCatalogEntry Create(
        string code,
        string description,
        string descriptionVi,
        string dapperSql,
        string requiredParams,
        string optionalParams,
        string? requiredRole = null)
    {
        return new QueryCatalogEntry
        {
            Code = code,
            Description = description,
            DescriptionVi = descriptionVi,
            DapperSql = dapperSql,
            RequiredParams = requiredParams,
            OptionalParams = optionalParams,
            RequiredRole = requiredRole,
            IsActive = true
        };
    }
}
