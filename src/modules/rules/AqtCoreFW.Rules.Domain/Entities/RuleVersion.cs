using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Rules.Domain.Entities;

/// <summary>
/// Immutable snapshot of a RuleSet at a given version number.
/// Created on each ActivateRuleVersion command — allows rollback and audit.
/// T1 mutable entity: AuditableEntity (non-generic, long PK).
/// RuleSetId: long (FK to RuleSet.Id internal PK).
/// </summary>
public sealed class RuleVersion : AuditableEntity
{
    /// <summary>FK to RuleSet.Id (long, T2 internal PK).</summary>
    public long RuleSetId { get; private set; }
    public int VersionNumber { get; private set; }
    /// <summary>Full JSON snapshot of the RuleSet + all Rules at activation time.</summary>
    public string Snapshot { get; private set; } = string.Empty;
    public DateTime? ActivatedAt { get; private set; }

    private RuleVersion() { } // EF Core

    /// <summary>Factory — creates a version snapshot (not yet activated).</summary>
    public static RuleVersion Create(
        long ruleSetId,
        int versionNumber,
        string snapshot,
        Guid createdBy)
    {
        var version = new RuleVersion
        {
            RuleSetId = ruleSetId,
            VersionNumber = versionNumber,
            Snapshot = snapshot
        };
        version.SetAuditCreate(createdBy);
        return version;
    }

    /// <summary>Stamps the activation timestamp when the RuleSet goes Active.</summary>
    public void MarkActivated() => ActivatedAt = DateTime.UtcNow;
}
