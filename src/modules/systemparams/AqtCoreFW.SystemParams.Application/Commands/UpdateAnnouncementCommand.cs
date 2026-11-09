using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SystemParams.Domain.Entities;

namespace AqtCoreFW.SystemParams.Application.Commands;

/// <summary>Update existing announcement. Id is long (T1 bigint PK).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateAnnouncementCommand(
    long Id,
    string Title,
    string Content,
    AnnouncementSeverity Severity,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    bool IsActive) : ICommand;
