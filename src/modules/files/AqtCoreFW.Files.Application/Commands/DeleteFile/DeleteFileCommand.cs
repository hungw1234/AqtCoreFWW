using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Files.Application.Commands.DeleteFile;

/// <summary>Soft-delete a file record. Storage object retained for audit; logical access removed.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record DeleteFileCommand(Guid FileId, Guid TenantId, Guid DeletedBy) : ICommand;
