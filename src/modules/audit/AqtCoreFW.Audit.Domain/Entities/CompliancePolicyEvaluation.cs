using AqtCoreFW.Audit.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Result of evaluating an entity against a CompliancePolicy — M15 AI Governance.
/// Append-only record created by EvaluateCompliancePolicyHandler.
/// T1: long PK (DB-assigned, via non-generic AuditableEntity). CSV classification: T1.
/// PolicyId flipped to long (CompliancePolicy.Id is now bigint).
/// EntityId stays Guid — polymorphic FK with no single long PK target.
/// </summary>
public sealed class CompliancePolicyEvaluation : AuditableEntity
{
    /// <summary>FK to CompliancePolicy.Id (bigint identity PK).</summary>
    public long PolicyId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    /// <summary>Polymorphic FK — stays Guid (no single long-PK entity to reference).</summary>
    public Guid EntityId { get; private set; }
    public EvaluationResult Result { get; private set; }
    public string? Details { get; private set; }
    public DateTime EvaluatedAt { get; private set; }

    private CompliancePolicyEvaluation() { }

    public static CompliancePolicyEvaluation Create(
        long policyId,
        string entityType,
        Guid entityId,
        EvaluationResult result,
        string? details)
    {
        return new CompliancePolicyEvaluation
        {
            PolicyId = policyId,
            EntityType = entityType,
            EntityId = entityId,
            Result = result,
            Details = details,
            EvaluatedAt = DateTime.UtcNow
        };
    }
}
