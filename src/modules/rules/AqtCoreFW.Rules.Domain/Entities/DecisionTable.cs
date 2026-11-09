using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Rules.Domain.Entities;

/// <summary>
/// Decision table entity — stores tabular rule definitions as JSON columns.
/// InputColumns / OutputColumns / Rows are serialised JSON arrays.
/// T1 mutable entity: AuditableEntity (non-generic, long PK).
/// RuleSetId: long (FK to RuleSet.Id internal PK).
/// TenantId: Guid (FK exception list — tenant isolation ref).
/// </summary>
public sealed class DecisionTable : AuditableEntity, ITenantScoped
{
    /// <summary>FK to RuleSet.Id (long, T2 internal PK).</summary>
    public long RuleSetId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    /// <summary>JSON array of input column definitions.</summary>
    public string InputColumns { get; private set; } = "[]";
    /// <summary>JSON array of output column definitions.</summary>
    public string OutputColumns { get; private set; } = "[]";
    /// <summary>JSON array of row objects.</summary>
    public string Rows { get; private set; } = "[]";

    private DecisionTable() { } // EF Core

    /// <summary>Factory — creates a new DecisionTable linked to a RuleSet.</summary>
    public static DecisionTable Create(
        long ruleSetId,
        string name,
        string inputColumns,
        string outputColumns,
        string rows,
        Guid tenantId,
        Guid createdBy)
    {
        var table = new DecisionTable
        {
            RuleSetId = ruleSetId,
            Name = name,
            InputColumns = inputColumns,
            OutputColumns = outputColumns,
            Rows = rows,
            TenantId = tenantId
        };
        table.SetAuditCreate(createdBy);
        return table;
    }
}
