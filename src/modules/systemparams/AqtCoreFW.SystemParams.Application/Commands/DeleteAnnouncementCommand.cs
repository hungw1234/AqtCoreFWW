using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.SystemParams.Application.Commands;

/// <summary>Soft-delete announcement (sets IsActive=false). Id is long (T1 bigint PK).</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteAnnouncementCommand(long Id) : ICommand;
