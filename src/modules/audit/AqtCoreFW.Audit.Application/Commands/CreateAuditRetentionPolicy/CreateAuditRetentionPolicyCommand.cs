using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.Commands.CreateAuditRetentionPolicy;

/// <summary>
/// QĐ 742 §3.2.b — admin creates new (tenant, category) retention rule.
/// Decorated as <see cref="AuditCategory.ConfigChange"/> so the action is itself audited
/// (plan §"Security Considerations": retention policy change MUST self-audit).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateAuditRetentionPolicyCommand(
    Guid? TenantId,
    AuditCategory? Category,
    int RetentionDays,
    bool ArchiveBeforePurge,
    string? ArchivePath) : ICommand<long>;
