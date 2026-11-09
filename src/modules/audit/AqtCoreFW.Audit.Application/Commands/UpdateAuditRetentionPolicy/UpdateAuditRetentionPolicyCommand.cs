using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.Commands.UpdateAuditRetentionPolicy;

/// <summary>
/// Updates retention window + archive flags. <see cref="RowVersion"/> is the base64
/// concurrency token returned by GET — server rejects stale updates with a 409.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateAuditRetentionPolicyCommand(
    long Id,
    int RetentionDays,
    bool ArchiveBeforePurge,
    string? ArchivePath,
    string RowVersion) : ICommand;
