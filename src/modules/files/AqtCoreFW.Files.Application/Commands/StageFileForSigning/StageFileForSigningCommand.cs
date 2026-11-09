using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Files.Application.Commands.StageFileForSigning;

/// <summary>
/// Copies a tenant-owned PDF from the Files bucket ("aqtcorefw-files") into the signing-original
/// bucket ("signing-original") under a freshly minted document id, so the DocFlow USB path can
/// download it. Without this copy the DocFlow handler would receive a key from the wrong bucket
/// and return 404 on every signing attempt.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record StageFileForSigningCommand(Guid FileId, Guid TenantId) : ICommand<StageFileForSigningResult>;
