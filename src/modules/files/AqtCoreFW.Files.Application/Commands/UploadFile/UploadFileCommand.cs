using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Files.Application.Commands.UploadFile;

/// <summary>
/// Upload a file — phase 1 of two-phase async pipeline.
/// Returns 202 Accepted with fileId; ClamAV scan runs async via Hangfire.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record UploadFileCommand(
    Stream FileStream,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    Guid TenantId,
    Guid UploadedBy,
    Guid? CaseId = null,
    ClassificationLevel Classification = ClassificationLevel.Internal) : ICommand<FileUploadResultDto>;
