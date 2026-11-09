using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.Commands.DeleteAuditRetentionPolicy;

/// <summary>
/// Deletes a retention policy. The seeded global default policy
/// (Id = 00000000-0000-0000-0000-000000000001) cannot be deleted — that would leave
/// the purge job without a fallback. Handler enforces this guard.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteAuditRetentionPolicyCommand(long Id, string RowVersion) : ICommand;
